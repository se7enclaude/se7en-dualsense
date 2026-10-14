using System.Windows;
using System.Windows.Media;
using Se7en.DualSense.Hid;
using Se7en.DualSense.Output;
using Se7en.DualSense.Processing;

namespace Se7en.DualSense.App.Services;

public enum NotifyKind
{
    Info,
    Success,
    Warning,
}

/// <summary>
/// Pont entre le thread d'E/S de la manette et l'interface, et chef d'orchestre des profils.
/// Le thread HID n'est jamais bloqué par l'UI : l'interface lit un instantané à chaque image.
/// </summary>
public sealed class ControllerService : IDisposable
{
    private readonly DualSenseConnection _connection = new();
    private DualSenseDeviceInfo? _lastDevice;
    private long _lastReportCount = -1;
    private ProcessWatcher? _watcher;
    private string? _gameExe;

    public ControllerService()
    {
        _connection.Log += msg => OnUi(() => Log?.Invoke(msg));
        HidHide = new HidHideService(AppSettings.Directory);
        Profiles = new ProfileStore(AppSettings.Directory);
        ActiveProfile = Profiles.Find(AppSettings.Current.SelectedProfileId) ?? Profiles.Default;

        Output = new OutputManager(_connection, HidHide, ActiveProfile.Mode);
        Output.Log += msg => OnUi(() => Log?.Invoke(msg));
        Output.StatusChanged += status => OnUi(() => OutputStatusChanged?.Invoke(status));
        ApplyProfileSettings(ActiveProfile);
    }

    private static void OnUi(Action a) => Application.Current?.Dispatcher.BeginInvoke(a);

    // ───────────── Accès ─────────────

    public HidHideService HidHide { get; }
    public OutputManager Output { get; }
    public ProfileStore Profiles { get; }
    public DualSenseConnection Connection => _connection;
    public DualSenseDeviceInfo? Device => _connection.Device;

    /// <summary>Profil appliqué en ce moment (celui d'un jeu détecté, sinon celui choisi).</summary>
    public Profile ActiveProfile { get; private set; }

    /// <summary>Nom de l'exécutable du jeu qui a activé le profil, sinon null.</summary>
    public string? ActiveGame => _gameExe;

    // ───────────── Événements (thread UI) ─────────────

    public event Action<FrameData>? Frame;
    public event Action<DualSenseDeviceInfo?>? DeviceChanged;
    public event Action<OutputStatus>? OutputStatusChanged;
    /// <summary>Un autre profil devient actif (choix manuel ou jeu détecté) : les pages se reconstruisent.</summary>
    public event Action<Profile>? ActiveProfileChanged;
    /// <summary>Le profil actif a été modifié (réglage enregistré) : simple rafraîchissement.</summary>
    public event Action<Profile>? ProfileUpdated;
    public event Action<string>? Log;
    /// <summary>Notification courte à afficher (titre, détail, type).</summary>
    public event Action<string, string, NotifyKind>? Notify;

    public void Start()
    {
        _connection.Start();
        if (!IsDemo)
        {
            Output.Start();
            _watcher = new ProcessWatcher(Profiles);
            _watcher.GameChanged += (profile, exe) => OnUi(() => OnGameChanged(profile, exe));
        }
        CompositionTarget.Rendering += OnRendering;
    }

    // ───────────── Profils ─────────────

    /// <summary>Choix manuel d'un profil (mémorisé). Un jeu associé reprend la main quand il est lancé.</summary>
    public void SelectProfile(Profile profile)
    {
        AppSettings.Current.SelectedProfileId = profile.Id;
        AppSettings.Save();
        if (_gameExe == null)
            Activate(profile, null);
    }

    /// <summary>Enregistre un profil modifié ; s'il est actif, ses réglages sont appliqués aussitôt.</summary>
    public void UpdateProfile(Profile profile)
    {
        Profiles.Save(profile);
        if (profile.Id == ActiveProfile.Id)
        {
            ActiveProfile = profile;
            ApplyProfileSettings(profile);
            Output.SetMode(profile.Mode);
            ProfileUpdated?.Invoke(profile);
        }
    }

    /// <summary>Change le mode de sortie du profil actif.</summary>
    public void SetOutputMode(OutputMode mode) => UpdateProfile(ActiveProfile with { Mode = mode });

    private void OnGameChanged(Profile? profile, string? exe)
    {
        if (profile != null)
        {
            Activate(profile, exe);
            Notify?.Invoke($"Profil « {profile.Name} » appliqué", $"{exe} détecté · {profile.Mode.Label()}", NotifyKind.Success);
        }
        else
        {
            var selected = Profiles.Find(AppSettings.Current.SelectedProfileId) ?? Profiles.Default;
            Activate(selected, null);
            Notify?.Invoke("Jeu fermé", $"Retour au profil « {selected.Name} »", NotifyKind.Info);
        }
    }

