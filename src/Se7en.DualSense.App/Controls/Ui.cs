using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Threading;
using Se7en.DualSense.Processing;

namespace Se7en.DualSense.App.Controls;

/// <summary>Petites fabriques d'éléments d'interface réutilisées par les pages de réglages.</summary>
public static class Ui
{
    public static TextBlock Text(string text, string style, Thickness? margin = null)
    {
        var tb = new TextBlock { Text = text, Margin = margin ?? new Thickness(0) };
        tb.SetResourceReference(FrameworkElement.StyleProperty, style);
        return tb;
    }

    public static ContentControl Card(UIElement content, Thickness? margin = null, double padding = 24)
    {
        var card = new ContentControl { Content = content, Margin = margin ?? new Thickness(0), Padding = new Thickness(padding) };
        card.SetResourceReference(FrameworkElement.StyleProperty, "Card");
        return card;
    }

    /// <summary>Curseur avec libellé et valeur affichée ; <paramref name="format"/> met en forme la valeur.</summary>
    public static FrameworkElement Slider(string label, double min, double max, double value, Func<double, string> format,
        Action<double> onChange, double step = 0.01, string? hint = null)
    {
        var root = new StackPanel { Margin = new Thickness(0, 0, 0, 16) };
        var head = new DockPanel();
        var readout = new TextBlock { Text = format(value) };
        readout.SetResourceReference(FrameworkElement.StyleProperty, "Text.Readout");
        DockPanel.SetDock(readout, Dock.Right);
        head.Children.Add(readout);
        head.Children.Add(Text(label, "Text.Label"));
        root.Children.Add(head);
        if (hint != null)
            root.Children.Add(Text(hint, "Text.Caption", new Thickness(0, 2, 0, 0)));

        var slider = new System.Windows.Controls.Slider
        {
            Minimum = min, Maximum = max, Value = value, SmallChange = step, LargeChange = step * 5,
            TickFrequency = step, IsSnapToTickEnabled = true, Margin = new Thickness(0, 6, 0, 0),
        };
        slider.ValueChanged += (_, e) =>
        {
            readout.Text = format(e.NewValue);
            onChange(e.NewValue);
        };
        root.Children.Add(slider);
        return root;
    }

    public static ToggleButton Switch(bool isOn, Action<bool> onChange)
    {
        var t = new ToggleButton { IsChecked = isOn };
        t.SetResourceReference(FrameworkElement.StyleProperty, "Switch");
        t.Checked += (_, _) => onChange(true);
        t.Unchecked += (_, _) => onChange(false);
        return t;
    }

    /// <summary>Contrôle segmenté (pilules) ; renvoie l'index choisi.</summary>
    public static Border Segmented(string[] options, int selected, Action<int> onChange)
    {
        var group = Guid.NewGuid().ToString("N");
        var panel = new StackPanel { Orientation = Orientation.Horizontal };
        for (int i = 0; i < options.Length; i++)
        {
            int index = i;
            var rb = new RadioButton { Content = options[i], GroupName = group, IsChecked = i == selected };
            rb.SetResourceReference(FrameworkElement.StyleProperty, "Segment");
            rb.Checked += (_, _) => onChange(index);
            panel.Children.Add(rb);
        }
        var border = new Border { CornerRadius = new CornerRadius(20), Padding = new Thickness(3), HorizontalAlignment = HorizontalAlignment.Left, Child = panel };
        border.SetResourceReference(Border.BackgroundProperty, "Surface.Inset");
        return border;
    }

    public static Button Button(string text, string style, RoutedEventHandler onClick, Thickness? margin = null)
    {
        var b = new Button { Content = text, Margin = margin ?? new Thickness(0) };
        b.SetResourceReference(FrameworkElement.StyleProperty, style);
        b.Click += onClick;
        return b;
    }

    /// <summary>Retire un élément de son conteneur actuel (pour le réutiliser lors d'une reconstruction de page).</summary>
    public static T Detach<T>(T element) where T : FrameworkElement
    {
        switch (element.Parent)
        {
            case Panel p: p.Children.Remove(element); break;
            case Decorator d: d.Child = null; break;
            case ContentControl c: c.Content = null; break;
        }
        return element;
    }

    /// <summary>Applique une configuration et renvoie l'élément (création en une expression).</summary>
    public static T With<T>(this T element, Action<T> configure) where T : FrameworkElement
    {
        configure(element);
        return element;
    }

    public static T WithMargin<T>(this T element, Thickness margin) where T : FrameworkElement
    {
        element.Margin = margin;
        return element;
    }

    public static string Percent(double v) => Math.Round(v * 100).ToString(CultureInfo.InvariantCulture) + " %";

    /// <summary>Regroupe des modifications rapides (curseurs) avant d'enregistrer.</summary>
    public sealed class Debouncer
    {
        private readonly DispatcherTimer _timer;
        private Action? _pending;

        public Debouncer(int milliseconds = 200)
        {
            _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(milliseconds) };
            _timer.Tick += (_, _) => { _timer.Stop(); var a = _pending; _pending = null; a?.Invoke(); };
        }

        public void Run(Action action)
        {
            _pending = action;
            _timer.Stop();
            _timer.Start();
        }
    }
}

/// <summary>Graphe de la courbe de réponse d'un stick, avec la zone morte grisée et la position actuelle.</summary>
public sealed class CurveGraph : LiveVisual
{
    private StickSettings _settings = new();
    private double _input = -1;

    public void SetSettings(StickSettings s)
    {
        _settings = s;
        InvalidateVisual();
    }

    /// <summary>Amplitude actuelle du stick (0-1), ou -1 pour masquer le point.</summary>
    public void SetInput(double magnitude)
    {
        if (Math.Abs(magnitude - _input) < 0.004) return;
        _input = magnitude;
        InvalidateVisual();
    }

    protected override void OnRender(DrawingContext dc)
    {
        double w = ActualWidth, h = ActualHeight;
        if (w <= 0 || h <= 0) return;
        var rect = new Rect(0, 0, w, h);
        dc.DrawRoundedRectangle(Res("Surface.Inset"), null, rect, 8, 8);

        var dashed = new Pen(Res("Border.Dashed"), 1) { DashStyle = new DashStyle(new double[] { 3, 3 }, 0) };
        dc.DrawLine(dashed, new Point(0, h), new Point(w, 0)); // référence linéaire

        double inner = _settings.InnerDeadzone, outer = _settings.OuterDeadzone;
        dc.DrawRectangle(Res("Veil"), null, new Rect(0, 0, w * inner, h));
        if (outer > 0) dc.DrawRectangle(Res("Veil"), null, new Rect(w * (1 - outer), 0, w * outer, h));

        double Out(double m)
        {
            var (x, _) = StickMath.ProcessStick(m, 0, StickCalibration.Identity, _settings with { SensitivityX = 1, InvertX = false });
            return x;
        }

        var geo = new StreamGeometry();
        using (var ctx = geo.Open())
        {
            ctx.BeginFigure(new Point(0, h), false, false);
            for (int i = 1; i <= 80; i++)
            {
                double m = i / 80.0;
                ctx.LineTo(new Point(m * w, h - Out(m) * h), true, false);
            }
        }
        geo.Freeze();
        dc.DrawGeometry(null, new Pen(Res("Accent"), 2.2) { LineJoin = PenLineJoin.Round }, geo);

        if (_input >= 0)
        {
            double m = Math.Min(_input, 1);
            var p = new Point(m * w, h - Out(m) * h);
            dc.DrawEllipse(Res("Ink"), null, p, 5, 5);
        }
    }
}
