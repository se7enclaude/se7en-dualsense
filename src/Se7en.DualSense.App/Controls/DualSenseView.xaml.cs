using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using Se7en.DualSense.App.Services;
using Se7en.DualSense.Hid;

namespace Se7en.DualSense.App.Controls;

/// <summary>
/// Grande DualSense (photo) animée : elle flotte, s'incline avec la manette réelle, et chaque
/// commande s'illumine en direct grâce à des calques posés sur la photo.
/// Toutes les animations sont calculées en une seule passe par image.
/// </summary>
public partial class DualSenseView : UserControl
{
    private const double FloatAmplitude = 6;
    private const double FloatPeriodSeconds = 3.6;
    private const double StickTravel = 8;
    private const double PressTimeConstant = 0.045;

    private static readonly Color LightbarOff = Color.FromRgb(0xDC, 0xDF, 0xE6);

    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private double _lastTime;
    private readonly List<PressAnim> _presses = new();
    private readonly List<SolidColorBrush> _accentBrushes = new();
    private Color _accent = Color.FromRgb(0x1A, 0x1A, 0xF0);

    private double _l2, _r2, _lx, _ly, _rx, _ry, _l3, _r3, _touchClick, _tilt;
    private readonly FingerAnim _finger0, _finger1;

    private readonly SolidColorBrush _lightbarBrush = new(LightbarOff);
    private readonly SolidColorBrush _leftRingBrush = new(Colors.Transparent);
    private readonly SolidColorBrush _rightRingBrush = new(Colors.Transparent);

    public DualSenseView()
    {
        InitializeComponent();
        LoadPhotos();

        LightbarL.Stroke = LightbarR.Stroke = LightbarGlowL.Stroke = LightbarGlowR.Stroke = _lightbarBrush;
        LeftStickRing.Stroke = _leftRingBrush;
        RightStickRing.Stroke = _rightRingBrush;

        Fill(L2Glow, R2Glow, TouchTint, L1Over, R1Over, CreateOver, OptionsOver, DpadUpOver, DpadDownOver,
            DpadLeftOver, DpadRightOver, TriangleFill, SquareFill, CircleFill, CrossFill, MuteOver);
        PsOver.Stroke = AccentBrush();
        PsOver.Fill = AccentBrush(0.25);

        AddPress(DualSenseButtons.L1, L1Over);
        AddPress(DualSenseButtons.R1, R1Over);
        AddPress(DualSenseButtons.Create, CreateOver);
        AddPress(DualSenseButtons.Options, OptionsOver);
        AddPress(DualSenseButtons.DpadUp, DpadUpOver);
        AddPress(DualSenseButtons.DpadDown, DpadDownOver);
        AddPress(DualSenseButtons.DpadLeft, DpadLeftOver);
        AddPress(DualSenseButtons.DpadRight, DpadRightOver);
        AddPress(DualSenseButtons.Triangle, TriangleOver);
        AddPress(DualSenseButtons.Square, SquareOver);
        AddPress(DualSenseButtons.Circle, CircleOver);
        AddPress(DualSenseButtons.Cross, CrossOver);
        AddPress(DualSenseButtons.PS, PsOver);
        AddPress(DualSenseButtons.Mute, MuteOver);

        _finger0 = new FingerAnim(Finger0, Finger0Glow);
        _finger1 = new FingerAnim(Finger1, Finger1Glow);

        RefreshAccent();
        ThemeManager.ThemeChanged += RefreshAccent;
        Unloaded += (_, _) => ThemeManager.ThemeChanged -= RefreshAccent;
    }

    /// <summary>
    /// Les photos ne sont pas versionnées (image produit protégée) : si elles manquent,
    /// on affiche un message au lieu de planter au chargement du XAML.
    /// </summary>
    private void LoadPhotos()
    {
        try
        {
            Photo.Source = LoadImage("dualsense.png");
            StickLeftImage.Source = LoadImage("stick-left.png");
            StickRightImage.Source = LoadImage("stick-right.png");
        }
        catch (Exception ex) when (ex is IOException or UriFormatException or NotSupportedException)
        {
            Stage.Visibility = Visibility.Collapsed;
            MissingPhoto.Visibility = Visibility.Visible;
        }
    }

