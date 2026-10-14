using System.Diagnostics;
using System.IO;
using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace Se7en.DualSense.App.Services;

/// <summary>
/// Détection des conflits avec Steam Input : Steam peut prendre la main sur la DualSense ou sur la manette
/// virtuelle (doubles appuis, touches mélangées). Lecture seule de la configuration de Steam.
/// </summary>
public static class SteamInput
{
    public sealed record Status(bool SteamRunning, int? PlayStationSupport, int? XboxSupport);

    public static Status Detect()
    {
        bool running = false;
        try { running = Process.GetProcessesByName("steam").Length > 0; } catch { /* liste indisponible */ }
        int? ps = null, xbox = null;
        try
        {
            if (Registry.GetValue(@"HKEY_CURRENT_USER\Software\Valve\Steam", "SteamPath", null) is string steam)
            {
                string config = Path.Combine(steam.Replace('/', '\\'), "config", "config.vdf");
                if (File.Exists(config))
                {
                    string text = File.ReadAllText(config);
                    ps = Read(text, "SteamController_PSSupport");
                    xbox = Read(text, "SteamController_XBoxSupport");
                }
            }
        }
        catch { /* Steam absent */ }
        return new Status(running, ps, xbox);
    }

    private static int? Read(string vdf, string key)
    {
        var m = Regex.Match(vdf, "\"" + key + "\"\\s+\"(\\d+)\"");
        return m.Success ? int.Parse(m.Groups[1].Value) : null;
    }

    /// <summary>Vrai si Steam risque d'intercepter la manette pour ce mode de sortie.</summary>
    public static bool MayConflict(Status s, Se7en.DualSense.Output.OutputMode mode) => s.SteamRunning && mode switch
    {
        Se7en.DualSense.Output.OutputMode.DualShock4 or Se7en.DualSense.Output.OutputMode.Native => s.PlayStationSupport is null or > 0,
        Se7en.DualSense.Output.OutputMode.Xbox360 => s.XboxSupport is > 0,
        _ => false,
    };

    public static void OpenSteamControllerSettings()
    {
        try { Process.Start(new ProcessStartInfo("steam://open/settings/controller") { UseShellExecute = true }); }
        catch
        {
            try { Process.Start(new ProcessStartInfo("steam://open/main") { UseShellExecute = true }); } catch { /* Steam absent */ }
        }
    }
}
