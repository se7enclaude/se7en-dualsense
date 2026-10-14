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
}

public static class OutputModeExtensions
{
    public static string Label(this OutputMode mode) => mode switch
    {
        OutputMode.DualShock4 => "DualShock 4",
        OutputMode.Xbox360 => "Xbox 360",
        _ => "DualSense native",
    };

    public static bool IsVirtual(this OutputMode mode) => mode != OutputMode.Native;
}

/// <summary>Retour envoyé par le jeu à la manette virtuelle.</summary>
public readonly record struct GameFeedback(byte HeavyMotor, byte LightMotor, byte? LightbarRed, byte? LightbarGreen, byte? LightbarBlue)
{
    public bool HasLightbar => LightbarRed.HasValue;
}
