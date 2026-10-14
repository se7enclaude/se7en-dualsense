using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;
using Se7en.DualSense.Hid;
using Se7en.DualSense.Processing;

namespace Se7en.DualSense.Output;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum LightbarMode
{
    /// <summary>Couleur demandée par le jeu (mode DS4), sinon couleur personnalisée.</summary>
    FromGame,
    Custom,
    /// <summary>Vert → orange → rouge selon la batterie.</summary>
    Battery,
    Off,
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum AudioRumbleMode
{
    Off,
    /// <summary>Basses uniquement (explosions, moteurs, musique).</summary>
    Bass,
    /// <summary>Tout le son : basses → gros moteur, médiums → petit moteur.</summary>
    Full,
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum TriggerEffectKind
{
    Off,
    Resistance,
    Section,
    Vibration,
}

/// <summary>Réglage d'une gâchette adaptative (valeurs 0-1).</summary>
public sealed record TriggerEffectSettings
{
    public TriggerEffectKind Kind { get; init; } = TriggerEffectKind.Off;
    public double Start { get; init; } = 0.3;
    public double End { get; init; } = 0.7;
    public double Force { get; init; } = 0.6;
    public double Frequency { get; init; } = 0.3;

    public TriggerEffect ToEffect() => Kind switch
    {
        TriggerEffectKind.Resistance => TriggerEffect.Resistance(Start, Force),
        TriggerEffectKind.Section => TriggerEffect.Section(Start, End, Force),
        TriggerEffectKind.Vibration => TriggerEffect.Vibration(Frequency, Force, Start),
        _ => TriggerEffect.Off,
    };
}

/// <summary>Retours envoyés à la vraie DualSense.</summary>
public sealed record FeedbackSettings
{
    /// <summary>Intensité des vibrations (0 = coupées, 1 = normales, 2 = doublées).</summary>
    public double RumbleIntensity { get; init; } = 1;
    public LightbarMode Lightbar { get; init; } = LightbarMode.FromGame;
    public byte Red { get; init; } = 0x5B;
    public byte Green { get; init; } = 0x2D;
    public byte Blue { get; init; } = 0xE0;
    /// <summary>Luminosité de la barre lumineuse (0-1).</summary>
    public double Brightness { get; init; } = 1;
    /// <summary>LED joueur (1-4).</summary>
    public int PlayerNumber { get; init; } = 1;
    public TriggerEffectSettings LeftTrigger { get; init; } = new();
    public TriggerEffectSettings RightTrigger { get; init; } = new();
    /// <summary>Vibrations générées depuis le son de Windows (utile pour les jeux sans vibrations).</summary>
    public AudioRumbleMode AudioRumble { get; init; } = AudioRumbleMode.Off;
    public double AudioRumbleIntensity { get; init; } = 1;
}

/// <summary>Profil : un réglage complet, éventuellement associé à des jeux (exécutables).</summary>
public sealed record Profile
{
    public string Id { get; init; } = Guid.NewGuid().ToString("N");
    public string Name { get; init; } = "Nouveau profil";
    /// <summary>Noms d'exécutables (ex. « eldenring.exe ») qui activent ce profil automatiquement.</summary>
    public List<string> Executables { get; init; } = new();
    public OutputMode Mode { get; init; } = OutputMode.DualShock4;
    public ProcessingSettings Processing { get; init; } = new();
    public FeedbackSettings Feedback { get; init; } = new();

    public const string DefaultId = "default";
    public const string DesktopId = "desktop";

    public static Profile CreateDefault() => new() { Id = DefaultId, Name = "Par défaut" };

    /// <summary>
    /// Profil « Bureau Windows » : la manette devient clavier + souris (stick droit = curseur,
    /// stick gauche = molette, R2 / ✕ = clic gauche, L2 / ○ = clic droit, pavé tactile = souris…).
    /// </summary>
    public static Profile CreateDesktop() => new()
    {
        Id = DesktopId,
        Name = "Bureau Windows",
        Mode = OutputMode.KeyboardMouse,
        Processing = new ProcessingSettings
        {
            RightStick = new StickSettings { InnerDeadzone = 0.1, Curve = CurveType.Smooth },
            LeftStick = new StickSettings { InnerDeadzone = 0.15 },
            Mouse = new MouseSettings { RightStick = StickRole.Mouse, LeftStick = StickRole.Scroll, Speed = 1 },
            Touchpad = new TouchpadSettings { Mode = TouchpadMode.Mouse },
            Actions = new Dictionary<DualSenseButtons, InputAction>
            {
                [DualSenseButtons.Cross] = InputAction.ForMouse(MouseAction.LeftClick),
                [DualSenseButtons.R2] = InputAction.ForMouse(MouseAction.LeftClick),
                [DualSenseButtons.Circle] = InputAction.ForMouse(MouseAction.RightClick),
                [DualSenseButtons.L2] = InputAction.ForMouse(MouseAction.RightClick),
                [DualSenseButtons.Square] = InputAction.ForKeys(0x0D),          // Entrée
                [DualSenseButtons.Triangle] = InputAction.ForKeys(0x1B),        // Échap
                [DualSenseButtons.DpadUp] = InputAction.ForKeys(0x26),
                [DualSenseButtons.DpadDown] = InputAction.ForKeys(0x28),
                [DualSenseButtons.DpadLeft] = InputAction.ForKeys(0x25),
                [DualSenseButtons.DpadRight] = InputAction.ForKeys(0x27),
                [DualSenseButtons.L1] = InputAction.ForMouse(MouseAction.Back),
                [DualSenseButtons.R1] = InputAction.ForMouse(MouseAction.Forward),
                [DualSenseButtons.Options] = InputAction.ForKeys(0x5B),         // Windows
                [DualSenseButtons.Create] = InputAction.ForKeys(0x5B, 0x09),    // Windows + Tab
                [DualSenseButtons.R3] = InputAction.ForMouse(MouseAction.MiddleClick),
                [DualSenseButtons.L3] = InputAction.ForKeys(0x5B, 0xA2, 0x4F),  // Win + Ctrl + O : clavier visuel
            },
        },
    };

    public bool IsDefault => Id == DefaultId;
}

/// <summary>Profils enregistrés en JSON (un fichier par profil) dans %AppData%\se7en DualSense\profiles.</summary>
public sealed class ProfileStore
{
    private static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    private readonly string _dir;
    private readonly List<Profile> _profiles = new();

    public ProfileStore(string dataDirectory)
    {
        _dir = Path.Combine(dataDirectory, "profiles");
        Directory.CreateDirectory(_dir);
        Load();
    }

    public event Action? Changed;

    public IReadOnlyList<Profile> All => _profiles;

    public Profile Default => _profiles.First(p => p.IsDefault);

    public Profile? Find(string? id) => _profiles.FirstOrDefault(p => p.Id == id);

    private void Load()
    {
        foreach (var file in Directory.EnumerateFiles(_dir, "*.json"))
        {
            try
            {
                var p = JsonSerializer.Deserialize<Profile>(File.ReadAllText(file), Json);
                if (p != null && _profiles.All(x => x.Id != p.Id))
                    _profiles.Add(p);
            }
            catch
            {
                // Profil illisible : ignoré
            }
        }
        if (_profiles.All(p => !p.IsDefault))
            Save(Profile.CreateDefault());
        Sort();
    }

    private void Sort() => _profiles.Sort((a, b) => a.IsDefault ? -1 : b.IsDefault ? 1 : string.Compare(a.Name, b.Name, StringComparison.CurrentCultureIgnoreCase));

    public void Save(Profile profile)
    {
        int i = _profiles.FindIndex(p => p.Id == profile.Id);
        if (i >= 0) _profiles[i] = profile; else _profiles.Add(profile);
        Sort();
        File.WriteAllText(Path.Combine(_dir, profile.Id + ".json"), JsonSerializer.Serialize(profile, Json));
        Changed?.Invoke();
    }

    public void Delete(string id)
    {
        if (id == Profile.DefaultId) return;
        _profiles.RemoveAll(p => p.Id == id);
        try { File.Delete(Path.Combine(_dir, id + ".json")); } catch { /* déjà supprimé */ }
        Changed?.Invoke();
    }

    public void Export(Profile profile, string path) => File.WriteAllText(path, JsonSerializer.Serialize(profile, Json));

    /// <summary>Importe un profil partagé (nouvel identifiant pour éviter les collisions).</summary>
    public Profile Import(string path)
    {
        var p = JsonSerializer.Deserialize<Profile>(File.ReadAllText(path), Json)
                ?? throw new InvalidDataException("Fichier de profil invalide.");
        p = p with { Id = Guid.NewGuid().ToString("N"), Name = p.IsDefault ? "Profil importé" : p.Name };
        Save(p);
        return p;
    }
}

/// <summary>
/// Surveille les processus lancés (toutes les 2 s) et signale le profil associé au jeu en cours,
/// ou null quand aucun jeu associé ne tourne.
/// </summary>
public sealed class ProcessWatcher : IDisposable
{
    private readonly ProfileStore _store;
    private readonly Timer _timer;
    private string? _current;

    public ProcessWatcher(ProfileStore store)
    {
        _store = store;
        _timer = new Timer(_ => Scan(), null, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2));
    }

    /// <summary>(profil, nom du jeu) ; profil null = plus aucun jeu associé.</summary>
    public event Action<Profile?, string?>? GameChanged;

    private void Scan()
    {
        try
        {
            var running = new HashSet<string>(
                Process.GetProcesses().Select(p => { try { return p.ProcessName + ".exe"; } catch { return ""; } }),
                StringComparer.OrdinalIgnoreCase);

            Profile? match = null;
            string? exe = null;
            foreach (var p in _store.All.Where(p => !p.IsDefault))
            {
                exe = p.Executables.FirstOrDefault(e => running.Contains(Path.GetFileName(e)));
                if (exe != null) { match = p; break; }
            }

            string? key = match?.Id;
            if (key == _current) return;
            _current = key;
            GameChanged?.Invoke(match, exe);
        }
        catch
        {
            // Liste des processus momentanément indisponible
        }
    }

    public void Dispose() => _timer.Dispose();
}
