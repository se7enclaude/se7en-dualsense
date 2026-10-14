using Se7en.DualSense.Hid;
using Se7en.DualSense.Processing;
using Xunit;

namespace Se7en.DualSense.Tests;

/// <summary>Flick stick, gyroscope en souris, recentrage automatique.</summary>
public class AimTests
{
    private static DualSenseState Pad(byte rx = 128, byte ry = 128) => new()
    {
        LeftX = 128, LeftY = 128, RightX = rx, RightY = ry, IsFullReport = true,
    };

    [Fact]
    public void Flick_to_the_right_turns_a_quarter()
    {
        var s = new FlickStickSettings { Enabled = true, CountsPer360 = 3600, FlickTime = 0 };
        var f = new FlickStick();
        double counts = f.Step(1, 0, s, 0.004);   // stick à fond à droite = 90°
        Assert.Equal(900, counts, 1);
    }

    [Fact]
    public void Rotating_the_stick_rotates_the_camera()
    {
        var s = new FlickStickSettings { Enabled = true, CountsPer360 = 3600, FlickTime = 0 };
        var f = new FlickStick();
        f.Step(0, -1, s, 0.004);                  // flick vers l'avant : 0°
        double counts = f.Step(1, 0, s, 0.004);   // rotation de 90° dans le sens horaire
        Assert.Equal(900, counts, 1);
    }

    [Fact]
    public void Gradual_flick_is_spread_over_flick_time()
    {
        var s = new FlickStickSettings { Enabled = true, CountsPer360 = 3600, FlickTime = 0.1 };
        var f = new FlickStick();
        double first = f.Step(-1, 0, s, 0.004);
        double total = first;
        for (int i = 0; i < 49; i++) total += f.Step(-1, 0, s, 0.004); // 200 ms
        Assert.True(Math.Abs(first) < 100);
        Assert.Equal(-900, total, 1);
    }

    [Fact]
    public void Gyro_mouse_moves_cursor_without_touching_the_stick()
    {
        var p = new InputProcessor
        {
            Settings = new ProcessingSettings { Gyro = new GyroSettings { Enabled = true, Output = GyroOutput.Mouse, Activation = GyroActivation.Always, Deadzone = 0, MouseSensitivity = 10 } },
            Options = new GlobalInputOptions { AutoRecenter = false },
        };
        var s = Pad();
        s.GyroY = -16 * 90; // 90 °/s de lacet
        var a = default(ActionFrame);
        p.Process(s, out var o, ref a, 1, 0.1);
        Assert.Equal(90, a.MouseDx, 1);
        Assert.InRange(o.RightX, 127, 128);
    }

    [Fact]
    public void Auto_recenter_follows_a_resting_drift_only_when_controller_is_placed()
    {
        var p = new InputProcessor { Options = new GlobalInputOptions { AutoRecenter = true } };
        var drifting = Pad(rx: 136, ry: 122); // stick droit décalé, manette posée (gyroscope nul)
        var a = default(ActionFrame);
        for (int i = 0; i < 2000; i++) p.Process(drifting, out _, ref a, i * 0.004, 0.004); // 8 s
        Assert.True(p.RightCalibration.CenterX > 0.03);
        Assert.True(p.AutoRecenterCount > 0);

        // Manette tenue en main (gyroscope actif) : aucune correction
        var held = new InputProcessor { Options = new GlobalInputOptions { AutoRecenter = true } };
        var moving = drifting;
        moving.GyroX = 400;
        for (int i = 0; i < 2000; i++) held.Process(moving, out _, ref a, i * 0.004, 0.004);
        Assert.Equal(0, held.RightCalibration.CenterX);
    }

    [Fact]
    public void Suppressed_actions_keep_only_system_shortcuts()
    {
        var p = new InputProcessor
        {
            Settings = new ProcessingSettings { Actions = { [DualSenseButtons.Cross] = InputAction.ForKeys(0x20) } },
            Options = new GlobalInputOptions { SuppressActions = true, PsShortcuts = false, MuteControlsMicrophone = true },
        };
        var s = Pad();
        s.Buttons = DualSenseButtons.Cross | DualSenseButtons.Mute;
        var a = default(ActionFrame);
        p.Process(s, out _, ref a, 1, 0.004);
        Assert.True(a.Keys.IsEmpty);
        Assert.True(a.HasSystem(SystemAction.ToggleMicrophone));
    }
}
