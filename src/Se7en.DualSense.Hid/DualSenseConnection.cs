using HidSharp;

namespace Se7en.DualSense.Hid;

/// <summary>Callback appelé sur le thread de lecture à chaque rapport décodé. Doit rester très rapide.</summary>
public delegate void DualSenseStateHandler(in DualSenseState state);

/// <summary>
/// Gère la détection, la lecture et la reconnexion automatique d'une DualSense.
/// Toute l'E/S HID se fait sur un thread dédié : lecture bloquante → callback → écriture
/// éventuelle du rapport de sortie si l'état demandé a changé.
/// </summary>
public sealed class DualSenseConnection : IDisposable
{
    private const string BluetoothHidServiceGuid = "{00001124-0000-1000-8000-00805f9b34fb}";
    private const int ReadTimeoutMs = 1000;
    private const int RetryDelayMs = 1500;

    private readonly object _stateLock = new();
    private readonly object _outputLock = new();
    private readonly AutoResetEvent _deviceListChanged = new(false);

    private Thread? _thread;
    private volatile bool _stopping;
    private HidStream? _stream;

    private DualSenseState _latest;
    private DualSenseOutputState _desiredOutput = DualSenseOutputState.Default;
    private bool _outputDirty = true;
    private volatile bool _outputEnabled = true;

    /// <summary>Événement déclenché sur le thread de lecture à chaque rapport.</summary>
    public event DualSenseStateHandler? StateReceived;
    public event Action<DualSenseDeviceInfo>? Connected;
    public event Action<DualSenseDeviceInfo>? Disconnected;
    /// <summary>Messages d'information / d'erreur en français.</summary>
    public event Action<string>? Log;

    public DualSenseDeviceInfo? Device { get; private set; }

    public bool IsConnected => Device != null;

    /// <summary>Nombre de rapports reçus depuis la connexion (pour mesurer la fréquence).</summary>
    public long ReportCount { get; private set; }

    /// <summary>
    /// Si false, aucun rapport de sortie n'est envoyé (mode passthrough : lecture seule,
    /// pour ne pas interférer avec un jeu qui pilote lui-même la DualSense).
    /// </summary>
    public bool OutputEnabled
    {
        get => _outputEnabled;
        set
        {
            _outputEnabled = value;
            if (value)
                lock (_outputLock) _outputDirty = true;
        }
    }

    /// <summary>Copie du dernier état reçu (thread-safe).</summary>
    public DualSenseState Latest
    {
        get { lock (_stateLock) return _latest; }
    }

    public void Start()
    {
        if (_thread != null)
            return;

        DeviceList.Local.Changed += OnDeviceListChanged;
        _thread = new Thread(Run)
        {
            Name = "DualSense-IO",
            IsBackground = true,
            Priority = ThreadPriority.Highest,
        };
        _thread.Start();
    }

    /// <summary>Demande un nouvel état de sortie ; il sera envoyé par le thread d'E/S.</summary>
    public void SetOutput(in DualSenseOutputState output)
    {
        lock (_outputLock)
        {
            if (_desiredOutput == output)
                return;
            _desiredOutput = output;
            _outputDirty = true;
        }
    }

    public DualSenseOutputState GetOutput()
    {
        lock (_outputLock) return _desiredOutput;
    }

    private void OnDeviceListChanged(object? sender, DeviceListChangedEventArgs e) => _deviceListChanged.Set();

    private void Run()
    {
        bool reportedMissing = false;

        while (!_stopping)
        {
            HidDevice? hid = FindDevice();
            if (hid == null)
            {
                if (!reportedMissing)
                {
                    Log?.Invoke("En attente d'une DualSense (branchez-la en USB ou connectez-la en Bluetooth)…");
                    reportedMissing = true;
                }
                _deviceListChanged.WaitOne(RetryDelayMs);
                continue;
            }

            reportedMissing = false;
            try
            {
                RunSession(hid);
            }
            catch (Exception ex) when (ex is IOException or ObjectDisposedException or TimeoutException
                                           or UnauthorizedAccessException or InvalidOperationException)
            {
                if (!_stopping)
                    Log?.Invoke($"Communication interrompue avec la manette : {ex.Message}");
            }
            finally
            {
                CloseStream();
                var info = Device;
                Device = null;
                if (info != null)
                {
                    lock (_stateLock) _latest = default;
                    Disconnected?.Invoke(info);
                    if (!_stopping)
                        Log?.Invoke("Manette déconnectée. Reconnexion automatique dès qu'elle réapparaît.");
                }
            }

            if (!_stopping)
                _deviceListChanged.WaitOne(RetryDelayMs);
        }
    }

    private static HidDevice? FindDevice()
    {
        try
        {
            return DeviceList.Local
                .GetHidDevices(DualSenseIds.SonyVendorId)
                .Where(d => DualSenseIds.IsSupported(d.VendorID, d.ProductID))
                .Where(d => d.GetMaxInputReportLength() >= 10)
                // Priorité à l'USB si les deux sont présents
                .OrderBy(d => IsBluetooth(d) ? 1 : 0)
                .FirstOrDefault();
        }
        catch
        {
            return null;
        }
    }

