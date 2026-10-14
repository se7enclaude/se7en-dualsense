using System.Diagnostics;
using Se7en.DualSense.Hid;

namespace Se7en.DualSense.Processing;

/// <summary>
/// Applique le traitement complet à chaque rapport (thread d'E/S, sans allocation) :
/// échange des sticks, calibration, zones mortes, courbes, gâchettes, gyroscope, pavé tactile (zones, gestes, souris),
/// raccourcis PS, couche secondaire, remapping, actions clavier / souris / système, macros, turbo.
/// Les réglages sont remplacés atomiquement (référence immuable), sans verrou.
/// </summary>
public sealed class InputProcessor
{
    // ~ 1024 unités brutes de gyroscope par 64 °/s sur la DualSense
    private const double GyroUnitsPerDegree = 16.0;
    // Vitesse du curseur au stick à fond (pixels / s) pour une vitesse de 1
    private const double StickMousePixelsPerSecond = 1400;
    private const DualSenseButtons ZoneBits = DualSenseButtons.TouchZone1 | DualSenseButtons.TouchZone2 | DualSenseButtons.TouchZone3 | DualSenseButtons.TouchZone4;

    private volatile ProcessingSettings _settings = ProcessingSettings.Default;
    private volatile Dictionary<string, Macro> _macros = new();
    private volatile GlobalInputOptions _options = GlobalInputOptions.Default;
    private volatile StickCalibration _leftCal = StickCalibration.Identity;
    private volatile StickCalibration _rightCal = StickCalibration.Identity;
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private double _lastTime;

    private DualSenseButtons _prevButtons;
    private readonly double[] _pressTime = new double[32];
    private readonly TouchpadTracker _touch = new();
    private readonly PsShortcuts _ps = new();
    private readonly MacroRun[] _runs = new MacroRun[8];

    public ProcessingSettings Settings
    {
        get => _settings;
        set
        {
            var s = value ?? ProcessingSettings.Default;
            _macros = s.Macros.GroupBy(m => m.Id).ToDictionary(g => g.Key, g => g.First());
            _settings = s;
        }
    }

    public GlobalInputOptions Options
    {
        get => _options;
        set => _options = value ?? GlobalInputOptions.Default;
    }

    public void SetCalibration(StickCalibration left, StickCalibration right)
    {
        _leftCal = left ?? StickCalibration.Identity;
        _rightCal = right ?? StickCalibration.Identity;
    }

    public StickCalibration LeftCalibration => _leftCal;
    public StickCalibration RightCalibration => _rightCal;

    /// <summary>Traitement sans sortie clavier / souris (tests, aperçus).</summary>
    public void Process(in DualSenseState raw, out DualSenseState output)
    {
        var actions = default(ActionFrame);
        Process(raw, out output, ref actions);
    }

    public void Process(in DualSenseState raw, out DualSenseState output, ref ActionFrame actions)
    {
        double now = _clock.Elapsed.TotalSeconds;
        double dt = Math.Clamp(now - _lastTime, 0, 0.05);
        _lastTime = now;
        Process(raw, out output, ref actions, now, dt);
    }

