using System.IO;
using System.Text.Json;
using System.Windows;

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
        UiSettings.Save(new UiSettings.Data(theme));
        ThemeChanged?.Invoke();
    }

    public static void Toggle() => Apply(Current == AppTheme.Light ? AppTheme.Dark : AppTheme.Light);
}

/// <summary>Préférences d'interface persistées dans %AppData%\se7en DualSense\ui.json.</summary>
public static class UiSettings
{
    public sealed record Data(AppTheme Theme);

    public static string Directory { get; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "se7en DualSense");

    private static string FilePath => Path.Combine(Directory, "ui.json");

    public static Data Load()
    {
        try
        {
            if (File.Exists(FilePath))
                return JsonSerializer.Deserialize<Data>(File.ReadAllText(FilePath)) ?? new Data(AppTheme.Light);
        }
        catch
        {
            // Fichier illisible : on repart des valeurs par défaut
        }
        return new Data(AppTheme.Light);
    }

    public static void Save(Data data)
    {
        try
        {
            System.IO.Directory.CreateDirectory(Directory);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(data));
        }
        catch
        {
            // Non bloquant
        }
    }
}
