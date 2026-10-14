using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using Se7en.DualSense.App.Controls;
using Se7en.DualSense.App.Services;
using Se7en.DualSense.Hid;
using Se7en.DualSense.Output;

namespace Se7en.DualSense.App.Pages;

/// <summary>
/// Lanceur de jeux plein écran, piloté à la manette : la bibliothèque détectée (Steam, Epic, Rockstar)
/// en jaquettes ; croix / stick pour choisir, ✕ pour lancer, ○ pour fermer.
/// </summary>
public sealed class LauncherWindow : Window
{
    private const double CardW = 200, CardH = 300;
    private static LauncherWindow? _open;

    private readonly WrapPanel _grid = new() { HorizontalAlignment = HorizontalAlignment.Center };
    private readonly ScrollViewer _scroll;
    private readonly TextBlock _title = new(), _subtitle = new(), _clock = new();
    private readonly PadNavigator _nav = new();
    private readonly List<(InstalledGame Game, Border Card)> _cards = new();
    private int _index;
    private bool _launching;

    /// <summary>Ouvre le lanceur (ou le referme s'il est déjà ouvert).</summary>
    public static void Toggle()
    {
        if (_open != null) { _open.Close(); return; }
        _open = new LauncherWindow();
        _open.Show();
        _open.Activate();
    }

    private LauncherWindow()
    {
        Title = "se7en · Lanceur";
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        WindowState = WindowState.Maximized;
        Topmost = true;
        Background = new SolidColorBrush(Color.FromRgb(0x0B, 0x0B, 0x0E));
        Foreground = Brushes.White;
        SetResourceReference(FontFamilyProperty, "Font.Sans");

        var root = new DockPanel { Margin = new Thickness(72, 48, 72, 36) };

        // En-tête : marque, titre du jeu sélectionné, heure
        var head = new DockPanel { Margin = new Thickness(0, 0, 0, 28) };
        _clock.FontSize = 22;
        _clock.Opacity = 0.7;
        _clock.SetResourceReference(TextBlock.FontFamilyProperty, "Font.Mono");
        DockPanel.SetDock(_clock, Dock.Right);
        head.Children.Add(_clock);
        var brand = new TextBlock { Text = "se7en", FontSize = 30, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 28, 0) };
        brand.SetResourceReference(TextBlock.FontFamilyProperty, "Font.Brand");
        head.Children.Add(brand);
        var titles = new StackPanel();
        _title.FontSize = 40;
        _title.SetResourceReference(TextBlock.FontFamilyProperty, "Font.Display");
        _title.TextTrimming = TextTrimming.CharacterEllipsis;
        _subtitle.FontSize = 15;
        _subtitle.Opacity = 0.6;
        titles.Children.Add(_title);
        titles.Children.Add(_subtitle);
        head.Children.Add(titles);
        DockPanel.SetDock(head, Dock.Top);
        root.Children.Add(head);

