using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Windows;
using Se7en.DualSense.Output;

namespace Se7en.DualSense.App.Services;

public enum AppTheme
{
    Light,
    Dark,
}

/// <summary>Bascule entre les palettes Axis claire et sombre (toutes les couleurs sont en DynamicResource).</summary>
public static class ThemeManager
{
    private static readonly Uri LightUri = new("pack://application:,,,/Themes/Light.xaml");
    private static readonly Uri DarkUri = new("pack://application:,,,/Themes/Dark.xaml");

    public static AppTheme Current { get; private set; } = AppTheme.Light;

    public static event Action? ThemeChanged;

    public static void Apply(AppTheme theme)
    {
        var merged = Application.Current.Resources.MergedDictionaries;
        var palette = new ResourceDictionary { Source = theme == AppTheme.Dark ? DarkUri : LightUri };

        // La palette est toujours le premier dictionnaire fusionné
        if (merged.Count > 0)
            merged[0] = palette;
        else
            merged.Add(palette);

        Current = theme;
        AppSettings.Current.Theme = theme;
        AppSettings.Save();
        ThemeChanged?.Invoke();
    }

    public static void Toggle() => Apply(Current == AppTheme.Light ? AppTheme.Dark : AppTheme.Light);
}

/// <summary>Préférences persistées dans %AppData%\se7en DualSense\settings.json.</summary>
public sealed class AppSettings
{
    public AppTheme Theme { get; set; } = AppTheme.Light;

    [JsonConverter(typeof(JsonStringEnumConverter))]
    public OutputMode OutputMode { get; set; } = OutputMode.DualShock4;

    public static string Directory { get; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "se7en DualSense");

    private static string FilePath => Path.Combine(Directory, "settings.json");

    public static AppSettings Current { get; private set; } = Load();

    private static AppSettings Load()
    {
        try
        {
            if (File.Exists(FilePath))
                return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(FilePath)) ?? new AppSettings();
        }
        catch
        {
            // Fichier illisible : on repart des valeurs par défaut
        }
        return new AppSettings();
    }

    public static void Save()
    {
        try
        {
            System.IO.Directory.CreateDirectory(Directory);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(Current, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch
        {
            // Non bloquant
        }
    }
}
