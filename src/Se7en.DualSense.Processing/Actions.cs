using System.Runtime.CompilerServices;
using System.Text.Json.Serialization;
using Se7en.DualSense.Hid;

namespace Se7en.DualSense.Processing;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum ActionKind
{
    /// <summary>Touche(s) du clavier, appuyées ensemble (ex. Ctrl + C).</summary>
    Key,
    Mouse,
    Macro,
    /// <summary>Action de l'application ou de Windows (micro, volume, profil…).</summary>
    System,
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum MouseAction
{
    LeftClick,
    RightClick,
    MiddleClick,
    Back,
    Forward,
    WheelUp,
    WheelDown,
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum SystemAction
{
    ToggleMicrophone,
    NextProfile,
    PreviousProfile,
    Screenshot,
    ShowApp,
    ToggleOverlay,
    VolumeUp,
    VolumeDown,
    VolumeMute,
    MediaPlayPause,
    MediaNext,
    MediaPrevious,
    PowerOff,
    ToggleDesktopMode,
    OpenLauncher,
    ToggleKeyboard,
}

/// <summary>
/// Ce que déclenche un bouton quand il n'envoie pas un bouton de manette :
/// touche(s) du clavier, clic ou molette, macro, action système.
/// </summary>
public sealed record InputAction
{
    public ActionKind Kind { get; init; }

    /// <summary>
    /// Touches appuyées ensemble. Code &lt; 0x100 : touche virtuelle Windows (VK_*) ;
    /// 0x100 + code : code de balayage brut (position physique, ex. 0x111 = touche W/Z).
    /// </summary>
    public List<ushort> Keys { get; init; } = new();

    public MouseAction Mouse { get; init; }
    public string? MacroId { get; init; }
    public SystemAction System { get; init; }

    public static InputAction ForKeys(params ushort[] keys) => new() { Kind = ActionKind.Key, Keys = keys.ToList() };
    public static InputAction ForMouse(MouseAction m) => new() { Kind = ActionKind.Mouse, Mouse = m };
    public static InputAction ForMacro(string id) => new() { Kind = ActionKind.Macro, MacroId = id };
    public static InputAction ForSystem(SystemAction a) => new() { Kind = ActionKind.System, System = a };
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum MacroMode
{
    /// <summary>Joue la séquence une fois à chaque appui.</summary>
    Once,
    /// <summary>Rejoue en boucle tant que le bouton est maintenu.</summary>
    WhileHeld,
    /// <summary>Un appui lance la boucle, le suivant l'arrête.</summary>
    Toggle,
}

/// <summary>Une étape : ces boutons maintenus pendant cette durée (aucun bouton = pause).</summary>
public sealed record MacroStep(DualSenseButtons Buttons, int DurationMs);

public sealed record Macro
{
    public string Id { get; init; } = Guid.NewGuid().ToString("N");
    public string Name { get; init; } = "Nouvelle macro";
    public List<MacroStep> Steps { get; init; } = new();
    public MacroMode Mode { get; init; } = MacroMode.Once;

    public int TotalMs => Steps.Sum(s => Math.Max(1, s.DurationMs));
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum TouchpadMode
{
    /// <summary>Pavé tactile transmis tel quel (DS4) / clic = bouton pavé.</summary>
    Default,
    /// <summary>Le clic dépend de la zone touchée : 2 ou 4 boutons supplémentaires.</summary>
    Zones,
    /// <summary>Le pavé déplace la souris de Windows ; clic = clic gauche, clic à deux doigts = clic droit.</summary>
    Mouse,
}

public sealed record TouchpadSettings
{
    public TouchpadMode Mode { get; init; } = TouchpadMode.Default;
    /// <summary>2 (gauche / droite) ou 4 (quarts).</summary>
    public int ZoneCount { get; init; } = 2;
    /// <summary>Glissements rapides du doigt → entrées SwipeUp/Down/Left/Right.</summary>
    public bool Gestures { get; init; }
    public double MouseSpeed { get; init; } = 1;
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum StickRole
{
    /// <summary>Stick de manette (comportement normal).</summary>
    Gamepad,
    /// <summary>Déplace le curseur de la souris.</summary>
    Mouse,
    /// <summary>Molette (vertical) et défilement horizontal.</summary>
    Scroll,
    /// <summary>Touches de déplacement (positions W A S D / Z Q S D).</summary>
    Movement,
}

public sealed record MouseSettings
{
    public StickRole LeftStick { get; init; } = StickRole.Gamepad;
    public StickRole RightStick { get; init; } = StickRole.Gamepad;
    /// <summary>Vitesse du curseur au stick (multiplicateur).</summary>
    public double Speed { get; init; } = 1;
    public double ScrollSpeed { get; init; } = 1;
}

/// <summary>
/// « Flick stick » (jeux de tir à la souris) : pousser le stick droit tourne instantanément la caméra
/// dans cette direction, puis le faire tourner fait pivoter la caméra d'autant.
/// </summary>
public sealed record FlickStickSettings
{
    public bool Enabled { get; init; }
    /// <summary>Mouvement de souris (en points) qui fait faire un tour complet dans le jeu.</summary>
    public double CountsPer360 { get; init; } = 3600;
    /// <summary>Durée du « flick » (s) : 0 = instantané.</summary>
    public double FlickTime { get; init; } = 0.1;
}

/// <summary>Options globales de l'application appliquées au traitement (indépendantes du profil).</summary>
public sealed record GlobalInputOptions
{
    /// <summary>PS + bouton = raccourci système ; un appui court sur PS reste transmis au jeu.</summary>
    public bool PsShortcuts { get; init; } = true;
    /// <summary>Le bouton micro coupe / rétablit le micro de Windows.</summary>
    public bool MuteControlsMicrophone { get; init; } = true;
    /// <summary>Recentrage automatique des sticks quand la manette est posée (anti-drift continu).</summary>
    public bool AutoRecenter { get; init; } = true;
    /// <summary>Clavier virtuel ou lanceur ouvert : la manette ne produit plus d'actions clavier / souris.</summary>
    public bool SuppressActions { get; init; }

    public static GlobalInputOptions Default { get; } = new();
}

/// <summary>Ensemble de touches (codes 0-511, voir <see cref="InputAction.Keys"/>) sans allocation.</summary>
[InlineArray(8)]
public struct KeyBits
{
    private ulong _element;

    public void Set(ushort code)
    {
        if (code < 512) this[code >> 6] |= 1UL << (code & 63);
    }

    public readonly bool Has(int code) => code < 512 && (this[code >> 6] & (1UL << (code & 63))) != 0;

    public readonly bool SameAs(in KeyBits other)
    {
        for (int i = 0; i < 8; i++)
            if (this[i] != other[i]) return false;
        return true;
    }

    public readonly bool IsEmpty
    {
        get
        {
            for (int i = 0; i < 8; i++)
                if (this[i] != 0) return false;
            return true;
        }
    }
}

[Flags]
public enum MouseButtonFlags : byte
{
    None = 0,
    Left = 1,
    Right = 2,
    Middle = 4,
    Back = 8,
    Forward = 16,
}

/// <summary>Sorties clavier / souris / système produites par un rapport.</summary>
public struct ActionFrame
{
    public KeyBits Keys;
    public MouseButtonFlags MouseButtons;
    /// <summary>Déplacement du curseur (pixels, fractionnaire).</summary>
    public double MouseDx, MouseDy;
    /// <summary>Molette en crans (positif = vers le haut / la droite).</summary>
    public double Wheel, HWheel;
    /// <summary>Actions système déclenchées par ce rapport (un bit par <see cref="SystemAction"/>).</summary>
    public ulong SystemTriggered;

    public readonly bool HasSystem(SystemAction a) => (SystemTriggered & (1UL << (int)a)) != 0;
}
