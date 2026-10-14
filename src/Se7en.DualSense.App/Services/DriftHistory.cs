using System.IO;
using System.Text.Json;

namespace Se7en.DualSense.App.Services;

/// <summary>Décalage moyen au repos de chaque stick pour une journée (valeurs brutes, avant calibration).</summary>
public sealed record DriftDay(DateOnly Date, double Left, double Right, int Samples);

/// <summary>
/// Historique de l'usure des sticks, par manette : chaque fois qu'un stick est immobile, son décalage
/// brut au repos est noté. Une moyenne par jour, conservée un an (drift-history.json).
/// </summary>
public static class DriftHistory
{
    private static readonly object Lock = new();
    private static Dictionary<string, List<DriftDay>>? _data;
    private static DateTime _lastSave = DateTime.MinValue;
    private static bool _dirty;

    private static string FilePath => Path.Combine(AppSettings.Directory, "drift-history.json");

    private static Dictionary<string, List<DriftDay>> Data
    {
        get
        {
            if (_data != null) return _data;
            try
            {
                if (File.Exists(FilePath))
                    _data = JsonSerializer.Deserialize<Dictionary<string, List<DriftDay>>>(File.ReadAllText(FilePath));
            }
            catch { /* fichier illisible */ }
            return _data ??= new();
        }
    }

    public static void Record(string identity, double left, double right)
    {
        if (double.IsNaN(left) && double.IsNaN(right)) return;
        lock (Lock)
        {
            var list = Data.TryGetValue(identity, out var l) ? l : Data[identity] = new();
            var today = DateOnly.FromDateTime(DateTime.Now);
            var last = list.Count > 0 ? list[^1] : null;
            if (last == null || last.Date != today)
            {
                list.Add(new DriftDay(today, double.IsNaN(left) ? 0 : left, double.IsNaN(right) ? 0 : right, 1));
                list.RemoveAll(d => d.Date < today.AddDays(-365));
            }
            else
            {
                int n = last.Samples;
                list[^1] = last with
                {
                    Left = double.IsNaN(left) ? last.Left : (last.Left * n + left) / (n + 1),
                    Right = double.IsNaN(right) ? last.Right : (last.Right * n + right) / (n + 1),
                    Samples = Math.Min(n + 1, 10000),
                };
            }
            _dirty = true;
            if (DateTime.UtcNow - _lastSave > TimeSpan.FromSeconds(60))
                SaveLocked();
        }
    }

    public static IReadOnlyList<DriftDay> Get(string? identity)
    {
        lock (Lock)
        {
            if (identity != null && Data.TryGetValue(identity, out var list))
                return list.ToList();
            // Sans identifiant : la manette la plus suivie
            return Data.Values.OrderByDescending(l => l.Count).FirstOrDefault()?.ToList() ?? new List<DriftDay>();
        }
    }

    public static void Flush()
    {
        lock (Lock)
            if (_dirty) SaveLocked();
    }

    private static void SaveLocked()
    {
        try
        {
            Directory.CreateDirectory(AppSettings.Directory);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(Data));
            _dirty = false;
        }
        catch { /* non bloquant */ }
        _lastSave = DateTime.UtcNow;
    }
}
