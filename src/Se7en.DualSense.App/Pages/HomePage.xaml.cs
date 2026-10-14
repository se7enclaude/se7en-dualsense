using System.Diagnostics;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Se7en.DualSense.App.Controls;
using Se7en.DualSense.App.Services;
using Se7en.DualSense.Hid;
using Se7en.DualSense.Output;

namespace Se7en.DualSense.App.Pages;

public partial class HomePage : UserControl, ILivePage
{
    private readonly Stopwatch _rateClock = Stopwatch.StartNew();
    private long _rateCount;
    private string _lastLeft = "", _lastRight = "", _lastTouch = "", _lastStamp = "";
    private int _lastL2 = -1, _lastR2 = -1;
    private readonly List<(DualSenseButtons Flag, ButtonGlyph Glyph, string Name)> _glyphs = new();
    private DualSenseButtons _lastButtons = (DualSenseButtons)uint.MaxValue;
    private bool _syncingMode;
    private DriverBannerAction _bannerAction;
    private readonly Controller3DView? _view3D;
    private readonly DualSenseView? _view2D;

    private enum DriverBannerAction { None, Install, Elevate, Restart }

    /// <summary>Demande d'ouverture d'une page ("games", "mapping"…).</summary>
    public event Action<string>? NavigateRequested;

    public HomePage()
    {
        InitializeComponent();
        // Manette 3D temps réel si le modèle est présent, sinon le rendu 2D
        var view3D = new Controller3DView();
        if (view3D.IsAvailable)
        {
            _view3D = view3D;
            ControllerHost.Content = view3D;
        }
        else
        {
            _view2D = new DualSenseView { ShowInputOverlays = false, IsHitTestVisible = false };
            ControllerHost.Content = _view2D;
        }
        BuildButtonGlyphs();
        int hour = DateTime.Now.Hour;
        Greeting.Text = hour is >= 5 and < 18 ? "BONJOUR" : "BONSOIR";
        DemoBadge.Visibility = ControllerService.IsDemo ? Visibility.Visible : Visibility.Collapsed;
        SyncModeSelector(App.Controller?.ActiveProfile.Mode ?? OutputMode.DualShock4);
        RefreshDriverBanner();
    }

    // ───────────── Mode de sortie ─────────────

    private void OnModeChecked(object sender, RoutedEventArgs e)
    {
        if (_syncingMode || App.Controller == null) return;
        var mode = sender == ModeXbox ? OutputMode.Xbox360 : sender == ModeNative ? OutputMode.Native : sender == ModeKbm ? OutputMode.KeyboardMouse : OutputMode.DualShock4;
        App.Controller.SetOutputMode(mode);
        ModeMessage.Text = "Changement de mode…";
    }

    private void SyncModeSelector(OutputMode mode)
    {
        _syncingMode = true;
        (mode switch { OutputMode.Xbox360 => ModeXbox, OutputMode.Native => ModeNative, OutputMode.KeyboardMouse => ModeKbm, _ => ModeDs4 }).IsChecked = true;
        _syncingMode = false;
        OutputBadge.Text = mode.Label();
        ModeStat.Text = mode switch { OutputMode.Xbox360 => "XBOX 360", OutputMode.Native => "NATIVE", OutputMode.KeyboardMouse => "CLAVIER", _ => "DS4" };
    }

    public void OnOutputStatus(OutputStatus status)
    {
        SyncModeSelector(status.Mode);
        ModeMessage.Text = status.Message;
        bool warn = status.Mode == OutputMode.Native || (status.Mode.IsVirtual() && status.PadActive && !status.DeviceHidden)
                    || (status.Mode == OutputMode.KeyboardMouse && !status.DeviceHidden);
        ModeMessage.SetResourceReference(TextBlock.ForegroundProperty, warn ? "Warning.Text" : "Text.Secondary");
    }

    public void ShowLog(string message) => ModeMessage.Text = message;

    // ───────────── Pilotes ─────────────

