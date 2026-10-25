using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using Se7en.DualSense.App.Controls;
using Se7en.DualSense.App.Services;
using Se7en.DualSense.Hid;

namespace Se7en.DualSense.App.Pages;

/// <summary>
/// Écran d'ouverture. Chronologie (secondes) :
///   0.0 – 1.5  « se7en » puis « DUALSENSE » apparaissent lettre par lettre, la ligne lumineuse s'étire
///   1.9 – 2.5  le titre s'efface en grandissant
///   2.2 – 3.2  le décor s'allume (halo, grille, typo fantôme), la manette 3D arrive
///   2.6 – 3.7  marque, menu, accroche, état de la manette, boutons, points forts
///   ensuite    la manette 3D suit la vraie manette (et la souris), le halo suit légèrement le curseur
/// Un clic ou une touche pendant l'intro saute directement à la fin ; Entrée entre dans l'application.
/// </summary>
public partial class WelcomeView : UserControl
{
    private const double IntroEnd = 3.7;
    private const double ExitDuration = 0.45;

    private readonly Stopwatch _clock = new();
    private readonly List<TextBlock> _word1 = new(), _word2 = new();
    private readonly List<FrameworkElement> _symbols = new();
    private readonly Controller3DView? _view3D;
    private double _timeOffset;
    private double _exitStart = -1;
    private string? _exitTarget;
    private Point _mouse = new(720, 450);
    private double _parX, _parY;
    private bool _running;
    private string _lastStatus = "";

    /// <summary>Déclenché à la fin de la sortie ; argument = page demandée (null = accueil).</summary>
    public event Action<string?>? Entered;

    public WelcomeView()
    {
        InitializeComponent();
        var view = new Controller3DView();
        if (view.IsAvailable)
        {
            _view3D = view;
            ControllerHost.Content = view;
        }
        BuildIntroTitle();
        Loaded += (_, _) => Start();
        Unloaded += (_, _) => Stop();
        PreviewMouseDown += OnPreviewMouseDown;
        PreviewKeyDown += OnPreviewKeyDown;
        MouseMove += (_, e) => _mouse = e.GetPosition(Scene);
        Apply(0);
    }

