using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Windows;

namespace Se7en.DualSense.App.Services;

/// <summary>Une version publiée plus récente que celle en cours.</summary>
public sealed record UpdateInfo(Version Version, string Tag, string Name, string Notes, string AssetName, string AssetUrl, long Size, string? Sha256);

/// <summary>
/// Mises à jour automatiques via les publications GitHub du projet : le .exe publié est téléchargé,
/// son empreinte SHA-256 vérifiée (si la publication la fournit), puis il remplace l'exécutable en cours
/// (l'ancien est renommé en .old et supprimé au démarrage suivant).
/// </summary>
public static class UpdateService
{
    public const string Repository = "se7enclaude/se7en-dualsense";
    private static readonly HttpClient Http = CreateClient();

    private static HttpClient CreateClient()
    {
        var c = new HttpClient { Timeout = TimeSpan.FromMinutes(5) };
        c.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("se7en-dualsense", CurrentVersion.ToString(3)));
        c.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        return c;
    }

    public static Version CurrentVersion => Assembly.GetEntryAssembly()?.GetName().Version ?? new Version(0, 0, 0);

    /// <summary>
    /// Vrai pour l'exécutable autonome publié (fichier unique) : on peut le remplacer.
    /// Faux pendant le développement (dotnet run, dossier bin).
    /// </summary>
    public static bool CanSelfUpdate => string.IsNullOrEmpty(typeof(UpdateService).Assembly.Location) && Environment.ProcessPath != null;

    /// <summary>Renvoie la dernière publication si elle est plus récente, sinon null.</summary>
    public static async Task<UpdateInfo?> CheckAsync(CancellationToken ct = default)
    {
        using var response = await Http.GetAsync($"https://api.github.com/repos/{Repository}/releases/latest", ct);
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
            return null; // aucune publication (ou dépôt privé)
        response.EnsureSuccessStatusCode();
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
        var root = doc.RootElement;
        string tag = root.GetProperty("tag_name").GetString() ?? "";
        if (!TryParseVersion(tag, out var version) || version <= CurrentVersion)
            return null;

        string notes = root.TryGetProperty("body", out var body) ? body.GetString() ?? "" : "";
        foreach (var asset in root.GetProperty("assets").EnumerateArray())
        {
            string name = asset.GetProperty("name").GetString() ?? "";
            if (!name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) continue;
            string? sha = null;
            // Empreinte fournie par GitHub (« sha256:… ») ou écrite dans les notes de version
            if (asset.TryGetProperty("digest", out var digest) && digest.GetString() is { } d && d.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase))
                sha = d[7..];
            sha ??= Regex.Match(notes, @"SHA-?256\s*:?\s*([0-9a-fA-F]{64})", RegexOptions.IgnoreCase) is { Success: true } m ? m.Groups[1].Value : null;
            return new UpdateInfo(version, tag, root.TryGetProperty("name", out var n) ? n.GetString() ?? tag : tag, notes,
                name, asset.GetProperty("browser_download_url").GetString()!, asset.GetProperty("size").GetInt64(), sha);
        }
        return null;
    }

    public static bool TryParseVersion(string tag, out Version version)
    {
        var m = Regex.Match(tag, @"(\d+)\.(\d+)(?:\.(\d+))?");
        version = m.Success
            ? new Version(int.Parse(m.Groups[1].Value), int.Parse(m.Groups[2].Value), m.Groups[3].Success ? int.Parse(m.Groups[3].Value) : 0)
            : new Version(0, 0);
        return m.Success;
    }

    /// <summary>Télécharge le nouvel exécutable dans un dossier temporaire et vérifie son empreinte.</summary>
    public static async Task<string> DownloadAsync(UpdateInfo update, IProgress<double> progress, CancellationToken ct = default)
    {
        string dir = Path.Combine(Path.GetTempPath(), "se7en-dualsense-update");
        Directory.CreateDirectory(dir);
        string path = Path.Combine(dir, "se7en DualSense.exe");

        using (var response = await Http.GetAsync(update.AssetUrl, HttpCompletionOption.ResponseHeadersRead, ct))
        {
            response.EnsureSuccessStatusCode();
            long total = response.Content.Headers.ContentLength ?? update.Size;
            await using var src = await response.Content.ReadAsStreamAsync(ct);
            await using var dst = File.Create(path);
            var buffer = new byte[81920];
            long done = 0;
            int read;
            while ((read = await src.ReadAsync(buffer, ct)) > 0)
            {
                await dst.WriteAsync(buffer.AsMemory(0, read), ct);
                done += read;
                if (total > 0) progress.Report((double)done / total);
            }
        }

        if (update.Sha256 != null)
        {
            await using var f = File.OpenRead(path);
            string actual = Convert.ToHexString(await SHA256.HashDataAsync(f, ct));
            if (!actual.Equals(update.Sha256, StringComparison.OrdinalIgnoreCase))
            {
                File.Delete(path);
                throw new InvalidDataException("Le fichier téléchargé est corrompu (empreinte SHA-256 différente) : mise à jour annulée.");
            }
        }
        return path;
    }

    /// <summary>Remplace l'exécutable en cours par la nouvelle version puis la relance.</summary>
    public static void InstallAndRestart(string downloaded)
    {
        string exe = Environment.ProcessPath ?? throw new InvalidOperationException("Chemin de l'application introuvable.");
        string old = exe + ".old";
        if (File.Exists(old)) File.Delete(old);
        File.Move(exe, old);           // un exécutable en cours peut être renommé, pas écrasé
        try
        {
            File.Copy(downloaded, exe);
        }
        catch
        {
            File.Move(old, exe);       // retour arrière
            throw;
        }
        try { File.Delete(downloaded); } catch { /* temporaire */ }
        App.Controller?.Output.EmergencyRestore();
        Process.Start(new ProcessStartInfo(exe, "--skip-intro --updated") { UseShellExecute = true });
        Application.Current.Shutdown();
    }

    /// <summary>Au démarrage : supprime l'ancienne version laissée par une mise à jour.</summary>
    public static void CleanupPreviousVersion()
    {
        try
        {
            if (Environment.ProcessPath is { } exe && File.Exists(exe + ".old"))
                File.Delete(exe + ".old");
        }
        catch { /* encore verrouillé : au prochain démarrage */ }
    }

    /// <summary>Vérification discrète au démarrage (au plus une fois par jour).</summary>
    public static async Task<UpdateInfo?> CheckInBackgroundAsync()
    {
        var settings = AppSettings.Current;
        if (!settings.AutoCheckUpdates || DateTime.Now - settings.LastUpdateCheck < TimeSpan.FromHours(20))
            return null;
        try
        {
            var update = await CheckAsync();
            settings.LastUpdateCheck = DateTime.Now;
            AppSettings.Save();
            return update != null && update.Tag != settings.SkippedVersion ? update : null;
        }
        catch
        {
            return null; // hors ligne : on réessaiera plus tard
        }
    }
}
