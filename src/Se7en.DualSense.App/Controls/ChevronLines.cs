using System.Windows;
using System.Windows.Media;

namespace Se7en.DualSense.App.Controls;

/// <summary>
/// Les deux lignes lumineuses inclinées de l'accueil : de chaque côté, un segment haut
/// bleu qui s'estompe vers le centre, puis un segment bas sombre qui se renforce vers le bas.
/// </summary>
public sealed class ChevronLines : FrameworkElement
{
    /// <summary>Position horizontale des lignes, en fraction de la largeur (depuis chaque bord).</summary>
    public double Inset { get; set; } = 0.24;

    protected override void OnRender(DrawingContext dc)
    {
        double w = ActualWidth, h = ActualHeight;
        if (w <= 0 || h <= 0) return;

        var blue = Color.FromRgb(0x4B, 0x57, 0xF5);
        var ink = Color.FromRgb(0x22, 0x25, 0x2C);
        double x = w * Inset;
        double lean = h * 0.085;

        foreach (int side in new[] { -1, 1 })
        {
            // side = -1 : ligne gauche, 1 : ligne droite (miroir)
            double edge = side < 0 ? x : w - x;
            double dir = side < 0 ? 1 : -1;

            var top = new Point(edge, 0);
            var mid1 = new Point(edge + dir * lean, h * 0.32);
            var mid2 = new Point(edge + dir * lean * 0.82, h * 0.45);
            var bottom = new Point(edge, h);

            var upper = new LinearGradientBrush(Color.FromArgb(255, blue.R, blue.G, blue.B), Color.FromArgb(0, blue.R, blue.G, blue.B),
                new Point(0, 0), new Point(0, 1)) { MappingMode = BrushMappingMode.RelativeToBoundingBox };
            var lower = new LinearGradientBrush(Color.FromArgb(0, ink.R, ink.G, ink.B), Color.FromArgb(235, ink.R, ink.G, ink.B),
                new Point(0, 0), new Point(0, 1));

            dc.DrawLine(new Pen(upper, 2.2) { StartLineCap = PenLineCap.Round }, top, mid1);
            dc.DrawLine(new Pen(lower, 2.2) { EndLineCap = PenLineCap.Round }, mid2, bottom);
        }
    }
}
