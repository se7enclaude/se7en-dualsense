using Se7en.DualSense.Hid;
using Se7en.DualSense.Processing;
using Xunit;

namespace Se7en.DualSense.Tests;

public class StickProcessingTests
{
    private static readonly StickSettings NoDeadzone = new() { InnerDeadzone = 0, OuterDeadzone = 0 };

    [Fact]
    public void Drift_is_removed_by_center_calibration()
    {
        // Stick au repos mais décalé de +0.12 / -0.08 : après calibration, sortie nulle
        var cal = StickCalibration.Identity with { CenterX = 0.12, CenterY = -0.08 };
        var (x, y) = StickMath.ProcessStick(0.12, -0.08, cal, new StickSettings { InnerDeadzone = 0.02 });
        Assert.Equal(0, x);
        Assert.Equal(0, y);
    }

    [Fact]
    public void Small_drift_is_absorbed_by_radial_deadzone()
    {
        var (x, y) = StickMath.ProcessStick(0.05, 0.05, StickCalibration.Identity, new StickSettings { InnerDeadzone = 0.08 });
        Assert.Equal((0.0, 0.0), (x, y));
    }

    [Fact]
    public void Radial_deadzone_preserves_direction()
    {
        var (x, y) = StickMath.ProcessStick(0.6, 0.6, StickCalibration.Identity, new StickSettings { InnerDeadzone = 0.1, OuterDeadzone = 0 });
        Assert.Equal(x, y, 6);
        Assert.True(x > 0);
    }

    [Fact]
    public void Output_starts_from_zero_just_after_deadzone()
    {
        // Pas de saut : juste après la zone morte, la sortie est quasi nulle (rescaling)
        var (x, _) = StickMath.ProcessStick(0.101, 0, StickCalibration.Identity, new StickSettings { InnerDeadzone = 0.1, OuterDeadzone = 0 });
        Assert.InRange(x, 0, 0.01);
    }

    [Fact]
    public void Outer_deadzone_reaches_full_deflection_early()
    {
        var (x, _) = StickMath.ProcessStick(0.9, 0, StickCalibration.Identity, new StickSettings { InnerDeadzone = 0, OuterDeadzone = 0.1 });
        Assert.Equal(1, x, 6);
    }

    [Fact]
    public void Range_calibration_restores_full_deflection_of_worn_stick()
    {
        // Stick usé qui ne dépasse pas 0.85 à droite
        var cal = StickCalibration.Identity with { MaxX = 0.85 };
        var (x, _) = StickMath.ProcessStick(0.85, 0, cal, NoDeadzone);
        Assert.Equal(1, x, 6);
    }

    [Theory]
    [InlineData(CurveType.Linear, 0.5, 0.5)]
    [InlineData(CurveType.Smooth, 0.5, 0.25)]
    [InlineData(CurveType.Aggressive, 0.25, 0.5)]
    public void Response_curves(CurveType curve, double input, double expected)
    {
        Assert.Equal(expected, StickMath.ApplyCurve(input, curve, 1.5), 6);
    }

    [Fact]
    public void Anti_deadzone_skips_game_deadzone()
    {
        var (x, _) = StickMath.ProcessStick(0.2, 0, StickCalibration.Identity, NoDeadzone with { AntiDeadzone = 0.2 });
        Assert.Equal(0.36, x, 6); // 0.2 + 0.8 × 0.2
    }

    [Fact]
    public void Invert_and_sensitivity()
    {
        var (x, y) = StickMath.ProcessStick(0.5, 0.5, StickCalibration.Identity, NoDeadzone with { InvertY = true, SensitivityX = 0.5 });
        Assert.True(x > 0 && x < 0.3);
        Assert.True(y < 0);
    }

    [Fact]
    public void Byte_conversion_round_trip_keeps_center()
    {
        Assert.Equal(128, StickMath.ToByte(StickMath.Normalize(128)));
        Assert.Equal(0, StickMath.ToByte(-1));
        Assert.Equal(255, StickMath.ToByte(1));
    }
}

public class TriggerAndButtonTests
{
    [Fact]
    public void Trigger_deadzone_and_rescale()
    {
        var s = new TriggerSettings { Deadzone = 0.1 };
        Assert.Equal(0, StickMath.ProcessTrigger(20, s));
        Assert.Equal(255, StickMath.ProcessTrigger(255, s));
    }

    [Fact]
    public void Hair_trigger_is_all_or_nothing()
    {
        var s = new TriggerSettings { Deadzone = 0.02, HairTrigger = true };
        Assert.Equal(0, StickMath.ProcessTrigger(3, s));
        Assert.Equal(255, StickMath.ProcessTrigger(30, s));
    }

    [Fact]
    public void Remap_swaps_and_disables_buttons()
    {
        var map = new Dictionary<DualSenseButtons, DualSenseButtons>
        {
            [DualSenseButtons.Cross] = DualSenseButtons.Circle,
            [DualSenseButtons.Circle] = DualSenseButtons.Cross,
            [DualSenseButtons.Mute] = DualSenseButtons.None,
        };
        var output = InputProcessor.Remap(DualSenseButtons.Cross | DualSenseButtons.Mute | DualSenseButtons.L1, map);
        Assert.Equal(DualSenseButtons.Circle | DualSenseButtons.L1, output);
    }

    [Fact]
    public void Processor_applies_calibration_and_swap()
    {
        var p = new InputProcessor();
        p.SetCalibration(StickCalibration.Identity with { CenterX = 0.1 }, StickCalibration.Identity);
        p.Settings = ProcessingSettings.Default with { LeftStick = new StickSettings { InnerDeadzone = 0.02 } };

        var raw = new DualSenseState { LeftX = StickMath.ToByte(0.1), LeftY = 128, RightX = 128, RightY = 128 };
        p.Process(raw, out var output);
        Assert.Equal(128, output.LeftX); // drift compensé

        p.Settings = p.Settings with { SwapSticks = true };
        raw = new DualSenseState { LeftX = 255, LeftY = 128, RightX = 128, RightY = 128 };
        p.Process(raw, out output);
        Assert.Equal(128, output.LeftX);
        Assert.True(output.RightX > 240);
    }

    [Fact]
    public void Calibration_sampler_measures_rest_position()
    {
        var sampler = new CalibrationSampler();
        sampler.BeginCenter();
        for (int i = 0; i < 100; i++)
            sampler.Add(new DualSenseState { LeftX = 140, LeftY = 120, RightX = 128, RightY = 128 });
        var (left, _) = sampler.Result(StickCalibration.Identity, StickCalibration.Identity, includeRange: false);
        Assert.Equal(StickMath.Normalize(140), left.CenterX, 6);
        Assert.Equal(StickMath.Normalize(120), left.CenterY, 6);
        Assert.Equal(0, sampler.CenterNoise, 6);
    }
}