    private static BitmapImage LoadImage(string name)
    {
        var image = new BitmapImage();
        image.BeginInit();
        image.UriSource = new Uri($"pack://application:,,,/Assets/Images/{name}");
        image.CacheOption = BitmapCacheOption.OnLoad;
        image.EndInit();
        image.Freeze();
        return image;
    }

    /// <summary>Décalage vertical actuel du flottement (pour la parallaxe du décor).</summary>
    public double FloatOffset => Float.Y;

    private SolidColorBrush AccentBrush(double opacity = 1)
    {
        var b = new SolidColorBrush(_accent) { Opacity = opacity };
        _accentBrushes.Add(b);
        return b;
    }

    private void Fill(params Shape[] shapes)
    {
        foreach (var s in shapes)
            s.Fill = AccentBrush(s == TouchTint ? 0.35 : 0.92);
    }

    private void AddPress(DualSenseButtons flag, UIElement overlay) => _presses.Add(new PressAnim(flag, overlay));

    private void RefreshAccent()
    {
        if (TryFindResource("Color.Accent") is Color c)
            _accent = c;
        foreach (var b in _accentBrushes)
            b.Color = _accent;
        foreach (var p in _presses)
            p.SetColor(_accent);
        _finger0.SetColor(_accent);
        _finger1.SetColor(_accent);
    }

    /// <summary>À appeler à chaque image avec le dernier état connu.</summary>
    public void Update(in FrameData frame)
    {
        double now = _clock.Elapsed.TotalSeconds;
        double dt = Math.Clamp(now - _lastTime, 0, 0.1);
        _lastTime = now;

        var s = frame.State;
        bool connected = frame.IsConnected;
        double press = 1 - Math.Exp(-dt / PressTimeConstant);
        double smooth = 1 - Math.Exp(-dt / 0.03);

        // ── Flottement + ombre au sol ──
        double phase = Math.Sin(now * 2 * Math.PI / FloatPeriodSeconds);
        Float.Y = -FloatAmplitude * phase;
        double lift = (phase + 1) / 2;
        FloorShadowScale.ScaleX = FloorShadowScale.ScaleY = 1 - 0.1 * lift;
        FloorShadow.Opacity = 0.95 - 0.35 * lift;

        // ── Inclinaison : balancement léger + roulis réel (accéléromètre) ──
        double targetTilt = 0.6 * Math.Sin(now * 2 * Math.PI / 7.0);
        if (connected && s.IsFullReport)
        {
            double ax = s.AccelX, ay = s.AccelY, az = s.AccelZ;
            double norm = Math.Sqrt(ay * ay + az * az);
            if (norm > 1000)
                targetTilt += Math.Clamp(Math.Atan2(ax, norm) * 180 / Math.PI * 0.35, -9, 9);
        }
        _tilt += (targetTilt - _tilt) * (1 - Math.Exp(-dt / 0.12));
        Tilt.Angle = _tilt;

        // ── Boutons ──
        var buttons = connected ? s.Buttons : DualSenseButtons.None;
        foreach (var p in _presses)
            p.Step((buttons & p.Flag) != 0 ? 1 : 0, press);

        // ── Gâchettes : lueur derrière la manette ──
        _l2 += ((connected ? s.L2 / 255.0 : 0) - _l2) * smooth;
        _r2 += ((connected ? s.R2 / 255.0 : 0) - _r2) * smooth;
        L2Glow.Opacity = _l2;
        R2Glow.Opacity = _r2;

        // ── Sticks ──
        double tlx = connected ? Normalize(s.LeftX) : 0, tly = connected ? Normalize(s.LeftY) : 0;
        double trx = connected ? Normalize(s.RightX) : 0, try_ = connected ? Normalize(s.RightY) : 0;
        _lx += (tlx - _lx) * smooth; _ly += (tly - _ly) * smooth;
        _rx += (trx - _rx) * smooth; _ry += (try_ - _ry) * smooth;
        LeftStickShift.X = _lx * StickTravel; LeftStickShift.Y = _ly * StickTravel;
        RightStickShift.X = _rx * StickTravel; RightStickShift.Y = _ry * StickTravel;

        _l3 += (((buttons & DualSenseButtons.L3) != 0 ? 1 : 0) - _l3) * press;
        _r3 += (((buttons & DualSenseButtons.R3) != 0 ? 1 : 0) - _r3) * press;
        double lDeflect = Math.Min(1, Math.Sqrt(_lx * _lx + _ly * _ly));
        double rDeflect = Math.Min(1, Math.Sqrt(_rx * _rx + _ry * _ry));
        _leftRingBrush.Color = WithAlpha(_accent, Math.Max(_l3, lDeflect * 0.6));
        _rightRingBrush.Color = WithAlpha(_accent, Math.Max(_r3, rDeflect * 0.6));

        // ── Pavé tactile ──
        _touchClick += (((buttons & DualSenseButtons.Touchpad) != 0 ? 1 : 0) - _touchClick) * press;
        TouchTint.Opacity = _touchClick;
        _finger0.Step(connected ? s.Touch0 : default, press);
        _finger1.Step(connected ? s.Touch1 : default, press);

        // ── Barre lumineuse : couleur réellement envoyée, éteinte si déconnectée ──
        var o = frame.Output;
        var lightbar = connected ? Color.FromRgb(o.LightbarRed, o.LightbarGreen, o.LightbarBlue) : LightbarOff;
        if (lightbar.R + lightbar.G + lightbar.B < 30)
            lightbar = LightbarOff;
        _lightbarBrush.Color = lightbar;
        double breathe = 0.4 + 0.25 * (Math.Sin(now * 2 * Math.PI / 2.8) + 1) / 2;
        LightbarGlowL.Opacity = LightbarGlowR.Opacity = connected ? breathe : 0;
    }

