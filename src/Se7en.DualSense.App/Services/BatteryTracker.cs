using System.IO;
using System.Text.Json;

namespace Se7en.DualSense.App.Services;

/// <summary>Un relevé de batterie.</summary>
public sealed record BatterySample(DateTime Time, int Percent, bool Charging);

/// <summary>
/// Historique de la batterie (7 jours, par manette) et estimation de l'autonomie restante :
/// vitesse de décharge mesurée sur la session en cours (la DualSense annonce sa charge par paliers de 10 %).
/// </summary>
public static class BatteryTracker
{
    private static readonly object Lock = new();
    private static Dictionary<string, List<BatterySample>>? _data;
    private static DateTime _lastSave;

    private static string FilePath => Path.Combine(AppSettings.Directory, "battery-history.json");

    private static Dictionary<string, List<BatterySample>> Data
    {
        get
        {
            if (_data != null) return _data;
            try
            {
                if (File.Exists(FilePath))
                    _data = JsonSerializer.Deserialize<Dictionary<string, List<BatterySample>>>(File.ReadAllText(FilePath));
            }
            catch { /* illisible */ }
            return _data ??= new();
        }
    }

    /// <summary>À appeler régulièrement : un relevé est gardé quand la charge ou l'état change, sinon toutes les 10 min.</summary>
    public static void Record(string identity, int percent, bool charging)
    {
        lock (Lock)
        {
            var list = Data.TryGetValue(identity, out var l) ? l : Data[identity] = new();
            var now = DateTime.Now;
            var last = list.Count > 0 ? list[^1] : null;
            if (last != null && last.Percent == percent && last.Charging == charging && now - last.Time < TimeSpan.FromMinutes(10))
                return;
            list.Add(new BatterySample(now, percent, charging));
            list.RemoveAll(s => s.Time < now.AddDays(-7));
            if (DateTime.UtcNow - _lastSave > TimeSpan.FromMinutes(2))
            {
                _lastSave = DateTime.UtcNow;
                try { File.WriteAllText(FilePath, JsonSerializer.Serialize(Data)); } catch { /* non bloquant */ }
            }
        }
    }

    public static IReadOnlyList<BatterySample> Get(string? identity)
    {
        lock (Lock)
            return identity != null && Data.TryGetValue(identity, out var l) ? l.ToList() : new List<BatterySample>();
    }

    /// <summary>
    /// Autonomie restante estimée, ou null si la décharge n'a pas encore été mesurée
    /// (il faut avoir vu la charge baisser d'au moins un palier depuis le débranchement).
    /// </summary>
    public static TimeSpan? Remaining(string? identity, int percent)
    {
        var list = Get(identity).ToList();
        if (list.Count < 2 || list[^1].Charging) return null;
        // Session de décharge en cours : depuis le dernier passage en charge
        int start = list.FindLastIndex(s => s.Charging) + 1;
        var session = list.Skip(start).ToList();
        if (session.Count < 2) return null;
        // Il faut avoir vu la charge baisser d'au moins un palier
        if (session.All(s => s.Percent >= session[0].Percent)) return null;
        var b = session[^1];
        double hours = (b.Time - session[0].Time).TotalHours;
        double used = session[0].Percent - b.Percent;
        if (used <= 0 || hours <= 0.05) return null;
        double perHour = used / hours;
        return TimeSpan.FromHours(Math.Clamp(percent / perHour, 0, 24));
    }

    public static string Format(TimeSpan t) => t.TotalHours >= 1 ? $"{(int)t.TotalHours} h {t.Minutes:00}" : $"{t.Minutes} min";
}
