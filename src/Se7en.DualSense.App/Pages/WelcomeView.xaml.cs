using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Se7en.DualSense.App.Pages;

/// <summary>
/// Écran d'ouverture. Chronologie (secondes) :
///   0.0 – 1.5  « se7en » puis « DUALSENSE » apparaissent lettre par lettre, la ligne lumineuse s'étire
///   1.9 – 2.5  le titre s'efface en grandissant
///   2.2 – 3.1  « DUAL » descend, « SENSE » monte, la manette se pose au premier plan
///   2.6 – 3.6  logo, menu, accroche, sous-titre puis bouton
///   ensuite    flottement de la manette, parallaxe à la souris, curseur décoratif
/// Un clic ou une touche pendant l'intro saute directement à la fin.
/// </summary>
public partial class WelcomeView : UserControl
{
    private const double IntroEnd = 3.7;
    private const double ExitDuration = 0.45;
    private const double ControllerAngle = -7;

    private readonly Stopwatch _clock = new();
    private readonly List<TextBlock> _word1 = new(), _word2 = new();
    private double _timeOffset;
    private double _exitStart = -1;
    private string? _exitTarget;
    private Point _mouse = new(720, 450), _ring = new(720, 450), _dot = new(720, 450);
    private double _parX, _parY;
    private bool _running, _mouseSeen;

    /// <summary>Déclenché à la fin de la sortie ; argument = page demandée (null = accueil).</summary>
    public event Action<string?>? Entered;

    public WelcomeView()
    {
        InitializeComponent();
        LoadPhoto();
        BuildIntroTitle();
        Loaded += (_, _) => Start();
        Unloaded += (_, _) => Stop();
        PreviewMouseDown += OnPreviewMouseDown;
        PreviewKeyDown += OnPreviewKeyDown;
        MouseMove += (_, e) =>
        {
            _mouse = e.GetPosition(Scene);
            if (!_mouseSeen) { _mouseSeen = true; _ring = _dot = _mouse; }
        };
        Apply(0);
    }

