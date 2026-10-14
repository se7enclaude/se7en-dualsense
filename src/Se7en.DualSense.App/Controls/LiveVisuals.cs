using System.Globalization;
using System.Windows;
using System.Windows.Media;
using Se7en.DualSense.App.Services;
using Se7en.DualSense.Hid;

namespace Se7en.DualSense.App.Controls;

/// <summary>Base des visualiseurs dessinés à la main (OnRender) : rapides et sans arbre visuel.</summary>
public abstract class LiveVisual : FrameworkElement
{
    protected LiveVisual()
    {
        Loaded += (_, _) => ThemeManager.ThemeChanged += InvalidateVisual;
        Unloaded += (_, _) => ThemeManager.ThemeChanged -= InvalidateVisual;
    }

    protected Brush Res(string key) => TryFindResource(key) as Brush ?? Brushes.Magenta;

    protected Color ResColor(string key) => TryFindResource(key) is Color c ? c : Colors.Magenta;

    protected Typeface MonoFace => new((FontFamily)FindResource("Font.Mono"), FontStyles.Normal, FontWeights.Medium, FontStretches.Normal);

    protected FormattedText Text(string s, double size, Brush brush, Typeface? face = null) =>
        new(s, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, face ?? MonoFace, size, brush,
            VisualTreeHelper.GetDpi(this).PixelsPerDip);

    protected static SolidColorBrush Frozen(Color c)
    {
        var b = new SolidColorBrush(c);
        b.Freeze();
        return b;
    }
}

/// <summary>Puits circulaire avec le point du stick (design Axis « StickVisualizer »).</summary>
public sealed class StickVisualizer : LiveVisual
{
    private double _x, _y;

    public static readonly DependencyProperty DeadzoneProperty = DependencyProperty.Register(
        nameof(Deadzone), typeof(double), typeof(StickVisualizer),
        new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsRender));

    /// <summary>Zone morte radiale (0-1) dessinée en pointillés.</summary>
    public double Deadzone
    {
        get => (double)GetValue(DeadzoneProperty);
        set => SetValue(DeadzoneProperty, value);
    }

    // Traînée : dernières positions, qui s'estompent en ~0,35 s
    private readonly (double X, double Y, double T)[] _trail = new (double, double, double)[18];
    private int _trailHead, _trailCount;
    private static readonly System.Diagnostics.Stopwatch Clock = System.Diagnostics.Stopwatch.StartNew();
    private const double TrailSeconds = 0.35;

    /// <summary>Position normalisée, -1..1, Y vers le bas.</summary>
    public void SetPosition(double x, double y)
    {
        double now = Clock.Elapsed.TotalSeconds;
        bool fading = _trailCount > 0 && now - _trail[(_trailHead + _trail.Length - 1) % _trail.Length].T < TrailSeconds;
        if (Math.Abs(x - _x) < 0.0005 && Math.Abs(y - _y) < 0.0005)
        {
            if (fading) InvalidateVisual();
            return;
        }
        _trail[_trailHead] = (_x, _y, now);
        _trailHead = (_trailHead + 1) % _trail.Length;
        _trailCount = Math.Min(_trailCount + 1, _trail.Length);
        _x = x;
        _y = y;
        InvalidateVisual();
    }

    protected override void OnRender(DrawingContext dc)
    {
        double size = Math.Min(ActualWidth, ActualHeight);
        if (size <= 0) return;
        double r = size / 2;
        var c = new Point(ActualWidth / 2, ActualHeight / 2);

        var inset = Res("Surface.Inset");
        var dashed = new Pen(Res("Border.Dashed"), 1) { DashStyle = new DashStyle(new double[] { 3, 3 }, 0) };
        var subtle = new Pen(Res("Border.Subtle"), 1);
        var accent = ResColor("Color.Accent");

        // Puits plat + filet intérieur (style éditorial), croix en pointillés
        dc.DrawEllipse(inset, new Pen(Res("Border.Hairline"), 1), c, r - 0.5, r - 0.5);
        dc.DrawLine(dashed, new Point(c.X, c.Y - r + 12), new Point(c.X, c.Y + r - 12));
        dc.DrawLine(dashed, new Point(c.X - r + 12, c.Y), new Point(c.X + r - 12, c.Y));
        dc.DrawEllipse(null, subtle, c, r * 0.5, r * 0.5);

        if (Deadzone > 0)
        {
            var dz = new Pen(Res("Accent.Line"), 1.5) { DashStyle = new DashStyle(new double[] { 3, 2 }, 0) };
            dc.DrawEllipse(Res("Accent.Soft"), dz, c, Deadzone * r, Deadzone * r);
        }

        // Point + anneau
        double travel = r - 14;
        var p = new Point(c.X + _x * travel, c.Y + _y * travel);
        double mag = Math.Sqrt(_x * _x + _y * _y);
        bool idle = mag <= Math.Max(Deadzone, 0.02);
        var dotColor = idle ? Color.FromRgb(0x9A, 0xA0, 0xAE) : accent;

        // Traînée du mouvement (points de plus en plus petits et transparents)
        double t = Clock.Elapsed.TotalSeconds;
        for (int i = 0; i < _trailCount; i++)
        {
            var (tx, ty, tt) = _trail[(_trailHead - 1 - i + _trail.Length * 2) % _trail.Length];
            double age = (t - tt) / TrailSeconds;
            if (age >= 1) continue;
            var tp = new Point(c.X + tx * travel, c.Y + ty * travel);
            byte alpha = (byte)(110 * (1 - age));
            dc.DrawEllipse(Frozen(Color.FromArgb(alpha, accent.R, accent.G, accent.B)), null, tp, 7 * (1 - age * 0.6), 7 * (1 - age * 0.6));
        }

        dc.DrawEllipse(null, new Pen(Res("Border.Default"), 1), p, 22, 22);
        if (!idle)
        {
            var halo = new RadialGradientBrush(Color.FromArgb(140, accent.R, accent.G, accent.B), Color.FromArgb(0, accent.R, accent.G, accent.B));
            dc.DrawEllipse(halo, null, p, 18, 18);
        }
        dc.DrawEllipse(Frozen(dotColor), null, p, 8, 8);
    }
}

