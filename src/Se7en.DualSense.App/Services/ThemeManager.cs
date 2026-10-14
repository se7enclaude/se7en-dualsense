using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Windows;
using System.Windows.Media;
using Se7en.DualSense.Output;
using Se7en.DualSense.Processing;

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
        ApplyAccent();
        ThemeChanged?.Invoke();
    }

    public static void Toggle() => Apply(Current == AppTheme.Light ? AppTheme.Dark : AppTheme.Light);

    // ───────────── Couleur d'accent ─────────────

    /// <summary>Accents proposés : (clé, nom, couleur en thème clair, couleur en thème sombre).</summary>
    public static readonly (string Key, string Name, Color Light, Color Dark)[] Accents =
    {
        ("violet", "Violet", Color.FromRgb(0x5B, 0x2D, 0xE0), Color.FromRgb(0x7B, 0x57, 0xEA)),
        ("blue", "Bleu", Color.FromRgb(0x00, 0x6F, 0xCD), Color.FromRgb(0x2E, 0x8B, 0xEA)),
        ("cyan", "Cyan", Color.FromRgb(0x0A, 0x8F, 0xA6), Color.FromRgb(0x2C, 0xC4, 0xDB)),
        ("green", "Vert", Color.FromRgb(0x14, 0x8F, 0x52), Color.FromRgb(0x2E, 0xC2, 0x73)),
        ("orange", "Orange", Color.FromRgb(0xD9, 0x5B, 0x0B), Color.FromRgb(0xF5, 0x81, 0x2F)),
        ("red", "Rouge", Color.FromRgb(0xCF, 0x22, 0x3A), Color.FromRgb(0xF0, 0x4A, 0x5E)),
        ("pink", "Rose", Color.FromRgb(0xC8, 0x2A, 0x7A), Color.FromRgb(0xEE, 0x5A, 0xA0)),
        ("lightbar", "Barre lumineuse", Color.FromRgb(0x5B, 0x2D, 0xE0), Color.FromRgb(0x7B, 0x57, 0xEA)),
    };

    private static ResourceDictionary? _accentDictionary;
    private static Color _liveColor = Color.FromRgb(0x5B, 0x2D, 0xE0);

    public static string AccentKey => AppSettings.Current.Accent;

    public static void SetAccent(string key)
    {
        AppSettings.Current.Accent = key;
        AppSettings.Save();
        ApplyAccent();
        ThemeChanged?.Invoke();
    }

    /// <summary>Accent « barre lumineuse » : suit la couleur envoyée à la manette (rendue lisible).</summary>
    public static void SetLiveColor(Color c)
    {
        if (AccentKey != "lightbar") return;
        // Couleur trop sombre ou éteinte : on garde la précédente
        if (Math.Max(c.R, Math.Max(c.G, c.B)) < 40) return;
        var readable = Readable(c);
        if (Distance(readable, _liveColor) < 18) return;
        _liveColor = readable;
        ApplyAccent();
        ThemeChanged?.Invoke();
    }

    public static void ApplyAccent()
    {
        var entry = Accents.FirstOrDefault(a => a.Key == AccentKey);
        if (entry.Key == null) entry = Accents[0];
        bool dark = Current == AppTheme.Dark;
        var baseColor = entry.Key == "lightbar" ? (dark ? Mix(_liveColor, Colors.White, 0.15) : _liveColor) : dark ? entry.Dark : entry.Light;

        var d = new ResourceDictionary();
        Color soft = dark ? Color.FromArgb(0x2E, baseColor.R, baseColor.G, baseColor.B) : Mix(baseColor, Colors.White, 0.9);
        d["Color.Accent"] = baseColor;
        d["Color.AccentSoft"] = soft;
        d["Accent"] = Frozen(baseColor);
        d["Accent.Hover"] = Frozen(dark ? Mix(baseColor, Colors.White, 0.15) : Mix(baseColor, Colors.Black, 0.15));
        d["Accent.Press"] = Frozen(dark ? Mix(baseColor, Colors.Black, 0.2) : Mix(baseColor, Colors.Black, 0.35));
        d["Accent.Soft"] = Frozen(soft);
        d["Accent.Line"] = Frozen(Mix(baseColor, Colors.White, 0.4));
        d["Text.Accent"] = Frozen(dark ? Mix(baseColor, Colors.White, 0.3) : baseColor);
        d["Shadow.Glow"] = new System.Windows.Media.Effects.DropShadowEffect
        {
            Color = baseColor, Direction = 270, ShadowDepth = 4, BlurRadius = 22, Opacity = dark ? 0.5 : 0.45,
        };

        var merged = Application.Current.Resources.MergedDictionaries;
        if (_accentDictionary != null) merged.Remove(_accentDictionary);
        _accentDictionary = d;
        merged.Add(d);
    }

    private static SolidColorBrush Frozen(Color c)
    {
        var b = new SolidColorBrush(c);
        b.Freeze();
        return b;
    }

    private static Color Mix(Color a, Color b, double t) => Color.FromRgb(
        (byte)(a.R + (b.R - a.R) * t), (byte)(a.G + (b.G - a.G) * t), (byte)(a.B + (b.B - a.B) * t));

    private static double Distance(Color a, Color b) => Math.Abs(a.R - b.R) + Math.Abs(a.G - b.G) + Math.Abs(a.B - b.B);

    /// <summary>Assombrit une couleur trop claire pour qu'un texte blanc reste lisible dessus.</summary>
    private static Color Readable(Color c)
    {
        double lum = (0.2126 * c.R + 0.7152 * c.G + 0.0722 * c.B) / 255;
        return lum > 0.55 ? Mix(c, Colors.Black, (lum - 0.45) * 1.2) : c;
    }
}