    private void RefreshDriverBanner()
    {
        if (ControllerService.IsDemo)
            return;
        var drivers = DriverStatus.Detect();
        if (!drivers.AllInstalled)
        {
            var missing = new List<string>();
            if (!drivers.ViGEmBusInstalled) missing.Add("ViGEmBus (manettes virtuelles)");
            if (!drivers.HidHideInstalled) missing.Add("HidHide (évite la double entrée)");
            ShowBanner("Pilotes manquants",
                "Nécessaires pour jouer avec la DualSense : " + string.Join(", ", missing) + ". Installation guidée depuis les sources officielles.",
                "Installer", DriverBannerAction.Install);
        }
        else if (App.ElevationDeclined)
        {
            ShowBanner("Droits administrateur requis",
                "Sans eux, la DualSense ne peut pas être masquée : un jeu peut voir deux manettes.",
                "Relancer en administrateur", DriverBannerAction.Elevate);
        }
        else
        {
            DriverBanner.Visibility = Visibility.Collapsed;
            _bannerAction = DriverBannerAction.None;
        }
    }

    private void ShowBanner(string title, string text, string button, DriverBannerAction action)
    {
        DriverTitle.Text = title;
        DriverText.Text = text;
        DriverButton.Content = button;
        DriverButton.IsEnabled = true;
        _bannerAction = action;
        DriverBanner.Visibility = Visibility.Visible;
    }

    private async void OnDriverButtonClick(object sender, RoutedEventArgs e)
    {
        switch (_bannerAction)
        {
            case DriverBannerAction.Install:
                await InstallDriversAsync();
                break;
            case DriverBannerAction.Elevate:
            case DriverBannerAction.Restart:
                RestartApplication(elevated: true);
                break;
        }
    }

