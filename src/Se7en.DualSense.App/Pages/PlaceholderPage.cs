using System.Windows;
using System.Windows.Controls;
using Se7en.DualSense.App.Controls;

namespace Se7en.DualSense.App.Pages;

/// <summary>Page d'une section qui sera développée dans une phase ultérieure.</summary>
public sealed class PlaceholderPage : UserControl
{
    public PlaceholderPage(string overline, string title, string subtitle, int phase)
    {
        var root = new StackPanel();

        root.Children.Add(new TextBlock { Text = overline, Style = Find("Text.Overline"), Margin = new Thickness(0, 0, 0, 10) });
        root.Children.Add(new TextBlock { Text = title, Style = Find("Text.H1") });
        root.Children.Add(new TextBlock
        {
            Text = subtitle,
            Style = Find("Text.Body"),
            MaxWidth = 560,
            HorizontalAlignment = HorizontalAlignment.Left,
            Margin = new Thickness(0, 10, 0, 28),
        });

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
