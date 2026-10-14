using System.Security.Principal;
using Nefarius.ViGEm.Client;
using Se7en.DualSense.Hid;
using Se7en.DualSense.Processing;

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
    private readonly object _snapshotLock = new();
    private DualSenseState _lastProcessed;
    private FeedbackSettings _feedback = new();
    private GameFeedback? _lastGame;
    private int _battery = 100;
    private bool _micMuted;
    private (byte R, byte G, byte B)? _lightbarOverride;
    private TriggerEffect? _leftPulse, _rightPulse;
    private Timer? _pulseTimer;
    private (byte Heavy, byte Light) _lastRumble;
    private byte _audioHeavy, _audioLight;
    private readonly KeyboardMouseEmitter _emitter = new();
    private volatile bool _emitActions;

    public OutputManager(DualSenseConnection connection, HidHideService hidHide, OutputMode initialMode)
    {
        _connection = connection;
        _hidHide = hidHide;
        _mode = initialMode;
        _hidHide.Log += msg => Log?.Invoke(msg);
    }

    public event Action<OutputStatus>? StatusChanged;
    public event Action<string>? Log;
    /// <summary>Action système déclenchée par la manette (thread d'E/S).</summary>
    public event Action<SystemAction>? SystemActionTriggered;

    /// <summary>LED joueur imposée (plusieurs manettes : 1, 2, 3, 4), sinon celle du profil.</summary>
    public int? PlayerNumberOverride
    {
        get => _playerOverride;
        set { _playerOverride = value; ComposeOutput(); }
    }
    private int? _playerOverride;

    /// <summary>Vibrations du jeu (après intensité) : servent à l'haptique HD et aux effets.</summary>
    public event Action<byte, byte>? GameRumbleChanged;

    /// <summary>Couleur imposée par un effet animé (arc-en-ciel, musique, écran…), null = couleur du profil.</summary>
    public void SetLightbarOverride((byte R, byte G, byte B)? color)
    {
        if (color == _lightbarOverride) return;
        _lightbarOverride = color;
        ComposeOutput();
    }

    /// <summary>Gâchette qui vibre brièvement (son fort pendant un tir).</summary>
    public void PulseTrigger(bool left, int milliseconds)
    {
        var effect = TriggerEffect.Vibration(0.55, 1, 0.05);
        if (left) _leftPulse = effect; else _rightPulse = effect;
        ComposeOutput();
        _pulseTimer?.Dispose();
        _pulseTimer = new Timer(_ => { _leftPulse = _rightPulse = null; ComposeOutput(); }, null, milliseconds, Timeout.Infinite);
    }

    /// <summary>LED du bouton micro : allumée quand le micro de Windows est coupé.</summary>
    public void SetMicrophoneMuted(bool muted)
    {
        if (muted == _micMuted) return;
        _micMuted = muted;
        ComposeOutput();
    }

    /// <summary>Vibrations générées depuis le son (0-255 par moteur), combinées à celles du jeu.</summary>
    public void SetAudioRumble(byte heavy, byte light)
    {
        if (heavy == _audioHeavy && light == _audioLight) return;
        _audioHeavy = heavy;
        _audioLight = light;
        ComposeOutput();
    }

    public OutputMode Mode => _mode;

    /// <summary>Traitement appliqué avant l'envoi au jeu (calibration, zones mortes, remapping…).</summary>
    public InputProcessor Processor { get; } = new();

    /// <summary>Assistant de calibration, alimenté par les rapports bruts.</summary>
    public CalibrationSampler Calibration { get; } = new();

    /// <summary>Dernier état après traitement (ce que reçoit le jeu).</summary>
    public DualSenseState LastProcessed
    {
        get { lock (_snapshotLock) return _lastProcessed; }
    }

    /// <summary>Applique les réglages de retour (vibrations, barre lumineuse, LED, gâchettes adaptatives).</summary>
    public void SetFeedback(FeedbackSettings feedback)
    {
        _feedback = feedback;
        ComposeOutput();
    }

    /// <summary>Niveau de batterie, pour la barre lumineuse en mode « batterie ».</summary>
    public void SetBatteryLevel(int percent)
    {
        if (Math.Abs(percent - _battery) < 5) return;
        _battery = percent;
        if (_feedback.Lightbar == LightbarMode.Battery)
            ComposeOutput();
    }

    /// <summary>Test des vibrations depuis l'interface (0-1 par moteur), sans passer par un jeu.</summary>
    public void TestRumble(double heavy, double light)
    {
        _lastGame = new GameFeedback((byte)(heavy * 255), (byte)(light * 255), _lastGame?.LightbarRed, _lastGame?.LightbarGreen, _lastGame?.LightbarBlue);
        ComposeOutput();
    }

    /// <summary>Calcule l'état de sortie complet à partir du profil et du dernier retour du jeu.</summary>
    private void ComposeOutput()
    {
        var f = _feedback;
        var game = _lastGame;
        double k = Math.Clamp(f.RumbleIntensity, 0, 2);

        byte r, g, b;
        switch (f.Lightbar)
        {
            case LightbarMode.FromGame when game is { HasLightbar: true } && _mode == OutputMode.DualShock4:
                (r, g, b) = (game.Value.LightbarRed!.Value, game.Value.LightbarGreen!.Value, game.Value.LightbarBlue!.Value);
                break;
            case LightbarMode.Battery:
                (r, g, b) = _battery > 50 ? ((byte)0x17, (byte)0xC0, (byte)0x6B) : _battery > 20 ? ((byte)0xF0, (byte)0x9A, (byte)0x1C) : ((byte)0xE8, (byte)0x24, (byte)0x2B);
                break;
            case LightbarMode.Off:
                (r, g, b) = (0, 0, 0);
                break;
            default:
                (r, g, b) = (f.Red, f.Green, f.Blue);
                break;
        }
        if (_lightbarOverride is { } o && f.Lightbar is LightbarMode.Rainbow or LightbarMode.Breathing or LightbarMode.Music or LightbarMode.Screen)
            (r, g, b) = o;
        double br = Math.Clamp(f.Brightness, 0, 1);
        byte heavy = (byte)Math.Clamp((game?.HeavyMotor ?? 0) * k, 0, 255), light = (byte)Math.Clamp((game?.LightMotor ?? 0) * k, 0, 255);
        if ((heavy, light) != _lastRumble)
        {
            _lastRumble = (heavy, light);
            GameRumbleChanged?.Invoke(heavy, light);
        }

        var output = new DualSenseOutputState
        {
            HeavyMotor = Math.Max(heavy, _audioHeavy),
            LightMotor = Math.Max(light, _audioLight),
            AudioHaptics = f.AudioRumble == AudioRumbleMode.Haptics,
            MuteLed = _micMuted,
            LightbarRed = (byte)(r * br),
            LightbarGreen = (byte)(g * br),
            LightbarBlue = (byte)(b * br),
            PlayerLeds = (_playerOverride ?? f.PlayerNumber) switch { 2 => 0x0A, 3 => 0x15, 4 => 0x1B, _ => 0x04 },
            LeftTrigger = _leftPulse ?? f.LeftTrigger.ToEffect(),
            RightTrigger = _rightPulse ?? f.RightTrigger.ToEffect(),
        };
        _connection.SetOutput(output);
    }

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
            _hidHideReady = _hidHide.EnsureSessionReady();

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

    /// <summary>
    /// Débranche puis rebranche la manette virtuelle, comme un câble qu'on remet : un jeu qui l'a perdue
    /// (retour après Alt + Tab) la redécouvre. Faux s'il n'y a pas de manette virtuelle (mode natif, clavier / souris).
    /// </summary>
    public bool Replug()
    {
        if (_pad == null || _disposed) return false;
        _ = Task.Run(async () =>
        {
            await _applyGate.WaitAsync();
            try
            {
                if (_disposed) return;
                DisposePad();
            }
            finally
            {
                _applyGate.Release();
            }
            // Laisse Windows et le jeu voir le débranchement avant le rebranchement
            await Task.Delay(300);
            await ApplyAsync();
        });
        return true;
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

            if (mode == OutputMode.KeyboardMouse && device != null)
            {
                DisposePad();
                _connection.OutputEnabled = true;
                bool hidden = HideDevice(device);
                message = hidden
                    ? "La manette pilote le clavier et la souris ; les jeux ne la voient plus."
                    : "La manette pilote le clavier et la souris. Sans HidHide, les jeux la voient aussi.";
            }
            else if (mode.IsVirtual() && device != null && _client != null)
            {
                if (_pad?.Mode != mode)
                {
                    DisposePad();
                    _pad = CreatePad(mode);
                    _lastGame = null;
                    ComposeOutput();
                }
                _connection.OutputEnabled = true;

                bool hidden = HideDevice(device);

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

            _emitActions = mode != OutputMode.Native && device != null;
            if (!_emitActions) _emitter.ReleaseAll();
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

    private bool HideDevice(DualSenseDeviceInfo device)
    {
        string instance = HidHideService.ToInstanceId(device.DevicePath);
        if (_hiddenInstance != null && !string.Equals(_hiddenInstance, instance, StringComparison.OrdinalIgnoreCase))
            UnhideCurrent();
        return _hidHideReady && (_hiddenInstance != null || TryHide(instance));
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
        _lastGame = null;
        ComposeOutput();
    }

    /// <summary>Thread d'E/S : transmet l'état au jeu.</summary>
    private void OnState(in DualSenseState state)
    {
        Calibration.Add(state);
        var actions = default(ActionFrame);
        Processor.Process(state, out var processed, ref actions);
        lock (_snapshotLock) _lastProcessed = processed;

        if (_emitActions)
            _emitter.Apply(actions);
        if (actions.SystemTriggered != 0 && SystemActionTriggered is { } handler)
        {
            for (int i = 0; i < 64; i++)
                if ((actions.SystemTriggered & (1UL << i)) != 0)
                    handler((SystemAction)i);
        }

        var pad = _pad;
        if (pad == null) return;
        try
        {
            pad.Submit(processed);
        }
        catch
        {
            // Manette virtuelle en cours de remplacement : rapport ignoré
        }
    }

    /// <summary>Vibrations et barre lumineuse demandées par le jeu → vraie DualSense.</summary>
    private void OnFeedback(GameFeedback f)
    {
        // Un retour sans couleur (Xbox) conserve la dernière couleur reçue du jeu
        if (!f.HasLightbar && _lastGame is { HasLightbar: true } prev)
            f = f with { LightbarRed = prev.LightbarRed, LightbarGreen = prev.LightbarGreen, LightbarBlue = prev.LightbarBlue };
        _lastGame = f;
        ComposeOutput();
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
        _emitActions = false;
        try { _emitter.ReleaseAll(); } catch { /* fermeture */ }
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
