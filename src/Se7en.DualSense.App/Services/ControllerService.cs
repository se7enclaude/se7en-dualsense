using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
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

/// <summary>Une manette branchée : sa connexion, sa sortie vers les jeux, son suivi (batterie, usure).</summary>
public sealed class ControllerSlot : IDisposable
{
    public ControllerSlot(int index, HidHideService hidHide, OutputMode mode)
    {
        Index = index;
        Output = new OutputManager(Connection, hidHide, mode);
    }

    public int Index { get; set; }
    public DualSenseConnection Connection { get; } = new();
    public OutputManager Output { get; }
    public DualSenseDeviceInfo? Device => Connection.Device;

    /// <summary>Usure : décalage brut au repos (sans calibration), pour l'historique.</summary>
    public DriftDetector RawDrift { get; } = new();

    internal DualSenseDeviceInfo? LastDevice;
    internal long LastReportCount = -1;
    internal int BatteryAlertLevel = 100;
    internal int LastDriftEvaluation;
    internal int LastRecenterCount;
    internal DateTime DisconnectedSince = DateTime.MaxValue;
    internal string? AppliedProfileId;

    /// <summary>Profil propre à cette manette (Réglages › Manettes), sinon elle suit le profil actif.</summary>
    public Profile? OwnProfile { get; internal set; }

    public string Label => $"Manette {Index + 1}";

    public void Dispose()
    {
        Output.Dispose();
        Connection.Dispose();
    }
}

/// <summary>
/// Pont entre les threads d'E/S des manettes et l'interface, et chef d'orchestre des profils.
/// Le thread HID n'est jamais bloqué par l'UI : l'interface lit un instantané à chaque image.
/// Plusieurs manettes : une « place » par manette ; l'interface affiche la manette sélectionnée.
/// </summary>
public sealed class ControllerService : IDisposable
{
    private readonly List<ControllerSlot> _slots = new();
    private ControllerSlot _selected;
    private ProcessWatcher? _watcher;
    private GameFocusWatcher? _focus;
    private bool _nativeFocusHinted;
    private string? _gameExe;
    private Timer? _slotTimer;
    private readonly DispatcherTimer _micTimer = new() { Interval = TimeSpan.FromMilliseconds(700) };
    private readonly AudioEngine _audio = new();
    private readonly DispatcherTimer _effects = new() { Interval = TimeSpan.FromMilliseconds(33) };
    private readonly System.Diagnostics.Stopwatch _effectClock = System.Diagnostics.Stopwatch.StartNew();
    private bool _screenSampling;
    private bool _actionsSuppressed;
    private bool? _micMuted;
    private string? _profileBeforeDesktop;

    public ControllerService()
    {
        HidHide = new HidHideService(AppSettings.Directory);
        Profiles = new ProfileStore(AppSettings.Directory);
        ActiveProfile = Profiles.Find(AppSettings.Current.SelectedProfileId) ?? Profiles.Default;
        _selected = AddSlot(start: false);
        _audio.Rumble += (heavy, light) =>
        {
            foreach (var slot in Slots) slot.Output.SetAudioRumble(heavy, light);
        };
        _audio.Transient += OnAudioTransient;
        _effects.Tick += (_, _) => StepLightbarEffects();
        ApplyProfileSettings(ActiveProfile);
    }

    private static void OnUi(Action a) => Application.Current?.Dispatcher.BeginInvoke(a);

    // ───────────── Accès ─────────────

    public HidHideService HidHide { get; }
    public ProfileStore Profiles { get; }

    /// <summary>Toutes les manettes (au moins une place, même sans manette branchée).</summary>
    public IReadOnlyList<ControllerSlot> Slots
    {
        get { lock (_slots) return _slots.ToList(); }
    }

    public ControllerSlot Selected => _selected;

    /// <summary>Sortie de la manette sélectionnée.</summary>
    public OutputManager Output => _selected.Output;
    public DualSenseConnection Connection => _selected.Connection;
    public DualSenseDeviceInfo? Device => _selected.Connection.Device;