    private static double Normalize(byte v) => Math.Clamp((v - 127.5) / 127.5, -1, 1);

    private static Color WithAlpha(Color c, double alpha) =>
        Color.FromArgb((byte)(Math.Clamp(alpha, 0, 1) * 255), c.R, c.G, c.B);

    /// <summary>Calque d'appui : apparaît en fondu avec un halo de la couleur d'accent.</summary>
    private sealed class PressAnim
    {
        private readonly UIElement _overlay;
        private readonly DropShadowEffect _glow = new() { ShadowDepth = 0, BlurRadius = 16, Opacity = 0.9 };
        private double _level = -1;

        public DualSenseButtons Flag { get; }

        public PressAnim(DualSenseButtons flag, UIElement overlay)
        {
            Flag = flag;
            _overlay = overlay;
            _overlay.Effect = _glow;
            _overlay.Opacity = 0;
            _level = 0;
        }

        public void SetColor(Color c) => _glow.Color = c;

        public void Step(double target, double k)
        {
            if (_level == target)
                return;
            _level += (target - _level) * k;
            if (Math.Abs(_level - target) < 0.004)
                _level = target;
            _overlay.Opacity = _level;
        }
    }

    /// <summary>Point lumineux représentant un doigt sur le pavé tactile.</summary>
    private sealed class FingerAnim
    {
        // Zone utile du pavé dans le repère de la photo
        private const double Left = 172, Top = 42, Width = 162, Height = 84;

        private readonly Ellipse _dot;
        private readonly DropShadowEffect _glow;
        private readonly SolidColorBrush _brush = new(Colors.Blue);
        private double _opacity, _x = double.NaN, _y;

        public FingerAnim(Ellipse dot, DropShadowEffect glow)
        {
            _dot = dot;
            _glow = glow;
            _dot.Fill = _brush;
        }

        public void SetColor(Color c)
        {
            _brush.Color = c;
            _glow.Color = c;
        }

        public void Step(TouchPoint t, double k)
        {
            if (t.Active)
            {
                double x = Left + t.X / (double)TouchPoint.MaxX * Width;
                double y = Top + t.Y / (double)TouchPoint.MaxY * Height;
                if (double.IsNaN(_x) || _opacity < 0.05) { _x = x; _y = y; }
                else { _x += (x - _x) * 0.6; _y += (y - _y) * 0.6; }
                Canvas.SetLeft(_dot, _x - _dot.Width / 2);
                Canvas.SetTop(_dot, _y - _dot.Height / 2);
            }
            _opacity += ((t.Active ? 1 : 0) - _opacity) * k;
            _dot.Opacity = _opacity;
        }
    }
}