    private void Activate(Profile profile, string? exe)
    {
        _gameExe = exe;
        ActiveProfile = profile;
        ApplyProfileSettings(profile);
        Output.SetMode(profile.Mode);
        ActiveProfileChanged?.Invoke(profile);
    }

    private void ApplyProfileSettings(Profile profile)
    {
        Output.Processor.Settings = profile.Processing;
        Output.SetFeedback(profile.Feedback);
    }

    // ───────────── Calibration (par manette) ─────────────

    private static string CalibrationKey(DualSenseDeviceInfo? device) =>
        string.IsNullOrWhiteSpace(device?.SerialNumber) ? "*" : device!.SerialNumber!;

    private void LoadCalibration(DualSenseDeviceInfo? device)
    {
        var all = AppSettings.Current.Calibrations;
        if (all.TryGetValue(CalibrationKey(device), out var c) || all.TryGetValue("*", out c))
            Output.Processor.SetCalibration(c.Left, c.Right);
        else
            Output.Processor.SetCalibration(StickCalibration.Identity, StickCalibration.Identity);
    }

    public void SaveCalibration(StickCalibration left, StickCalibration right)
    {
        Output.Processor.SetCalibration(left, right);
        AppSettings.Current.Calibrations[CalibrationKey(Device)] = new CalibrationPair(left, right);
        AppSettings.Save();
    }

    public void ResetCalibration() => SaveCalibration(StickCalibration.Identity, StickCalibration.Identity);

    // ───────────── Image par image ─────────────

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
            bool wasConnected = _lastDevice != null;
            _lastDevice = device;
            _lastReportCount = -1;
            LoadCalibration(device);
            DeviceChanged?.Invoke(device);
            if (device != null)
                Notify?.Invoke($"{device.DisplayName} connectée", $"Connexion {device.ConnectionLabel}", NotifyKind.Success);
            else if (wasConnected)
                Notify?.Invoke("Manette déconnectée", "Reconnexion automatique dès qu'elle réapparaît", NotifyKind.Warning);
        }

        long count = _connection.ReportCount;
        bool changed = count != _lastReportCount;
        _lastReportCount = count;

        var state = _connection.Latest;
        if (device != null && state.IsFullReport)
            Output.SetBatteryLevel(state.BatteryPercent);

        Frame?.Invoke(new FrameData(state, Output.LastProcessed, device, _connection.GetOutput(), changed));
    }

    // ───────────── Mode démo ─────────────

    /// <summary>
    /// Mode démo (option --demo) : simule une manette pour prévisualiser l'interface.
    /// Volontairement explicite pour ne jamais masquer une vraie manette par erreur.
    /// </summary>
    public static bool IsDemo { get; set; }

    /// <summary>Option --demo-all : toutes les touches simulées enfoncées en même temps.</summary>
    public static bool DemoAllButtons { get; set; }

    private static readonly DualSenseDeviceInfo DemoDevice = new("demo", ConnectionType.Usb, false, "DEMO", 64, 48);
    private readonly System.Diagnostics.Stopwatch _demoClock = System.Diagnostics.Stopwatch.StartNew();

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
        var buttons = DemoAllButtons ? cycle.Aggregate(DualSenseButtons.None, (a, b) => a | b) : cycle[(int)(t / 0.7) % cycle.Length];
        var state = new DualSenseState
        {
            // Léger drift simulé sur le stick gauche pour montrer la correction
            LeftX = (byte)(134 + 100 * Math.Cos(t * 1.3) * Math.Max(0, Math.Sin(t * 0.4))),
            LeftY = (byte)(124 + 100 * Math.Sin(t * 1.3) * Math.Max(0, Math.Sin(t * 0.4))),
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
            GyroY = (short)(400 * Math.Sin(t * 0.8)),
        };
        Output.Calibration.Add(state);
        Output.Processor.Process(state, out var processed);
        Frame?.Invoke(new FrameData(state, processed, DemoDevice, _connection.GetOutput(), true));
    }

    public void Dispose()
    {
        CompositionTarget.Rendering -= OnRendering;
        _watcher?.Dispose();
        Output.Dispose();
        _connection.Dispose();
    }
}

/// <summary>Une image : état brut, état traité (envoyé au jeu), manette, retours.</summary>
public readonly record struct FrameData(
    DualSenseState State,
    DualSenseState Processed,
    DualSenseDeviceInfo? Device,
    DualSenseOutputState Output,
    bool HasNewReport)
{
    public bool IsConnected => Device != null;
}
