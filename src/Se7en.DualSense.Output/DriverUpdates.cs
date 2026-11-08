using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace Se7en.DualSense.Output;

/// <summary>Versions de pilotes : « v1.5.230.0 », « 1.5.230 » et « 1.5.230.0 » désignent la même version.</summary>
public static class DriverVersion
{
    private static readonly Regex Pattern = new(@"(\d+)(?:\.(\d+))?(?:\.(\d+))?(?:\.(\d+))?", RegexOptions.CultureInvariant);

    /// <summary>Version normalisée sur quatre composantes (les manquantes valent 0), ou null.</summary>
    public static Version? Parse(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        var m = Pattern.Match(text);
        if (!m.Success) return null;
        try
        {
            int Part(int i) => m.Groups[i].Success ? int.Parse(m.Groups[i].Value, CultureInfo.InvariantCulture) : 0;
            return new Version(Part(1), Part(2), Part(3), Part(4));
        }
        catch (OverflowException)
        {
            return null;
        }
    }

    /// <summary>Affichage court : « 1.5.230 » (la 4ᵉ composante n'apparaît que si elle n'est pas nulle).</summary>
    public static string Display(Version v) => v.Revision > 0 ? v.ToString(4) : v.ToString(3);

    public static string Display(string? text) => Parse(text) is { } v ? Display(v) : (text ?? "?");
}

public enum DriverUpdateState
{
    /// <summary>Pilote absent : c'est l'installation (pas la mise à jour) qui s'applique.</summary>
    NotInstalled,
    /// <summary>Dernière version publiée encore inconnue (jamais vérifié, hors ligne).</summary>
    NotChecked,
    /// <summary>Pilote présent mais version installée illisible.</summary>
    InstalledVersionUnknown,
    UpToDate,
    UpdateAvailable,
}

/// <summary>Dernière publication connue d'un pilote (mémorisée entre deux vérifications).</summary>
public sealed class DriverReleaseInfo
{
    public string Tag { get; set; } = "";
    public string AssetName { get; set; } = "";
    public string AssetUrl { get; set; } = "";
    /// <summary>Empreinte SHA-256 publiée par GitHub (champ « digest »), en hexadécimal ; null si absente.</summary>
    public string? Sha256 { get; set; }
    public long Size { get; set; }

    [System.Text.Json.Serialization.JsonIgnore]
    public Version? Version => DriverVersion.Parse(Tag);
}

/// <summary>État persistant des vérifications (fichier driver-updates.json du dossier de données).</summary>
public sealed class DriverUpdateStateFile
{
    /// <summary>Dernière vérification réussie (UTC) ; null si jamais.</summary>
    public DateTime? LastCheckUtc { get; set; }
    public Dictionary<string, DriverReleaseInfo> Latest { get; set; } = new();
    /// <summary>Dernière version déjà signalée à l'utilisateur, par pilote (une seule notification par version).</summary>
    public Dictionary<string, string> Notified { get; set; } = new();

    public DriverReleaseInfo? LatestOf(DriverKind kind) => Latest.TryGetValue(kind.ToString(), out var r) ? r : null;

    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    public static DriverUpdateStateFile Load(string path)
    {
        try
        {
            if (File.Exists(path))
                return JsonSerializer.Deserialize<DriverUpdateStateFile>(File.ReadAllText(path)) ?? new();
        }
        catch { /* fichier abîmé : on repart de zéro */ }
        return new();
    }

    public void Save(string path)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            string tmp = path + ".tmp";
            File.WriteAllText(tmp, JsonSerializer.Serialize(this, Json));
            File.Move(tmp, path, overwrite: true);
        }
        catch { /* disque indisponible : la prochaine vérification réécrira */ }
    }
}

/// <summary>Logique pure (sans réseau ni registre) : comparaison, échéance, notification, textes.</summary>
public static class DriverUpdateLogic
{
    public static readonly TimeSpan CheckInterval = TimeSpan.FromHours(12);