    /// <summary>Variante à horloge explicite (secondes), pour les tests.</summary>
    public void Process(in DualSenseState raw, out DualSenseState output, ref ActionFrame actions, double now, double dt)
    {
        var s = _settings;
        var opt = _options;
        output = raw;

        // ── Sticks ──
        byte lx = raw.LeftX, ly = raw.LeftY, rx = raw.RightX, ry = raw.RightY;
        if (s.SwapSticks)
            (lx, ly, rx, ry) = (rx, ry, lx, ly);

        var (plx, ply) = StickMath.ProcessStick(StickMath.Normalize(lx), StickMath.Normalize(ly), s.SwapSticks ? _rightCal : _leftCal, s.LeftStick);
        var (prx, pry) = StickMath.ProcessStick(StickMath.Normalize(rx), StickMath.Normalize(ry), s.SwapSticks ? _leftCal : _rightCal, s.RightStick);

        // ── Gâchettes ──
        output.L2 = StickMath.ProcessTrigger(raw.L2, s.L2);
        output.R2 = StickMath.ProcessTrigger(raw.R2, s.R2);

        // ── Gyroscope → stick droit ──
        if (s.Gyro.Enabled && raw.IsFullReport && GyroActive(s.Gyro.Activation, raw))
        {
            double yaw = -raw.GyroY / GyroUnitsPerDegree;   // °/s, rotation horizontale
            double pitch = -raw.GyroX / GyroUnitsPerDegree; // °/s, rotation verticale
            double dz = s.Gyro.Deadzone;
            yaw = Math.Abs(yaw) < dz ? 0 : yaw;
            pitch = Math.Abs(pitch) < dz ? 0 : pitch;
            // 360 °/s ≈ stick à fond
            double gx = yaw / 360.0 * 3 * s.Gyro.Sensitivity * (s.Gyro.InvertX ? -1 : 1);
            double gy = pitch / 360.0 * 3 * s.Gyro.Sensitivity * (s.Gyro.InvertY ? -1 : 1);
            prx = Math.Clamp(prx + gx, -1, 1);
            pry = Math.Clamp(pry + gy, -1, 1);
        }

        // ── Sticks souris / molette / déplacement ──
        ApplyStickRole(s.Mouse.LeftStick, ref plx, ref ply, s.Mouse, dt, ref actions);
        ApplyStickRole(s.Mouse.RightStick, ref prx, ref pry, s.Mouse, dt, ref actions);

        output.LeftX = StickMath.ToByte(plx);
        output.LeftY = StickMath.ToByte(ply);
        output.RightX = StickMath.ToByte(prx);
        output.RightY = StickMath.ToByte(pry);

        // ── Boutons physiques (les bits numériques L2/R2 suivent les gâchettes traitées) ──
        var buttons = (raw.Buttons & ~(DualSenseButtons.L2 | DualSenseButtons.R2))
                      | (output.L2 > 0 ? DualSenseButtons.L2 : 0)
                      | (output.R2 > 0 ? DualSenseButtons.R2 : 0);

        // ── Pavé tactile : zones, gestes, souris ──
        buttons = _touch.Process(raw, buttons, s.Touchpad, now, ref actions);
        if (s.Touchpad.Mode == TouchpadMode.Mouse)
        {
            output.Touch0 = default;
            output.Touch1 = default;
        }

        // ── Bouton micro → micro de Windows ──
        if (opt.MuteControlsMicrophone)
        {
            if ((buttons & DualSenseButtons.Mute) != 0 && (_prevButtons & DualSenseButtons.Mute) == 0)
                actions.SystemTriggered |= 1UL << (int)SystemAction.ToggleMicrophone;
        }

        // ── Raccourcis PS + bouton ──
        var physical = buttons;
        if (opt.PsShortcuts)
            buttons = _ps.Process(buttons, now, ref actions);

        var pressed = physical & ~_prevButtons;
        _prevButtons = physical;
        if (opt.MuteControlsMicrophone)
            buttons &= ~DualSenseButtons.Mute;

        // ── Couche secondaire ──
        bool layer = s.ShiftButton != DualSenseButtons.None && (buttons & s.ShiftButton) == s.ShiftButton;
        if (layer)
            buttons &= ~s.ShiftButton;

        // ── Remapping et actions ──
        var gamepad = DualSenseButtons.None;
        for (int bit = 0; bit < 31; bit++)
        {
            var flag = (DualSenseButtons)(1u << bit);
            if ((pressed & flag) != 0)
                _pressTime[bit] = now;
            if ((buttons & flag) == 0) continue;

            InputAction? action = null;
            DualSenseButtons? target = null;
            if (layer)
            {
                if (s.ShiftActions.TryGetValue(flag, out var a)) action = a;
                else if (s.ShiftRemap.TryGetValue(flag, out var r)) target = r;
            }
            if (action == null && target == null)
            {
                if (s.Actions.TryGetValue(flag, out var a)) action = a;
                else if (s.Remap.TryGetValue(flag, out var r)) target = r;
            }

            if (action != null)
                ApplyAction(action, flag, (pressed & flag) != 0, now - _pressTime[bit], dt, now, ref actions);
            else if (target != null)
                gamepad |= target.Value;
            else
                // Une zone du pavé sans affectation reste un clic de pavé
                gamepad |= (flag & ZoneBits) != 0 ? DualSenseButtons.Touchpad : flag;
        }

        // ── Macros en cours ──
        gamepad |= StepMacros(physical, now);

        // ── Turbo ──
        if (s.Turbo != DualSenseButtons.None && (gamepad & s.Turbo) != 0)
        {
            double period = 1.0 / Math.Clamp(s.TurboRate, 2, 30);
            bool on = (now % period) < period / 2;
            if (!on)
                gamepad &= ~s.Turbo;
        }

        output.Buttons = gamepad & ~DualSenseButtonGroups.Virtual;
    }

