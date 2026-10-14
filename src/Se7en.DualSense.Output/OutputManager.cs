using System.Security.Principal;
using Nefarius.ViGEm.Client;
using Se7en.DualSense.Hid;

namespace Se7en.DualSense.Output;

/// <summary>État courant de la sortie, pour l'interface.</summary>
public sealed record OutputStatus(OutputMode Mode, bool PadActive, bool DeviceHidden, string Message);

/// <summary>
/// Orchestre la sortie vers les jeux :
///  - DS4 / Xbox 360 : une manette virtuelle reçoit chaque rapport (thread d'E/S, sans allocation),
///    la vraie DualSense est masquée par HidHide pour éviter la double entrée ;
///  - DualSense native : plus de manette virtuelle, la DualSense est démasquée et lue en lecture seule.
///
/// Ordre sûr à la bascule : vers un mode virtuel, la manette virtuelle est créée AVANT de masquer la vraie ;
/// vers le mode natif, la vraie est démasquée AVANT de débrancher la virtuelle.
/// </summary>
public sealed class OutputManager : IDisposable
{
    private readonly DualSenseConnection _connection;
    private readonly HidHideService _hidHide;
    private readonly SemaphoreSlim _applyGate = new(1, 1);
    private ViGEmClient? _client;
    private volatile IVirtualPad? _pad;
    private string? _hiddenInstance;
    private OutputMode _mode;
    private bool _hidHideReady;
    private bool _disposed;

    public OutputManager(DualSenseConnection connection, HidHideService hidHide, OutputMode initialMode)
    {
        _connection = connection;
        _hidHide = hidHide;
        _mode = initialMode;
        _hidHide.Log += msg => Log?.Invoke(msg);
    }

    public event Action<OutputStatus>? StatusChanged;
    public event Action<string>? Log;

    public OutputMode Mode => _mode;

    /// <summary>Si vrai (mode DS4), la couleur de barre lumineuse demandée par le jeu est appliquée.</summary>
    public bool LightbarFromGame { get; set; } = true;

    public OutputStatus Status { get; private set; } = new(OutputMode.Native, false, false, "");

    public static bool IsElevated
    {
        get
        {
            using var id = WindowsIdentity.GetCurrent();
            return new WindowsPrincipal(id).IsInRole(WindowsBuiltInRole.Administrator);
        }
    }

    public void Start()
    {
        if (_hidHide.IsInstalled && IsElevated)
        {
            _hidHide.RecoverFromJournal();
            _hidHideReady = _hidHide.RegisterSelf();
        }

        try
        {
            _client = new ViGEmClient();
        }
        catch (Exception ex)
        {
            _client = null;
            Log?.Invoke(ex is Nefarius.ViGEm.Client.Exceptions.VigemBusNotFoundException
                ? "ViGEmBus n'est pas installé : l'émulation DualShock 4 / Xbox 360 est indisponible."
                : $"ViGEmBus inaccessible : {ex.Message}");
        }

        _connection.StateReceived += OnState;
        _connection.Connected += _ => RequestApply();
        _connection.Disconnected += _ => RequestApply();
        RequestApply();
    }

    public void SetMode(OutputMode mode)
    {
        if (mode == _mode) return;
        _mode = mode;
        RequestApply();
    }

    /// <summary>Les opérations HidHide lancent un processus : jamais sur le thread d'E/S ni l'UI.</summary>
    private void RequestApply() => _ = Task.Run(ApplyAsync);