    /// <summary>Profil appliqué en ce moment (celui d'un jeu détecté, sinon celui choisi).</summary>
    public Profile ActiveProfile { get; private set; }

    /// <summary>Nom de l'exécutable du jeu qui a activé le profil, sinon null.</summary>
    public string? ActiveGame => _gameExe;

    /// <summary>État du micro de Windows (null : aucun micro).</summary>
    public bool? MicrophoneMuted => _micMuted;

    // ───────────── Événements (thread UI) ─────────────

    public event Action<FrameData>? Frame;
    public event Action<DualSenseDeviceInfo?>? DeviceChanged;
    /// <summary>Une manette apparaît ou disparaît, ou la sélection change.</summary>
    public event Action? ControllersChanged;
    public event Action<OutputStatus>? OutputStatusChanged;
    /// <summary>Un autre profil devient actif (choix manuel ou jeu détecté) : les pages se reconstruisent.</summary>
    public event Action<Profile>? ActiveProfileChanged;
    /// <summary>Le profil actif a été modifié (réglage enregistré) : simple rafraîchissement.</summary>
    public event Action<Profile>? ProfileUpdated;
    public event Action<string>? Log;
    /// <summary>Notification courte à afficher (titre, détail, type).</summary>
    public event Action<string, string, NotifyKind>? Notify;
    /// <summary>Raccourci PS + △ : afficher la fenêtre.</summary>
    public event Action? ShowAppRequested;
    /// <summary>Raccourci PS + pavé : afficher / masquer l'overlay.</summary>
    public event Action? OverlayToggleRequested;
    public event Action<bool?>? MicrophoneChanged;
    /// <summary>Raccourci PS + R3 : lanceur de jeux.</summary>
    public event Action? LauncherRequested;
    /// <summary>Raccourci PS + L3 : clavier virtuel.</summary>
    public event Action? KeyboardRequested;

    /// <summary>Moteur audio (vibrations depuis le son, haptique HD, son vers la manette).</summary>
    public AudioEngine Audio => _audio;

    public void Start()
    {
        ApplyGlobalOptions();
        _selected.Connection.Start();
        if (!IsDemo)
        {
            _selected.Output.Start();
            _watcher = new ProcessWatcher(Profiles);
            _watcher.GameChanged += (profile, exe) => OnUi(() => OnGameChanged(profile, exe));
            _focus = new GameFocusWatcher(Profiles);
            _focus.GameRefocused += OnGameRefocused;
            // Nouvelle manette branchée : une place de plus (vérification toutes les 2 s)
            _slotTimer = new Timer(_ => CheckForNewControllers(), null, 2000, 2000);
            _micTimer.Tick += (_, _) => PollMicrophone();
            _micTimer.Start();
            PollMicrophone();
        }
        CompositionTarget.Rendering += OnRendering;
    }

    // ───────────── Plusieurs manettes ─────────────

    private ControllerSlot AddSlot(bool start)
    {
        ControllerSlot slot;
        lock (_slots)
        {
            slot = new ControllerSlot(_slots.Count, HidHide, ActiveProfile.Mode);
            _slots.Add(slot);
        }
        slot.Connection.Log += msg => OnUi(() => Log?.Invoke(msg));
        slot.Output.Log += msg => OnUi(() => Log?.Invoke(msg));
        slot.Output.StatusChanged += status => OnUi(() => { if (slot == _selected) OutputStatusChanged?.Invoke(status); });
        slot.Output.SystemActionTriggered += action => OnUi(() => OnSystemAction(slot, action));
        slot.Output.GameRumbleChanged += (heavy, light) => _audio.SetGameRumble(heavy, light);
        slot.Output.Processor.Settings = ActiveProfile.Processing;
        slot.Output.SetFeedback(ActiveProfile.Feedback);
        if (start)
        {
            ApplyGlobalOptions();
            slot.Connection.Start();
            slot.Output.Start();
            UpdatePlayerLeds();
        }
        return slot;
    }

