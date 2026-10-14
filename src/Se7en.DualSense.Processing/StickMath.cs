namespace Se7en.DualSense.Processing;

/// <summary>Calculs purs sur les sticks et gâchettes (testés unitairement).</summary>
public static class StickMath
{
    /// <summary>0-255 (centre 127.5) → [-1, 1].</summary>
    public static double Normalize(byte raw) => Math.Clamp((raw - 127.5) / 127.5, -1, 1);

    /// <summary>[-1, 1] → 0-255.</summary>
    public static byte ToByte(double v) => (byte)Math.Clamp(Math.Round(127.5 + v * 127.5), 0, 255);

    /// <summary>
    /// Chaîne complète d'un stick : centre → amplitude → zone morte radiale (interne/externe)
    /// → courbe → anti-zone morte → sensibilité par axe → inversion.
    /// Entrée et sortie normalisées dans [-1, 1].
    /// </summary>
    public static (double X, double Y) ProcessStick(double x, double y, StickCalibration cal, StickSettings s)
    {
        // 1. Compensation du centre réel (anti-drift)
        x -= cal.CenterX;
        y -= cal.CenterY;

        // 2. Amplitude : chaque demi-axe ramené à sa course réelle mesurée
        x = x >= 0 ? x / Math.Max(0.2, cal.MaxX - cal.CenterX) : x / Math.Max(0.2, cal.CenterX - cal.MinX);
        y = y >= 0 ? y / Math.Max(0.2, cal.MaxY - cal.CenterY) : y / Math.Max(0.2, cal.CenterY - cal.MinY);

        // 3. Zone morte radiale (ronde, pas carrée : la direction est préservée)
        double mag = Math.Sqrt(x * x + y * y);
        if (mag < 1e-9)
            return (0, 0);
        double inner = Math.Clamp(s.InnerDeadzone, 0, 0.9);
        double outer = Math.Clamp(s.OuterDeadzone, 0, 0.9 - inner);
        if (mag <= inner)
            return (0, 0);
        double t = Math.Clamp((Math.Min(mag, 1) - inner) / (1 - inner - outer), 0, 1);

        // 4. Courbe de réponse
        t = ApplyCurve(t, s.Curve, s.CustomExponent);

        // 5. Anti-zone morte : saute la zone morte que le jeu applique lui-même
        double anti = Math.Clamp(s.AntiDeadzone, 0, 0.9);
        if (t > 0)
            t = anti + (1 - anti) * t;

        double dirX = x / mag, dirY = y / mag;
        double ox = dirX * t * s.SensitivityX;
        double oy = dirY * t * s.SensitivityY;
        if (s.InvertX) ox = -ox;
        if (s.InvertY) oy = -oy;
        return (Math.Clamp(ox, -1, 1), Math.Clamp(oy, -1, 1));
    }

    public static double ApplyCurve(double t, CurveType curve, double exponent) => curve switch
    {
        CurveType.Smooth => t * t,
        CurveType.Aggressive => Math.Sqrt(t),
        CurveType.Custom => Math.Pow(t, Math.Clamp(exponent, 0.2, 5)),
        _ => t,
    };

    /// <summary>Gâchette 0-255 → 0-255 avec zones mortes et tout-ou-rien.</summary>
    public static byte ProcessTrigger(byte raw, TriggerSettings s)
    {
        double v = raw / 255.0;
        double dz = Math.Clamp(s.Deadzone, 0, 0.9);
        if (v <= dz)
            return 0;
        if (s.HairTrigger)
            return 255;
        double outer = Math.Clamp(s.OuterDeadzone, 0, 0.9 - dz);
        double t = Math.Clamp((v - dz) / (1 - dz - outer), 0, 1);
        return (byte)Math.Round(t * 255);
    }
}
