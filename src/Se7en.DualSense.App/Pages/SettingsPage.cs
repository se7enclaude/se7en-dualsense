using System.Diagnostics;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Se7en.DualSense.App.Controls;
using Se7en.DualSense.App.Services;
using Se7en.DualSense.Output;

namespace Se7en.DualSense.App.Pages;

/// <summary>Onglet Réglages : apparence, démarrage, interface, pilotes, secours, mises à jour, crédits.</summary>
public sealed class SettingsPage : UserControl
{
    public const string RepositoryUrl = "https://github.com/se7enclaude/se7en-dualsense";

    private readonly IShell _shell;
    private readonly StackPanel _root = new();

    /// <summary>Déclenché quand un réglage d'interface change (overlay, navigation…).</summary>
    public event Action? InterfaceSettingsChanged;

    public SettingsPage(IShell shell)
    {
        _shell = shell;
        Content = _root;
        Build();
    }

    private static AppSettings S => AppSettings.Current;

    private static void Save() => AppSettings.Save();

    private void Build()
    {
        _root.Children.Clear();
        _root.Children.Add(new PageHeader
        {
            Overline = "SYSTÈME",
            Title = "Réglages",
            Subtitle = "Apparence, démarrage, pilotes et informations sur l'application.",
        });

        // ── Apparence ──
        Section("APPARENCE",
            new SettingRow
            {
                Label = "Thème",
                Description = "Le thème clair est la référence du design ; le sombre convient aux sessions de nuit.",
                Control = Ui.Segmented(new[] { "Clair", "Sombre" }, ThemeManager.Current == AppTheme.Dark ? 1 : 0,
                    i => ThemeManager.Apply(i == 1 ? AppTheme.Dark : AppTheme.Light)),
            },
            new SettingRow
            {
                Label = "Couleur d'accent",
                Description = "« Barre lumineuse » : l'interface prend la couleur de la manette, en direct.",
                Control = AccentPicker(),
            },
            new SettingRow
            {
                Label = "Animation d'ouverture",
                Description = "Affiche l'intro « se7en DualSense » et l'écran d'accueil au lancement.",
                Control = Ui.Switch(S.ShowIntro, v => { S.ShowIntro = v; Save(); }),
                Last = true,
            });

        // ── Démarrage ──
        Section("DÉMARRAGE",
            new SettingRow
            {
                Label = "Démarrer avec Windows",
                Description = "se7en DualSense se lance à l'ouverture de session, réduit dans la zone de notification, avec les droits nécessaires.",
                Control = Ui.Switch(S.StartWithWindows, v =>
                {
                    if (StartupService.Enable(v, out var error))
                    {
                        S.StartWithWindows = v;
                        Save();
                        _shell.Toast(v ? "Démarrage automatique activé" : "Démarrage automatique désactivé", "");
                    }
                    else
                    {
                        _shell.Toast("Impossible de modifier le démarrage", error, NotifyKind.Warning);
                        Build();
                    }
                }),
            },
            new SettingRow
            {
                Label = "Fermer dans la zone de notification",
                Description = "La croix de la fenêtre garde l'application active en arrière-plan. Quitter depuis l'icône près de l'horloge.",
                Control = Ui.Switch(S.CloseToTray, v => { S.CloseToTray = v; Save(); }),
                Last = true,
            });

        // ── Manette ──
        var bluetooth = App.Controller.Slots.Where(x => x.Device?.Connection == Se7en.DualSense.Hid.ConnectionType.Bluetooth).ToList();
        Section("MANETTE",
            new SettingRow
            {
                Label = "Raccourcis PS + bouton",
                Description = "Maintenez PS et appuyez sur un bouton. Un appui court sur PS reste transmis au jeu.",
                Control = Ui.Switch(S.PsShortcuts, v => { S.PsShortcuts = v; Save(); App.Controller.ApplyGlobalOptions(); }),
            },
            PsShortcutList(),
            new SettingRow
            {
                Label = "Alerte batterie faible",
                Description = "Notification à 20 % puis à 10 %, même quand la fenêtre est fermée.",
                Control = Ui.Switch(S.BatteryAlerts, v => { S.BatteryAlerts = v; Save(); }),
            },
            new SettingRow
            {
                Label = "Éteindre la manette",
                Description = bluetooth.Count > 0
                    ? "Coupe la liaison Bluetooth : la DualSense s'éteint. Raccourci : PS + Options."
                    : "Disponible en Bluetooth (en USB, la manette est alimentée par le câble).",
                Control = Ui.Button("Éteindre", "Button.Secondary", (_, _) => App.Controller.PowerOff()).With(b => b.IsEnabled = bluetooth.Count > 0),
                Last = true,
            });

        // ── Interface ──
        Section("INTERFACE",
            new SettingRow
            {
                Label = "Overlay en jeu",
                Description = "Petite capsule en bas à droite de l'écran : batterie, profil et mode. Visible en mode fenêtré sans bordure.",
                Control = Ui.Switch(S.ShowOverlay, v => { S.ShowOverlay = v; Save(); InterfaceSettingsChanged?.Invoke(); }),
            },
            new SettingRow
            {
                Label = "Naviguer avec la manette",
                Description = "Quand la fenêtre est active : croix directionnelle pour se déplacer, ✕ pour valider, ○ pour revenir, L1 / R1 pour changer d'onglet.",
                Control = Ui.Switch(S.ControllerNavigation, v => { S.ControllerNavigation = v; Save(); }),
            },
            new SettingRow
            {
                Label = "Notifications",
                Description = "Connexion de la manette, changement de profil ou de mode.",
                Control = Ui.Switch(S.NotifyOnConnect, v => { S.NotifyOnConnect = v; Save(); }),
                Last = true,
            });

        // ── Pilotes ──
        var drivers = DriverStatus.Detect();
        Section("PILOTES",
            new SettingRow
            {
                Label = "ViGEmBus",
                Description = "Crée les manettes virtuelles DualShock 4 et Xbox 360.",
                Control = Status(drivers.ViGEmBusInstalled),
            },
            new SettingRow
            {
                Label = "HidHide",
                Description = "Masque la vraie manette aux jeux pour éviter la double entrée.",
                Control = Status(drivers.HidHideInstalled),
            },
            new SettingRow
            {
                Label = "Droits administrateur",
                Description = "Nécessaires au masquage HidHide.",
                Control = Status(OutputManager.IsElevated, "Actifs", "Absents"),
            },
            new SettingRow
            {
                Label = "Réinstaller les pilotes",
                Description = "Télécharge la dernière version officielle (Nefarius) et relance l'installation.",
                Control = Ui.Button("Réinstaller", "Button.Secondary", async (_, _) => await ReinstallAsync()),
                Last = true,
            });

        // ── Secours ──
        Section("SECOURS",
            new SettingRow
            {
                Label = "Rendre la manette visible",
                Description = "Si la DualSense n'apparaît plus dans Windows ou dans un jeu après un arrêt brutal, la démasque immédiatement.",
                Control = Ui.Button("Démasquer", "Button.Secondary", (_, _) =>
                {
                    App.Controller.Output.EmergencyRestore();
                    _shell.Toast("Manette démasquée", "Elle est de nouveau visible pour Windows et les jeux.", NotifyKind.Success);
                }),
                Last = true,
            });

        // ── Mises à jour & à propos ──
        string version = Assembly.GetEntryAssembly()?.GetName().Version?.ToString(3) ?? "?";
        Section("MISES À JOUR",
            new SettingRow
            {
                Label = $"Version {version}",
                Description = UpdateService.CanSelfUpdate
                    ? "Les nouvelles versions publiées sur GitHub s'installent en un clic."
                    : "Version de développement : les mises à jour automatiques concernent l'exécutable publié.",
                Control = Ui.Button("Rechercher", "Button.Secondary", async (sender, _) => await CheckUpdatesAsync((Button)sender)),
            },
            new SettingRow
            {
                Label = "Vérifier automatiquement",
                Description = "Une fois par jour au démarrage ; rien n'est installé sans votre accord.",
                Control = Ui.Switch(S.AutoCheckUpdates, v => { S.AutoCheckUpdates = v; Save(); }),
                Last = true,
            });

        // ── Installation ──
        if (InstallService.CanInstall)
            Section("INSTALLATION",
                new SettingRow
                {
                    Label = "Installer sur ce PC",
                    Description = $"Copie l'application dans {InstallService.InstallDirectory} et ajoute les raccourcis Bureau et menu Démarrer.",
                    Control = Ui.Button("Installer", "Button.Primary", (_, _) => Install()),
                    Last = true,
                });
        else if (InstallService.IsInstalled)
            Section("INSTALLATION",
                new SettingRow
                {
                    Label = "Raccourcis",
                    Description = "Recrée les raccourcis sur le Bureau et dans le menu Démarrer.",
                    Control = Ui.Button("Créer", "Button.Secondary", (_, _) =>
                    {
                        try
                        {
                            InstallService.CreateShortcuts(InstallService.InstalledExe, desktop: true);
                            _shell.Toast("Raccourcis créés", "Bureau et menu Démarrer", NotifyKind.Success);
                        }
                        catch (Exception ex) { _shell.Toast("Raccourcis impossibles", ex.Message, NotifyKind.Warning); }
                    }),
                },
                new SettingRow
                {
                    Label = "Désinstaller",
                    Description = "Retire l'application, ses raccourcis et le démarrage automatique. La manette est démasquée.",
                    Control = Ui.Button("Désinstaller", "Button.Secondary", (_, _) => ConfirmUninstall()),
                    Last = true,
                });

        var credits = new StackPanel();
        credits.Children.Add(Ui.Text("À PROPOS & CRÉDITS", "Text.Overline", new Thickness(0, 0, 0, 12)));
        credits.Children.Add(Ui.Text(
            "se7en DualSense — utilisez votre DualSense dans tous vos jeux PC.\n\n" +
            "Modèle 3D de la manette : « Playstation 5 Dualsense » par AHarmlessPotato (Sketchfab), licence CC BY 4.0. " +
            "Modifié : éclairage studio cuit dans la texture, découpe en pièces mobiles, cadrage.\n" +
            "Pilotes : ViGEmBus et HidHide par Nefarius Software Solutions.\n" +
            "Lecture HID : HidSharp (Apache 2.0). Audio : NAudio (MIT). Icônes : Lucide (ISC).\n" +
            "Polices : Anton, Hanken Grotesk, Geist Mono, Syne (SIL Open Font License 1.1).\n\n" +
            "« PlayStation », « DualSense » et « DualShock » sont des marques de Sony Interactive Entertainment. " +
            "Ce projet n'est ni affilié ni approuvé par Sony.", "Text.Body"));
        credits.Children.Add(Ui.Button("Code source sur GitHub", "Button.Secondary", (_, _) => OpenUrl(RepositoryUrl), new Thickness(0, 16, 0, 0)));
        _root.Children.Add(Ui.Card(credits, new Thickness(0, 0, 0, 16), 20));
    }

