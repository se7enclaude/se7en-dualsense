using System.Windows.Input;
using Se7en.DualSense.App.Controls;
using Se7en.DualSense.Hid;
using Se7en.DualSense.Processing;
using MouseAction = Se7en.DualSense.Processing.MouseAction;

namespace Se7en.DualSense.App.Pages;

/// <summary>Noms français des boutons, touches et actions affichés dans l'interface.</summary>
public static class Labels
{
    /// <summary>Boutons de la manette, dans l'ordre de la manette.</summary>
    public static readonly (DualSenseButtons Flag, string Glyph, string Name, GlyphShape Shape)[] Buttons =
    {
        (DualSenseButtons.Cross, "✕", "Croix", GlyphShape.Circle),
        (DualSenseButtons.Circle, "○", "Rond", GlyphShape.Circle),
        (DualSenseButtons.Square, "□", "Carré", GlyphShape.Circle),
        (DualSenseButtons.Triangle, "△", "Triangle", GlyphShape.Circle),
        (DualSenseButtons.DpadUp, "↑", "Haut", GlyphShape.Rounded),
        (DualSenseButtons.DpadDown, "↓", "Bas", GlyphShape.Rounded),
        (DualSenseButtons.DpadLeft, "←", "Gauche", GlyphShape.Rounded),
        (DualSenseButtons.DpadRight, "→", "Droite", GlyphShape.Rounded),
        (DualSenseButtons.L1, "L1", "L1", GlyphShape.Pill),
        (DualSenseButtons.R1, "R1", "R1", GlyphShape.Pill),
        (DualSenseButtons.L2, "L2", "L2 (bouton)", GlyphShape.Pill),
        (DualSenseButtons.R2, "R2", "R2 (bouton)", GlyphShape.Pill),
        (DualSenseButtons.L3, "L3", "L3", GlyphShape.Pill),
        (DualSenseButtons.R3, "R3", "R3", GlyphShape.Pill),
        (DualSenseButtons.Create, "CREATE", "Create", GlyphShape.Pill),
        (DualSenseButtons.Options, "OPTIONS", "Options", GlyphShape.Pill),
        (DualSenseButtons.PS, "PS", "PS", GlyphShape.Pill),
        (DualSenseButtons.Touchpad, "PAVÉ", "Clic pavé", GlyphShape.Pill),
        (DualSenseButtons.Mute, "MICRO", "Micro", GlyphShape.Pill),
    };

    /// <summary>Commandes supplémentaires de la DualSense Edge.</summary>
    public static readonly (DualSenseButtons Flag, string Glyph, string Name, GlyphShape Shape)[] EdgeButtons =
    {
        (DualSenseButtons.PaddleLeft, "LB", "Palette arrière gauche", GlyphShape.Pill),
        (DualSenseButtons.PaddleRight, "RB", "Palette arrière droite", GlyphShape.Pill),
        (DualSenseButtons.FnLeft, "FN G", "Fn gauche", GlyphShape.Pill),
        (DualSenseButtons.FnRight, "FN D", "Fn droit", GlyphShape.Pill),
    };

    /// <summary>Zones et gestes du pavé tactile.</summary>
    public static readonly (DualSenseButtons Flag, string Glyph, string Name, GlyphShape Shape)[] TouchInputs =
    {
        (DualSenseButtons.TouchZone1, "Z1", "Zone 1 (gauche / haut-gauche)", GlyphShape.Pill),
        (DualSenseButtons.TouchZone2, "Z2", "Zone 2 (droite / haut-droite)", GlyphShape.Pill),
        (DualSenseButtons.TouchZone3, "Z3", "Zone 3 (bas-gauche)", GlyphShape.Pill),
        (DualSenseButtons.TouchZone4, "Z4", "Zone 4 (bas-droite)", GlyphShape.Pill),
        (DualSenseButtons.SwipeUp, "⇡", "Glisser vers le haut", GlyphShape.Rounded),
        (DualSenseButtons.SwipeDown, "⇣", "Glisser vers le bas", GlyphShape.Rounded),
        (DualSenseButtons.SwipeLeft, "⇠", "Glisser vers la gauche", GlyphShape.Rounded),
        (DualSenseButtons.SwipeRight, "⇢", "Glisser vers la droite", GlyphShape.Rounded),
    };

    public static string ButtonName(DualSenseButtons b)
    {
        foreach (var list in new[] { Buttons, EdgeButtons, TouchInputs })
            foreach (var x in list)
                if (x.Flag == b) return x.Name;
        return b.ToString();
    }