    private static bool GyroActive(GyroActivation a, in DualSenseState raw) => a switch
    {
        GyroActivation.WhileAiming => raw.L2 > 64,
        GyroActivation.WhileR1 => (raw.Buttons & DualSenseButtons.R1) != 0,
        _ => true,
    };

    private static void ApplyStickRole(StickRole role, ref double x, ref double y, MouseSettings m, double dt, ref ActionFrame actions)
    {
        switch (role)
        {
            case StickRole.Mouse:
                actions.MouseDx += x * StickMousePixelsPerSecond * m.Speed * dt;
                actions.MouseDy += y * StickMousePixelsPerSecond * m.Speed * dt;
                break;
            case StickRole.Scroll:
                actions.Wheel += -y * 12 * m.ScrollSpeed * dt;
                actions.HWheel += x * 12 * m.ScrollSpeed * dt;
                break;
            case StickRole.Movement:
                // Positions physiques W / A / S / D (Z / Q / S / D en AZERTY)
                if (y < -0.45) actions.Keys.Set(0x111);
                if (y > 0.45) actions.Keys.Set(0x11F);
                if (x < -0.45) actions.Keys.Set(0x11E);
                if (x > 0.45) actions.Keys.Set(0x120);
                break;
            default:
                return;
        }
        x = 0;
        y = 0;
    }

    private void ApplyAction(InputAction action, DualSenseButtons trigger, bool edge, double heldFor, double dt, double now, ref ActionFrame actions)
    {
        switch (action.Kind)
        {
            case ActionKind.Key:
                foreach (var k in action.Keys)
                    actions.Keys.Set(k);
                break;

            case ActionKind.Mouse:
                switch (action.Mouse)
                {
                    case MouseAction.LeftClick: actions.MouseButtons |= MouseButtonFlags.Left; break;
                    case MouseAction.RightClick: actions.MouseButtons |= MouseButtonFlags.Right; break;
                    case MouseAction.MiddleClick: actions.MouseButtons |= MouseButtonFlags.Middle; break;
                    case MouseAction.Back: actions.MouseButtons |= MouseButtonFlags.Back; break;
                    case MouseAction.Forward: actions.MouseButtons |= MouseButtonFlags.Forward; break;
                    case MouseAction.WheelUp:
                    case MouseAction.WheelDown:
                        // Un cran à l'appui, puis défilement continu si le bouton reste maintenu
                        double sign = action.Mouse == MouseAction.WheelUp ? 1 : -1;
                        if (edge) actions.Wheel += sign;
                        else if (heldFor > 0.35) actions.Wheel += sign * 10 * dt;
                        break;
                }
                break;

            case ActionKind.System:
                if (edge)
                    actions.SystemTriggered |= 1UL << (int)action.System;
                break;

            case ActionKind.Macro:
                if (edge && action.MacroId != null && _macros.TryGetValue(action.MacroId, out var macro))
                    StartOrToggleMacro(macro, trigger, now);
                break;
        }
    }

    // ───────────── Macros ─────────────

    private struct MacroRun
    {
        public Macro? Macro;
        public DualSenseButtons Trigger;
        public double Start;
    }

    private void StartOrToggleMacro(Macro macro, DualSenseButtons trigger, double now)
    {
        if (macro.Steps.Count == 0) return;
        for (int i = 0; i < _runs.Length; i++)
        {
            if (!ReferenceEquals(_runs[i].Macro, macro)) continue;
            // Mode bascule : le deuxième appui arrête ; sinon on relance depuis le début
            if (macro.Mode == MacroMode.Toggle)
                _runs[i] = default;
            else
                _runs[i].Start = now;
            return;
        }
        for (int i = 0; i < _runs.Length; i++)
        {
            if (_runs[i].Macro != null) continue;
            _runs[i] = new MacroRun { Macro = macro, Trigger = trigger, Start = now };
            return;
        }
    }

