using System.Text.Json.Serialization;
using Se7en.DualSense.Hid;

namespace Se7en.DualSense.Processing;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum CurveType
{
    /// <summary>Réponse proportionnelle.</summary>
    Linear,
    /// <summary>Plus de précision au centre (petits mouvements atténués).</summary>
    Smooth,
    /// <summary>Réponse rapide dès les petits mouvements.</summary>
    Aggressive,
    /// <summary>Exposant libre (<see cref="StickSettings.CustomExponent"/>).</summary>
    Custom,
}

/// <summary>
/// Calibration matérielle d'un stick, mesurée par l'assistant (indépendante du profil de jeu).
/// Valeurs normalisées dans [-1, 1].
/// </summary>
public sealed record StickCalibration
{
    /// <summary>Position réelle de repos (le décalage à compenser : c'est le drift).</summary>
    public double CenterX { get; init; }
    public double CenterY { get; init; }

    /// <summary>Extrêmes réels mesurés sur chaque axe (valeurs brutes normalisées, avant correction du centre).</summary>
    public double MinX { get; init; } = -1;
    public double MaxX { get; init; } = 1;
    public double MinY { get; init; } = -1;
    public double MaxY { get; init; } = 1;

    public static StickCalibration Identity { get; } = new();
}

/// <summary>Réglages d'un stick propres à un profil.</summary>
public sealed record StickSettings
{
    /// <summary>Zone morte radiale centrale (0-0.5).</summary>
    public double InnerDeadzone { get; init; } = 0.06;
    /// <summary>Zone morte externe : la course maximale est atteinte avant le bord (0-0.3).</summary>
    public double OuterDeadzone { get; init; } = 0.02;
    /// <summary>Compense la zone morte interne du jeu (0-0.4).</summary>
    public double AntiDeadzone { get; init; }
    public CurveType Curve { get; init; } = CurveType.Linear;
    /// <summary>Exposant de la courbe personnalisée (&lt; 1 rapide, &gt; 1 précise).</summary>
    public double CustomExponent { get; init; } = 1.5;
    public double SensitivityX { get; init; } = 1;
    public double SensitivityY { get; init; } = 1;
    public bool InvertX { get; init; }
    public bool InvertY { get; init; }
}

public sealed record TriggerSettings
{
    /// <summary>Course ignorée au début (0-0.5).</summary>
    public double Deadzone { get; init; } = 0.03;
    /// <summary>Pression maximale atteinte avant la fin de course (0-0.5).</summary>
    public double OuterDeadzone { get; init; }
    /// <summary>Tout-ou-rien : la moindre pression = gâchette à fond.</summary>
    public bool HairTrigger { get; init; }
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum GyroActivation
{
    Always,
    /// <summary>Seulement quand L2 est enfoncée (visée).</summary>
    WhileAiming,
    /// <summary>Seulement quand R1 est maintenu.</summary>
    WhileR1,
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum GyroOutput
{
    /// <summary>Ajouté au stick droit de la manette virtuelle.</summary>
    Stick,
    /// <summary>Déplace la souris (jeux de tir au clavier / souris) : précision maximale.</summary>
    Mouse,
}

public sealed record GyroSettings
{
    public bool Enabled { get; init; }
    public GyroOutput Output { get; init; } = GyroOutput.Stick;
    /// <summary>Souris : pixels par degré de rotation de la manette.</summary>
    public double MouseSensitivity { get; init; } = 12;
    public GyroActivation Activation { get; init; } = GyroActivation.WhileAiming;
    /// <summary>Multiplicateur de sensibilité (0.1-5).</summary>
    public double Sensitivity { get; init; } = 1;
    /// <summary>Petits tremblements ignorés (degrés/s).</summary>
    public double Deadzone { get; init; } = 1.5;
    public bool InvertX { get; init; }
    public bool InvertY { get; init; }
}

/// <summary>Tout le traitement appliqué entre la vraie manette et la manette virtuelle.</summary>
public sealed record ProcessingSettings
{
    public StickSettings LeftStick { get; init; } = new();
    public StickSettings RightStick { get; init; } = new();
    public TriggerSettings L2 { get; init; } = new();
    public TriggerSettings R2 { get; init; } = new();
    public bool SwapSticks { get; init; }
    public GyroSettings Gyro { get; init; } = new();

    /// <summary>Bouton physique → bouton(s) envoyé(s) au jeu. Absent = inchangé, None = désactivé.</summary>
    public Dictionary<DualSenseButtons, DualSenseButtons> Remap { get; init; } = new();

    /// <summary>Boutons qui se répètent automatiquement quand on les maintient.</summary>
    public DualSenseButtons Turbo { get; init; }

    /// <summary>Fréquence du turbo (appuis par seconde).</summary>
    public double TurboRate { get; init; } = 12;

    /// <summary>Bouton physique → touche, clic, macro ou action système (prioritaire sur <see cref="Remap"/>).</summary>
    public Dictionary<DualSenseButtons, InputAction> Actions { get; init; } = new();

    /// <summary>Bouton qui active la couche secondaire tant qu'il est maintenu (None = pas de couche).</summary>
    public DualSenseButtons ShiftButton { get; init; }

    /// <summary>Couche secondaire : mêmes principes que <see cref="Remap"/> et <see cref="Actions"/>.</summary>
    public Dictionary<DualSenseButtons, DualSenseButtons> ShiftRemap { get; init; } = new();
    public Dictionary<DualSenseButtons, InputAction> ShiftActions { get; init; } = new();

    public TouchpadSettings Touchpad { get; init; } = new();
    public FlickStickSettings FlickStick { get; init; } = new();
    public MouseSettings Mouse { get; init; } = new();
    public List<Macro> Macros { get; init; } = new();

    public static ProcessingSettings Default { get; } = new();
}