    public static DriverUpdateState Compare(bool installed, string? installedVersion, string? latestTag)
    {
        if (!installed) return DriverUpdateState.NotInstalled;
        var latest = DriverVersion.Parse(latestTag);
        if (latest == null) return DriverUpdateState.NotChecked;
        var current = DriverVersion.Parse(installedVersion);
        if (current == null) return DriverUpdateState.InstalledVersionUnknown;
        return current >= latest ? DriverUpdateState.UpToDate : DriverUpdateState.UpdateAvailable;
    }

    /// <summary>Vrai si la dernière vérification réussie date de plus de 12 h (ou n'a jamais eu lieu, ou est dans le futur).</summary>
    public static bool IsCheckDue(DateTime? lastCheckUtc, DateTime nowUtc) =>
        lastCheckUtc is not { } last || last > nowUtc || nowUtc - last >= CheckInterval;

    /// <summary>Une mise à jour est à signaler si elle est disponible et que cette version n'a pas déjà été signalée.</summary>
    public static bool ShouldNotify(DriverUpdateState state, string? latestTag, string? notifiedTag) =>
        state == DriverUpdateState.UpdateAvailable
        && DriverVersion.Parse(latestTag) is { } latest
        && DriverVersion.Parse(notifiedTag) != latest;

    private static readonly string[] Months = { "janv.", "févr.", "mars", "avr.", "mai", "juin", "juil.", "août", "sept.", "oct.", "nov.", "déc." };

    /// <summary>« 9 nov. 2026 » (heure locale).</summary>
    public static string FormatDate(DateTime local) => $"{local.Day} {Months[local.Month - 1]} {local.Year}";

    /// <summary>« le 9 nov. 2026 à 14:05 ».</summary>
    public static string FormatDateTime(DateTime local) => $"le {FormatDate(local)} à {local:HH\\:mm}";

    /// <summary>Texte de l'état d'un pilote pour les Réglages.</summary>
    public static string Describe(DriverUpdateState state, string? installedVersion, string? latestTag, DateTime? lastCheckLocal)
    {
        string installed = DriverVersion.Display(installedVersion);
        string latest = DriverVersion.Display(latestTag);
        return state switch
        {
            DriverUpdateState.NotInstalled => "Absent : à installer depuis l'accueil.",
            DriverUpdateState.UpToDate => lastCheckLocal is { } d
                ? $"À jour (version {installed}, vérifié le {FormatDate(d)})"
                : $"À jour (version {installed})",
            DriverUpdateState.UpdateAvailable => $"Mise à jour disponible : {installed} → {latest}",
            DriverUpdateState.InstalledVersionUnknown => $"Installé (version inconnue) · dernière version publiée : {latest}",
            _ => installedVersion != null
                ? $"Installé (version {installed}) · dernière version pas encore vérifiée"
                : "Installé · dernière version pas encore vérifiée",
        };
    }
}

/// <summary>Lecture de la version installée d'un pilote.</summary>
public static class InstalledDriverVersion
{
    /// <summary>
    /// DisplayVersion de l'entrée de désinstallation Windows (même numérotation que les publications GitHub),
    /// puis la clé HidHide, puis la version du fichier .sys en dernier recours. Null si introuvable.
    /// </summary>
    public static string? Read(DriverKind kind)
    {
        string displayName = kind == DriverKind.ViGEmBus ? "ViGEm Bus Driver" : "HidHide";
        string? best = null;
        foreach (string root in new[] { @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall", @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall" })
        {
            try
            {
                using var uninstall = Registry.LocalMachine.OpenSubKey(root);
                if (uninstall == null) continue;
                foreach (string name in uninstall.GetSubKeyNames())
                {
                    using var k = uninstall.OpenSubKey(name);
                    if (k?.GetValue("DisplayName") is not string dn || !dn.Equals(displayName, StringComparison.OrdinalIgnoreCase)) continue;
                    if (k.GetValue("Publisher") is string pub && !pub.Contains("Nefarius", StringComparison.OrdinalIgnoreCase)) continue;
                    if (k.GetValue("DisplayVersion") is string v && DriverVersion.Parse(v) is { } parsed
                        && (DriverVersion.Parse(best) is not { } b || parsed > b))
                        best = v;
                }
            }
            catch { /* registre inaccessible */ }
        }
        if (best != null) return best;

        if (kind == DriverKind.HidHide)
        {
            try
            {
                using var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Nefarius Software Solutions e.U.\HidHide");
                if (key?.GetValue("Version") is string v && DriverVersion.Parse(v) != null) return v;
            }
            catch { /* registre inaccessible */ }
        }

        try
        {
            string sys = Path.Combine(Environment.SystemDirectory, "drivers", kind == DriverKind.ViGEmBus ? "ViGEmBus.sys" : "HidHide.sys");
            if (File.Exists(sys) && System.Diagnostics.FileVersionInfo.GetVersionInfo(sys).FileVersion is { Length: > 0 } fv)
                return fv;
        }
        catch { /* fichier inaccessible */ }
        return null;
    }
}