    /// <summary>Tableau des raccourcis PS + bouton (deux colonnes).</summary>
    private static FrameworkElement PsShortcutList()
    {
        var grid = new System.Windows.Controls.Primitives.UniformGrid { Columns = 2, Margin = new Thickness(0, 4, 0, 12) };
        foreach (var (button, _, label) in Se7en.DualSense.Processing.PsShortcuts.Table)
        {
            var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 3, 12, 3) };
            var glyph = Labels.Buttons.FirstOrDefault(b => b.Flag == button);
            row.Children.Add(new ButtonGlyph { Text = "PS", Shape = GlyphShape.Pill, Height = 26, MinWidth = 26, FontSize = 10 });
            row.Children.Add(Ui.Text("+", "Text.Caption", new Thickness(6, 0, 6, 0)).With(t => t.VerticalAlignment = VerticalAlignment.Center));
            row.Children.Add(new ButtonGlyph { Text = glyph.Glyph ?? "?", Shape = glyph.Shape, Height = 26, MinWidth = 26, Width = glyph.Shape == GlyphShape.Pill ? double.NaN : 26, FontSize = glyph.Shape == GlyphShape.Pill ? 10 : 13 });
            row.Children.Add(Ui.Text(label, "Text.Body", new Thickness(10, 0, 0, 0)).With(t => t.VerticalAlignment = VerticalAlignment.Center));
            grid.Children.Add(row);
        }
        return grid;
    }

    /// <summary>Pastilles de couleur d'accent ; la pastille choisie est cerclée.</summary>
    private FrameworkElement AccentPicker()
    {
        var panel = new StackPanel { Orientation = Orientation.Horizontal };
        bool dark = ThemeManager.Current == AppTheme.Dark;
        foreach (var (key, name, light, darkColor) in ThemeManager.Accents)
        {
            string k = key;
            Brush fill = key == "lightbar"
                ? new LinearGradientBrush(new GradientStopCollection
                {
                    new(Color.FromRgb(0x2E, 0x8B, 0xEA), 0), new(Color.FromRgb(0xEE, 0x5A, 0xA0), 0.5), new(Color.FromRgb(0xF5, 0x81, 0x2F), 1),
                }, 45)
                : new SolidColorBrush(dark ? darkColor : light);
            var dot = new Border { Width = 22, Height = 22, CornerRadius = new CornerRadius(11), Background = fill };
            var ring = new Border
            {
                Width = 30, Height = 30, CornerRadius = new CornerRadius(15), BorderThickness = new Thickness(2), Child = dot,
                Margin = new Thickness(0, 0, 4, 0), Cursor = System.Windows.Input.Cursors.Hand, ToolTip = name, Background = Brushes.Transparent,
            };
            if (ThemeManager.AccentKey == key) ring.SetResourceReference(Border.BorderBrushProperty, "Border.Strong");
            else ring.BorderBrush = Brushes.Transparent;
            ring.MouseLeftButtonUp += (_, _) => { ThemeManager.SetAccent(k); Build(); };
            panel.Children.Add(ring);
        }
        return panel;
    }

    private async Task CheckUpdatesAsync(Button button)
    {
        button.IsEnabled = false;
        try
        {
            var update = await UpdateService.CheckAsync();
            S.LastUpdateCheck = DateTime.Now;
            Save();
            if (update == null)
                _shell.Toast("Vous êtes à jour", $"Version {UpdateService.CurrentVersion.ToString(3)}", NotifyKind.Success);
            else
                UpdateDialog.Show(_shell, update);
        }
        catch (Exception ex)
        {
            _shell.Toast("Vérification impossible", ex.Message, NotifyKind.Warning);
        }
        finally
        {
            button.IsEnabled = true;
        }
    }

    private void Install()
    {
        try
        {
            InstallService.InstallAndRestart(desktopShortcut: true);
        }
        catch (Exception ex)
        {
            _shell.Toast("Installation impossible", ex.Message, NotifyKind.Warning);
        }
    }

    private void ConfirmUninstall()
    {
        var body = new StackPanel { Width = 460 };
        body.Children.Add(Ui.Text("Désinstaller se7en DualSense ?", "Text.H1", new Thickness(0, 0, 0, 10)));
        body.Children.Add(Ui.Text("L'application, ses raccourcis et le démarrage automatique seront retirés ; la manette redevient visible. " +
                                  "Les pilotes ViGEmBus et HidHide restent installés (désinstallables depuis les Paramètres de Windows).",
            "Text.Body", new Thickness(0, 0, 0, 16)));
        var removeData = new CheckBox { Content = "Supprimer aussi mes profils et réglages" };
        body.Children.Add(removeData);
        var actions = new DockPanel { Margin = new Thickness(0, 24, 0, 0), LastChildFill = false };
        actions.Children.Add(Ui.Button("Annuler", "Button.Secondary", (_, _) => _shell.CloseModal()));
        var ok = Ui.Button("Désinstaller", "Button.Primary", (_, _) => InstallService.Uninstall(removeData.IsChecked == true));
        DockPanel.SetDock(ok, Dock.Right);
        actions.Children.Add(ok);
        body.Children.Add(actions);
        _shell.ShowModal(body);
    }

    private void Section(string title, params UIElement[] rows)
    {
        var panel = new StackPanel();
        panel.Children.Add(Ui.Text(title, "Text.Overline", new Thickness(0, 0, 0, 4)));
        foreach (var r in rows) panel.Children.Add(r);
        _root.Children.Add(Ui.Card(panel, new Thickness(0, 0, 0, 16), 20));
    }

    private static Badge Status(bool ok, string yes = "Installé", string no = "Absent") =>
        new() { ShowDot = true, Text = ok ? yes : no, Tone = ok ? BadgeTone.Success : BadgeTone.Warning };

    private async Task ReinstallAsync()
    {
        var answer = MessageBox.Show(
            "Télécharger et réinstaller ViGEmBus et HidHide depuis les publications officielles de Nefarius sur GitHub ?",
            "Réinstaller les pilotes", MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (answer != MessageBoxResult.Yes) return;
        var progress = new Progress<string>(msg => _shell.Toast("Pilotes", msg));
        foreach (var kind in new[] { DriverKind.ViGEmBus, DriverKind.HidHide })
        {
            try { await DriverInstaller.InstallAsync(kind, progress); }
            catch (Exception ex) { _shell.Toast("Échec de l'installation", $"{kind.DisplayName()} : {ex.Message}", NotifyKind.Warning); }
        }
        Build();
    }

    private static void OpenUrl(string url)
    {
        try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); }
        catch { /* navigateur indisponible */ }
    }
}
