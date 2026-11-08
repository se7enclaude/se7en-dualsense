using System.Runtime.InteropServices;
using System.Diagnostics;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;
using Nefarius.ViGEm.Client;
using Nefarius.ViGEm.Client.Exceptions;

namespace Se7en.DualSense.Output;

public enum DriverKind
{
    ViGEmBus,
    HidHide,
}

public sealed record DriverStatus(bool ViGEmBusInstalled, bool HidHideInstalled)
{
    public bool AllInstalled => ViGEmBusInstalled && HidHideInstalled;

    public static DriverStatus Detect()
    {
        bool vigem;
        try
        {
            using var client = new ViGEmClient();
            vigem = true;
        }
        catch (VigemBusNotFoundException)
        {
            vigem = false;
        }
        catch
        {
            // Bus présent mais inaccessible : on le considère installé, l'erreur sera remontée à l'usage
            vigem = true;
        }
        return new DriverStatus(vigem, new HidHideService(Path.GetTempPath()).IsInstalled);
    }
}

/// <summary>
/// Télécharge et lance les installeurs officiels (releases GitHub de Nefarius).
/// L'installeur n'est exécuté que si sa signature numérique est valide et émise pour Nefarius.
/// </summary>
public static class DriverInstaller
{
    private static readonly HttpClient Http = CreateClient();