        // Pied : aide des boutons
        var hints = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 24, 0, 0) };
        foreach (var (glyph, label) in new[] { ("✕", "Lancer"), ("○", "Fermer"), ("△", "Actualiser") })
        {
            var g = new TextBlock { Text = glyph, Foreground = Brushes.White, FontSize = 15, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
            g.SetResourceReference(TextBlock.FontFamilyProperty, "Font.Glyph");
            hints.Children.Add(new Border { Width = 34, Height = 34, CornerRadius = new CornerRadius(17), Background = new SolidColorBrush(Color.FromArgb(0x30, 0xFF, 0xFF, 0xFF)), Child = g });
            hints.Children.Add(new TextBlock { Text = label, FontSize = 15, Opacity = 0.75, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(10, 0, 32, 0) });
        }
        DockPanel.SetDock(hints, Dock.Bottom);
        root.Children.Add(hints);

        _scroll = new ScrollViewer { Content = _grid, VerticalScrollBarVisibility = ScrollBarVisibility.Hidden, Focusable = false };
        root.Children.Add(_scroll);
        Content = root;

        var tick = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        tick.Tick += (_, _) => _clock.Text = DateTime.Now.ToString("HH:mm");
        tick.Start();
        _clock.Text = DateTime.Now.ToString("HH:mm");

        Loaded += async (_, _) => await LoadAsync();
        App.Controller.Frame += OnFrame;
        App.Controller.SetActionsSuppressed(true);
        App.SuspendControllerNavigation = true;
        PreviewKeyDown += OnKey;
        Closed += (_, _) =>
        {
            tick.Stop();
            App.Controller.Frame -= OnFrame;
            App.Controller.SetActionsSuppressed(false);
            App.SuspendControllerNavigation = false;
            _open = null;
        };
    }

    private async Task LoadAsync()
    {
        _title.Text = "BIBLIOTHÈQUE";
        _subtitle.Text = "Recherche des jeux installés…";
        var games = await Task.Run(GameLibrary.Detect);
        _grid.Children.Clear();
        _cards.Clear();
        foreach (var g in games)
        {
            var card = Card(g);
            _cards.Add((g, card));
            _grid.Children.Add(card);
        }
        if (_cards.Count == 0)
        {
            _title.Text = "AUCUN JEU TROUVÉ";
            _subtitle.Text = "Steam, Epic Games et Rockstar Games sont détectés automatiquement.";
            return;
        }
        _index = Math.Clamp(_index, 0, _cards.Count - 1);
        Select(_index, animate: false);
    }

    private Border Card(InstalledGame g)
    {
        UIElement content;
        if (g.CoverPath != null && File.Exists(g.CoverPath))
        {
            var img = new BitmapImage();
            img.BeginInit();
            img.UriSource = new Uri(g.CoverPath);
            img.DecodePixelWidth = 300;
            img.CacheOption = BitmapCacheOption.OnLoad;
            img.EndInit();
            content = new Image { Source = img, Stretch = Stretch.UniformToFill };
        }
        else
        {
            // Pas de jaquette : nom du jeu sur fond dégradé
            var text = new TextBlock
            {
                Text = g.Name.ToUpperInvariant(), FontSize = 26, TextWrapping = TextWrapping.Wrap, Foreground = Brushes.White,
                VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(18), LineHeight = 28, LineStackingStrategy = LineStackingStrategy.BlockLineHeight,
            };
            text.SetResourceReference(TextBlock.FontFamilyProperty, "Font.Display");
            content = new Grid
            {
                Background = new LinearGradientBrush(Color.FromRgb(0x2A, 0x2A, 0x33), Color.FromRgb(0x14, 0x14, 0x18), 90),
                Children = { text },
            };
        }
        var card = new Border
        {
            Width = CardW, Height = CardH, Margin = new Thickness(14), CornerRadius = new CornerRadius(14),
            BorderThickness = new Thickness(3), BorderBrush = Brushes.Transparent, ClipToBounds = true,
            Child = new Border { CornerRadius = new CornerRadius(11), ClipToBounds = true, Child = content },
            RenderTransformOrigin = new Point(0.5, 0.5), RenderTransform = new ScaleTransform(1, 1), Opacity = 0.72, Cursor = Cursors.Hand,
        };
        card.MouseLeftButtonUp += (_, _) => { Select(_cards.FindIndex(c => c.Card == card), true); Launch(); };
        return card;
    }

    private void Select(int index, bool animate = true)
    {
        if (_cards.Count == 0) return;
        index = Math.Clamp(index, 0, _cards.Count - 1);
        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
        for (int i = 0; i < _cards.Count; i++)
        {
            var card = _cards[i].Card;
            bool sel = i == index;
            double scale = sel ? 1.08 : 1;
            var st = (ScaleTransform)card.RenderTransform;
            var d = TimeSpan.FromMilliseconds(animate ? 180 : 0);
            st.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(scale, d) { EasingFunction = ease });
            st.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(scale, d) { EasingFunction = ease });
            card.BeginAnimation(OpacityProperty, new DoubleAnimation(sel ? 1 : 0.62, d));
            if (sel) card.SetResourceReference(Border.BorderBrushProperty, "Accent"); else card.BorderBrush = Brushes.Transparent;
            card.Effect = sel ? new DropShadowEffect { Color = (Color)FindResource("Color.Accent"), BlurRadius = 40, ShadowDepth = 0, Opacity = 0.7 } : null;
        }
        _index = index;
        var g = _cards[index].Game;
        _title.Text = g.Name.ToUpperInvariant();
        var profile = App.Controller.Profiles.All.FirstOrDefault(p => g.Executable != null && p.Executables.Contains(Path.GetFileName(g.Executable), StringComparer.OrdinalIgnoreCase));
        _subtitle.Text = g.Store + (profile != null ? $" · profil « {profile.Name} » ({profile.Mode.Label()})" : $" · profil actuel ({App.Controller.ActiveProfile.Mode.Label()})");
        _cards[index].Card.BringIntoView(new Rect(-40, -60, CardW + 80, CardH + 120));
    }

    private int Columns => Math.Max(1, (int)(_grid.ActualWidth / (CardW + 28)));

    private void OnFrame(FrameData frame)
    {
        _nav.Update(frame);
        if (_nav.Move.X != 0) Select(_index + _nav.Move.X);
        if (_nav.Move.Y != 0) Select(_index + _nav.Move.Y * Columns);
        if (_nav.Was(DualSenseButtons.Cross)) Launch();
        if (_nav.Was(DualSenseButtons.Circle) || _nav.Was(DualSenseButtons.PS)) Close();
        if (_nav.Was(DualSenseButtons.Triangle)) _ = LoadAsync();
    }

    private void OnKey(object sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Left: Select(_index - 1); break;
            case Key.Right: Select(_index + 1); break;
            case Key.Up: Select(_index - Columns); break;
            case Key.Down: Select(_index + Columns); break;
            case Key.Enter: Launch(); break;
            case Key.Escape: Close(); break;
            default: return;
        }
        e.Handled = true;
    }

    private void Launch()
    {
        if (_launching || _cards.Count == 0) return;
        var g = _cards[_index].Game;
        string? target = g.LaunchUri ?? g.Executable;
        if (target == null)
        {
            _subtitle.Text = "Impossible de trouver l'exécutable de ce jeu.";
            SoundService.Play(UiSound.Warning);
            return;
        }
        _launching = true;
        SoundService.Play(UiSound.Success);
        try
        {
            var psi = new ProcessStartInfo(target) { UseShellExecute = true };
            if (g.LaunchUri == null) psi.WorkingDirectory = Path.GetDirectoryName(target) ?? "";
            Process.Start(psi);
            App.Shell?.Toast("Lancement", g.Name, NotifyKind.Success);
            Close();
        }
        catch (Exception ex)
        {
            _launching = false;
            _subtitle.Text = "Lancement impossible : " + ex.Message;
        }
    }
}
