namespace Se7en.DualSense.Output;

/// <summary>Comment les jeux voient la manette.</summary>
public enum OutputMode
{
    /// <summary>DualShock 4 virtuelle : icônes PlayStation, pavé tactile, bouton PS.</summary>
    DualShock4,
    /// <summary>Xbox 360 virtuelle : compatibilité maximale.</summary>
    Xbox360,
    /// <summary>DualSense réelle exposée telle quelle (gâchettes adaptatives, haptique) ; aucun traitement.</summary>
    Native,
    /// <summary>Aucune manette pour les jeux : la DualSense pilote le clavier et la souris (bureau, jeux sans manette).</summary>
    KeyboardMouse,
}

public static class OutputModeExtensions
{
    public static string Label(this OutputMode mode) => mode switch
    {
        OutputMode.DualShock4 => "DualShock 4",
        OutputMode.Xbox360 => "Xbox 360",
        OutputMode.KeyboardMouse => "Clavier / souris",
        _ => "DualSense native",
    };

    /// <summary>Une manette virtuelle est présentée aux jeux.</summary>
    public static bool IsVirtual(this OutputMode mode) => mode is OutputMode.DualShock4 or OutputMode.Xbox360;

    /// <summary>La vraie DualSense est masquée aux jeux (tous les modes sauf natif).</summary>
    public static bool HidesDevice(this OutputMode mode) => mode != OutputMode.Native;
}

/// <summary>Retour envoyé par le jeu à la manette virtuelle.</summary>
public readonly record struct GameFeedback(byte HeavyMotor, byte LightMotor, byte? LightbarRed, byte? LightbarGreen, byte? LightbarBlue)
{
    public bool HasLightbar => LightbarRed.HasValue;
}