    private async Task ApplyAsync()
    {
        await _applyGate.WaitAsync();
        try
        {
            if (_disposed) return;
            var device = _connection.Device;
            var mode = _mode;
            string message;

            if (mode.IsVirtual() && device != null && _client != null)
            {
                if (_pad?.Mode != mode)
                {
                    DisposePad();
                    _pad = CreatePad(mode);
                    _connection.SetOutput(_connection.GetOutput() with { HeavyMotor = 0, LightMotor = 0 });
                }
                _connection.OutputEnabled = true;

                string instance = HidHideService.ToInstanceId(device.DevicePath);
                if (_hiddenInstance != null && !string.Equals(_hiddenInstance, instance, StringComparison.OrdinalIgnoreCase))
                    UnhideCurrent();
                bool hidden = _hidHideReady && (_hiddenInstance != null || TryHide(instance));

                message = hidden
                    ? $"Les jeux voient une manette {mode.Label()}."
                    : _hidHide.IsInstalled
                        ? $"Manette {mode.Label()} active, mais la DualSense n'a pas pu être masquée : un jeu peut voir deux manettes."
                        : $"Manette {mode.Label()} active. HidHide n'est pas installé : un jeu peut voir deux manettes.";
            }
            else
            {
                UnhideCurrent();
                DisposePad();
                _connection.OutputEnabled = mode != OutputMode.Native;

                message = mode == OutputMode.Native
                    ? "Les jeux voient la DualSense directement. Anti-drift et mappage inactifs dans ce mode."
                    : _client == null
                        ? "ViGEmBus n'est pas installé : l'émulation est indisponible."
                        : "En attente de la manette.";
            }

            Status = new OutputStatus(mode, _pad != null, _hiddenInstance != null, message);
            StatusChanged?.Invoke(Status);
        }
        catch (Exception ex)
        {
            Log?.Invoke($"Changement de mode impossible : {ex.Message}");
        }
        finally
        {
            _applyGate.Release();
        }
    }

    private IVirtualPad CreatePad(OutputMode mode)
    {
        IVirtualPad pad = mode == OutputMode.Xbox360 ? new VirtualXbox360(_client!) : new VirtualDs4(_client!);
        pad.Feedback += OnFeedback;
        return pad;
    }

    private bool TryHide(string instance)
    {
        if (!_hidHide.Hide(instance)) return false;
        _hiddenInstance = instance;
        return true;
    }

    private void UnhideCurrent()
    {
        if (_hiddenInstance == null) return;
        if (_hidHide.Unhide(_hiddenInstance))
            _hiddenInstance = null;
    }

    private void DisposePad()
    {
        var pad = _pad;
        _pad = null;
        if (pad == null) return;
        pad.Feedback -= OnFeedback;
        pad.Dispose();
        _connection.SetOutput(_connection.GetOutput() with { HeavyMotor = 0, LightMotor = 0 });
    }

    /// <summary>Thread d'E/S : transmet l'état au jeu.</summary>
    private void OnState(in DualSenseState state)
    {
        var pad = _pad;
        if (pad == null) return;
        try
        {
            pad.Submit(state);
        }
        catch
        {
            // Manette virtuelle en cours de remplacement : rapport ignoré
        }
    }

    /// <summary>Vibrations et barre lumineuse demandées par le jeu → vraie DualSense.</summary>
    private void OnFeedback(GameFeedback f)
    {
        var o = _connection.GetOutput() with { HeavyMotor = f.HeavyMotor, LightMotor = f.LightMotor };
        if (f.HasLightbar && LightbarFromGame)
            o = o with { LightbarRed = f.LightbarRed!.Value, LightbarGreen = f.LightbarGreen!.Value, LightbarBlue = f.LightbarBlue!.Value };
        _connection.SetOutput(o);
    }

    /// <summary>Démasque immédiatement (fermeture, plantage) : la manette ne reste jamais invisible.</summary>
    public void EmergencyRestore()
    {
        try { _hidHide.UnhideAll(); } catch { /* dernier recours */ }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _connection.StateReceived -= OnState;
        _applyGate.Wait(3000);
        try
        {
            EmergencyRestore();
            _hiddenInstance = null;
            DisposePad();
            _client?.Dispose();
        }
        finally
        {
            _applyGate.Release();
        }
    }
}
