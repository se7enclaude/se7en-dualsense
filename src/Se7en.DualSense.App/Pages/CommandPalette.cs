using System.Globalization;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Se7en.DualSense.App.Controls;
using Se7en.DualSense.App.Services;
using Se7en.DualSense.Output;

namespace Se7en.DualSense.App.Pages;

/// <summary>
/// Recherche rapide (Ctrl + K) : onglets, réglages et actions, filtrés au fil de la frappe (sans accents).
/// ↑ ↓ pour choisir, Entrée pour lancer, Échap pour fermer.
/// </summary>
public static class CommandPalette
{
    private sealed record Entry(string Title, string Subtitle, string Icon, string Keywords, Action Run);

    private static IEnumerable<Entry> Entries(IShell shell)
    {
        var c = App.Controller;
        yield return new("Accueil", "Onglet", "Icon.House", "home tableau", () => shell.Navigate("home"));
        yield return new("Jeux & profils", "Onglet", "Icon.LibraryBig", "profil jeu steam epic bibliotheque", () => shell.Navigate("games"));
        yield return new("Sticks", "Onglet · drift, zones mortes, courbes", "Icon.Crosshair", "drift zone morte courbe joystick", () => shell.Navigate("sticks"));
        yield return new("Gâchettes", "Onglet · zones mortes, gâchettes adaptatives", "Icon.Gauge", "l2 r2 adaptatives trigger", () => shell.Navigate("triggers"));
        yield return new("Mappage", "Onglet · boutons, clavier, macros, pavé tactile", "Icon.SlidersHorizontal", "remap touche clavier souris macro couche turbo pave", () => shell.Navigate("mapping"));
        yield return new("Gyroscope", "Onglet · visée, souris, flick stick", "Icon.Move", "gyro visee flick souris", () => shell.Navigate("gyro"));
        yield return new("Vibrations & lumière", "Onglet · barre lumineuse, haptique, son", "Icon.Vibrate", "vibration haptique lumiere barre led son micro", () => shell.Navigate("feedback"));
        yield return new("Diagnostic", "Onglet · circularité, batterie, usure", "Icon.Activity", "test circularite batterie usure historique", () => shell.Navigate("diagnostic"));
        yield return new("Réglages", "Onglet", "Icon.Settings", "parametres options theme demarrage mise a jour", () => shell.Navigate("settings"));

        yield return new("Calibrer les sticks", "Action · corrige le drift", "Icon.Target", "calibration drift", () => shell.Navigate("sticks"));
        yield return new("Lanceur de jeux", "Action · plein écran (PS + R3)", "Icon.LibraryBig", "lancer jeu plein ecran", LauncherWindow.Toggle);
        yield return new("Clavier virtuel", "Action · (PS + L3)", "Icon.Keyboard", "clavier ecrire taper", KeyboardOverlay.Toggle);
        yield return new(c.ActiveProfile.Id == Profile.DesktopId ? "Quitter le mode bureau" : "Mode bureau", "Action · manette = souris (PS + □)", "Icon.Move", "bureau souris clavier windows", c.ToggleDesktopMode);
        yield return new("Changer de thème", "Action · clair / sombre", ThemeManager.Current == AppTheme.Light ? "Icon.Moon" : "Icon.Sun", "theme sombre clair nuit", ThemeManager.Toggle);
        yield return new("Éteindre la manette", "Action · Bluetooth (PS + Options)", "Icon.Power", "eteindre off bluetooth", () => c.PowerOff());
        yield return new("Rechercher une mise à jour", "Action", "Icon.Download", "update version", () => shell.Navigate("settings"));
        foreach (var mode in new[] { OutputMode.DualShock4, OutputMode.Xbox360, OutputMode.Native, OutputMode.KeyboardMouse })
        {
            var m = mode;
            yield return new("Les jeux voient : " + m.Label(), "Mode de sortie", "Icon.Gamepad2", "mode sortie " + m.Label(), () => c.SetOutputMode(m));
        }
        foreach (var p in c.Profiles.All)
        {
            var profile = p;
            yield return new("Profil « " + p.Name + " »", "Activer ce profil · " + p.Mode.Label(), "Icon.Layers", "profil " + p.Name, () => c.SelectProfile(profile));
        }
    }

