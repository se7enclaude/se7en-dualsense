using System.Diagnostics;
using Se7en.DualSense.Hid;

namespace Se7en.DualSense.Processing;

/// <summary>
/// Applique le traitement complet à chaque rapport (thread d'E/S, sans allocation) :
/// échange des sticks, calibration, zones mortes, courbes, gâchettes, remapping, turbo, gyroscope.
/// Les réglages sont remplacés atomiquement (référence immuable), sans verrou.
/// </summary>
public sealed class InputProcessor
{
    // ~ 1024 unités brutes de gyroscope par 64 °/s sur la DualSense
    private const double GyroUnitsPerDegree = 16.0;

    private volatile ProcessingSettings _settings = ProcessingSettings.Default;
    private volatile StickCalibration _leftCal = StickCalibration.Identity;
    private volatile StickCalibration _rightCal = StickCalibration.Identity;
    private readonly Stopwatch _clock = Stopwatch.StartNew();

    public ProcessingSettings Settings
    {
        get => _settings;
        set => _settings = value ?? ProcessingSettings.Default;
    }

    public void SetCalibration(StickCalibration left, StickCalibration right)
    {
        _leftCal = left ?? StickCalibration.Identity;
        _rightCal = right ?? StickCalibration.Identity;
    }

    public StickCalibration LeftCalibration => _leftCal;
    public StickCalibration RightCalibration => _rightCal;

    public void Process(in DualSenseState raw, out DualSenseState output)
    {
        var s = _settings;
        output = raw;

        // ── Sticks ──
        byte lx = raw.LeftX, ly = raw.LeftY, rx = raw.RightX, ry = raw.RightY;
        if (s.SwapSticks)
            (lx, ly, rx, ry) = (rx, ry, lx, ly);

        var (plx, ply) = StickMath.ProcessStick(StickMath.Normalize(lx), StickMath.Normalize(ly), s.SwapSticks ? _rightCal : _leftCal, s.LeftStick);
        var (prx, pry) = StickMath.ProcessStick(StickMath.Normalize(rx), StickMath.Normalize(ry), s.SwapSticks ? _leftCal : _rightCal, s.RightStick);

        // ── Gâchettes ──
        output.L2 = StickMath.ProcessTrigger(raw.L2, s.L2);
        output.R2 = StickMath.ProcessTrigger(raw.R2, s.R2);

        // ── Gyroscope → stick droit ──
        if (s.Gyro.Enabled && raw.IsFullReport && GyroActive(s.Gyro.Activation, raw))
        {
            double yaw = -raw.GyroY / GyroUnitsPerDegree;   // °/s, rotation horizontale
            double pitch = -raw.GyroX / GyroUnitsPerDegree; // °/s, rotation verticale
            double dz = s.Gyro.Deadzone;
            yaw = Math.Abs(yaw) < dz ? 0 : yaw;
            pitch = Math.Abs(pitch) < dz ? 0 : pitch;
            // 360 °/s ≈ stick à fond
            double gx = yaw / 360.0 * 3 * s.Gyro.Sensitivity * (s.Gyro.InvertX ? -1 : 1);
            double gy = pitch / 360.0 * 3 * s.Gyro.Sensitivity * (s.Gyro.InvertY ? -1 : 1);
            prx = Math.Clamp(prx + gx, -1, 1);
            pry = Math.Clamp(pry + gy, -1, 1);
        }

        output.LeftX = StickMath.ToByte(plx);
        output.LeftY = StickMath.ToByte(ply);
        output.RightX = StickMath.ToByte(prx);
        output.RightY = StickMath.ToByte(pry);

        // ── Boutons : remapping puis turbo ──
        var buttons = raw.Buttons;
        // Les bits numériques L2/R2 suivent les gâchettes traitées
        buttons = (buttons & ~(DualSenseButtons.L2 | DualSenseButtons.R2))
                  | (output.L2 > 0 ? DualSenseButtons.L2 : 0)
                  | (output.R2 > 0 ? DualSenseButtons.R2 : 0);

        if (s.Remap.Count > 0)
            buttons = Remap(buttons, s.Remap);

        if (s.Turbo != DualSenseButtons.None && (buttons & s.Turbo) != 0)
        {
            double period = 1.0 / Math.Clamp(s.TurboRate, 2, 30);
            bool on = (_clock.Elapsed.TotalSeconds % period) < period / 2;
            if (!on)
                buttons &= ~s.Turbo;
        }

        output.Buttons = buttons;
    }

    private static bool GyroActive(GyroActivation a, in DualSenseState raw) => a switch
    {
        GyroActivation.WhileAiming => raw.L2 > 64,
        GyroActivation.WhileR1 => (raw.Buttons & DualSenseButtons.R1) != 0,
        _ => true,
    };

    public static DualSenseButtons Remap(DualSenseButtons input, Dictionary<DualSenseButtons, DualSenseButtons> map)
    {
        var output = DualSenseButtons.None;
        for (int bit = 0; bit < 32; bit++)
        {
            var flag = (DualSenseButtons)(1u << bit);
            if ((input & flag) == 0) continue;
            output |= map.TryGetValue(flag, out var target) ? target : flag;
        }
        return output;
    }
}