/// <summary>Barre verticale en pilule pour L2 / R2 (design Axis « TriggerMeter »).</summary>
public sealed class TriggerMeter : LiveVisual
{
    private double _value;

    public void SetValue(double v)
    {
        v = Math.Clamp(v, 0, 1);
        if (Math.Abs(v - _value) < 0.002) return;
        _value = v;
        InvalidateVisual();
    }

    protected override void OnRender(DrawingContext dc)
    {
        double w = ActualWidth, h = ActualHeight;
        if (w <= 0 || h <= 0) return;
        double rad = w / 2;
        dc.DrawRoundedRectangle(Res("Surface.Inset"), null, new Rect(0, 0, w, h), rad, rad);
        if (_value <= 0) return;
        double inner = h - 8;
        double fill = Math.Max(6, inner * _value);
        dc.DrawRoundedRectangle(Res("Accent"), null, new Rect(4, 4 + inner - fill, w - 8, fill), (w - 8) / 2, (w - 8) / 2);
    }
}

/// <summary>Représentation du pavé tactile avec la position des doigts.</summary>
public sealed class TouchpadVisual : LiveVisual
{
    private TouchPoint _t0, _t1;
    private bool _clicked;

    public void SetTouches(TouchPoint t0, TouchPoint t1, bool clicked)
    {
        if (t0 == _t0 && t1 == _t1 && clicked == _clicked) return;
        _t0 = t0;
        _t1 = t1;
        _clicked = clicked;
        InvalidateVisual();
    }

    protected override void OnRender(DrawingContext dc)
    {
        double w = ActualWidth, h = ActualHeight;
        if (w <= 0 || h <= 0) return;
        var rect = new Rect(0, 0, w, h);
        dc.DrawRoundedRectangle(Res(_clicked ? "Accent.Soft" : "Surface.Inset"), _clicked ? new Pen(Res("Accent"), 1.5) : null, rect, 8, 8);

        var accent = ResColor("Color.Accent");
        DrawFinger(dc, _t0, w, h, accent, "1");
        DrawFinger(dc, _t1, w, h, accent, "2");
    }

    private void DrawFinger(DrawingContext dc, TouchPoint t, double w, double h, Color accent, string label)
    {
        if (!t.Active) return;
        var p = new Point(8 + t.X / (double)TouchPoint.MaxX * (w - 16), 8 + t.Y / (double)TouchPoint.MaxY * (h - 16));
        var halo = new RadialGradientBrush(Color.FromArgb(120, accent.R, accent.G, accent.B), Color.FromArgb(0, accent.R, accent.G, accent.B));
        dc.DrawEllipse(halo, null, p, 22, 22);
        dc.DrawEllipse(Frozen(accent), null, p, 10, 10);
        var txt = Text(label, 10, Brushes.White);
        dc.DrawText(txt, new Point(p.X - txt.Width / 2, p.Y - txt.Height / 2));
    }
}

/// <summary>Jauge de batterie (design Axis « BatteryMeter »).</summary>
public sealed class BatteryMeter : LiveVisual
{
    private int _percent = -1;
    private bool _charging;

    public BatteryMeter()
    {
        Width = 64;
        Height = 14;
    }

    public void SetLevel(int percent, bool charging)
    {
        if (percent == _percent && charging == _charging) return;
        _percent = percent;
        _charging = charging;
        InvalidateVisual();
    }

    protected override void OnRender(DrawingContext dc)
    {
        var secondary = Res("Text.Secondary");
        var body = new Rect(0.75, 1.75, 24, 11.5);
        dc.DrawRoundedRectangle(null, new Pen(secondary, 1.5), body, 4, 4);
        dc.DrawRoundedRectangle(secondary, null, new Rect(25.5, 5.5, 2, 4), 1, 1);
        if (_percent < 0) return;

        var fill = _charging ? Res("Success") : _percent <= 15 ? Res("Danger") : _percent <= 30 ? Res("Warning") : Res("Text.Primary");
        dc.DrawRoundedRectangle(fill, null, new Rect(3.25, 4.25, 19 * _percent / 100.0, 6.5), 1.5, 1.5);

        var txt = Text($"{_percent}%", 12, secondary);
        dc.DrawText(txt, new Point(33, (ActualHeight - txt.Height) / 2));
    }
}
