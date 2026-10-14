using System.Windows;
using System.Windows.Controls;

namespace Se7en.DualSense.App.Controls;

public enum GlyphShape
{
    /// <summary>Boutons de façade △ ○ ✕ □.</summary>
    Circle,
    /// <summary>Croix directionnelle.</summary>
    Rounded,
    /// <summary>Libellés texte : L1, Options, PS…</summary>
    Pill,
}

/// <summary>Glyphe de bouton (design Axis « ButtonGlyph ») qui s'allume quand la touche est enfoncée.</summary>
public class ButtonGlyph : Control
{
    public static readonly DependencyProperty TextProperty = DependencyProperty.Register(
        nameof(Text), typeof(string), typeof(ButtonGlyph), new PropertyMetadata(""));

    public static readonly DependencyProperty ShapeProperty = DependencyProperty.Register(
        nameof(Shape), typeof(GlyphShape), typeof(ButtonGlyph), new PropertyMetadata(GlyphShape.Pill));

    public static readonly DependencyProperty IsPressedProperty = DependencyProperty.Register(
        nameof(IsPressed), typeof(bool), typeof(ButtonGlyph), new PropertyMetadata(false));

    public string Text
    {
        get => (string)GetValue(TextProperty);
        set => SetValue(TextProperty, value);
    }

    public GlyphShape Shape
    {
        get => (GlyphShape)GetValue(ShapeProperty);
        set => SetValue(ShapeProperty, value);
    }

    public bool IsPressed
    {
        get => (bool)GetValue(IsPressedProperty);
        set => SetValue(IsPressedProperty, value);
    }
}
