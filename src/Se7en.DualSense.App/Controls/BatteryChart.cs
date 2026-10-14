using System.Globalization;
using System.Windows;
using System.Windows.Media;
using Se7en.DualSense.App.Services;

namespace Se7en.DualSense.App.Controls;

/// <summary>Charge de la batterie sur les dernières 24 h (zones de charge en surbrillance).</summary>
public sealed class BatteryChart : LiveVisual
{
    private IReadOnlyList<BatterySample> _samples = Array.Empty<BatterySample>();

    public void SetData(IReadOnlyList<BatterySample> samples)
    {
        _samples = samples;
        InvalidateVisual();
    }

    protected override void OnRender(DrawingContext dc)
    {
        double w = ActualWidth, h = ActualHeight;
        if (w <= 0 || h <= 0) return;
        const double left = 40, bottom = 22, top = 8;
        var plot = new Rect(left, top, Math.Max(1, w - left - 8), Math.Max(1, h - top - bottom));
        dc.DrawRoundedRectangle(Res("Surface.Inset"), null, new Rect(0, 0, w, h), 8, 8);
        var label = Res("Text.Tertiary");
        var grid = new Pen(Res("Border.Hairline"), 1);
        foreach (int pct in new[] { 0, 50, 100 })
        {
            double y = plot.Bottom - plot.Height * pct / 100;
            dc.DrawLine(grid, new Point(plot.Left, y), new Point(plot.Right, y));
            var t = Text(pct + " %", 10, label);
            dc.DrawText(t, new Point(plot.Left - t.Width - 8, y - t.Height / 2));
        }

        var end = DateTime.Now;
        var start = end.AddHours(-24);
        var points = _samples.Where(s => s.Time >= start).ToList();
        if (points.Count == 0)
        {
            var empty = Text("Pas encore de relevé sur 24 h", 12, label);
            dc.DrawText(empty, new Point(plot.Left + (plot.Width - empty.Width) / 2, plot.Top + (plot.Height - empty.Height) / 2));
            return;
        }
        Point P(BatterySample s) => new(plot.Left + plot.Width * (s.Time - start).TotalHours / 24, plot.Bottom - plot.Height * s.Percent / 100.0);

        // Périodes de charge
        var charge = Res("Success.Soft");
        for (int i = 0; i < points.Count; i++)
        {
            if (!points[i].Charging) continue;
            double x0 = P(points[i]).X, x1 = i + 1 < points.Count ? P(points[i + 1]).X : plot.Right;
            dc.DrawRectangle(charge, null, new Rect(x0, plot.Top, Math.Max(1, x1 - x0), plot.Height));
        }

        // Courbe en escalier (la charge est annoncée par paliers)
        var geo = new StreamGeometry();
        using (var ctx = geo.Open())
        {
            ctx.BeginFigure(P(points[0]), false, false);
            for (int i = 1; i < points.Count; i++)
            {
                var p = P(points[i]);
                ctx.LineTo(new Point(p.X, P(points[i - 1]).Y), true, false);
                ctx.LineTo(p, true, false);
            }
            ctx.LineTo(new Point(plot.Right, P(points[^1]).Y), true, false);
        }
        geo.Freeze();
        dc.DrawGeometry(null, new Pen(Res("Accent"), 2.2) { LineJoin = PenLineJoin.Round }, geo);

        var a = Text("il y a 24 h", 10, label);
        dc.DrawText(a, new Point(plot.Left, plot.Bottom + 5));
        var b = Text("maintenant", 10, label);
        dc.DrawText(b, new Point(plot.Right - b.Width, plot.Bottom + 5));
    }
}