    private static bool IsBluetooth(HidDevice d)
    {
        if (d.DevicePath.Contains(BluetoothHidServiceGuid, StringComparison.OrdinalIgnoreCase))
            return true;
        return d.GetMaxInputReportLength() >= DualSenseReportParser.BtInputReportLength;
    }

    private void RunSession(HidDevice hid)
    {
        if (!hid.TryOpen(out HidStream stream))
        {
            Log?.Invoke("Impossible d'ouvrir la manette. Elle est peut-être masquée par HidHide ou utilisée en mode exclusif par une autre application.");
            return;
        }

        _stream = stream;
        stream.ReadTimeout = ReadTimeoutMs;

        var connection = IsBluetooth(hid) ? ConnectionType.Bluetooth : ConnectionType.Usb;
        string? serial = null;
        try { serial = hid.GetSerialNumber(); } catch { /* facultatif */ }

        var info = new DualSenseDeviceInfo(
            hid.DevicePath,
            connection,
            hid.ProductID == DualSenseIds.DualSenseEdgeProductId,
            serial,
            hid.GetMaxInputReportLength(),
            hid.GetMaxOutputReportLength());

        // La lecture du rapport feature 0x05 (calibration) active le mode complet 0x31 en Bluetooth.
        RequestCalibrationReport(hid, stream);

        Device = info;
        ReportCount = 0;
        lock (_outputLock) _outputDirty = true;
        Log?.Invoke($"{info.DisplayName} connectée en {info.ConnectionLabel}.");
        Connected?.Invoke(info);

        var input = new byte[Math.Max(hid.GetMaxInputReportLength(), DualSenseReportParser.BtInputReportLength)];
        var output = new OutputWriter(stream, info);
        bool releaseLightbar = true;

        while (!_stopping)
        {
            int read;
            try
            {
                read = stream.Read(input, 0, input.Length);
            }
            catch (TimeoutException)
            {
                // Pas de données (rapport BT réduit sans changement) : vérifier que la manette existe toujours.
                if (!DeviceStillPresent(hid.DevicePath))
                    return;
                read = 0;
            }

            if (read > 0 && DualSenseReportParser.TryParse(input.AsSpan(0, read), out var state))
            {
                ReportCount++;
                lock (_stateLock) _latest = state;
                StateReceived?.Invoke(in state);
            }

            if (_outputEnabled)
            {
                DualSenseOutputState toSend;
                lock (_outputLock)
                {
                    if (!_outputDirty)
                        continue;
                    _outputDirty = false;
                    toSend = _desiredOutput;
                }
                output.Send(toSend, releaseLightbar);
                releaseLightbar = false;
            }
        }
    }

    private void RequestCalibrationReport(HidDevice hid, HidStream stream)
    {
        try
        {
            var feature = new byte[Math.Max(hid.GetMaxFeatureReportLength(), 41)];
            feature[0] = 0x05;
            stream.GetFeature(feature);
        }
        catch (Exception ex)
        {
            Log?.Invoke($"Lecture du rapport de calibration impossible ({ex.Message}). En Bluetooth, les données complètes peuvent ne pas être disponibles.");
        }
    }

    private static bool DeviceStillPresent(string path)
    {
        try
        {
            return DeviceList.Local.GetHidDevices(DualSenseIds.SonyVendorId)
                .Any(d => string.Equals(d.DevicePath, path, StringComparison.OrdinalIgnoreCase));
        }
        catch
        {
            return false;
        }
    }

    private void CloseStream()
    {
        var s = Interlocked.Exchange(ref _stream, null);
        try { s?.Dispose(); } catch { /* ignoré */ }
    }

    public void Dispose()
    {
        _stopping = true;
        DeviceList.Local.Changed -= OnDeviceListChanged;
        _deviceListChanged.Set();
        CloseStream();
        _thread?.Join(2000);
        _deviceListChanged.Dispose();
    }

    /// <summary>Envoie les rapports de sortie (gère la séquence BT et la taille attendue par Windows).</summary>
    private sealed class OutputWriter
    {
        private readonly HidStream _stream;
        private readonly DualSenseDeviceInfo _info;
        private readonly byte[] _exact;
        private readonly byte[] _padded;
        private bool _usePadded;
        private byte _sequence;

        public OutputWriter(HidStream stream, DualSenseDeviceInfo info)
        {
            _stream = stream;
            _info = info;
            int exact = info.Connection == ConnectionType.Usb
                ? DualSenseOutputReport.UsbLength
                : DualSenseOutputReport.BtLength;
            _exact = new byte[exact];
            _padded = new byte[Math.Max(exact, info.MaxOutputReportLength)];
            // Si Windows annonce une taille différente, on part directement sur le tampon complété.
            _usePadded = info.MaxOutputReportLength > exact;
        }

        public void Send(in DualSenseOutputState state, bool releaseLightbar)
        {
            var buffer = _usePadded ? _padded : _exact;
            Array.Clear(buffer);
            int len = DualSenseOutputReport.Build(buffer, _info.Connection, state, _sequence, releaseLightbar);
            _sequence = (byte)((_sequence + 1) & 0x0F);
            try
            {
                _stream.Write(buffer, 0, _usePadded ? buffer.Length : len);
            }
            catch (IOException) when (!_usePadded && _padded.Length > _exact.Length)
            {
                _usePadded = true;
                Send(state, releaseLightbar);
            }
        }
    }
}