    /// <summary>Une TextBlock par lettre pour pouvoir les animer une à une.</summary>
    private void BuildIntroTitle()
    {
        var brand = (FontFamily)FindResource("Font.Brand");
        var display = (FontFamily)FindResource("Font.Display");

        foreach (char ch in "se7en")
        {
            var tb = new TextBlock
            {
                Text = ch.ToString(), FontFamily = brand, FontSize = 120, Foreground = Brushes.White,
                RenderTransform = new TranslateTransform(), Opacity = 0,
            };
            IntroWord1.Children.Add(tb);
            _word1.Add(tb);
        }

        foreach (char ch in "DUALSENSE")
        {
            var tb = new TextBlock
            {
                Text = ch.ToString(), FontFamily = display, FontSize = 64,
                RenderTransform = new TranslateTransform(), Opacity = 0,
            };
            tb.SetResourceReference(TextBlock.ForegroundProperty, "Highlight");
            IntroWord2.Children.Add(tb);
            _word2.Add(tb);
        }

        // Les quatre symboles de la manette (triangle, rond, croix, carré) s'allument un à un sous la ligne lumineuse
        var row = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 26, 0, 0) };
        string[] shapes =
        [
            "M14,2 L26,24 L2,24 Z",                         // triangle
            "M14,2 A12,12 0 1 1 13.99,2 Z",                  // rond
            "M4,4 L24,24 M24,4 L4,24",                       // croix
            "M4,4 L24,4 L24,24 L4,24 Z",                     // carré
        ];
        foreach (var d in shapes)
        {
            var path = new Path
            {
                Data = Geometry.Parse(d), StrokeThickness = 2.6, Stretch = Stretch.None, Width = 28, Height = 28,
                StrokeLineJoin = PenLineJoin.Round, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round,
                Margin = new Thickness(14, 0, 14, 0), Opacity = 0, RenderTransformOrigin = new Point(.5, .5),
                RenderTransform = new TransformGroup { Children = { new ScaleTransform(), new RotateTransform() } },
            };
            path.SetResourceReference(Shape.StrokeProperty, "Highlight");
            row.Children.Add(path);
            _symbols.Add(path);
        }
        ((Panel)IntroLine.Parent).Children.Add(row);
    }

    private void Start()
    {
        if (_running) return;
        _running = true;
        _clock.Restart();
        CompositionTarget.Rendering += OnRendering;
        if (App.Controller != null) App.Controller.Frame += OnFrame;
        Focus();
    }

    private void Stop()
    {
        if (!_running) return;
        _running = false;
        CompositionTarget.Rendering -= OnRendering;
        if (App.Controller != null) App.Controller.Frame -= OnFrame;
    }

    /// <summary>La manette 3D et la pastille d'état suivent la vraie manette.</summary>
    private void OnFrame(FrameData frame)
    {
        _view3D?.Update(frame);
        string status = !frame.IsConnected
            ? "En attente de la manette · USB ou Bluetooth"
            : $"{frame.Device!.DisplayName} connectée · {frame.Device.ConnectionLabel}" +
              (frame.State.IsFullReport ? $" · {frame.State.BatteryPercent} %" : "");
        if (status == _lastStatus) return;
        _lastStatus = status;
        StatusText.Text = status;
        StatusDot.SetResourceReference(Shape.FillProperty, frame.IsConnected ? "Highlight" : "Text.Tertiary");
    }

    private double Now => _clock.Elapsed.TotalSeconds + _timeOffset;

    private void SkipIntro()
    {
        if (Now < IntroEnd)
            _timeOffset += IntroEnd - Now;
    }

    private void OnPreviewMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (Now < IntroEnd)
        {
            SkipIntro();
            e.Handled = true;
        }
    }

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (Now < IntroEnd)
        {
            SkipIntro();
            e.Handled = true;
        }
        else if (e.Key is Key.Enter or Key.Space or Key.Escape)
        {
            BeginExit(null);
            e.Handled = true;
        }
    }

    private void OnCtaClick(object sender, RoutedEventArgs e) => BeginExit(null);

    private void OnNavClick(object sender, RoutedEventArgs e) => BeginExit((sender as FrameworkElement)?.Tag as string);

    private void BeginExit(string? target)
    {
        if (_exitStart >= 0) return;
        _exitTarget = target;
        _exitStart = Now;
    }

    private void OnRendering(object? sender, EventArgs e)
    {
        double t = Now;
        Apply(t);

        if (_exitStart >= 0)
        {
            double p = EaseInCubic(Math.Clamp((t - _exitStart) / ExitDuration, 0, 1));
            Root.Opacity = 1 - p;
            Root.RenderTransform = new ScaleTransform(1 + 0.04 * p, 1 + 0.04 * p, ActualWidth / 2, ActualHeight / 2);
            if (p >= 1)
            {
                Stop();
                Visibility = Visibility.Collapsed;
                Entered?.Invoke(_exitTarget);
            }
        }
    }

    /// <summary>État complet de la scène au temps t (fonction pure du temps : sauter l'intro est trivial).</summary>
    private void Apply(double t)
    {
        // ── Intro : titre lettre par lettre ──
        for (int i = 0; i < _word1.Count; i++)
        {
            double p = EaseOutCubic(Seg(t, 0.15 + i * 0.07, 0.75 + i * 0.07));
            _word1[i].Opacity = p;
            ((TranslateTransform)_word1[i].RenderTransform).Y = Lerp(46, 0, p);
        }
        for (int i = 0; i < _word2.Count; i++)
        {
            double p = EaseOutQuint(Seg(t, 0.7 + i * 0.045, 1.4 + i * 0.045));
            _word2[i].Opacity = p;
            _word2[i].Margin = new Thickness(Lerp(14, 3, p), 0, Lerp(14, 3, p), 0);
        }
        IntroLine.Width = 440 * EaseOutQuint(Seg(t, 1.0, 1.7));
        for (int i = 0; i < _symbols.Count; i++)
        {
            double p = EaseOutCubic(Seg(t, 1.05 + i * 0.12, 1.5 + i * 0.12));
            var g = (TransformGroup)_symbols[i].RenderTransform;
            _symbols[i].Opacity = p;
            ((ScaleTransform)g.Children[0]).ScaleX = ((ScaleTransform)g.Children[0]).ScaleY = Lerp(0.2, 1, p);
            ((RotateTransform)g.Children[1]).Angle = Lerp(-120, 0, p);
        }

        double introOut = EaseInCubic(Seg(t, 1.9, 2.5));
        Intro.Opacity = 1 - introOut;
        IntroScale.ScaleX = IntroScale.ScaleY = 1 + 0.12 * introOut;
        Intro.Visibility = introOut >= 1 ? Visibility.Collapsed : Visibility.Visible;

        // ── Parallaxe douce à la souris une fois la scène installée ──
        double settle = Seg(t, 3.1, 3.7);
        double mx = Math.Clamp((_mouse.X - 720) / 720, -1, 1), my = Math.Clamp((_mouse.Y - 450) / 450, -1, 1);
        _parX += (mx - _parX) * 0.06;
        _parY += (my - _parY) * 0.06;

        // ── Décor ──
        double light = EaseOutCubic(Seg(t, 2.2, 3.2));
        Glow.Opacity = 0.5 * light;
        Glow2.Opacity = light;
        GlowShift.X = 30 * _parX * settle;
        GlowShift.Y = 20 * _parY * settle;
        Grid.Opacity = light;
        GhostText.Opacity = light;
        GhostShift.X = -12 * _parX * settle;
        GhostShift.Y = Lerp(60, 0, EaseOutQuint(Seg(t, 2.2, 3.3)));

        // ── Manette 3D ──
        double arrive = EaseOutQuint(Seg(t, 2.35, 3.3));
        ControllerHost.Opacity = arrive;
        ControllerScale.ScaleX = ControllerScale.ScaleY = Lerp(0.82, 1, arrive);
        ControllerShift.X = 8 * _parX * settle;
        ControllerShift.Y = Lerp(90, 0, arrive) + 6 * _parY * settle;
        DragHint.Opacity = Seg(t, 3.6, 4.2) * 0.9;

        // ── Barre du haut, accroche, boutons, points forts ──
        Reveal(Brand, BrandShift, Seg(t, 2.6, 3.1), 0, -12);
        Reveal(Nav, NavShift, Seg(t, 2.7, 3.2), 0, -12);
        Reveal(Overline, OverlineShift, Seg(t, 2.7, 3.3), -30, 0);
        Reveal(Headline1, Headline1Shift, Seg(t, 2.8, 3.4), -40, 0);
        Reveal(Headline2, Headline2Shift, Seg(t, 2.9, 3.5), -40, 0);
        Reveal(Subtitle, SubtitleShift, Seg(t, 3.0, 3.6), -30, 0);
        Status.Opacity = EaseOutCubic(Seg(t, 3.1, 3.6));
        Reveal(Actions, ActionsShift, Seg(t, 3.15, 3.7), 0, 14);
        Reveal(Features, FeaturesShift, Seg(t, 3.3, 3.9), 0, 14);
        Nav.IsHitTestVisible = Actions.IsHitTestVisible = t > 3.4;

        SkipHint.Opacity = t < IntroEnd ? 0.8 * Seg(t, 0.5, 0.9) * (1 - Seg(t, 1.9, 2.3)) : 0;
    }

    private static void Reveal(UIElement el, TranslateTransform shift, double p, double fromX, double fromY)
    {
        double e = EaseOutCubic(p);
        el.Opacity = e;
        shift.X = Lerp(fromX, 0, e);
        shift.Y = Lerp(fromY, 0, e);
    }

    private static double Seg(double t, double start, double end) => Math.Clamp((t - start) / (end - start), 0, 1);
    private static double Lerp(double a, double b, double p) => a + (b - a) * p;
    private static double EaseOutCubic(double p) => 1 - Math.Pow(1 - p, 3);
    private static double EaseOutQuint(double p) => 1 - Math.Pow(1 - p, 5);
    private static double EaseInCubic(double p) => p * p * p;
}
