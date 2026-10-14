using System.ComponentModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Threading;
using Se7en.DualSense.App.Services;
using Se7en.DualSense.Output;

namespace Se7en.DualSense.App;

public partial class App : Application
{
    public static ControllerService Controller { get; private set; } = null!;

    /// <summary>Option --skip-intro : ouvre directement le tableau de bord.</summary>
    public static bool SkipIntro { get; private set; }

    /// <summary>Vrai si l'utilisateur a refusé l'élévation : le masquage HidHide est alors indisponible.</summary>
    public static bool ElevationDeclined { get; private set; }

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        var args = e.Args;

        // Secours : démasquer la manette puis quitter (si elle était restée invisible)
        if (args.Contains("--unhide-all"))
        {
            new HidHideService(AppSettings.Directory).UnhideAll();
            MessageBox.Show("La DualSense est de nouveau visible pour Windows et les jeux.", "se7en DualSense");
            Shutdown();
            return;
        }

        ControllerService.IsDemo = args.Contains("--demo") || args.Contains("--demo-all");
        ControllerService.DemoAllButtons = args.Contains("--demo-all");
        SkipIntro = args.Contains("--skip-intro");

        // HidHide exige les droits administrateur : relance élevée une seule fois au démarrage
        if (!ControllerService.IsDemo && !OutputManager.IsElevated && !args.Contains("--no-elevate")
            && new HidHideService(AppSettings.Directory).IsInstalled)
        {
            if (TryRelaunchElevated(args))
            {
                Shutdown();
                return;
            }
            ElevationDeclined = true;
        }

        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += (_, _) => Controller?.Output.EmergencyRestore();
        AppDomain.CurrentDomain.ProcessExit += (_, _) => Controller?.Output.EmergencyRestore();
        SessionEnding += (_, _) => Controller?.Output.EmergencyRestore();

        if (AppSettings.Current.Theme != AppTheme.Light)
            ThemeManager.Apply(AppSettings.Current.Theme);

        Controller = new ControllerService();
        Controller.Start();

        MainWindow = new MainWindow();
        MainWindow.Show();
    }

    private static bool TryRelaunchElevated(string[] args)
    {
        string? exe = Environment.ProcessPath;
        if (exe == null) return false;
        try
        {
            // --no-elevate évite toute boucle de relance
            string arguments = string.Join(" ", args.Append("--no-elevate").Select(a => "\"" + a.Replace("\"", "") + "\""));
            Process.Start(new ProcessStartInfo(exe, arguments) { UseShellExecute = true, Verb = "runas" });
            return true;
        }
        catch (Win32Exception)
        {
            // L'utilisateur a refusé l'invite de contrôle de compte
            return false;
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        Controller?.Dispose();
        base.OnExit(e);
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        MessageBox.Show(
            "Une erreur inattendue s'est produite :\n\n" + e.Exception.Message,
            "se7en DualSense", MessageBoxButton.OK, MessageBoxImage.Warning);
        e.Handled = true;
    }
}