    private async Task InstallDriversAsync()
    {
        var drivers = DriverStatus.Detect();
        var toInstall = new List<DriverKind>();
        if (!drivers.ViGEmBusInstalled) toInstall.Add(DriverKind.ViGEmBus);
        if (!drivers.HidHideInstalled) toInstall.Add(DriverKind.HidHide);
        if (toInstall.Count == 0) { RefreshDriverBanner(); return; }

        string list = string.Join(" et ", toInstall.Select(k => k.DisplayName()));
        var answer = MessageBox.Show(
            $"se7en DualSense va télécharger puis installer {list} depuis les publications officielles de Nefarius sur GitHub " +
            "(quelques Mo chacun). Windows vous demandera l'autorisation administrateur, puis un assistant d'installation s'ouvrira.\n\n" +
            "Continuer ?",
            "Installer les pilotes", MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (answer != MessageBoxResult.Yes)
            return;

        DriverButton.IsEnabled = false;
        var progress = new Progress<string>(msg => DriverText.Text = msg);
        bool allOk = true;
        foreach (var kind in toInstall)
        {
            try
            {
                allOk &= await DriverInstaller.InstallAsync(kind, progress);
            }
            catch (Exception ex)
            {
                allOk = false;
                DriverText.Text = $"Échec de l'installation de {kind.DisplayName()} : {ex.Message}";
            }
        }

        if (allOk)
            ShowBanner("Pilotes installés",
                "Redémarrez se7en DualSense pour les activer. Si l'installeur l'a demandé, redémarrez d'abord Windows.",
                "Redémarrer l'application", DriverBannerAction.Restart);
        else
            DriverButton.IsEnabled = true;
    }

    private static void RestartApplication(bool elevated)
    {
        string? exe = Environment.ProcessPath;
        if (exe == null) return;
        try
        {
            var psi = new ProcessStartInfo(exe, "--skip-intro") { UseShellExecute = true };
            if (elevated) psi.Verb = "runas";
            Process.Start(psi);
            Application.Current.Shutdown();
        }
        catch (System.ComponentModel.Win32Exception)
        {
            // Élévation refusée : on reste sur l'instance actuelle
        }
    }

    /// <summary>Glyphes groupés comme sur la manette : façade, croix, gâchettes et sticks, système.</summary>
    private void BuildButtonGlyphs()
    {
        (DualSenseButtons, string, string, GlyphShape)[][] groups =
        [
            [
                (DualSenseButtons.Triangle, "△", "Triangle", GlyphShape.Circle), (DualSenseButtons.Circle, "○", "Rond", GlyphShape.Circle),
                (DualSenseButtons.Cross, "✕", "Croix", GlyphShape.Circle), (DualSenseButtons.Square, "□", "Carré", GlyphShape.Circle),
            ],
            [
                (DualSenseButtons.DpadUp, "↑", "Haut", GlyphShape.Rounded), (DualSenseButtons.DpadDown, "↓", "Bas", GlyphShape.Rounded),
                (DualSenseButtons.DpadLeft, "←", "Gauche", GlyphShape.Rounded), (DualSenseButtons.DpadRight, "→", "Droite", GlyphShape.Rounded),
            ],
            [
                (DualSenseButtons.L1, "L1", "L1", GlyphShape.Pill), (DualSenseButtons.R1, "R1", "R1", GlyphShape.Pill),
                (DualSenseButtons.L2, "L2", "L2", GlyphShape.Pill), (DualSenseButtons.R2, "R2", "R2", GlyphShape.Pill),
                (DualSenseButtons.L3, "L3", "L3", GlyphShape.Pill), (DualSenseButtons.R3, "R3", "R3", GlyphShape.Pill),
            ],
            [
                (DualSenseButtons.Create, "CREATE", "Create", GlyphShape.Pill), (DualSenseButtons.Options, "OPTIONS", "Options", GlyphShape.Pill),
                (DualSenseButtons.PS, "PS", "PS", GlyphShape.Pill), (DualSenseButtons.Touchpad, "PAVÉ", "Pavé", GlyphShape.Pill),
                (DualSenseButtons.Mute, "MICRO", "Micro", GlyphShape.Pill),
            ],
        ];

        foreach (var group in groups)
        {
            var panel = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 28, 8) };
            foreach (var (flag, text, name, shape) in group)
            {
                var glyph = new ButtonGlyph { Text = text, Shape = shape, Margin = new Thickness(0, 0, 8, 0), ToolTip = name };
                panel.Children.Add(glyph);
                _glyphs.Add((flag, glyph, name));
            }
            ButtonsPanel.Children.Add(panel);
        }
    }

    private void UpdateButtons(DualSenseButtons buttons)
    {
        if (buttons == _lastButtons) return;
        _lastButtons = buttons;
        var pressed = new List<string>();
        foreach (var (flag, glyph, name) in _glyphs)
        {
            bool down = (buttons & flag) != 0;
            glyph.IsPressed = down;
            if (down) pressed.Add(name);
        }
        ButtonsReadout.Text = pressed.Count == 0 ? "Aucune touche" : string.Join(" + ", pressed);
    }

    private void OnProfilesClick(object sender, RoutedEventArgs e) => NavigateRequested?.Invoke("games");

    private void OnLauncherClick(object sender, RoutedEventArgs e) => LauncherWindow.Toggle();

    private void OnDesktopClick(object sender, RoutedEventArgs e) => App.Controller.ToggleDesktopMode();

    // ───────────── Steam Input ─────────────

    private DateTime _lastSteamCheck;

    private async void RefreshSteamHint()
    {
        if (ControllerService.IsDemo || AppSettings.Current.SteamHintDismissed) { SteamCard.Visibility = Visibility.Collapsed; return; }
        var status = await Task.Run(SteamInput.Detect);
        var mode = App.Controller.ActiveProfile.Mode;
        bool show = SteamInput.MayConflict(status, mode);
        SteamCard.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
        SteamText.Text = mode == OutputMode.Xbox360
            ? "La prise en charge des manettes Xbox de Steam est active : dans un jeu Steam, elle peut doubler les appuis. Désactivez Steam Input pour ce jeu en cas de souci."
            : "La prise en charge PlayStation de Steam peut capter la manette et doubler les appuis dans les jeux Steam. Désactivez-la, ou Steam Input pour ce jeu.";
    }

    private void OnSteamDismiss(object sender, RoutedEventArgs e)
    {
        AppSettings.Current.SteamHintDismissed = true;
        AppSettings.Save();
        SteamCard.Visibility = Visibility.Collapsed;
    }

    private void OnSteamGuide(object sender, RoutedEventArgs e)
    {
        var shell = App.Shell;
        if (shell == null) return;
        var body = new StackPanel { Width = 540 };
        body.Children.Add(Ui.Text("STEAM INPUT", "Text.Overline", new Thickness(0, 0, 0, 10)));
        body.Children.Add(Ui.Text("Éviter les doubles appuis", "Text.H1", new Thickness(0, 0, 0, 12)));
        body.Children.Add(Ui.Text(
            "Steam peut « traduire » lui-même les manettes PlayStation et Xbox. Avec se7en, c'est inutile et cela peut doubler les appuis.\n\n" +
            "Pour tous les jeux : Steam › Paramètres › Manette › « Prise en charge des manettes PlayStation » : Désactivé.\n\n" +
            "Pour un seul jeu : clic droit sur le jeu dans la bibliothèque › Propriétés › Manette › « Désactiver Steam Input ».\n\n" +
            "Astuce : en mode Xbox 360, gardez la prise en charge Xbox de Steam sur « Désactivé » ou sur la valeur par défaut.",
            "Text.Body"));
        var actions = new DockPanel { Margin = new Thickness(0, 24, 0, 0), LastChildFill = false };
        actions.Children.Add(Ui.Button("Fermer", "Button.Secondary", (_, _) => shell.CloseModal()));
        var open = Ui.Button("Ouvrir les réglages de Steam", "Button.Primary", (_, _) => { SteamInput.OpenSteamControllerSettings(); shell.CloseModal(); });
        DockPanel.SetDock(open, Dock.Right);
        actions.Children.Add(open);
        body.Children.Add(actions);
        shell.ShowModal(body);
    }

    private void OnFixDriftClick(object sender, RoutedEventArgs e) => NavigateRequested?.Invoke("sticks");

    public void SetDriftAlert(bool drift)
    {
        var v = drift ? Visibility.Visible : Visibility.Collapsed;
        if (DriftCard.Visibility != v) DriftCard.Visibility = v;
    }

    public void OnProfileChanged(Profile profile)
    {
        ProfileBadge.Text = "Profil · " + profile.Name;
        DesktopCaption.Text = profile.Id == Profile.DesktopId ? "Actif · cliquer pour revenir" : "La manette pilote la souris";
        SyncModeSelector(profile.Mode);
        RefreshSteamHint();
    }

    private void OnCustomizeClick(object sender, RoutedEventArgs e) => NavigateRequested?.Invoke("sticks");

    public void OnDeviceChanged(DualSenseDeviceInfo? device)
    {
        bool connected = device != null;
        StatusBadge.Text = connected ? "Connectée" : "Déconnectée";
        StatusBadge.Tone = connected ? BadgeTone.Success : BadgeTone.Neutral;
        LinkBadge.Visibility = connected ? Visibility.Visible : Visibility.Collapsed;

        if (connected)
        {
            LinkBadge.Text = device!.ConnectionLabel;
            LinkValue.Text = device.ConnectionLabel.ToUpperInvariant();
            LinkIcon.Data = (Geometry)FindResource(device.Connection == ConnectionType.Usb ? "Icon.Usb" : "Icon.Bluetooth");
            HeroHint.Text = $"{device.DisplayName} connectée en {device.ConnectionLabel}. " +
                            "Appuyez sur un bouton, bougez les sticks ou touchez le pavé : la manette réagit en direct.";
        }
        else
        {
            LinkValue.Text = "AUCUNE";
            RateValue.Text = "—";
            HeroHint.Text = "En attente de la manette. Branchez-la en USB ou connectez-la en Bluetooth : elle est détectée automatiquement.";
            SetStamp("—");
        }
    }

    public void Update(in FrameData frame)
    {
        _view3D?.Update(frame);
        _view2D?.Update(frame);

        var s = frame.State;
        if (!frame.IsConnected)
        {
            LeftStickViz.SetPosition(0, 0);
            RightStickViz.SetPosition(0, 0);
            L2Meter.SetValue(0);
            R2Meter.SetValue(0);
            TouchViz.SetTouches(default, default, false);
            UpdateButtons(DualSenseButtons.None);
            return;
        }

        if (frame.HasNewReport)
        {
            UpdateButtons(s.Buttons);
            UpdateStick(s.LeftX, s.LeftY, LeftStickViz, LeftStickReadout, LeftStickRaw, ref _lastLeft);
            UpdateStick(s.RightX, s.RightY, RightStickViz, RightStickReadout, RightStickRaw, ref _lastRight);

            L2Meter.SetValue(s.L2 / 255.0);
            R2Meter.SetValue(s.R2 / 255.0);
            int l2 = (int)Math.Round(s.L2 / 2.55), r2 = (int)Math.Round(s.R2 / 2.55);
            if (l2 != _lastL2) { L2Readout.Text = l2 + "%"; _lastL2 = l2; }
            if (r2 != _lastR2) { R2Readout.Text = r2 + "%"; _lastR2 = r2; }

            TouchViz.SetTouches(s.Touch0, s.Touch1, s.IsPressed(DualSenseButtons.Touchpad));
            string touch = (s.Touch0.Active, s.Touch1.Active) switch
            {
                (false, false) => "Aucun contact",
                (true, false) => $"x {s.Touch0.X,4}  y {s.Touch0.Y,4}",
                (false, true) => $"x {s.Touch1.X,4}  y {s.Touch1.Y,4}",
                _ => "2 doigts",
            };
            if (touch != _lastTouch) { TouchReadout.Text = touch; _lastTouch = touch; }

            if (s.IsFullReport)
                SetStamp(s.BatteryPercent + "%");
        }

        // Autonomie restante et Steam : vérifiés toutes les 30 s
        if (DateTime.UtcNow - _lastSteamCheck > TimeSpan.FromSeconds(30))
        {
            _lastSteamCheck = DateTime.UtcNow;
            RefreshSteamHint();
            var left = App.Controller.BatteryRemaining();
            BatteryCaption.Text = Stamp.Caption = s.Charging != ChargingState.Discharging ? (s.Charging == ChargingState.Full ? "CHARGÉE" : "EN CHARGE")
                : left is { } t ? "≈ " + BatteryTracker.Format(t).ToUpperInvariant() : "BATTERIE";
        }

        if (_rateClock.ElapsedMilliseconds >= 1000)
        {
            long count = App.Controller.Connection.ReportCount;
            double hz = (count - _rateCount) * 1000.0 / _rateClock.ElapsedMilliseconds;
            _rateCount = count;
            _rateClock.Restart();
            RateValue.Text = hz.ToString("0", CultureInfo.InvariantCulture) + " HZ";
        }
    }

    private void SetStamp(string value)
    {
        if (value == _lastStamp) return;
        _lastStamp = value;
        Stamp.Value = value;
        BatteryValue.Text = value.Replace("%", " %");
        BatteryBar.Width = int.TryParse(value.TrimEnd('%'), out int pct) ? 120 * pct / 100.0 : 0;
    }

    private static void UpdateStick(byte rawX, byte rawY, StickVisualizer viz, TextBlock readout, TextBlock raw, ref string last)
    {
        double x = Math.Clamp((rawX - 127.5) / 127.5, -1, 1);
        double y = Math.Clamp((rawY - 127.5) / 127.5, -1, 1);
        viz.SetPosition(x, y);

        string text = $"X {Fmt(x)}  Y {Fmt(-y)}";
        if (text == last) return;
        last = text;
        readout.Text = text;
        raw.Text = $"brut {rawX} · {rawY}";
    }

    private static string Fmt(double v) =>
        (v >= 0 ? "+" : "−") + Math.Abs(v).ToString("0.000", CultureInfo.InvariantCulture);
}
