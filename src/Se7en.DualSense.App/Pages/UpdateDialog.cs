using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using Se7en.DualSense.App.Controls;
using Se7en.DualSense.App.Services;

namespace Se7en.DualSense.App.Pages;

/// <summary>Fenêtre « Nouvelle version » : notes de version, téléchargement avec progression, installation.</summary>
public static class UpdateDialog
{
    public static void Show(IShell shell, UpdateInfo update)
    {
        var body = new StackPanel { Width = 520 };
        body.Children.Add(Ui.Text("MISE À JOUR", "Text.Overline", new Thickness(0, 0, 0, 10)));
        body.Children.Add(Ui.Text($"Version {update.Version.ToString(3)} disponible", "Text.H1", new Thickness(0, 0, 0, 8)));
        body.Children.Add(Ui.Text($"Vous utilisez la version {UpdateService.CurrentVersion.ToString(3)}. " +
                                  $"Téléchargement : {update.Size / 1024.0 / 1024.0:0.0} Mo depuis GitHub" +
                                  (update.Sha256 != null ? ", empreinte vérifiée." : "."), "Text.Body", new Thickness(0, 0, 0, 14)));

        if (!string.IsNullOrWhiteSpace(update.Notes))
        {
            var notes = new TextBlock { Text = update.Notes.Trim(), TextWrapping = TextWrapping.Wrap, FontSize = 13 };
            notes.SetResourceReference(TextBlock.FontFamilyProperty, "Font.Sans");
            notes.SetResourceReference(TextBlock.ForegroundProperty, "Text.Secondary");
            var scroll = new ScrollViewer { Content = notes, MaxHeight = 200, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
            var box = new Border { Child = scroll, Padding = new Thickness(14), CornerRadius = new CornerRadius(10) };
            box.SetResourceReference(Border.BackgroundProperty, "Surface.Inset");
            body.Children.Add(box);
        }

        var progress = new ProgressBar { Height = 6, Margin = new Thickness(0, 16, 0, 0), Maximum = 1, Visibility = Visibility.Collapsed };
        body.Children.Add(progress);

        var actions = new DockPanel { Margin = new Thickness(0, 24, 0, 0), LastChildFill = false };
        actions.Children.Add(Ui.Button("Plus tard", "Button.Secondary", (_, _) => shell.CloseModal(), new Thickness(0, 0, 8, 0)));
        actions.Children.Add(Ui.Button("Ignorer cette version", "Button.Secondary", (_, _) =>
        {
            AppSettings.Current.SkippedVersion = update.Tag;
            AppSettings.Save();
            shell.CloseModal();
        }));

        Button install = null!;
        install = Ui.Button(UpdateService.CanSelfUpdate ? "Installer et redémarrer" : "Ouvrir la page", "Button.Primary", async (_, _) =>
        {
            if (!UpdateService.CanSelfUpdate)
            {
                try { Process.Start(new ProcessStartInfo(SettingsPage.RepositoryUrl + "/releases/latest") { UseShellExecute = true }); } catch { }
                shell.CloseModal();
                return;
            }
            install.IsEnabled = false;
            progress.Visibility = Visibility.Visible;
            try
            {
                string file = await UpdateService.DownloadAsync(update, new Progress<double>(v => progress.Value = v));
                UpdateService.InstallAndRestart(file);
            }
            catch (Exception ex)
            {
                shell.CloseModal();
                shell.Toast("Mise à jour impossible", ex.Message, NotifyKind.Warning);
            }
        });
        DockPanel.SetDock(install, Dock.Right);
        actions.Children.Add(install);
        body.Children.Add(actions);
        shell.ShowModal(body);
    }
}
