using System.ComponentModel;
using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using Se7en.DualSense.App.Controls;
using Se7en.DualSense.App.Pages;
using Se7en.DualSense.App.Services;
using Se7en.DualSense.Hid;
using Se7en.DualSense.Output;

namespace Se7en.DualSense.App;

public partial class MainWindow : Window, IShell
{
    /// <summary>Onglets, groupés en sections dans la barre latérale.</summary>
    private static readonly (string Section, string Key, string Label, string Icon)[] Tabs =
    {
        ("JOUER", "home", "Accueil", "Icon.House"),
        ("JOUER", "games", "Jeux & profils", "Icon.LibraryBig"),
        ("RÉGLER", "sticks", "Sticks", "Icon.Crosshair"),
        ("RÉGLER", "triggers", "Gâchettes", "Icon.Gauge"),
        ("RÉGLER", "mapping", "Mappage", "Icon.SlidersHorizontal"),
        ("RÉGLER", "gyro", "Gyroscope", "Icon.Move"),
        ("RÉGLER", "feedback", "Vibrations & lumière", "Icon.Vibrate"),
        ("SYSTÈME", "diagnostic", "Diagnostic", "Icon.Activity"),
        ("SYSTÈME", "settings", "Réglages", "Icon.Settings"),
    };

    private readonly Dictionary<string, FrameworkElement> _pages = new();
    private readonly Dictionary<string, RadioButton> _navItems = new();
    private readonly HomePage _home = new();
    private readonly SettingsPage _settings;
    private Ellipse? _driftDot;
    private string _current = "home";
    private readonly TrayService? _tray;
    private OverlayWindow? _overlay;
    private bool _quitting;
    private DualSenseButtons _lastNavButtons;
    private int _statusTick;

    public MainWindow()
    {
        InitializeComponent();
        App.Shell = this;

        _pages["home"] = _home;
        _pages["games"] = new GamesPage(this);
        _pages["sticks"] = new SticksPage(this);
        _pages["triggers"] = new TriggersPage();
        _pages["mapping"] = new MappingPage(this);
        _pages["gyro"] = new GyroPage();
        _pages["feedback"] = new FeedbackPage(this);
        _pages["diagnostic"] = new DiagnosticPage();
        _settings = new SettingsPage(this);
        _settings.InterfaceSettingsChanged += UpdateOverlay;
        _pages["settings"] = _settings;

        BuildNav();
        PageHost.Content = _home;
        _home.NavigateRequested += Navigate;

        // Écran d'ouverture : à la sortie, ouvrir la page demandée puis l'assistant de premier lancement
        Welcome.Entered += target =>
        {
            Navigate(target switch { "calibration" => "sticks", "mapping" => "mapping", "games" => "games", _ => "home" });
            ShowOnboardingIfNeeded();
        };
        if (App.SkipIntro || !AppSettings.Current.ShowIntro)
        {
            Welcome.Visibility = Visibility.Collapsed;
            Loaded += (_, _) => ShowOnboardingIfNeeded();
        }
        UpdateThemeIcon();

        var c = App.Controller;
        c.DeviceChanged += OnDeviceChanged;
        c.Frame += OnFrame;
        c.OutputStatusChanged += _home.OnOutputStatus;
        c.Log += _home.ShowLog;
        c.ActiveProfileChanged += OnProfileChanged;
        c.ProfileUpdated += OnProfileChanged;
        c.Notify += (title, message, kind) => { if (AppSettings.Current.NotifyOnConnect) Toast(title, message, kind); };
        OnDeviceChanged(c.Device);
        OnProfileChanged(c.ActiveProfile);

        PreviewKeyDown += OnShortcut;
        SizeChanged += (_, _) => UpdateCompactLayout();
        StateChanged += (_, _) => Root.Margin = WindowState == WindowState.Maximized ? new Thickness(7) : new Thickness(0);

        if (!ControllerService.IsDemo)
            _tray = new TrayService(ShowFromTray, Quit);
        if (App.StartPage != null)
            Dispatcher.BeginInvoke(() => Navigate(App.StartPage), System.Windows.Threading.DispatcherPriority.ApplicationIdle);
        UpdateOverlay();
    }