public sealed record CalibrationPair(StickCalibration Left, StickCalibration Right);

/// <summary>Préférences persistées dans %AppData%\se7en DualSense\settings.json.</summary>
public sealed class AppSettings
{
    public AppTheme Theme { get; set; } = AppTheme.Light;

    /// <summary>Profil choisi manuellement (un jeu associé reprend la main quand il tourne).</summary>
    public string SelectedProfileId { get; set; } = Profile.DefaultId;

    /// <summary>Calibration des sticks par manette (clé = numéro de série, « * » = toutes).</summary>
    public Dictionary<string, CalibrationPair> Calibrations { get; set; } = new();

    public bool StartWithWindows { get; set; }
    public bool StartMinimized { get; set; }
    /// <summary>La croix de la fenêtre réduit dans la zone de notification au lieu de quitter.</summary>
    public bool CloseToTray { get; set; } = true;
    public bool ShowOverlay { get; set; }
    public bool ShowIntro { get; set; } = true;
    public bool OnboardingDone { get; set; }
    /// <summary>Naviguer dans l'application avec la croix directionnelle et ✕ / ○.</summary>
    public bool ControllerNavigation { get; set; } = true;
    public bool NotifyOnConnect { get; set; } = true;

    /// <summary>Couleur d'accent de l'interface (voir <see cref="ThemeManager.Accents"/>).</summary>
    public string Accent { get; set; } = "violet";
    /// <summary>PS + bouton = raccourcis système.</summary>
    public bool PsShortcuts { get; set; } = true;
    /// <summary>Le bouton micro de la manette coupe le micro de Windows.</summary>
    public bool MuteControlsMicrophone { get; set; } = true;
    public bool BatteryAlerts { get; set; } = true;
    public bool AutoCheckUpdates { get; set; } = true;
    /// <summary>Recentrage automatique des sticks quand la manette est posée.</summary>
    public bool AutoRecenter { get; set; } = true;
    /// <summary>Manette virtuelle rebranchée quand on revient dans le jeu (Alt + Tab).</summary>
    public bool ReconnectOnFocus { get; set; } = true;
    /// <summary>Profil fixe par manette (clé = adresse de la manette).</summary>
    public Dictionary<string, string> ControllerProfiles { get; set; } = new();
    /// <summary>Copie du son du PC dans le haut-parleur / casque de la manette (USB).</summary>
    public bool MirrorSoundToController { get; set; }
    public double MirrorVolume { get; set; } = 0.8;
    /// <summary>Effets sonores de l'interface.</summary>
    public bool Sounds { get; set; } = true;
    public double SoundVolume { get; set; } = 0.5;
    /// <summary>Bandeau Steam Input masqué par l'utilisateur.</summary>
    public bool SteamHintDismissed { get; set; }
    public DateTime LastUpdateCheck { get; set; }
    /// <summary>Version proposée que l'utilisateur a choisi d'ignorer.</summary>
    public string? SkippedVersion { get; set; }

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
