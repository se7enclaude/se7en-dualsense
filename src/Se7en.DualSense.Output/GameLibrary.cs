using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace Se7en.DualSense.Output;

/// <summary>Jeu installé détecté sur le PC.</summary>
public sealed record InstalledGame(string Name, string Store, string InstallDir, string? Executable, string? CoverPath);

/// <summary>
/// Détection des jeux installés : Steam (bibliothèques + manifestes), Epic Games (manifestes .item)
/// et Rockstar Games Launcher (registre). Lecture seule, aucun réseau.
/// </summary>
public static partial class GameLibrary
{
    public static List<InstalledGame> Detect()
    {
        var games = new List<InstalledGame>();
        try { games.AddRange(DetectSteam()); } catch { /* Steam absent */ }
        try { games.AddRange(DetectEpic()); } catch { /* Epic absent */ }
        try { games.AddRange(DetectRockstar()); } catch { /* Rockstar absent */ }
        return games
            .GroupBy(g => g.Name, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.First())
            .OrderBy(g => g.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    // ───────────── Steam ─────────────

    [GeneratedRegex("\"path\"\\s+\"(?<p>[^\"]+)\"")]
    private static partial Regex SteamLibraryPath();

    [GeneratedRegex("\"(?<k>appid|name|installdir)\"\\s+\"(?<v>[^\"]*)\"")]
    private static partial Regex SteamManifestField();

    private static IEnumerable<InstalledGame> DetectSteam()
    {
        string? steam = Registry.GetValue(@"HKEY_CURRENT_USER\Software\Valve\Steam", "SteamPath", null) as string;
        if (steam == null) yield break;
        steam = steam.Replace('/', '\\');

        var libraries = new List<string> { steam };
        string vdf = Path.Combine(steam, "steamapps", "libraryfolders.vdf");
        if (File.Exists(vdf))
            libraries.AddRange(SteamLibraryPath().Matches(File.ReadAllText(vdf)).Select(m => m.Groups["p"].Value.Replace(@"\\", @"\")));

        foreach (var lib in libraries.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            string apps = Path.Combine(lib, "steamapps");
            if (!Directory.Exists(apps)) continue;
            foreach (var manifest in Directory.EnumerateFiles(apps, "appmanifest_*.acf"))
            {
                var fields = SteamManifestField().Matches(File.ReadAllText(manifest))
                    .GroupBy(m => m.Groups["k"].Value).ToDictionary(g => g.Key, g => g.First().Groups["v"].Value);
                if (!fields.TryGetValue("name", out var name) || !fields.TryGetValue("installdir", out var dir)) continue;
                // Outils Steam (redistribuables, Proton…) écartés
                if (name.StartsWith("Steamworks", StringComparison.OrdinalIgnoreCase) || name.Contains("Redistributable", StringComparison.OrdinalIgnoreCase))
                    continue;
                string installDir = Path.Combine(apps, "common", dir);
                string? cover = fields.TryGetValue("appid", out var id)
                    ? new[] { $"{id}_library_600x900.jpg", $"{id}/library_600x900.jpg" }
                        .Select(f => Path.Combine(steam, "appcache", "librarycache", f)).FirstOrDefault(File.Exists)
                    : null;
                yield return new InstalledGame(name, "Steam", installDir, GuessExecutable(installDir, name), cover);
            }
        }
    }

    // ───────────── Epic Games ─────────────

    private static IEnumerable<InstalledGame> DetectEpic()
    {
        string manifests = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            "Epic", "EpicGamesLauncher", "Data", "Manifests");
        if (!Directory.Exists(manifests)) yield break;
        foreach (var file in Directory.EnumerateFiles(manifests, "*.item"))
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(file));
            var root = doc.RootElement;
            string? name = root.TryGetProperty("DisplayName", out var n) ? n.GetString() : null;
            string? dir = root.TryGetProperty("InstallLocation", out var d) ? d.GetString() : null;
            string? exe = root.TryGetProperty("LaunchExecutable", out var e) ? e.GetString() : null;
            if (name == null || dir == null) continue;
            yield return new InstalledGame(name, "Epic Games", dir, exe != null ? Path.Combine(dir, exe) : GuessExecutable(dir, name), null);
        }
    }

    // ───────────── Rockstar Games ─────────────

    private static IEnumerable<InstalledGame> DetectRockstar()
    {
        using var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\WOW6432Node\Rockstar Games");
        if (key == null) yield break;
        foreach (var sub in key.GetSubKeyNames())
        {
            using var g = key.OpenSubKey(sub);
            if (g?.GetValue("InstallFolder") is not string dir || !Directory.Exists(dir)) continue;
            if (sub.Contains("Launcher", StringComparison.OrdinalIgnoreCase) || sub.Contains("Social Club", StringComparison.OrdinalIgnoreCase)) continue;
            yield return new InstalledGame(sub, "Rockstar Games", dir, GuessExecutable(dir, sub), null);
        }
    }

    /// <summary>Exécutable principal probable : le plus gros .exe hors outils (lanceurs, désinstalleurs…).</summary>
    public static string? GuessExecutable(string dir, string name)
    {
        if (!Directory.Exists(dir)) return null;
        string[] ignored = { "unins", "crash", "redist", "setup", "launcher", "helper", "report", "dxsetup", "vc_", "dotnet", "easyanticheat", "be_service" };
        try
        {
            return Directory.EnumerateFiles(dir, "*.exe", new EnumerationOptions { RecurseSubdirectories = true, MaxRecursionDepth = 3, IgnoreInaccessible = true })
                .Where(f => !ignored.Any(i => Path.GetFileName(f).Contains(i, StringComparison.OrdinalIgnoreCase)))
                .OrderByDescending(f => new FileInfo(f).Length)
                .FirstOrDefault();
        }
        catch
        {
            return null;
        }
    }
}