    /// <summary>Nom court d'une combinaison de boutons (« ✕ + □ »).</summary>
    public static string Combo(DualSenseButtons buttons)
    {
        if (buttons == DualSenseButtons.None) return "pause";
        var parts = new List<string>();
        foreach (var list in new[] { Buttons, EdgeButtons })
            foreach (var x in list)
                if ((buttons & x.Flag) != 0) parts.Add(x.Glyph);
        return string.Join(" + ", parts);
    }

    public static readonly (MouseAction Action, string Name)[] MouseActions =
    {
        (MouseAction.LeftClick, "Clic gauche"),
        (MouseAction.RightClick, "Clic droit"),
        (MouseAction.MiddleClick, "Clic molette"),
        (MouseAction.Back, "Précédent (souris)"),
        (MouseAction.Forward, "Suivant (souris)"),
        (MouseAction.WheelUp, "Molette vers le haut"),
        (MouseAction.WheelDown, "Molette vers le bas"),
    };

    public static readonly (SystemAction Action, string Name)[] SystemActions =
    {
        (SystemAction.ToggleMicrophone, "Couper / activer le micro"),
        (SystemAction.Screenshot, "Capture d'écran"),
        (SystemAction.VolumeUp, "Volume +"),
        (SystemAction.VolumeDown, "Volume −"),
        (SystemAction.VolumeMute, "Couper le son"),
        (SystemAction.MediaPlayPause, "Lecture / pause"),
        (SystemAction.MediaNext, "Piste suivante"),
        (SystemAction.MediaPrevious, "Piste précédente"),
        (SystemAction.NextProfile, "Profil suivant"),
        (SystemAction.PreviousProfile, "Profil précédent"),
        (SystemAction.ToggleDesktopMode, "Mode bureau on / off"),
        (SystemAction.ShowApp, "Ouvrir se7en DualSense"),
        (SystemAction.ToggleOverlay, "Overlay on / off"),
        (SystemAction.PowerOff, "Éteindre la manette"),
        (SystemAction.OpenLauncher, "Lanceur de jeux"),
        (SystemAction.ToggleKeyboard, "Clavier virtuel"),
    };

    public static string Describe(InputAction a, IEnumerable<Macro> macros) => a.Kind switch
    {
        ActionKind.Key => "Touche " + string.Join(" + ", a.Keys.Select(KeyName)),
        ActionKind.Mouse => MouseActions.FirstOrDefault(m => m.Action == a.Mouse).Name ?? a.Mouse.ToString(),
        ActionKind.Macro => "Macro « " + (macros.FirstOrDefault(m => m.Id == a.MacroId)?.Name ?? "supprimée") + " »",
        _ => SystemActions.FirstOrDefault(s => s.Action == a.System).Name ?? a.System.ToString(),
    };

    /// <summary>Nom d'une touche (code virtuel Windows, ou 0x100 + code de balayage).</summary>
    public static string KeyName(ushort code)
    {
        if (code >= 0x100)
            return (code & 0xFF) switch { 0x11 => "Z/W", 0x1E => "Q/A", 0x1F => "S", 0x20 => "D", var sc => $"Code {sc:X2}" };
        var key = KeyInterop.KeyFromVirtualKey(code);
        return key switch
        {
            Key.LeftCtrl or Key.RightCtrl => "Ctrl",
            Key.LeftShift or Key.RightShift => "Maj",
            Key.LeftAlt => "Alt",
            Key.RightAlt => "Alt Gr",
            Key.LWin or Key.RWin => "Windows",
            Key.Space => "Espace",
            Key.Return => "Entrée",
            Key.Escape => "Échap",
            Key.Back => "Retour arrière",
            Key.Delete => "Suppr",
            Key.Insert => "Inser",
            Key.Up => "↑",
            Key.Down => "↓",
            Key.Left => "←",
            Key.Right => "→",
            Key.PageUp => "Page ↑",
            Key.Next => "Page ↓",
            Key.Home => "Début",
            Key.End => "Fin",
            Key.Snapshot => "Impr. écran",
            Key.Capital => "Verr. maj",
            >= Key.D0 and <= Key.D9 => ((int)(key - Key.D0)).ToString(),
            >= Key.NumPad0 and <= Key.NumPad9 => "Pavé " + (int)(key - Key.NumPad0),
            _ => key.ToString(),
        };
    }
}