    private static HttpClient CreateClient()
    {
        var c = new HttpClient { Timeout = TimeSpan.FromMinutes(5) };
        c.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("se7en-dualsense", "1.0"));
        c.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        return c;
    }

    public static string DisplayName(this DriverKind kind) => kind == DriverKind.ViGEmBus ? "ViGEmBus" : "HidHide";

    private static string Repo(DriverKind kind) => kind == DriverKind.ViGEmBus ? "nefarius/ViGEmBus" : "nefarius/HidHide";

    /// <summary>Dernière publication GitHub du pilote (installeur Windows x64, empreinte SHA-256 si GitHub la fournit).</summary>
    public static async Task<DriverReleaseInfo> GetLatestReleaseAsync(DriverKind kind, CancellationToken ct = default)
    {
        using var meta = await Http.GetAsync($"https://api.github.com/repos/{Repo(kind)}/releases/latest", ct);
        meta.EnsureSuccessStatusCode();
        return ParseRelease(kind, await meta.Content.ReadAsStringAsync(ct));
    }

    /// <summary>Lit la réponse JSON de l'API GitHub « releases/latest ».</summary>
    public static DriverReleaseInfo ParseRelease(DriverKind kind, string json)
    {
        using var doc = JsonDocument.Parse(json);
        string version = doc.RootElement.GetProperty("tag_name").GetString() ?? "?";
        var asset = doc.RootElement.GetProperty("assets").EnumerateArray()
            .FirstOrDefault(a => a.GetProperty("name").GetString() is string n
                                 && n.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
                                 && n.Contains("x64", StringComparison.OrdinalIgnoreCase));
        if (asset.ValueKind == JsonValueKind.Undefined)
            throw new InvalidOperationException($"Aucun installeur Windows x64 trouvé pour {kind.DisplayName()} {version}.");

        string? sha = null;
        if (asset.TryGetProperty("digest", out var digest) && digest.ValueKind == JsonValueKind.String
            && digest.GetString() is { } d && d.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase)
            && d.Length == 7 + 64 && d[7..].All(Uri.IsHexDigit))
            sha = d[7..].ToUpperInvariant();
        return new DriverReleaseInfo
        {
            Tag = version,
            AssetName = asset.GetProperty("name").GetString()!,
            AssetUrl = asset.GetProperty("browser_download_url").GetString()!,
            Sha256 = sha,
            Size = asset.TryGetProperty("size", out var size) && size.ValueKind == JsonValueKind.Number ? size.GetInt64() : 0,
        };
    }

    /// <summary>Télécharge puis lance la dernière version de l'installeur ; renvoie true si l'installeur s'est terminé sans erreur.</summary>
    public static async Task<bool> InstallAsync(DriverKind kind, IProgress<string> progress, CancellationToken ct = default)
    {
        progress.Report($"Recherche de la dernière version de {kind.DisplayName()}…");
        return await InstallAsync(kind, await GetLatestReleaseAsync(kind, ct), progress, ct);
    }

    /// <summary>Télécharge puis lance l'installeur d'une publication connue ; renvoie true si l'installeur s'est terminé sans erreur.</summary>
    public static async Task<bool> InstallAsync(DriverKind kind, DriverReleaseInfo release, IProgress<string> progress, CancellationToken ct = default)
    {
        string name = kind.DisplayName();
        string version = DriverVersion.Display(release.Tag);
        if (!Uri.TryCreate(release.AssetUrl, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps
            || !(uri.Host.Equals("github.com", StringComparison.OrdinalIgnoreCase) || uri.Host.EndsWith(".github.com", StringComparison.OrdinalIgnoreCase)))
            throw new InvalidOperationException($"Adresse de téléchargement inattendue pour {name}.");
        string fileName = Path.GetFileName(release.AssetName);
        string url = release.AssetUrl;
        string dir = Path.Combine(Path.GetTempPath(), "se7en-dualsense-drivers");
        Directory.CreateDirectory(dir);
        string path = Path.Combine(dir, fileName);

        progress.Report($"Téléchargement de {name} {version}…");
        await using (var src = await Http.GetStreamAsync(url, ct))
        await using (var dst = File.Create(path))
            await src.CopyToAsync(dst, ct);

        if (release.Sha256 != null)
        {
            progress.Report($"Vérification de l'empreinte SHA-256 de {name}…");
            string actual;
            await using (var f = File.OpenRead(path))
                actual = Convert.ToHexString(await SHA256.HashDataAsync(f, ct));
            if (!actual.Equals(release.Sha256, StringComparison.OrdinalIgnoreCase))
            {
                File.Delete(path);
                throw new InvalidOperationException($"L'empreinte SHA-256 de l'installeur {name} ne correspond pas à celle publiée : installation annulée par sécurité.");
            }
        }

        progress.Report($"Vérification de la signature de {name}…");
        if (!IsSignedByNefarius(path))
        {
            File.Delete(path);
            throw new InvalidOperationException($"La signature de l'installeur {name} n'est pas valide : installation annulée par sécurité.");
        }

        progress.Report($"Installation de {name} : suivez l'assistant qui s'ouvre…");
        using var proc = Process.Start(new ProcessStartInfo(path) { UseShellExecute = true, Verb = "runas" })!;
        await proc.WaitForExitAsync(ct);

        // 0 = succès, 3010 = succès avec redémarrage requis
        bool ok = proc.ExitCode is 0 or 3010;
        progress.Report(ok
            ? (proc.ExitCode == 3010 ? $"{name} installé. Un redémarrage de Windows est nécessaire." : $"{name} installé.")
            : $"L'installation de {name} n'a pas abouti (code {proc.ExitCode}).");
        return ok;
    }

    /// <summary>
    /// Signature Authenticode valide selon Windows (WinVerifyTrust : chaîne, intégrité du fichier et horodatage —
    /// un installeur signé avant l'expiration du certificat reste valide) ET émise pour Nefarius.
    /// </summary>
    public static bool IsSignedByNefarius(string file)
    {
        try
        {
            if (!WinTrust.Verify(file)) return false;
#pragma warning disable SYSLIB0057 // lecture du certificat du signataire
            using var cert = new X509Certificate2(X509Certificate.CreateFromSignedFile(file));
#pragma warning restore SYSLIB0057
            return cert.Subject.Contains("Nefarius", StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    private static class WinTrust
    {
        private static Guid VerifyV2 = new("00AAC56B-CD44-11d0-8CC2-00C04FC295EE");

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct FileInfo
        {
            public int cbStruct;
            public string pcwszFilePath;
            public IntPtr hFile;
            public IntPtr pgKnownSubject;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct Data
        {
            public int cbStruct;
            public IntPtr pPolicyCallbackData;
            public IntPtr pSIPClientData;
            public int dwUIChoice;
            public int fdwRevocationChecks;
            public int dwUnionChoice;
            public IntPtr pFile;
            public int dwStateAction;
            public IntPtr hWVTStateData;
            public IntPtr pwszURLReference;
            public int dwProvFlags;
            public int dwUIContext;
            public IntPtr pSignatureSettings;
        }

        [DllImport("wintrust.dll", CharSet = CharSet.Unicode)]
        private static extern int WinVerifyTrust(IntPtr hwnd, ref Guid action, ref Data data);

        public static bool Verify(string path)
        {
            var info = new FileInfo { cbStruct = Marshal.SizeOf<FileInfo>(), pcwszFilePath = path };
            IntPtr pInfo = Marshal.AllocHGlobal(Marshal.SizeOf<FileInfo>());
            try
            {
                Marshal.StructureToPtr(info, pInfo, false);
                var data = new Data
                {
                    cbStruct = Marshal.SizeOf<Data>(),
                    dwUIChoice = 2,            // aucune interface
                    fdwRevocationChecks = 0,
                    dwUnionChoice = 1,         // fichier
                    pFile = pInfo,
                    dwStateAction = 0,
                    dwProvFlags = 0x10,        // WTD_REVOCATION_CHECK_NONE : pas d'accès réseau
                };
                return WinVerifyTrust(IntPtr.Zero, ref VerifyV2, ref data) == 0;
            }
            finally
            {
                Marshal.DestroyStructure<FileInfo>(pInfo);
                Marshal.FreeHGlobal(pInfo);
            }
        }
    }
}
