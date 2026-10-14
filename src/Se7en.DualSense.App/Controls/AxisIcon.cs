using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace Se7en.DualSense.App.Controls;

/// <summary>Icône Lucide (grille 24×24, contour 1.75) colorée par <see cref="Control.Foreground"/>.</summary>
public class AxisIcon : Control
{
    static AxisIcon()
    {
        DefaultStyleKeyProperty.OverrideMetadata(typeof(AxisIcon), new FrameworkPropertyMetadata(typeof(AxisIcon)));
        FocusableProperty.OverrideMetadata(typeof(AxisIcon), new FrameworkPropertyMetadata(false));
        IsTabStopProperty.OverrideMetadata(typeof(AxisIcon), new FrameworkPropertyMetadata(false));
    }

    public static readonly DependencyProperty DataProperty = DependencyProperty.Register(
        nameof(Data), typeof(Geometry), typeof(AxisIcon), new PropertyMetadata(null));

    public static readonly DependencyProperty StrokeThicknessProperty = DependencyProperty.Register(
        nameof(StrokeThickness), typeof(double), typeof(AxisIcon), new PropertyMetadata(1.75));

    public Geometry? Data
    {
        get => (Geometry?)GetValue(DataProperty);
        set => SetValue(DataProperty, value);
    }

    public double StrokeThickness
    {
        get => (double)GetValue(StrokeThicknessProperty);
        set => SetValue(StrokeThicknessProperty, value);
    }
}
