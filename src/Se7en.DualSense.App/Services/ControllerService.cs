using System.Windows;
using System.Windows.Media;
using Se7en.DualSense.Hid;
using Se7en.DualSense.Output;

namespace Se7en.DualSense.App.Services;

/// <summary>
/// Pont entre le thread d'E/S de la manette et l'interface.
/// Le thread HID n'est jamais bloqué par l'UI : l'interface lit simplement un instantané
/// à chaque image (CompositionTarget.Rendering) et ne redessine que si quelque chose a changé.
/// </summary>
public sealed class ControllerService : IDisposable
{
    private readonly DualSenseConnection _connection = new();
    private DualSenseDeviceInfo? _lastDevice;
    private long _lastReportCount = -1;

    public ControllerService()
    {
        _connection.Log += msg => Application.Current?.Dispatcher.BeginInvoke(() => Log?.Invoke(msg));
        HidHide = new HidHideService(AppSettings.Directory);
        Output = new OutputManager(_connection, HidHide, AppSettings.Current.OutputMode);
        Output.Log += msg => Application.Current?.Dispatcher.BeginInvoke(() => Log?.Invoke(msg));
        Output.StatusChanged += status => Application.Current?.Dispatcher.BeginInvoke(() => OutputStatusChanged?.Invoke(status));
    }

    public HidHideService HidHide { get; }

    /// <summary>Sortie vers les jeux (DS4 / Xbox 360 / native) et masquage HidHide.</summary>
    public OutputManager Output { get; }

    /// <summary>Appelé sur le thread UI quand le mode de sortie ou le masquage change.</summary>
    public event Action<OutputStatus>? OutputStatusChanged;

    public void SetOutputMode(OutputMode mode)
    {
        AppSettings.Current.OutputMode = mode;
        AppSettings.Save();
        Output.SetMode(mode);
    }

    /// <summary>Appelé sur le thread UI à chaque image où l'état a changé.</summary>
    public event Action<FrameData>? Frame;

    /// <summary>Appelé sur le thread UI lors d'une connexion / déconnexion.</summary>
    public event Action<DualSenseDeviceInfo?>? DeviceChanged;

    public event Action<string>? Log;

    public DualSenseDeviceInfo? Device => _connection.Device;

    public DualSenseConnection Connection => _connection;

    public void Start()
    {
        _connection.Start();
        if (!IsDemo)
            Output.Start();
        CompositionTarget.Rendering += OnRendering;
    }

    /// <summary>
    /// Mode démo (option --demo) : simule une manette pour prévisualiser l'interface.
    /// Volontairement explicite pour ne jamais masquer une vraie manette par erreur.
    /// </summary>
    public static bool IsDemo { get; set; }

    /// <summary>Option --demo-all : toutes les touches simulées enfoncées en même temps.</summary>
    public static bool DemoAllButtons { get; set; }
    private static readonly DualSenseDeviceInfo DemoDevice = new("demo", ConnectionType.Usb, false, null, 64, 48);
    private readonly System.Diagnostics.Stopwatch _demoClock = System.Diagnostics.Stopwatch.StartNew();

    private void OnRendering(object? sender, EventArgs e)
    {
        if (IsDemo)
        {
            RenderDemo();
            return;
        }

        var device = _connection.Device;
        if (!ReferenceEquals(device, _lastDevice))
        {
            _lastDevice = device;
            _lastReportCount = -1;
            DeviceChanged?.Invoke(device);
        }

        long count = _connection.ReportCount;
        bool changed = count != _lastReportCount;
        _lastReportCount = count;

        // On envoie quand même une image par rendu pour les animations (lissage), l'état ne coûte qu'une copie.
        Frame?.Invoke(new FrameData(_connection.Latest, device, _connection.GetOutput(), changed));
    }

    private void RenderDemo()
    {
        if (!ReferenceEquals(_lastDevice, DemoDevice))
        {
            _lastDevice = DemoDevice;
            DeviceChanged?.Invoke(DemoDevice);
        }

        double t = _demoClock.Elapsed.TotalSeconds;
        DualSenseButtons[] cycle =
        [
            DualSenseButtons.Cross, DualSenseButtons.Circle, DualSenseButtons.Triangle, DualSenseButtons.Square,
            DualSenseButtons.DpadUp, DualSenseButtons.DpadRight, DualSenseButtons.DpadDown, DualSenseButtons.DpadLeft,
            DualSenseButtons.L1, DualSenseButtons.R1, DualSenseButtons.Create, DualSenseButtons.Options,
            DualSenseButtons.PS, DualSenseButtons.Mute, DualSenseButtons.Touchpad, DualSenseButtons.L3,
        ];
        var all = DemoAllButtons;
        var buttons = all ? cycle.Aggregate(DualSenseButtons.None, (a, b) => a | b) : cycle[(int)(t / 0.7) % cycle.Length];
        var state = new DualSenseState
        {
            LeftX = (byte)(128 + 110 * Math.Cos(t * 1.3)),
            LeftY = (byte)(128 + 110 * Math.Sin(t * 1.3)),
            RightX = (byte)(128 + 90 * Math.Sin(t * 0.9)),
            RightY = 128,
            L2 = (byte)(127 + 127 * Math.Sin(t * 1.7)),
            R2 = (byte)(127 + 127 * Math.Sin(t * 1.7 + 2)),
            Buttons = buttons,
            Touch0 = new TouchPoint(true, 1, (ushort)(960 + 800 * Math.Sin(t)), (ushort)(540 + 400 * Math.Cos(t * 1.4))),
            BatteryPercent = 75,
            Charging = ChargingState.Charging,
            IsFullReport = true,
            AccelY = 8192,
            AccelX = (short)(1500 * Math.Sin(t * 0.6)),
        };
        var output = DualSenseOutputState.Default;
        Frame?.Invoke(new FrameData(state, DemoDevice, output, true));
    }

    public void Dispose()
    {
        CompositionTarget.Rendering -= OnRendering;
        Output.Dispose();
        _connection.Dispose();
    }
}

public readonly record struct FrameData(
    DualSenseState State,
    DualSenseDeviceInfo? Device,
    DualSenseOutputState Output,
    bool HasNewReport)
{
    public bool IsConnected => Device != null;
}