    // ───────────── Navigation ─────────────

    private void BuildNav()
    {
        string? section = null;
        foreach (var (sec, key, label, icon) in Tabs)
        {
            if (sec != section)
            {
                section = sec;
                var header = Ui.Text(sec, "Text.Overline", new Thickness(14, Nav.Children.Count == 0 ? 0 : 18, 0, 8));
                header.Tag = "section";
                Nav.Children.Add(header);
            }
            var item = new RadioButton { GroupName = "Nav", Content = label, Tag = FindResource(icon), Margin = new Thickness(0, 0, 0, 2), ToolTip = label };
            item.SetResourceReference(StyleProperty, "NavItem");
            string k = key;
            item.Checked += (_, _) => ShowPage(k);
            _navItems[key] = item;

            if (key == "sticks")
            {
                // Pastille rouge quand un drift est détecté
                var grid = new Grid();
                grid.Children.Add(item);
                _driftDot = new Ellipse { Width = 7, Height = 7, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 14, 0), Visibility = Visibility.Collapsed, IsHitTestVisible = false };
                _driftDot.SetResourceReference(Shape.FillProperty, "Danger");
                grid.Children.Add(_driftDot);
                Nav.Children.Add(grid);
            }
            else
            {
                Nav.Children.Add(item);
            }
        }
        _navItems["home"].IsChecked = true;
    }

    public void Navigate(string page)
    {
        if (_navItems.TryGetValue(page, out var item))
            item.IsChecked = true;
    }

    private void ShowPage(string key)
    {
        if (!_pages.TryGetValue(key, out var page) || PageHost == null) return;
        if (ReferenceEquals(PageHost.Content, page)) return;
        _current = key;
        PageHost.Content = page;
        PageScroll.ScrollToTop();

        // Transition : fondu + léger glissement vers le haut, sans rebond
        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
        PageHost.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(220)) { EasingFunction = ease });
        PageShift.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(10, 0, TimeSpan.FromMilliseconds(260)) { EasingFunction = ease });

        foreach (var (button, target) in new[] { (RailGames, "games"), (RailSticks, "sticks"), (RailMapping, "mapping") })
            button.SetResourceReference(ForegroundProperty, key == target ? "Text.Primary" : "Text.Tertiary");
    }

    private void OnRailClick(object sender, RoutedEventArgs e) => Navigate((string)((FrameworkElement)sender).Tag);

    private void OnProfileLinkClick(object sender, RoutedEventArgs e) => Navigate("games");

    /// <summary>Ctrl + 1…9 : onglets ; Échap : fermer la fenêtre modale.</summary>
    private void OnShortcut(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape && ModalHost.Visibility == Visibility.Visible)
        {
            CloseModal();
            e.Handled = true;
            return;
        }
        if ((Keyboard.Modifiers & ModifierKeys.Control) == 0) return;
        int index = e.Key >= Key.D1 && e.Key <= Key.D9 ? e.Key - Key.D1 : e.Key >= Key.NumPad1 && e.Key <= Key.NumPad9 ? e.Key - Key.NumPad1 : -1;
        if (index >= 0 && index < Tabs.Length)
        {
            Navigate(Tabs[index].Key);
            e.Handled = true;
        }
    }

    // ───────────── Mise en page adaptative ─────────────

    private void UpdateCompactLayout()
    {
        bool compact = ActualWidth < 1180;
        SidebarColumn.Width = new GridLength(compact ? 84 : 248);
        RailColumn.Width = new GridLength(compact ? 0 : 52);
        Rail.Visibility = RailRule.Visibility = compact ? Visibility.Collapsed : Visibility.Visible;
        Brand.Visibility = compact ? Visibility.Collapsed : Visibility.Visible;
        DeviceCard.Visibility = compact ? Visibility.Collapsed : Visibility.Visible;
        Sidebar.Margin = new Thickness(compact ? 12 : 16, 24, compact ? 12 : 16, 16);
        PageHost.Margin = new Thickness(compact ? 24 : 40, 8, compact ? 24 : 40, 40);
        foreach (var child in Nav.Children)
        {
            if (child is TextBlock { Tag: "section" } header)
                header.Visibility = compact ? Visibility.Collapsed : Visibility.Visible;
        }
        foreach (var (key, item) in _navItems)
            item.Content = compact ? null : Tabs.First(t => t.Key == key).Label;
    }

    // ───────────── IShell ─────────────

    public void ShowModal(UIElement content)
    {
        ModalCard.Child = content;
        ModalHost.Visibility = Visibility.Visible;
        ModalHost.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(180)));
    }

    public void CloseModal()
    {
        ModalHost.Visibility = Visibility.Collapsed;
        ModalCard.Child = null;
    }

    private void OnVeilMouseDown(object sender, MouseButtonEventArgs e) { /* volontairement modal : fermeture par les boutons */ }

    public void Toast(string title, string message, NotifyKind kind = NotifyKind.Info) => Toasts.Show(title, message, kind);

    public void SetDriftAlert(bool drift)
    {
        if (_driftDot != null) _driftDot.Visibility = drift ? Visibility.Visible : Visibility.Collapsed;
        _home.SetDriftAlert(drift);
    }

    // ───────────── Assistant de premier lancement ─────────────

    private void ShowOnboardingIfNeeded()
    {
        if (AppSettings.Current.OnboardingDone || ControllerService.IsDemo) return;
        new OnboardingWizard(this).Start();
    }

    // ───────────── Image par image ─────────────

    private void OnFrame(FrameData frame)
    {
        if (WindowState != WindowState.Minimized && IsVisible)
        {
            if (PageHost.Content is ILivePage live)
                live.Update(frame);
        }
        // Le détecteur de drift tourne même hors de la page Sticks
        if (!ReferenceEquals(PageHost.Content, _pages["sticks"]))
            ((ILivePage)_pages["sticks"]).Update(frame);

        if (frame.IsConnected && frame.State.IsFullReport)
            DeviceBattery.SetLevel(frame.State.BatteryPercent, frame.State.Charging == ChargingState.Charging);

        // Navigation à la manette (jamais en mode démo : les appuis simulés changeraient d'onglet)
        if (frame.IsConnected && IsActive && AppSettings.Current.ControllerNavigation && !ControllerService.IsDemo)
            HandleControllerNavigation(frame.State.Buttons);

        if (++_statusTick % 30 == 0)
            RefreshStatusText(frame);
    }

    /// <summary>Croix : déplacer le focus · ✕ : valider · ○ : retour · L1/R1 : onglet précédent/suivant.</summary>
    private void HandleControllerNavigation(DualSenseButtons buttons)
    {
        var pressed = buttons & ~_lastNavButtons;
        _lastNavButtons = buttons;
        if (pressed == DualSenseButtons.None || Welcome.Visibility == Visibility.Visible) return;

        var focused = Keyboard.FocusedElement as UIElement;
        void Move(FocusNavigationDirection dir)
        {
            if (focused == null || !IsAncestorOf(focused))
            {
                _navItems[_current].Focus();
                return;
            }
            if (focused is Slider s && dir is FocusNavigationDirection.Left or FocusNavigationDirection.Right)
            {
                s.Value = Math.Clamp(s.Value + (dir == FocusNavigationDirection.Right ? s.SmallChange : -s.SmallChange), s.Minimum, s.Maximum);
                return;
            }
            focused.MoveFocus(new TraversalRequest(dir));
        }

        if ((pressed & DualSenseButtons.DpadUp) != 0) Move(FocusNavigationDirection.Up);
        if ((pressed & DualSenseButtons.DpadDown) != 0) Move(FocusNavigationDirection.Down);
        if ((pressed & DualSenseButtons.DpadLeft) != 0) Move(FocusNavigationDirection.Left);
        if ((pressed & DualSenseButtons.DpadRight) != 0) Move(FocusNavigationDirection.Right);

        if ((pressed & DualSenseButtons.Cross) != 0 && focused != null)
        {
            switch (focused)
            {
                case RadioButton rb: rb.IsChecked = true; break;
                case ToggleButton tb: tb.IsChecked = !tb.IsChecked; break;
                case ComboBox cb: cb.IsDropDownOpen = !cb.IsDropDownOpen; break;
                case Button b when UIElementAutomationPeer.CreatePeerForElement(b)?.GetPattern(PatternInterface.Invoke) is IInvokeProvider invoke:
                    invoke.Invoke();
                    break;
            }
        }
        if ((pressed & DualSenseButtons.Circle) != 0)
        {
            if (ModalHost.Visibility == Visibility.Visible) CloseModal();
            else Navigate("home");
        }
        int index = Array.FindIndex(Tabs, t => t.Key == _current);
        if ((pressed & DualSenseButtons.L1) != 0) Navigate(Tabs[(index + Tabs.Length - 1) % Tabs.Length].Key);
        if ((pressed & DualSenseButtons.R1) != 0) Navigate(Tabs[(index + 1) % Tabs.Length].Key);
    }

    // ───────────── Manette, profil, overlay, icône ─────────────

    private void OnDeviceChanged(DualSenseDeviceInfo? device)
    {
        bool connected = device != null;
        DeviceName.Text = connected ? device!.DisplayName : "Aucune manette";
        DeviceHint.Visibility = connected ? Visibility.Collapsed : Visibility.Visible;
        DeviceBattery.Visibility = connected ? Visibility.Visible : Visibility.Collapsed;
        DeviceLinkIcon.Visibility = connected ? Visibility.Visible : Visibility.Collapsed;
        if (connected)
            DeviceLinkIcon.Data = (Geometry)FindResource(device!.Connection == ConnectionType.Usb ? "Icon.Usb" : "Icon.Bluetooth");
        DeviceIcon.SetResourceReference(Control.ForegroundProperty, connected ? "Highlight" : "Text.Tertiary");
        _home.OnDeviceChanged(device);
    }

    private void OnProfileChanged(Profile profile)
    {
        string game = App.Controller.ActiveGame != null ? " · en jeu" : "";
        ProfileLink.Content = $"{profile.Name} · {profile.Mode.Label()}{game}";
        _home.OnProfileChanged(profile);
    }

    private void RefreshStatusText(FrameData frame)
    {
        var p = App.Controller.ActiveProfile;
        string battery = frame.IsConnected && frame.State.IsFullReport ? $"{frame.State.BatteryPercent} %" : "—";
        string text = frame.IsConnected ? $"DualSense {battery} · {p.Name} · {p.Mode.Label()}" : "Aucune manette";
        _tray?.SetTooltip("se7en · " + text);
        _overlay?.SetText(text);
    }

    private void UpdateOverlay()
    {
        if (AppSettings.Current.ShowOverlay && _overlay == null)
        {
            _overlay = new OverlayWindow();
            _overlay.SetText("se7en DualSense");
            _overlay.Show();
        }
        else if (!AppSettings.Current.ShowOverlay && _overlay != null)
        {
            _overlay.Close();
            _overlay = null;
        }
    }

    private void ShowFromTray()
    {
        Show();
        if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;
        Activate();
    }

    private void Quit()
    {
        _quitting = true;
        Close();
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        if (!_quitting && AppSettings.Current.CloseToTray && _tray != null)
        {
            e.Cancel = true;
            Hide();
            _tray.SetTooltip("se7en DualSense · en arrière-plan");
            return;
        }
        base.OnClosing(e);
    }

    protected override void OnClosed(EventArgs e)
    {
        _tray?.Dispose();
        _overlay?.Close();
        base.OnClosed(e);
        Application.Current.Shutdown();
    }

    private void OnToggleTheme(object sender, RoutedEventArgs e)
    {
        ThemeManager.Toggle();
        UpdateThemeIcon();
    }

    private void UpdateThemeIcon() =>
        ThemeIcon.Data = (Geometry)FindResource(ThemeManager.Current == AppTheme.Light ? "Icon.Moon" : "Icon.Sun");

    private void OnMinimize(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

    private void OnMaximize(object sender, RoutedEventArgs e) =>
        WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;

    private void OnClose(object sender, RoutedEventArgs e) => Close();
}
