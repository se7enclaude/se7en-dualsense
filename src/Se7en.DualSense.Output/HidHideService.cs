using System.Diagnostics;
using System.Text.Json;
using Microsoft.Win32;

namespace Se7en.DualSense.Output;

/// <summary>
/// Pilote HidHide via son outil en ligne de commande (HidHideCLI.exe, droits administrateur requis).
///
/// Sécurité : chaque périphérique masqué est noté dans un journal sur disque AVANT d'être masqué.
/// Au démarrage suivant (même après un plantage ou un arrêt brutal), tout ce qui figure dans le journal
/// est démasqué : la manette ne peut jamais rester invisible.
/// </summary>
public sealed class HidHideService
{
    private readonly object _lock = new();
    private readonly string _journalPath;
    private readonly HashSet<string> _hidden = new(StringComparer.OrdinalIgnoreCase);

    public HidHideService(string dataDirectory)
    {
        Directory.CreateDirectory(dataDirectory);
        _journalPath = Path.Combine(dataDirectory, "hidhide-journal.json");
        CliPath = FindCli();
    }

    /// <summary>Chemin de HidHideCLI.exe, ou null si HidHide n'est pas installé.</summary>
    public string? CliPath { get; }

    public bool IsInstalled => CliPath != null;

    public event Action<string>? Log;

    private static string? FindCli()
    {
        var candidates = new List<string>();
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Nefarius Software Solutions e.U.\HidHide");
            if (key?.GetValue("Path") is string p)
                candidates.Add(Path.Combine(p, "x64", "HidHideCLI.exe"));
        }
        catch { /* registre inaccessible */ }

        string pf = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        candidates.Add(Path.Combine(pf, "Nefarius Software Solutions", "HidHide", "x64", "HidHideCLI.exe"));
        candidates.Add(Path.Combine(pf, "Nefarius Software Solutions e.U", "HidHide", "x64", "HidHideCLI.exe"));
        return candidates.FirstOrDefault(File.Exists);
    }

    /// <summary>
    /// Convertit un chemin de périphérique HID (\\?\hid#vid_054c&amp;pid_0ce6#7&amp;…#{guid})
    /// en identifiant d'instance Windows (HID\VID_054C&amp;PID_0CE6\7&amp;…) attendu par HidHide.
    /// </summary>
    public static string ToInstanceId(string devicePath)
    {
        string p = devicePath;
        if (p.StartsWith(@"\\?\", StringComparison.Ordinal))
            p = p.Substring(4);
        int guid = p.LastIndexOf("#{", StringComparison.Ordinal);
        if (guid >= 0)
            p = p.Substring(0, guid);
        return p.Replace('#', '\\').ToUpperInvariant();
    }

    /// <summary>Démasque tout ce que le journal contient (reste d'une session précédente).</summary>
    public void RecoverFromJournal()
    {
        lock (_lock)
        {
            var leftovers = ReadJournal();
            if (leftovers.Count == 0) return;
            Log?.Invoke($"Session précédente interrompue : démasquage de {leftovers.Count} périphérique(s).");
            if (Run(leftovers.SelectMany(id => new[] { "--dev-unhide", id }).ToArray()))
                WriteJournal(Array.Empty<string>());
        }
    }

    /// <summary>Autorise notre exécutable à voir les périphériques masqués (indispensable avant de masquer).</summary>
    public bool RegisterSelf()
    {
        string? exe = Environment.ProcessPath;
        return exe != null && Run("--app-reg", exe, "--cloak-on");
    }

    public bool Hide(string instanceId)
    {
        lock (_lock)
        {
            if (_hidden.Contains(instanceId)) return true;
            _hidden.Add(instanceId);
            WriteJournal(_hidden);          // journal d'abord : jamais de masquage non tracé
            if (Run("--dev-hide", instanceId))
                return true;
            _hidden.Remove(instanceId);
            WriteJournal(_hidden);
            return false;
        }
    }

    public bool Unhide(string instanceId)
    {
        lock (_lock)
        {
            bool ok = Run("--dev-unhide", instanceId);
            if (ok)
            {
                _hidden.Remove(instanceId);
                WriteJournal(_hidden);
            }
            return ok;
        }
    }

    /// <summary>Démasque tout ce que cette session a masqué. Appelé à la fermeture et en cas de plantage.</summary>
    public void UnhideAll()
    {
        lock (_lock)
        {
            var all = _hidden.Union(ReadJournal(), StringComparer.OrdinalIgnoreCase).ToList();
            if (all.Count == 0) return;
            if (Run(all.SelectMany(id => new[] { "--dev-unhide", id }).ToArray()))
            {
                _hidden.Clear();
                WriteJournal(Array.Empty<string>());
            }
        }
    }

    private bool Run(params string[] args)
    {
        if (CliPath == null)
            return false;
        try
        {
            var psi = new ProcessStartInfo(CliPath)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };
            foreach (var a in args) psi.ArgumentList.Add(a);
            using var p = Process.Start(psi)!;
            string err = p.StandardError.ReadToEnd();
            p.StandardOutput.ReadToEnd();
            if (!p.WaitForExit(8000))
            {
                try { p.Kill(); } catch { /* ignoré */ }
                Log?.Invoke("HidHide ne répond pas.");
                return false;
            }
            if (p.ExitCode != 0)
            {
                Log?.Invoke($"HidHide a refusé l'opération ({p.ExitCode}). {err.Trim()} Lancez se7en DualSense en administrateur.");
                return false;
            }
            return true;
        }
        catch (Exception ex)
        {
            Log?.Invoke($"Impossible d'utiliser HidHide : {ex.Message}");
            return false;
        }
    }

    private List<string> ReadJournal()
    {
        try
        {
            if (File.Exists(_journalPath))
                return JsonSerializer.Deserialize<List<string>>(File.ReadAllText(_journalPath)) ?? new();
        }
        catch { /* journal illisible */ }
        return new();
    }

    private void WriteJournal(IEnumerable<string> ids)
    {
        try { File.WriteAllText(_journalPath, JsonSerializer.Serialize(ids.ToList())); }
        catch { /* non bloquant */ }
    }
}
