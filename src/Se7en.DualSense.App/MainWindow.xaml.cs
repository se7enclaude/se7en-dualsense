using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Se7en.DualSense.App.Pages;
using Se7en.DualSense.App.Services;
using Se7en.DualSense.Hid;

namespace Se7en.DualSense.App;

public partial class MainWindow : Window
{
    private readonly HomePage _home = new();
    private readonly Dictionary<RadioButton, UserControl> _pages = new();

    public MainWindow()
    {
        InitializeComponent();

        _pages[NavHome] = _home;
        _pages[NavCalibration] = new PlaceholderPage("STICKS ANALOGIQUES", "Calibration",
            "Mesure du centre réel de chaque stick, zones mortes radiales et courbes de réponse pour corriger le drift.", 3);
        _pages[NavMapping] = new PlaceholderPage("PROFIL", "Mappage",
            "Réattribuez n'importe quel bouton, par profil de jeu. Le mode de sortie (DualShock 4, Xbox 360, DualSense native) se choisit dès maintenant sur l'accueil.", 5);
        _pages[NavFeedback] = new PlaceholderPage("RETOURS", "Vibrations & lumière",
            "Transmission des vibrations du jeu vers la DualSense et couleur de la barre lumineuse, personnalisée ou pilotée par le jeu.", 5);
        _pages[NavGames] = new PlaceholderPage("BIBLIOTHÈQUE", "Jeux & profils",
            "Un profil par jeu, avec son mode de sortie. se7en bascule automatiquement au lancement du jeu.", 5);

        PageHost.Content = _home;
        _home.NavigateRequested += target => (target switch
        {
            "games" => NavGames,
            "mapping" => NavMapping,
            "calibration" => NavCalibration,
            _ => NavHome,
        }).IsChecked = true;

        // Écran d'ouverture : à la sortie, ouvrir la page demandée depuis son menu
        Welcome.Entered += target =>
        {
            var nav = target switch
            {
                "calibration" => NavCalibration,
                "mapping" => NavMapping,
                "games" => NavGames,
                _ => NavHome,
            };
            nav.IsChecked = true;
        };
        if (App.SkipIntro)
            Welcome.Visibility = Visibility.Collapsed;
        UpdateThemeIcon();

        App.Controller.DeviceChanged += OnDeviceChanged;
        App.Controller.Frame += OnFrame;
        App.Controller.OutputStatusChanged += _home.OnOutputStatus;
        App.Controller.Log += _home.ShowLog;
        OnDeviceChanged(App.Controller.Device);

        StateChanged += (_, _) => Root.Margin = WindowState == WindowState.Maximized ? new Thickness(7) : new Thickness(0);
    }

    private void OnNavChecked(object sender, RoutedEventArgs e)
    {
        if (PageHost == null || sender is not RadioButton rb || !_pages.TryGetValue(rb, out var page))
            return;
        PageHost.Content = page;
        UpdateRail();
    }

    /// <summary>Rail vertical droit : raccourcis vers Jeux, Vibrations et Mappage.</summary>
    private void OnRailClick(object sender, RoutedEventArgs e)
    {
        var nav = (sender as FrameworkElement)?.Tag switch
        {
            "games" => NavGames,
            "feedback" => NavFeedback,
            "mapping" => NavMapping,
            _ => NavHome,
        };
        nav.IsChecked = true;
    }

    private void UpdateRail()
    {
        foreach (var (button, nav) in new[] { (RailGames, NavGames), (RailFeedback, NavFeedback), (RailMapping, NavMapping) })
            button.SetResourceReference(ForegroundProperty, nav.IsChecked == true ? "Text.Primary" : "Text.Tertiary");
    }

    private void OnFrame(FrameData frame)
    {
        if (ReferenceEquals(PageHost.Content, _home) && WindowState != WindowState.Minimized)
            _home.Update(frame);

        if (frame.IsConnected && frame.State.IsFullReport)
            DeviceBattery.SetLevel(frame.State.BatteryPercent, frame.State.Charging == ChargingState.Charging);
    }

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
