using System.Diagnostics;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
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
                Description = "Les nouvelles versions sont publiées sur GitHub.",
                Control = Ui.Button("Voir les versions", "Button.Secondary", (_, _) => OpenUrl(RepositoryUrl + "/releases")),
                Last = true,
            });

        var credits = new StackPanel();
        credits.Children.Add(Ui.Text("À PROPOS & CRÉDITS", "Text.Overline", new Thickness(0, 0, 0, 12)));
        credits.Children.Add(Ui.Text(
            "se7en DualSense — utilisez votre DualSense dans tous vos jeux PC.\n\n" +
            "Modèle 3D de la manette : « Playstation 5 Dualsense » par AHarmlessPotato (Sketchfab), licence CC BY 4.0. " +
            "Modifié : éclairage studio, cadrage, détourage.\n" +
            "Pilotes : ViGEmBus et HidHide par Nefarius Software Solutions.\n" +
            "Lecture HID : HidSharp (Apache 2.0). Icônes : Lucide (ISC).\n" +
            "Polices : Anton, Hanken Grotesk, Geist Mono, Syne (SIL Open Font License 1.1).\n\n" +
            "« PlayStation », « DualSense » et « DualShock » sont des marques de Sony Interactive Entertainment. " +
            "Ce projet n'est ni affilié ni approuvé par Sony.", "Text.Body"));
        credits.Children.Add(Ui.Button("Code source sur GitHub", "Button.Secondary", (_, _) => OpenUrl(RepositoryUrl), new Thickness(0, 16, 0, 0)));
        _root.Children.Add(Ui.Card(credits, new Thickness(0, 0, 0, 16), 20));
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