    private static string Fold(string s)
    {
        var d = s.Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(d.Length);
        foreach (char ch in d)
            if (CharUnicodeInfo.GetUnicodeCategory(ch) != UnicodeCategory.NonSpacingMark) sb.Append(char.ToLowerInvariant(ch));
        return sb.ToString();
    }

    public static void Show(IShell shell)
    {
        var all = Entries(shell).ToList();
        var list = new StackPanel();
        var box = new TextBox { FontSize = 16, Height = 46, Padding = new Thickness(14, 0, 14, 0), VerticalContentAlignment = VerticalAlignment.Center };
        var hint = Ui.Text("Onglet, réglage, profil ou action…", "Text.Caption", new Thickness(16, 0, 0, 0));
        hint.IsHitTestVisible = false;
        hint.VerticalAlignment = VerticalAlignment.Center;
        var field = new Grid();
        field.Children.Add(box);
        field.Children.Add(hint);
        var scroll = new ScrollViewer { Content = list, MaxHeight = 380, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Margin = new Thickness(0, 12, 0, 0) };
        var body = new StackPanel { Width = 560 };
        body.Children.Add(field);
        body.Children.Add(scroll);
        body.Children.Add(Ui.Text("↑ ↓ choisir · Entrée lancer · Échap fermer", "Text.Caption", new Thickness(0, 12, 0, 0)));

        List<Entry> shown = all;
        int index = 0;

        void Run(Entry e)
        {
            shell.CloseModal();
            e.Run();
        }

        void Refresh()
        {
            string q = Fold(box.Text.Trim());
            hint.Visibility = q.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
            shown = q.Length == 0 ? all : all.Where(e => q.Split(' ', StringSplitOptions.RemoveEmptyEntries)
                .All(w => Fold(e.Title + " " + e.Subtitle + " " + e.Keywords).Contains(w))).ToList();
            index = Math.Clamp(index, 0, Math.Max(0, shown.Count - 1));
            list.Children.Clear();
            for (int i = 0; i < shown.Count; i++)
            {
                var e = shown[i];
                bool sel = i == index;
                var icon = new AxisIcon { Width = 16, Height = 16 };
                icon.SetResourceReference(AxisIcon.DataProperty, e.Icon);
                icon.SetResourceReference(Control.ForegroundProperty, sel ? "OnAccent" : "Accent");
                var chip = new Border { Width = 32, Height = 32, CornerRadius = new CornerRadius(10), Child = icon, Margin = new Thickness(0, 0, 12, 0) };
                chip.SetResourceReference(Border.BackgroundProperty, sel ? "Accent" : "Accent.Soft");
                var texts = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
                texts.Children.Add(Ui.Text(e.Title, "Text.Label").With(t => t.FontWeight = FontWeights.SemiBold));
                texts.Children.Add(Ui.Text(e.Subtitle, "Text.Caption"));
                var row = new DockPanel { Children = { chip, texts } };
                var item = new Border { CornerRadius = new CornerRadius(12), Padding = new Thickness(10, 8, 10, 8), Child = row, Cursor = Cursors.Hand, Margin = new Thickness(0, 0, 0, 2) };
                item.SetResourceReference(Border.BackgroundProperty, sel ? "Surface.Inset" : "Transparent");
                if (!sel) item.Background = Brushes.Transparent;
                item.MouseLeftButtonUp += (_, _) => Run(e);
                list.Children.Add(item);
                if (sel) item.Loaded += (_, _) => item.BringIntoView();
            }
            if (shown.Count == 0)
                list.Children.Add(Ui.Text("Aucun résultat.", "Text.Body", new Thickness(12, 8, 0, 8)));
        }

        box.TextChanged += (_, _) => { index = 0; Refresh(); };
        box.PreviewKeyDown += (_, e) =>
        {
            switch (e.Key)
            {
                case Key.Down: index = Math.Min(index + 1, Math.Max(0, shown.Count - 1)); Refresh(); e.Handled = true; break;
                case Key.Up: index = Math.Max(index - 1, 0); Refresh(); e.Handled = true; break;
                case Key.Enter when shown.Count > 0: Run(shown[index]); e.Handled = true; break;
            }
        };
        Refresh();
        shell.ShowModal(body);
        box.Dispatcher.BeginInvoke(() => Keyboard.Focus(box), System.Windows.Threading.DispatcherPriority.Input);
    }
}
