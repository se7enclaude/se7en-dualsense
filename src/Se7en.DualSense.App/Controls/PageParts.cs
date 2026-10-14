using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using Se7en.DualSense.App.Services;

namespace Se7en.DualSense.App.Controls;

/// <summary>En-tête de page Axis : surtitre, titre Anton majuscule, sous-titre, filet pointillé.</summary>
public class PageHeader : StackPanel
{
    private readonly TextBlock _overline = new();
    private readonly TextBlock _title = new();
    private readonly TextBlock _subtitle = new();

    public PageHeader()
    {
        _overline.SetResourceReference(StyleProperty, "Text.Overline");
        _overline.Margin = new Thickness(0, 0, 0, 10);
        _title.SetResourceReference(StyleProperty, "Text.Title");
        _subtitle.SetResourceReference(StyleProperty, "Text.Body");
        _subtitle.Margin = new Thickness(0, 12, 0, 22);
        _subtitle.MaxWidth = 560;
        _subtitle.HorizontalAlignment = HorizontalAlignment.Left;
        var rule = new Line { Margin = new Thickness(0, 0, 0, 28) };
        rule.SetResourceReference(StyleProperty, "Rule.H");
        Children.Add(_overline);
        Children.Add(_title);
        Children.Add(_subtitle);
        Children.Add(rule);
    }

    public string Overline { get => _overline.Text; set => _overline.Text = value; }
    public string Title { get => _title.Text; set => _title.Text = value.ToUpperInvariant(); }
    public string Subtitle { get => _subtitle.Text; set => _subtitle.Text = value; }
}

/// <summary>Ligne de réglage : libellé + description à gauche, contrôle à droite, filet fin dessous.</summary>
[System.Windows.Markup.ContentProperty(nameof(Control))]
public class SettingRow : Grid
{
    private readonly TextBlock _label = new();
    private readonly TextBlock _description = new();
    private readonly ContentPresenter _control = new() { VerticalAlignment = VerticalAlignment.Center };
    private readonly Border _rule = new() { BorderThickness = new Thickness(0, 0, 0, 1), VerticalAlignment = VerticalAlignment.Bottom };

    public SettingRow()
    {
        ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        Margin = new Thickness(0, 0, 0, 0);
        MinHeight = 58;

        var texts = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 10, 24, 10) };
        _label.SetResourceReference(StyleProperty, "Text.Label");
        _label.FontSize = 14;
        _description.SetResourceReference(StyleProperty, "Text.Caption");
        _description.TextWrapping = TextWrapping.Wrap;
        _description.Margin = new Thickness(0, 3, 0, 0);
        texts.Children.Add(_label);
        texts.Children.Add(_description);
        Children.Add(texts);

        SetColumn(_control, 1);
        Children.Add(_control);

        _rule.SetResourceReference(Border.BorderBrushProperty, "Border.Subtle");
        SetColumnSpan(_rule, 2);
        Children.Add(_rule);
    }

    public string Label { get => _label.Text; set => _label.Text = value; }

    public string Description
    {
        get => _description.Text;
        set
        {
            _description.Text = value;
            _description.Visibility = string.IsNullOrEmpty(value) ? Visibility.Collapsed : Visibility.Visible;
        }
    }

    public object? Control { get => _control.Content; set => _control.Content = value; }

    /// <summary>Masque le filet du bas (dernière ligne d'une carte).</summary>
    public bool Last { get => _rule.Visibility != Visibility.Visible; set => _rule.Visibility = value ? Visibility.Collapsed : Visibility.Visible; }
}

/// <summary>
/// Pile de notifications (design Axis « Toast ») : capsule encre centrée en bas, disparition automatique.
/// </summary>
public class ToastHost : StackPanel
{
    public ToastHost()
    {
        VerticalAlignment = VerticalAlignment.Bottom;
        HorizontalAlignment = HorizontalAlignment.Center;
        Margin = new Thickness(0, 0, 0, 28);
        IsHitTestVisible = false;
    }

    public void Show(string title, string message, NotifyKind kind)
    {
        var dot = new Ellipse { Width = 8, Height = 8, Margin = new Thickness(0, 0, 12, 0), VerticalAlignment = VerticalAlignment.Center };
        dot.SetResourceReference(Shape.FillProperty, kind switch
        {
            NotifyKind.Success => "Success",
            NotifyKind.Warning => "Warning",
            _ => "Highlight",
        });

        var titleText = new TextBlock { Text = title, FontWeight = FontWeights.SemiBold, FontSize = 13 };
        titleText.SetResourceReference(TextBlock.FontFamilyProperty, "Font.Sans");
        titleText.SetResourceReference(TextBlock.ForegroundProperty, "OnInk");
        var msg = new TextBlock { Text = message, FontSize = 12, Margin = new Thickness(0, 2, 0, 0), Opacity = 0.7 };
        msg.SetResourceReference(TextBlock.FontFamilyProperty, "Font.Sans");
        msg.SetResourceReference(TextBlock.ForegroundProperty, "OnInk");
        msg.Visibility = string.IsNullOrEmpty(message) ? Visibility.Collapsed : Visibility.Visible;

        var texts = new StackPanel();
        texts.Children.Add(titleText);
        texts.Children.Add(msg);
        var row = new StackPanel { Orientation = Orientation.Horizontal };
        row.Children.Add(dot);
        row.Children.Add(texts);

        var toast = new Border
        {
            CornerRadius = new CornerRadius(24),
            Padding = new Thickness(20, 12, 24, 12),
            Margin = new Thickness(0, 8, 0, 0),
            Child = row,
            Opacity = 0,
            RenderTransform = new TranslateTransform(0, 16),
        };
        toast.SetResourceReference(Border.BackgroundProperty, "Ink");
        toast.SetResourceReference(EffectProperty, "Shadow.Pop");

        Children.Add(toast);
        while (Children.Count > 3)
            Children.RemoveAt(0);

        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
        // Apparition (220 ms), affichage 3,4 s, disparition (260 ms)
        var life = new DoubleAnimationUsingKeyFrames();
        life.KeyFrames.Add(new EasingDoubleKeyFrame(1, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(220)), ease));
        life.KeyFrames.Add(new LinearDoubleKeyFrame(1, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(3600))));
        life.KeyFrames.Add(new LinearDoubleKeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(3860))));
        life.Completed += (_, _) => Children.Remove(toast);
        toast.BeginAnimation(OpacityProperty, life);
        toast.RenderTransform.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(0, TimeSpan.FromMilliseconds(320)) { EasingFunction = ease });
    }
}