    private void LoadPhoto()
    {
        try
        {
            var image = new BitmapImage();
            image.BeginInit();
            image.UriSource = new Uri("pack://application:,,,/Assets/Images/dualsense.png");
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.EndInit();
            image.Freeze();
            Photo.Source = image;
        }
        catch (Exception ex) when (ex is IOException or NotSupportedException)
        {
            // Photo non versionnée absente : l'accueil s'affiche sans manette
        }
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
                Text = ch.ToString(), FontFamily = brand, FontSize = 120,
                RenderTransform = new TranslateTransform(), Opacity = 0,
            };
            tb.SetResourceReference(TextBlock.ForegroundProperty, "Text.Primary");
            IntroWord1.Children.Add(tb);
            _word1.Add(tb);
        }

        var lavender = (Brush)FindResource("Gradient.Lavender");
        foreach (char ch in "DUALSENSE")
        {
            var tb = new TextBlock
            {
                Text = ch.ToString(), FontFamily = display, FontSize = 64, Foreground = lavender,
                RenderTransform = new TranslateTransform(), Opacity = 0,
            };
            IntroWord2.Children.Add(tb);
            _word2.Add(tb);
        }
    }

    private void Start()
    {
        if (_running) return;
        _running = true;
        _clock.Restart();
        CompositionTarget.Rendering += OnRendering;
        Focus();
    }

    private void Stop()
    {
        if (!_running) return;
        _running = false;
        CompositionTarget.Rendering -= OnRendering;
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

        double introOut = EaseInCubic(Seg(t, 1.9, 2.5));
        Intro.Opacity = 1 - introOut;
        IntroScale.ScaleX = IntroScale.ScaleY = 1 + 0.12 * introOut;
        Intro.Visibility = introOut >= 1 ? Visibility.Collapsed : Visibility.Visible;

        // ── Parallaxe à la souris une fois l'accueil installé ──
        double settle = Seg(t, 3.1, 3.7);
        double mx = Math.Clamp((_mouse.X - 720) / 720, -1, 1), my = Math.Clamp((_mouse.Y - 450) / 450, -1, 1);
        _parX += (mx - _parX) * 0.08;
        _parY += (my - _parY) * 0.08;

        // ── Manette ──
        double arrive = EaseOutQuint(Seg(t, 2.35, 3.2));
        double idle = Math.Max(0, t - 3.2);
        double bob = Math.Sin(idle * 2 * Math.PI / 3.6) * Math.Min(1, idle);
        Controller.Opacity = arrive;
        ControllerScale.ScaleX = ControllerScale.ScaleY = Lerp(0.9, 1, arrive);
        ControllerRotate.Angle = Lerp(ControllerAngle - 6, ControllerAngle, arrive) + 1.5 * _parX * settle;
        ControllerShift.X = 10 * _parX * settle;
        ControllerShift.Y = Lerp(140, 0, arrive) - 9 * bob + 6 * _parY * settle;

        ControllerShadow.Opacity = 0.9 * arrive;
        double lift = (bob + 1) / 2;
        ShadowScale.ScaleX = ShadowScale.ScaleY = 1 - 0.08 * lift * Math.Min(1, idle);

        double glow = 0.4 + 0.25 * Math.Sin(t * 2 * Math.PI / 2.8);
        LightbarGlowL.Opacity = LightbarGlowR.Opacity = glow;

        // ── Typo géante ──
        double dual = EaseOutQuint(Seg(t, 2.2, 3.0));
        double sense = EaseOutQuint(Seg(t, 2.3, 3.1));
        DualText.Opacity = dual;
        DualShift.Y = Lerp(-480, 0, dual) - 6 * _parY * settle;
        DualShift.X = -8 * _parX * settle;
        SenseText.Opacity = sense;
        SenseShift.Y = Lerp(480, 0, sense) - 6 * _parY * settle;
        SenseShift.X = -8 * _parX * settle;

        // ── Logo, menu, accroche ──
        double logo = EaseOutCubic(Seg(t, 2.6, 3.1));
        Logo.Opacity = logo;
        LogoScale.ScaleX = LogoScale.ScaleY = Lerp(0.6, 1, logo);

        double nav = EaseOutCubic(Seg(t, 2.7, 3.3));
        Nav.Opacity = nav;
        NavShift.Y = Lerp(-12, 0, nav);

        SlideIn(Headline1, Headline1Shift, Seg(t, 2.7, 3.4));
        SlideIn(Headline2, Headline2Shift, Seg(t, 2.8, 3.5));
        SlideIn(Subtitle, SubtitleShift, Seg(t, 2.9, 3.6));

        double cta = EaseOutCubic(Seg(t, 3.0, 3.6));
        CtaButton.Opacity = cta;
        CtaScale.ScaleX = CtaScale.ScaleY = Lerp(0.9, 1, cta);
        CtaButton.IsHitTestVisible = cta > 0.9;
        ProfilesButton.Opacity = cta;
        ProfilesButton.IsHitTestVisible = cta > 0.9;
        Grid.Opacity = EaseOutCubic(Seg(t, 2.4, 3.2));
        Nav.IsHitTestVisible = nav > 0.9;

        SkipHint.Opacity = t < IntroEnd ? 0.8 * Seg(t, 0.5, 0.9) * (1 - Seg(t, 1.9, 2.3)) : 0;

        // ── Curseur décoratif ──
        CursorLayer.Opacity = _mouseSeen ? Seg(t, 3.3, 3.8) : 0;
        _dot = new Point(_dot.X + (_mouse.X - _dot.X) * 0.45, _dot.Y + (_mouse.Y - _dot.Y) * 0.45);
        _ring = new Point(_ring.X + (_mouse.X - _ring.X) * 0.14, _ring.Y + (_mouse.Y - _ring.Y) * 0.14);
        Canvas.SetLeft(CursorDot, _dot.X - 16);
        Canvas.SetTop(CursorDot, _dot.Y - 40);
        Canvas.SetLeft(CursorRing, _ring.X - 43);
        Canvas.SetTop(CursorRing, _ring.Y - 67);
    }

    private static void SlideIn(UIElement el, TranslateTransform shift, double p)
    {
        double e = EaseOutCubic(p);
        el.Opacity = e;
        shift.X = Lerp(-40, 0, e);
    }

    private static double Seg(double t, double start, double end) => Math.Clamp((t - start) / (end - start), 0, 1);
    private static double Lerp(double a, double b, double p) => a + (b - a) * p;
    private static double EaseOutCubic(double p) => 1 - Math.Pow(1 - p, 3);
    private static double EaseOutQuint(double p) => 1 - Math.Pow(1 - p, 5);
    private static double EaseInCubic(double p) => p * p * p;
}
