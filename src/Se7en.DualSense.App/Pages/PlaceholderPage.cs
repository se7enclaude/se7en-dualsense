using System.Windows;
using System.Windows.Controls;
using System.Windows.Shapes;
using Se7en.DualSense.App.Controls;

namespace Se7en.DualSense.App.Pages;

/// <summary>Page d'une section qui sera développée dans une phase ultérieure.</summary>
public sealed class PlaceholderPage : UserControl
{
    public PlaceholderPage(string overline, string title, string subtitle, int phase)
    {
        var root = new StackPanel();

        // En-tête de page : surtitre, titre Anton majuscule, sous-titre, filet pointillé
        root.Children.Add(new TextBlock { Text = overline, Style = Find("Text.Overline"), Margin = new Thickness(0, 0, 0, 10) });
        root.Children.Add(new TextBlock { Text = title.ToUpperInvariant(), Style = Find("Text.Title") });
        root.Children.Add(new TextBlock
        {
            Text = subtitle,
            Style = Find("Text.Body"),
            MaxWidth = 520,
            HorizontalAlignment = HorizontalAlignment.Left,
            Margin = new Thickness(0, 12, 0, 24),
        });
        root.Children.Add(new Line { Style = (Style)Application.Current.FindResource("Rule.H"), Margin = new Thickness(0, 0, 0, 28) });

        var body = new StackPanel { Orientation = Orientation.Horizontal };
        var icon = new AxisIcon { Width = 18, Height = 18, VerticalAlignment = VerticalAlignment.Center };
        icon.SetResourceReference(AxisIcon.DataProperty, "Icon.Info");
        icon.SetResourceReference(ForegroundProperty, "Accent");
        body.Children.Add(icon);
        body.Children.Add(new TextBlock
        {
            Text = $"Cette section arrive à la phase {phase}.",
            Style = Find("Text.Body"),
            Margin = new Thickness(12, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center,
        });

        root.Children.Add(new ContentControl { Style = (Style)Application.Current.FindResource("Card"), Content = body });
        Content = root;
    }

    private static Style Find(string key) => (Style)Application.Current.FindResource(key);
}