    /// <summary>Boutons produits par les macros actives à cet instant.</summary>
    private DualSenseButtons StepMacros(DualSenseButtons held, double now)
    {
        var result = DualSenseButtons.None;
        for (int i = 0; i < _runs.Length; i++)
        {
            var macro = _runs[i].Macro;
            if (macro == null) continue;
            int total = macro.TotalMs;
            double elapsed = (now - _runs[i].Start) * 1000;
            if (macro.Mode == MacroMode.WhileHeld && (held & _runs[i].Trigger) == 0)
            {
                _runs[i] = default;
                continue;
            }
            if (macro.Mode == MacroMode.Once && elapsed >= total)
            {
                _runs[i] = default;
                continue;
            }
            if (total > 0) elapsed %= total;
            foreach (var step in macro.Steps)
            {
                int d = Math.Max(1, step.DurationMs);
                if (elapsed < d)
                {
                    result |= step.Buttons;
                    break;
                }
                elapsed -= d;
            }
        }
        return result;
    }

    public static DualSenseButtons Remap(DualSenseButtons input, Dictionary<DualSenseButtons, DualSenseButtons> map)
    {
        var output = DualSenseButtons.None;
        for (int bit = 0; bit < 32; bit++)
        {
            var flag = (DualSenseButtons)(1u << bit);
            if ((input & flag) == 0) continue;
            output |= map.TryGetValue(flag, out var target) ? target : flag;
        }
        return output;
    }
}

/// <summary>
/// Raccourcis « PS + bouton ». PS est retenu tant qu'on ne sait pas s'il sert de raccourci :
/// un appui court est transmis au jeu au relâchement, un appui long est transmis dès 0,45 s.
/// </summary>
public sealed class PsShortcuts
{
    private const double LongPress = 0.45;
    private const double PulseLength = 0.12;

    /// <summary>Bouton combiné avec PS → action système.</summary>
    public static readonly (DualSenseButtons Button, SystemAction Action, string Label)[] Table =
    {
        (DualSenseButtons.Options, SystemAction.PowerOff, "Éteindre la manette (Bluetooth)"),
        (DualSenseButtons.Create, SystemAction.Screenshot, "Capture d'écran"),
        (DualSenseButtons.L1, SystemAction.PreviousProfile, "Profil précédent"),
        (DualSenseButtons.R1, SystemAction.NextProfile, "Profil suivant"),
        (DualSenseButtons.Triangle, SystemAction.ShowApp, "Ouvrir se7en DualSense"),
        (DualSenseButtons.Square, SystemAction.ToggleDesktopMode, "Mode bureau (clavier / souris)"),
        (DualSenseButtons.Touchpad, SystemAction.ToggleOverlay, "Afficher / masquer l'overlay"),
        (DualSenseButtons.DpadUp, SystemAction.VolumeUp, "Volume +"),
        (DualSenseButtons.DpadDown, SystemAction.VolumeDown, "Volume −"),
        (DualSenseButtons.Circle, SystemAction.VolumeMute, "Couper le son"),
        (DualSenseButtons.Cross, SystemAction.MediaPlayPause, "Lecture / pause"),
        (DualSenseButtons.DpadRight, SystemAction.MediaNext, "Piste suivante"),
        (DualSenseButtons.DpadLeft, SystemAction.MediaPrevious, "Piste précédente"),
    };

    private bool _psDown, _comboUsed, _passthrough;
    private double _psStart, _pulseUntil = -1;
    private DualSenseButtons _prev, _suppressed;

    public DualSenseButtons Process(DualSenseButtons buttons, double now, ref ActionFrame actions)
    {
        bool ps = (buttons & DualSenseButtons.PS) != 0;
        var newly = buttons & ~_prev & ~DualSenseButtons.PS;
        _prev = buttons;

        if (ps && !_psDown)
        {
            _psDown = true;
            _psStart = now;
            _comboUsed = false;
            _passthrough = false;
        }

        if (_psDown && !_passthrough && newly != DualSenseButtons.None)
        {
            foreach (var (button, action, _) in Table)
            {
                if ((newly & button) == 0) continue;
                actions.SystemTriggered |= 1UL << (int)action;
                _comboUsed = true;
                _suppressed |= button;
            }
        }

        if (_psDown && !ps)
        {
            _psDown = false;
            if (!_comboUsed && !_passthrough && now - _psStart < LongPress)
                _pulseUntil = now + PulseLength;
            _passthrough = false;
        }
        else if (_psDown && !_comboUsed && now - _psStart >= LongPress)
        {
            _passthrough = true;
        }

        // Un bouton de raccourci reste masqué au jeu jusqu'à son relâchement
        _suppressed &= buttons;
        var result = buttons & ~_suppressed & ~DualSenseButtons.PS;
        if ((_passthrough && ps) || now < _pulseUntil)
            result |= DualSenseButtons.PS;
        return result;
    }
}