/// <summary>État d'un pilote à un instant donné (installé, versions, état de mise à jour).</summary>
public sealed record DriverUpdateInfo(DriverKind Kind, bool Installed, string? InstalledVersion, DriverReleaseInfo? Latest, DriverUpdateState State);

/// <summary>
/// Vérifie au plus toutes les 12 h la dernière publication GitHub de chaque pilote et la compare à la version installée.
/// Hors ligne, le dernier résultat connu est conservé.
/// </summary>
public sealed class DriverUpdateChecker
{
    private readonly string _statePath;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public DriverUpdateChecker(string dataDirectory)
    {
        _statePath = Path.Combine(dataDirectory, "driver-updates.json");
        State = DriverUpdateStateFile.Load(_statePath);
    }

    public DriverUpdateStateFile State { get; private set; }

    public DateTime? LastCheckLocal => State.LastCheckUtc?.ToLocalTime();

    /// <summary>Déclenché après chaque vérification réussie.</summary>
    public event Action? Checked;

    public DriverUpdateInfo Info(DriverKind kind, DriverStatus status)
    {
        bool installed = kind == DriverKind.ViGEmBus ? status.ViGEmBusInstalled : status.HidHideInstalled;
        string? version = installed ? InstalledDriverVersion.Read(kind) : null;
        var latest = State.LatestOf(kind);
        return new DriverUpdateInfo(kind, installed, version, latest, DriverUpdateLogic.Compare(installed, version, latest?.Tag));
    }

    public IReadOnlyList<DriverUpdateInfo> Infos(DriverStatus status) =>
        new[] { Info(DriverKind.ViGEmBus, status), Info(DriverKind.HidHide, status) };

    /// <summary>
    /// Interroge GitHub si la dernière vérification date de plus de 12 h (ou toujours si <paramref name="force"/>).
    /// Renvoie true si la vérification a abouti ; en cas d'échec (hors ligne…), l'état précédent est gardé.
    /// </summary>
    public async Task<bool> CheckAsync(bool force, CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct);
        try
        {
            if (!force && !DriverUpdateLogic.IsCheckDue(State.LastCheckUtc, DateTime.UtcNow))
                return false;
            var results = new Dictionary<string, DriverReleaseInfo>();
            foreach (var kind in new[] { DriverKind.ViGEmBus, DriverKind.HidHide })
                results[kind.ToString()] = await DriverInstaller.GetLatestReleaseAsync(kind, ct);

            // Les notifications déjà faites sont gardées
            var fresh = new DriverUpdateStateFile { Latest = results, Notified = State.Notified, LastCheckUtc = DateTime.UtcNow };
            fresh.Save(_statePath);
            State = fresh;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            return false; // hors ligne, limite de l'API… : on garde le dernier résultat
        }
        finally
        {
            _gate.Release();
        }
        Checked?.Invoke();
        return true;
    }

    /// <summary>Mises à jour pas encore signalées ; elles sont marquées comme signalées.</summary>
    public IReadOnlyList<DriverUpdateInfo> TakeNewNotifications(DriverStatus status)
    {
        var list = new List<DriverUpdateInfo>();
        foreach (var info in Infos(status))
        {
            string key = info.Kind.ToString();
            State.Notified.TryGetValue(key, out var notified);
            if (!DriverUpdateLogic.ShouldNotify(info.State, info.Latest?.Tag, notified)) continue;
            State.Notified[key] = info.Latest!.Tag;
            list.Add(info);
        }
        if (list.Count > 0) State.Save(_statePath);
        return list;
    }
}
