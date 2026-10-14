using System.Globalization;
using System.Windows;
using System.Windows.Media;
using Se7en.DualSense.App.Services;

namespace Se7en.DualSense.App.Controls;

/// <summary>Courbes du décalage au repos de chaque stick dans le temps (un point par jour).</summary>
public sealed class DriftHistoryChart : LiveVisual
{
    private IReadOnlyList<DriftDay> _days = Array.Empty<DriftDay>();
    private const double Threshold = 0.06;

    public void SetData(IReadOnlyList<DriftDay> days)
    {
        _days = days;
        InvalidateVisual();
    }

    protected override void OnRender(DrawingContext dc)
    {
        double w = ActualWidth, h = ActualHeight;
        if (w <= 0 || h <= 0) return;
        const double left = 44, bottom = 22, top = 8;
        var plot = new Rect(left, top, Math.Max(1, w - left - 8), Math.Max(1, h - top - bottom));
        dc.DrawRoundedRectangle(Res("Surface.Inset"), null, new Rect(0, 0, w, h), 8, 8);

        double max = Math.Max(0.12, _days.Count == 0 ? 0 : _days.Max(d => Math.Max(d.Left, d.Right)) * 1.25);
        var grid = new Pen(Res("Border.Hairline"), 1);
        var label = Res("Text.Tertiary");
        for (int i = 0; i <= 4; i++)
        {
            double v = max * i / 4;
            double y = plot.Bottom - plot.Height * i / 4;
            dc.DrawLine(grid, new Point(plot.Left, y), new Point(plot.Right, y));
            var t = Text(v.ToString("0.00", CultureInfo.InvariantCulture), 10, label);
            dc.DrawText(t, new Point(plot.Left - t.Width - 8, y - t.Height / 2));
        }

        // Seuil : zone morte par défaut
        double ty = plot.Bottom - plot.Height * Threshold / max;
        dc.DrawLine(new Pen(Res("Danger"), 1.2) { DashStyle = new DashStyle(new double[] { 4, 3 }, 0) }, new Point(plot.Left, ty), new Point(plot.Right, ty));

        if (_days.Count == 0)
        {
            var empty = Text("Pas encore de mesure", 12, label);
            dc.DrawText(empty, new Point(plot.Left + (plot.Width - empty.Width) / 2, plot.Top + (plot.Height - empty.Height) / 2));
            return;
        }

        var first = _days[0].Date;
        int span = Math.Max(1, _days[^1].Date.DayNumber - first.DayNumber);
        Point P(DriftDay d, double v) => new(
            _days.Count == 1 ? plot.Left + plot.Width / 2 : plot.Left + plot.Width * (d.Date.DayNumber - first.DayNumber) / span,
            plot.Bottom - plot.Height * Math.Min(v, max) / max);

        void Line(Func<DriftDay, double> value, Brush brush)
        {
            var geo = new StreamGeometry();
            using (var ctx = geo.Open())
            {
                ctx.BeginFigure(P(_days[0], value(_days[0])), false, false);
                foreach (var d in _days.Skip(1)) ctx.LineTo(P(d, value(d)), true, true);
            }
            geo.Freeze();
            dc.DrawGeometry(null, new Pen(brush, 2.2) { LineJoin = PenLineJoin.Round }, geo);
            foreach (var d in _days) dc.DrawEllipse(brush, null, P(d, value(d)), 3, 3);
        }

        Line(d => d.Left, Res("Accent"));
        Line(d => d.Right, Res("Ink"));

        var a = Text(first.ToString("dd/MM", CultureInfo.InvariantCulture), 10, label);
        dc.DrawText(a, new Point(plot.Left, plot.Bottom + 5));
        if (_days.Count > 1)
        {
            var b = Text(_days[^1].Date.ToString("dd/MM", CultureInfo.InvariantCulture), 10, label);
            dc.DrawText(b, new Point(plot.Right - b.Width, plot.Bottom + 5));
        }
    }
}