    private void CheckForNewControllers()
    {
        try
        {
            var slots = Slots;
            if (slots.All(s => s.Device != null) && DualSenseConnection.CountUnclaimed() > 0)
                OnUi(() =>
                {
                    if (Slots.All(s => s.Device != null) && Slots.Count < 4)
                    {
                        AddSlot(start: true);
                        ControllersChanged?.Invoke();
                    }
                });
        }
        catch { /* énumération momentanément indisponible */ }
    }

    public void SelectSlot(ControllerSlot slot)
    {
        if (slot == _selected || !Slots.Contains(slot)) return;
        _selected = slot;
        _selected.LastDevice = null; // déclenche DeviceChanged à la prochaine image
        _selected.LastReportCount = -1;
        OutputStatusChanged?.Invoke(_selected.Output.Status);
        ControllersChanged?.Invoke();
    }

    private void RemoveSlot(ControllerSlot slot)
    {
        lock (_slots)
        {
            if (slot.Index == 0 || !_slots.Remove(slot)) return;
            for (int i = 0; i < _slots.Count; i++) _slots[i].Index = i;
        }
        if (_selected == slot) _selected = _slots[0];
        slot.Dispose();
        UpdatePlayerLeds();
        ControllersChanged?.Invoke();
    }

    private void UpdatePlayerLeds()
    {
        var slots = Slots;
        foreach (var s in slots)
            s.Output.PlayerNumberOverride = slots.Count > 1 ? s.Index + 1 : null;
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
            ProfileUpdated?.Invoke(profile);
        }
    }

    /// <summary>Change le mode de sortie du profil actif.</summary>
    public void SetOutputMode(OutputMode mode) => UpdateProfile(ActiveProfile with { Mode = mode });

    /// <summary>Aperçu immédiat d'un réglage (curseur en cours de déplacement), sur toutes les manettes.</summary>
    public void PreviewProcessing(ProcessingSettings settings)
    {
        foreach (var s in Slots.Where(s => s.OwnProfile == null)) s.Output.Processor.Settings = settings;
    }

    public void PreviewFeedback(FeedbackSettings feedback)
    {
        foreach (var s in Slots.Where(s => s.OwnProfile == null)) s.Output.SetFeedback(feedback);
        ConfigureAudio(feedback);
        UpdateEffectsTimer();
    }

    /// <summary>Profil effectivement utilisé par une manette.</summary>
    public Profile ProfileFor(ControllerSlot slot) => slot.OwnProfile ?? ActiveProfile;

    /// <summary>Attribue un profil fixe à une manette (null = suivre le profil actif), mémorisé par manette.</summary>
    public void AssignProfile(ControllerSlot slot, Profile? profile)
    {
        var id = slot.Device?.Identity;
        if (id != null)
        {
            if (profile == null) AppSettings.Current.ControllerProfiles.Remove(id);
            else AppSettings.Current.ControllerProfiles[id] = profile.Id;
            AppSettings.Save();
        }
        slot.OwnProfile = profile;
        ApplySlotProfile(slot);
        ControllersChanged?.Invoke();
    }

    private void ApplySlotProfile(ControllerSlot slot)
    {
        var p = ProfileFor(slot);
        slot.AppliedProfileId = p.Id;
        slot.Output.Processor.Settings = p.Processing;
        slot.Output.SetFeedback(p.Feedback);
        slot.Output.SetMode(p.Mode);
    }

    private void ConfigureAudio(FeedbackSettings f)
    {
        if (IsDemo) return;
        var s = AppSettings.Current;
        _audio.Configure(new AudioEngineConfig
        {
            Rumble = f.AudioRumble,
            Intensity = f.AudioRumble == AudioRumbleMode.Haptics ? f.AudioRumbleIntensity : f.AudioRumbleIntensity,
            Levels = Slots.Any(x => ProfileFor(x).Feedback.Lightbar == LightbarMode.Music) || f.Lightbar == LightbarMode.Music,
            Transients = f.AudioTriggers,
            Mirror = s.MirrorSoundToController,
            MirrorVolume = s.MirrorVolume,
        });
    }

    /// <summary>Réglages audio de l'application modifiés (copie du son vers la manette).</summary>
    public void RefreshAudio() => ConfigureAudio(ActiveProfile.Feedback);

    /// <summary>Clavier virtuel / lanceur ouverts : la manette ne pilote plus clavier et souris.</summary>
    public void SetActionsSuppressed(bool suppressed)
    {
        _actionsSuppressed = suppressed;
        ApplyGlobalOptions();
    }

    // ───────────── Effets de barre lumineuse, gâchettes sonores ─────────────

    private void UpdateEffectsTimer()
    {
        bool animated = Slots.Any(s => LightbarEffects.IsAnimated(ProfileFor(s).Feedback.Lightbar));
        bool screen = Slots.Any(s => ProfileFor(s).Feedback.Lightbar == LightbarMode.Screen);
        if (screen != _screenSampling)
        {
            _screenSampling = screen;
            if (screen) ScreenSampler.Start(); else ScreenSampler.Stop();
        }
        if (animated && !_effects.IsEnabled) _effects.Start();
        if (!animated && _effects.IsEnabled)
        {
            _effects.Stop();
            foreach (var s in Slots) s.Output.SetLightbarOverride(null);
        }
    }

    private void StepLightbarEffects()
    {
        double t = _effectClock.Elapsed.TotalSeconds;
        foreach (var s in Slots)
        {
            var f = ProfileFor(s).Feedback;
            s.Output.SetLightbarOverride(LightbarEffects.IsAnimated(f.Lightbar) ? LightbarEffects.Compute(f, t, _audio.Level) : null);
        }
    }

    /// <summary>Son fort (tir, explosion) : la gâchette enfoncée vibre brièvement.</summary>
    private void OnAudioTransient()
    {
        foreach (var s in Slots)
        {
            if (!ProfileFor(s).Feedback.AudioTriggers || s.Device == null) continue;
            var st = s.Connection.Latest;
            if (st.R2 > 40) s.Output.PulseTrigger(false, 90);
            if (st.L2 > 40) s.Output.PulseTrigger(true, 90);
        }
    }

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

    /// <summary>Retour dans le jeu (Alt + Tab) : la manette virtuelle est rebranchée pour que le jeu la retrouve.</summary>
    private void OnGameRefocused(string exe)
    {
        if (!AppSettings.Current.ReconnectOnFocus) return;
        int replugged = Slots.Count(s => s.Device != null && s.Output.Replug());
        if (replugged > 0)
        {
            Log?.Invoke($"Retour dans {exe} : manette rebranchée.");
            return;
        }
        if (!_nativeFocusHinted && Slots.Any(s => s.Device != null && s.Output.Mode == OutputMode.Native))
        {
            _nativeFocusHinted = true;
            Notify?.Invoke("Reconnexion après Alt + Tab",
                "En mode DualSense native, le jeu lit la manette lui-même : se7en ne peut pas la rebrancher. Passez en DualShock 4 ou Xbox si le jeu la perd.",
                NotifyKind.Info);
        }
    }

    private void Activate(Profile profile, string? exe)
    {
        _gameExe = exe;
        ActiveProfile = profile;
        ApplyProfileSettings(profile);
        ActiveProfileChanged?.Invoke(profile);
    }

    private void ApplyProfileSettings(Profile profile)
    {
        foreach (var s in Slots)
        {
            // Une manette avec son propre profil : on relit sa dernière version
            if (s.OwnProfile != null)
                s.OwnProfile = Profiles.Find(s.OwnProfile.Id) ?? s.OwnProfile;
            ApplySlotProfile(s);
        }
        ConfigureAudio(profile.Feedback);
        UpdateEffectsTimer();
    }

    /// <summary>Options de l'application (raccourcis PS, bouton micro) appliquées à toutes les manettes.</summary>
    public void ApplyGlobalOptions()
    {
        var options = new GlobalInputOptions
        {
            PsShortcuts = AppSettings.Current.PsShortcuts,
            MuteControlsMicrophone = AppSettings.Current.MuteControlsMicrophone,
            AutoRecenter = AppSettings.Current.AutoRecenter,
            SuppressActions = _actionsSuppressed,
        };
        foreach (var s in Slots) s.Output.Processor.Options = options;
    }

    private void CycleProfile(int direction)
    {
        var all = Profiles.All;
        if (all.Count < 2) return;
        int i = all.ToList().FindIndex(p => p.Id == ActiveProfile.Id);
        var next = all[(i + direction + all.Count) % all.Count];
        _gameExe = null;
        SelectProfile(next);
        Notify?.Invoke($"Profil « {next.Name} »", next.Mode.Label(), NotifyKind.Info);
    }

    /// <summary>Mode bureau : la manette devient clavier + souris, puis retour au profil précédent.</summary>
    public void ToggleDesktopMode()
    {
        if (ActiveProfile.Id == Profile.DesktopId)
        {
            var back = Profiles.Find(_profileBeforeDesktop) ?? Profiles.Default;
            _gameExe = null;
            SelectProfile(back);
            Notify?.Invoke("Mode bureau désactivé", $"Retour au profil « {back.Name} »", NotifyKind.Info);
            return;
        }
        var desktop = Profiles.Find(Profile.DesktopId);
        if (desktop == null)
        {
            desktop = Profile.CreateDesktop();
            Profiles.Save(desktop);
        }
        _profileBeforeDesktop = ActiveProfile.Id;
        _gameExe = null;
        SelectProfile(desktop);
        Notify?.Invoke("Mode bureau", "Stick droit : souris · ✕ : clic · ○ : clic droit · PS + □ pour revenir", NotifyKind.Success);
    }

    // ───────────── Actions système (raccourcis PS, bouton micro) ─────────────

    private void OnSystemAction(ControllerSlot slot, SystemAction action)
    {
        const ushort VkVolumeMute = 0xAD, VkVolumeDown = 0xAE, VkVolumeUp = 0xAF, VkNext = 0xB0, VkPrevious = 0xB1, VkPlayPause = 0xB3;
        switch (action)
        {
            case SystemAction.ToggleMicrophone:
                var muted = MicrophoneService.Toggle();
                if (muted == null)
                {
                    Notify?.Invoke("Aucun micro", "Windows ne trouve pas de micro à couper.", NotifyKind.Warning);
                    break;
                }
                SetMicState(muted);
                Notify?.Invoke(muted.Value ? "Micro coupé" : "Micro activé", "Micro de Windows", muted.Value ? NotifyKind.Warning : NotifyKind.Success);
                break;
            case SystemAction.NextProfile: CycleProfile(+1); break;
            case SystemAction.PreviousProfile: CycleProfile(-1); break;
            case SystemAction.Screenshot:
                KeyboardMouseEmitter.Tap(0x5B, 0x2C); // Windows + Impr. écran : enregistré dans Images\Captures d'écran
                Notify?.Invoke("Capture d'écran", "Enregistrée dans Images › Captures d'écran", NotifyKind.Success);
                break;
            case SystemAction.ShowApp: ShowAppRequested?.Invoke(); break;
            case SystemAction.ToggleOverlay: OverlayToggleRequested?.Invoke(); break;
            case SystemAction.VolumeUp: KeyboardMouseEmitter.Tap(VkVolumeUp); break;
            case SystemAction.VolumeDown: KeyboardMouseEmitter.Tap(VkVolumeDown); break;
            case SystemAction.VolumeMute: KeyboardMouseEmitter.Tap(VkVolumeMute); break;
            case SystemAction.MediaPlayPause: KeyboardMouseEmitter.Tap(VkPlayPause); break;
            case SystemAction.MediaNext: KeyboardMouseEmitter.Tap(VkNext); break;
            case SystemAction.MediaPrevious: KeyboardMouseEmitter.Tap(VkPrevious); break;
            case SystemAction.ToggleDesktopMode: ToggleDesktopMode(); break;
            case SystemAction.PowerOff: PowerOff(slot); break;
            case SystemAction.OpenLauncher: LauncherRequested?.Invoke(); break;
            case SystemAction.ToggleKeyboard: KeyboardRequested?.Invoke(); break;
        }
    }

    /// <summary>Éteint une manette Bluetooth.</summary>
    public bool PowerOff(ControllerSlot? slot = null)
    {
        slot ??= _selected;
        if (slot.Device is not { } device) return false;
        if (device.Connection != ConnectionType.Bluetooth)
        {
            Notify?.Invoke("Extinction impossible en USB", "Débranchez le câble : la manette s'éteint d'elle-même en Bluetooth.", NotifyKind.Warning);
            return false;
        }
        bool ok = slot.Connection.PowerOff();
        Notify?.Invoke(ok ? $"{slot.Label} éteinte" : "Extinction impossible",
            ok ? "Appuyez sur PS pour la rallumer." : "Windows n'a pas pu couper la liaison Bluetooth.", ok ? NotifyKind.Success : NotifyKind.Warning);
        return ok;
    }

    private void PollMicrophone()
    {
        var muted = MicrophoneService.IsMuted();
        if (muted != _micMuted) SetMicState(muted);
    }

    private void SetMicState(bool? muted)
    {
        _micMuted = muted;
        foreach (var s in Slots) s.Output.SetMicrophoneMuted(muted == true && AppSettings.Current.MuteControlsMicrophone);
        MicrophoneChanged?.Invoke(muted);
    }

    // ───────────── Calibration (par manette) ─────────────

    private static void LoadCalibration(ControllerSlot slot)
    {
        var device = slot.Device;
        var all = AppSettings.Current.Calibrations;
        if ((device?.Identity is { } id && all.TryGetValue(id, out var c))
            || (device?.SerialNumber is { } serial && all.TryGetValue(serial, out c))
            || all.TryGetValue("*", out c))
            slot.Output.Processor.SetCalibration(c.Left, c.Right);
        else
            slot.Output.Processor.SetCalibration(StickCalibration.Identity, StickCalibration.Identity);
    }

    public void SaveCalibration(StickCalibration left, StickCalibration right)
    {
        Output.Processor.SetCalibration(left, right);
        AppSettings.Current.Calibrations[Device?.Identity ?? "*"] = new CalibrationPair(left, right);
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

        foreach (var slot in Slots)
            TrackSlot(slot);

        var selected = _selected;
        long count = selected.Connection.ReportCount;
        bool changed = count != selected.LastReportCount;
        selected.LastReportCount = count;
        Frame?.Invoke(new FrameData(selected.Connection.Latest, selected.Output.LastProcessed, selected.Device, selected.Connection.GetOutput(), changed));
    }

    /// <summary>Connexion / déconnexion, calibration, batterie faible, usure : pour chaque manette.</summary>
    private void TrackSlot(ControllerSlot slot)
    {
        var device = slot.Device;
        if (!ReferenceEquals(device, slot.LastDevice))
        {
            bool wasConnected = slot.LastDevice != null;
            slot.LastDevice = device;
            LoadCalibration(slot);
            slot.LastRecenterCount = slot.Output.Processor.AutoRecenterCount;
            // Profil attribué à cette manette (mémorisé par adresse)
            var own = device?.Identity is { } ident && AppSettings.Current.ControllerProfiles.TryGetValue(ident, out var pid) ? Profiles.Find(pid) : null;
            if (!ReferenceEquals(own, slot.OwnProfile) || own != null)
            {
                slot.OwnProfile = own;
                ApplySlotProfile(slot);
            }
            // Manette branchée / débranchée en USB : sa carte son apparaît ou disparaît
            if (!IsDemo) _ = Task.Delay(1500).ContinueWith(_ => _audio.Refresh());
            if (slot == _selected)
                DeviceChanged?.Invoke(device);
            string who = Slots.Count > 1 ? slot.Label : device?.DisplayName ?? "Manette";
            if (device != null)
            {
                slot.DisconnectedSince = DateTime.MaxValue;
                slot.BatteryAlertLevel = 100;
                Notify?.Invoke($"{who} connectée", $"{device.DisplayName} · {device.ConnectionLabel}", NotifyKind.Success);
            }
            else if (wasConnected)
            {
                slot.DisconnectedSince = DateTime.UtcNow;
                Notify?.Invoke($"{who} déconnectée", "Reconnexion automatique dès qu'elle réapparaît", NotifyKind.Warning);
            }
            ControllersChanged?.Invoke();
        }

        // Place supplémentaire vide depuis un moment : libérée
        if (device == null && slot.Index > 0 && DateTime.UtcNow - slot.DisconnectedSince > TimeSpan.FromSeconds(4))
        {
            RemoveSlot(slot);
            return;
        }
        if (device == null) return;

        var state = slot.Connection.Latest;
        if (!state.IsFullReport) return;
        slot.Output.SetBatteryLevel(state.BatteryPercent);
        CheckBattery(slot, state);
        if (device.Identity is { } bid)
            BatteryTracker.Record(bid, state.BatteryPercent, state.Charging != ChargingState.Discharging);

        // Recentrage automatique : la calibration corrigée est mémorisée (au plus une fois par minute)
        int recenters = slot.Output.Processor.AutoRecenterCount;
        if (recenters != slot.LastRecenterCount && DateTime.UtcNow - _lastRecenterSave > TimeSpan.FromSeconds(60))
        {
            slot.LastRecenterCount = recenters;
            _lastRecenterSave = DateTime.UtcNow;
            var proc = slot.Output.Processor;
            AppSettings.Current.Calibrations[device.Identity ?? "*"] = new CalibrationPair(proc.LeftCalibration, proc.RightCalibration);
            AppSettings.Save();
        }

        // Historique d'usure (décalage brut au repos)
        slot.RawDrift.Add(state, StickCalibration.Identity, StickCalibration.Identity);
        if (slot.RawDrift.Evaluations != slot.LastDriftEvaluation)
        {
            slot.LastDriftEvaluation = slot.RawDrift.Evaluations;
            if (device.Identity is { } id && (!double.IsNaN(slot.RawDrift.LeftRest) || !double.IsNaN(slot.RawDrift.RightRest)))
                DriftHistory.Record(id, slot.RawDrift.LeftRest, slot.RawDrift.RightRest);
        }
    }

    private void CheckBattery(ControllerSlot slot, in DualSenseState state)
    {
        if (!AppSettings.Current.BatteryAlerts) return;
        if (state.Charging != ChargingState.Discharging)
        {
            slot.BatteryAlertLevel = 100;
            return;
        }
        int level = state.BatteryPercent;
        int threshold = level <= 10 ? 10 : level <= 20 ? 20 : 100;
        if (threshold < slot.BatteryAlertLevel)
        {
            slot.BatteryAlertLevel = threshold;
            string who = Slots.Count > 1 ? slot.Label : "DualSense";
            Notify?.Invoke($"Batterie faible · {level} %", $"{who} : pensez à la recharger", NotifyKind.Warning);
            BatteryLow?.Invoke(slot, level);
        }
    }

    private DateTime _lastRecenterSave;

    /// <summary>Autonomie restante estimée de la manette sélectionnée.</summary>
    public TimeSpan? BatteryRemaining()
    {
        var st = Connection.Latest;
        return Device?.Identity is { } id && st.IsFullReport ? BatteryTracker.Remaining(id, st.BatteryPercent) : null;
    }

    /// <summary>Batterie sous 20 % puis 10 % (pour la notification Windows).</summary>
    public event Action<ControllerSlot, int>? BatteryLow;

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
    private DualSenseDeviceInfo? _demoLast;

    private void RenderDemo()
    {
        if (!ReferenceEquals(_demoLast, DemoDevice))
        {
            _demoLast = DemoDevice;
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
            GyroY = (short)(150 * Math.Sin(t * 0.8)),
        };
        Output.Calibration.Add(state);
        Output.Processor.Process(state, out var processed);
        Frame?.Invoke(new FrameData(state, processed, DemoDevice, Connection.GetOutput(), true));
    }

    public void Dispose()
    {
        CompositionTarget.Rendering -= OnRendering;
        _micTimer.Stop();
        _effects.Stop();
        if (_screenSampling) ScreenSampler.Stop();
        _slotTimer?.Dispose();
        _watcher?.Dispose();
        _focus?.Dispose();
        _audio.Dispose();
        DriftHistory.Flush();
        foreach (var s in Slots) s.Dispose();
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