/// <summary>Pavé tactile : zones cliquables, gestes de glissement, souris.</summary>
public sealed class TouchpadTracker
{
    private const double PulseLength = 0.12;

    // Gestes
    private bool _down, _clickedDuringTouch;
    private double _startX, _startY, _startTime, _lastX, _lastY;
    private DualSenseButtons _pulse;
    private double _pulseUntil = -1;

    // Souris
    private bool _tracking;
    private byte _trackId;
    private double _mx, _my, _scrollY;
    private bool _wasClicked, _clickRight;

    public DualSenseButtons Process(in DualSenseState raw, DualSenseButtons buttons, TouchpadSettings s, double now, ref ActionFrame actions)
    {
        if (!raw.IsFullReport) return buttons;
        var t0 = raw.Touch0;
        var t1 = raw.Touch1;
        bool click = (buttons & DualSenseButtons.Touchpad) != 0;

        if (s.Gestures)
        {
            if (t0.Active)
            {
                if (!_down)
                {
                    _down = true;
                    _clickedDuringTouch = false;
                    _startX = t0.X; _startY = t0.Y; _startTime = now;
                }
                _lastX = t0.X; _lastY = t0.Y;
                if (click || t1.Active) _clickedDuringTouch = true;
            }
            else if (_down)
            {
                _down = false;
                double dx = _lastX - _startX, dy = _lastY - _startY;
                if (!_clickedDuringTouch && now - _startTime < 0.6)
                {
                    var swipe = Math.Abs(dx) > Math.Abs(dy)
                        ? (Math.Abs(dx) > 450 ? (dx > 0 ? DualSenseButtons.SwipeRight : DualSenseButtons.SwipeLeft) : DualSenseButtons.None)
                        : (Math.Abs(dy) > 280 ? (dy > 0 ? DualSenseButtons.SwipeDown : DualSenseButtons.SwipeUp) : DualSenseButtons.None);
                    if (swipe != DualSenseButtons.None)
                    {
                        _pulse = swipe;
                        _pulseUntil = now + PulseLength;
                    }
                }
            }
            if (now < _pulseUntil)
                buttons |= _pulse;
        }

        switch (s.Mode)
        {
            case TouchpadMode.Zones when click:
            {
                var p = t0.Active ? t0 : t1;
                if (!p.Active) break;
                buttons &= ~DualSenseButtons.Touchpad;
                buttons |= Zone(p, s.ZoneCount);
                break;
            }

            case TouchpadMode.Mouse:
            {
                buttons &= ~DualSenseButtons.Touchpad;
                // Clic : gauche, ou droit si deux doigts sont posés au moment du clic
                if (click && !_wasClicked) _clickRight = t0.Active && t1.Active;
                _wasClicked = click;
                if (click) actions.MouseButtons |= _clickRight ? MouseButtonFlags.Right : MouseButtonFlags.Left;

                if (t0.Active && t1.Active && !click)
                {
                    // Deux doigts : défilement vertical
                    double y = (t0.Y + t1.Y) / 2.0;
                    if (_tracking && _trackId == 255) actions.Wheel += -(y - _scrollY) / 90.0;
                    _scrollY = y;
                    _tracking = true;
                    _trackId = 255;
                }
                else if (t0.Active)
                {
                    if (_tracking && _trackId == t0.Id)
                    {
                        actions.MouseDx += (t0.X - _mx) * 0.8 * s.MouseSpeed;
                        actions.MouseDy += (t0.Y - _my) * 0.8 * s.MouseSpeed;
                    }
                    _mx = t0.X; _my = t0.Y;
                    _tracking = true;
                    _trackId = t0.Id;
                }
                else
                {
                    _tracking = false;
                }
                break;
            }
        }
        return buttons;
    }

    /// <summary>2 zones : gauche / droite ; 4 zones : haut-gauche, haut-droite, bas-gauche, bas-droite.</summary>
    public static DualSenseButtons Zone(TouchPoint p, int count)
    {
        bool right = p.X > TouchPoint.MaxX / 2;
        bool bottom = p.Y > TouchPoint.MaxY / 2;
        if (count < 4)
            return right ? DualSenseButtons.TouchZone2 : DualSenseButtons.TouchZone1;
        return (right, bottom) switch
        {
            (false, false) => DualSenseButtons.TouchZone1,
            (true, false) => DualSenseButtons.TouchZone2,
            (false, true) => DualSenseButtons.TouchZone3,
            _ => DualSenseButtons.TouchZone4,
        };
    }
}
