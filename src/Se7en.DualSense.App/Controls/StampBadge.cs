using System.Windows;
using System.Windows.Controls;

namespace Se7en.DualSense.App.Controls;

/// <summary>
/// Tampon rond (design Axis « StampBadge ») : disque violet, chiffre Anton italique citron,
/// anneau encre. Un seul par vue, légèrement tourné.
/// </summary>
public class StampBadge : Control
{
    public static readonly DependencyProperty ValueProperty = DependencyProperty.Register(
        nameof(Value), typeof(string), typeof(StampBadge), new PropertyMetadata("", (d, _) => ((StampBadge)d).UpdateFontSize()));

    public static readonly DependencyProperty CaptionProperty = DependencyProperty.Register(
        nameof(Caption), typeof(string), typeof(StampBadge), new PropertyMetadata(""));

    private static readonly DependencyPropertyKey ValueFontSizeKey = DependencyProperty.RegisterReadOnly(
        nameof(ValueFontSize), typeof(double), typeof(StampBadge), new PropertyMetadata(40.0));

    public static readonly DependencyProperty ValueFontSizeProperty = ValueFontSizeKey.DependencyProperty;

    public StampBadge()
    {
        SizeChanged += (_, _) => UpdateFontSize();
    }

    public string Value
    {
        get => (string)GetValue(ValueProperty);
        set => SetValue(ValueProperty, value);
    }

    public string Caption
    {
        get => (string)GetValue(CaptionProperty);
        set => SetValue(CaptionProperty, value);
    }

    /// <summary>Taille du chiffre, adaptée à sa longueur (comme le composant d'origine).</summary>
    public double ValueFontSize => (double)GetValue(ValueFontSizeProperty);

    private void UpdateFontSize()
    {
        double size = double.IsNaN(Width) ? Math.Max(ActualWidth, 1) : Width;
        int len = (Value ?? "").Length;
        double ratio = len <= 2 ? 0.46 : len <= 4 ? 0.34 : 0.26;
        SetValue(ValueFontSizeKey, Math.Round(size * ratio));
    }
}
