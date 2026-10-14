using System.Diagnostics;
using System.IO;
using System.Windows;

namespace Se7en.DualSense.App.Services;

/// <summary>
/// Installation de l'exécutable autonome : copie dans %LocalAppData%\Programs\se7en DualSense,
/// raccourcis Bureau et menu Démarrer, et désinstallation propre (raccourcis, démarrage, manette démasquée).
/// </summary>
public static class InstallService
{
    private const string AppName = "se7en DualSense";

    public static string InstallDirectory { get; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", AppName);

    public static string InstalledExe => Path.Combine(InstallDirectory, AppName + ".exe");

    private static string DesktopShortcut =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), AppName + ".lnk");

    private static string StartMenuShortcut =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs), AppName + ".lnk");

    /// <summary>L'application tourne depuis son dossier d'installation.</summary>
    public static bool IsInstalled =>
        Environment.ProcessPath is { } exe && string.Equals(Path.GetFullPath(exe), Path.GetFullPath(InstalledExe), StringComparison.OrdinalIgnoreCase);

    /// <summary>Exécutable autonome lancé depuis ailleurs (Téléchargements…) : on peut proposer l'installation.</summary>
    public static bool CanInstall => UpdateService.CanSelfUpdate && !IsInstalled;

    public static bool HasShortcuts => File.Exists(DesktopShortcut) || File.Exists(StartMenuShortcut);

    /// <summary>Copie l'exécutable, crée les raccourcis puis relance la copie installée.</summary>
    public static void InstallAndRestart(bool desktopShortcut)
    {
        string exe = Environment.ProcessPath ?? throw new InvalidOperationException("Chemin de l'application introuvable.");
        Directory.CreateDirectory(InstallDirectory);
        File.Copy(exe, InstalledExe, overwrite: true);
        CreateShortcuts(InstalledExe, desktopShortcut);
        if (AppSettings.Current.StartWithWindows)
            StartupService.Enable(true, out _, InstalledExe);
        App.Controller?.Output.EmergencyRestore();
        Process.Start(new ProcessStartInfo(InstalledExe, "--skip-intro") { UseShellExecute = true });
        Application.Current.Shutdown();
    }

    public static void CreateShortcuts(string target, bool desktop)
    {
        CreateShortcut(StartMenuShortcut, target);
        if (desktop) CreateShortcut(DesktopShortcut, target);
    }

    private static void CreateShortcut(string path, string target)
    {
        var type = Type.GetTypeFromProgID("WScript.Shell") ?? throw new InvalidOperationException("WScript.Shell indisponible.");
        dynamic shell = Activator.CreateInstance(type)!;
        try
        {
            dynamic link = shell.CreateShortcut(path);
            link.TargetPath = target;
            link.WorkingDirectory = Path.GetDirectoryName(target);
            link.IconLocation = target + ",0";
            link.Description = "Votre DualSense dans tous vos jeux";
            link.Save();
        }
        finally
        {
            System.Runtime.InteropServices.Marshal.FinalReleaseComObject(shell);
        }
    }

    /// <summary>
    /// Désinstalle : raccourcis, démarrage avec Windows, manette démasquée, puis suppression du dossier
    /// d'installation après la fermeture. Les profils (%AppData%) sont conservés sauf demande contraire.
    /// </summary>
    public static void Uninstall(bool removeData)
    {
        foreach (var lnk in new[] { DesktopShortcut, StartMenuShortcut })
            try { if (File.Exists(lnk)) File.Delete(lnk); } catch { /* ignoré */ }
        StartupService.Enable(false, out _);
        App.Controller?.Output.EmergencyRestore();

        var targets = new List<string>();
        if (Directory.Exists(InstallDirectory)) targets.Add(InstallDirectory);
        if (removeData) targets.Add(AppSettings.Directory);
        if (targets.Count > 0)
        {
            // Suppression différée : l'exécutable en cours ne peut pas s'effacer lui-même
            string cmd = "/c timeout /t 3 /nobreak >nul & " + string.Join(" & ", targets.Select(t => $"rmdir /s /q \"{t}\""));
            Process.Start(new ProcessStartInfo("cmd.exe", cmd) { CreateNoWindow = true, UseShellExecute = false });
        }
        Application.Current.Shutdown();
    }
}
