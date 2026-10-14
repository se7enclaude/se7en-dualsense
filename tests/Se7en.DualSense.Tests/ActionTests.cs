using Se7en.DualSense.Hid;
using Se7en.DualSense.Processing;
using Xunit;

namespace Se7en.DualSense.Tests;

/// <summary>Couche secondaire, actions clavier / souris, macros, pavé tactile, raccourcis PS, bouton micro.</summary>
public class ActionTests
{
    private static DualSenseState Pad(DualSenseButtons buttons = DualSenseButtons.None, TouchPoint touch = default, TouchPoint touch2 = default) => new()
    {
        LeftX = 128, LeftY = 128, RightX = 128, RightY = 128,
        Buttons = buttons,
        Touch0 = touch,
        Touch1 = touch2,
        IsFullReport = true,
    };

    private static (DualSenseState Out, ActionFrame Actions) Run(InputProcessor p, DualSenseState s, double now, double dt = 0.004)
    {
        var a = default(ActionFrame);
        p.Process(s, out var o, ref a, now, dt);
        return (o, a);
    }

    private static InputProcessor Processor(ProcessingSettings s, bool ps = false, bool mic = false) => new()
    {
        Settings = s,
        Options = new GlobalInputOptions { PsShortcuts = ps, MuteControlsMicrophone = mic },
    };

    [Fact]
    public void Key_action_replaces_gamepad_button()
    {
        var p = Processor(new ProcessingSettings { Actions = { [DualSenseButtons.Cross] = InputAction.ForKeys(0x20) } });
        var (o, a) = Run(p, Pad(DualSenseButtons.Cross), 1);
        Assert.Equal(DualSenseButtons.None, o.Buttons & DualSenseButtons.Cross);
        Assert.True(a.Keys.Has(0x20));
    }

    [Fact]
    public void Shift_layer_changes_mapping_only_while_held()
    {
        var p = Processor(new ProcessingSettings
        {
            ShiftButton = DualSenseButtons.L1,
            ShiftRemap = { [DualSenseButtons.Cross] = DualSenseButtons.Triangle },
        });
        var (normal, _) = Run(p, Pad(DualSenseButtons.Cross), 1);
        Assert.Equal(DualSenseButtons.Cross, normal.Buttons);

        var (shifted, _) = Run(p, Pad(DualSenseButtons.Cross | DualSenseButtons.L1), 1.1);
        // Le bouton de couche n'est pas transmis, ✕ devient △
        Assert.Equal(DualSenseButtons.Triangle, shifted.Buttons);
    }

    [Fact]
    public void Macro_plays_steps_in_order_then_stops()
    {
        var macro = new Macro
        {
            Id = "m",
            Steps = { new MacroStep(DualSenseButtons.Square, 100), new MacroStep(DualSenseButtons.None, 50), new MacroStep(DualSenseButtons.Triangle, 100) },
        };
        var p = Processor(new ProcessingSettings { Macros = { macro }, Actions = { [DualSenseButtons.R3] = InputAction.ForMacro("m") } });

        Run(p, Pad(DualSenseButtons.R3), 10.0);
        Assert.Equal(DualSenseButtons.Square, Run(p, Pad(), 10.05).Out.Buttons);
        Assert.Equal(DualSenseButtons.None, Run(p, Pad(), 10.12).Out.Buttons);
        Assert.Equal(DualSenseButtons.Triangle, Run(p, Pad(), 10.2).Out.Buttons);
        Assert.Equal(DualSenseButtons.None, Run(p, Pad(), 10.3).Out.Buttons);
    }

    [Fact]
    public void Touch_zone_click_becomes_zone_button_or_stays_touchpad()
    {
        var right = new TouchPoint(true, 1, 1700, 300);
        var mapped = Processor(new ProcessingSettings
        {
            Touchpad = new TouchpadSettings { Mode = TouchpadMode.Zones, ZoneCount = 2 },
            Remap = { [DualSenseButtons.TouchZone2] = DualSenseButtons.Options },
        });
        Assert.Equal(DualSenseButtons.Options, Run(mapped, Pad(DualSenseButtons.Touchpad, right), 1).Out.Buttons);

        var unmapped = Processor(new ProcessingSettings { Touchpad = new TouchpadSettings { Mode = TouchpadMode.Zones, ZoneCount = 4 } });
        Assert.Equal(DualSenseButtons.Touchpad, Run(unmapped, Pad(DualSenseButtons.Touchpad, right), 1).Out.Buttons);
    }

    [Fact]
    public void Quick_swipe_produces_a_short_pulse()
    {
        var p = Processor(new ProcessingSettings
        {
            Touchpad = new TouchpadSettings { Gestures = true },
            Remap = { [DualSenseButtons.SwipeRight] = DualSenseButtons.R1 },
        });
        Run(p, Pad(touch: new TouchPoint(true, 1, 300, 500)), 1.0);
        Run(p, Pad(touch: new TouchPoint(true, 1, 1500, 520)), 1.1);
        var (o, _) = Run(p, Pad(), 1.15);
        Assert.Equal(DualSenseButtons.R1, o.Buttons);
        Assert.Equal(DualSenseButtons.None, Run(p, Pad(), 1.5).Out.Buttons);
    }

    [Fact]
    public void Touchpad_mouse_moves_cursor_and_hides_touch_from_game()
    {
        var p = Processor(new ProcessingSettings { Touchpad = new TouchpadSettings { Mode = TouchpadMode.Mouse, MouseSpeed = 1 } });
        Run(p, Pad(touch: new TouchPoint(true, 3, 500, 500)), 1);
        var (o, a) = Run(p, Pad(touch: new TouchPoint(true, 3, 600, 450)), 1.01);
        Assert.True(a.MouseDx > 0);
        Assert.True(a.MouseDy < 0);
        Assert.False(o.Touch0.Active);
    }

    [Fact]
    public void Stick_as_mouse_moves_cursor_and_centers_stick()
    {
        var p = Processor(new ProcessingSettings { Mouse = new MouseSettings { RightStick = StickRole.Mouse } });
        var s = Pad();
        s.RightX = 255;
        var (o, a) = Run(p, s, 1, dt: 0.01);
        Assert.True(a.MouseDx > 5);
        Assert.InRange(o.RightX, 127, 128);
    }

    [Fact]
    public void Ps_shortcut_triggers_action_and_hides_both_buttons()
    {
        var p = Processor(new ProcessingSettings(), ps: true);
        Run(p, Pad(DualSenseButtons.PS), 1.0);
        var (o, a) = Run(p, Pad(DualSenseButtons.PS | DualSenseButtons.R1), 1.05);
        Assert.True(a.HasSystem(SystemAction.NextProfile));
        Assert.Equal(DualSenseButtons.None, o.Buttons);
        // Relâchement : PS n'est pas envoyé au jeu puisqu'il a servi de raccourci
        Assert.Equal(DualSenseButtons.None, Run(p, Pad(), 1.1).Out.Buttons);
    }

    [Fact]
    public void Short_ps_press_alone_is_still_sent_to_the_game()
    {
        var p = Processor(new ProcessingSettings(), ps: true);
        Assert.Equal(DualSenseButtons.None, Run(p, Pad(DualSenseButtons.PS), 1.0).Out.Buttons);
        Assert.Equal(DualSenseButtons.PS, Run(p, Pad(), 1.1).Out.Buttons);
        Assert.Equal(DualSenseButtons.None, Run(p, Pad(), 1.4).Out.Buttons);
    }

    [Fact]
    public void Mute_button_toggles_windows_microphone_once_per_press()
    {
        var p = Processor(new ProcessingSettings(), mic: true);
        var (o, a) = Run(p, Pad(DualSenseButtons.Mute), 1);
        Assert.True(a.HasSystem(SystemAction.ToggleMicrophone));
        Assert.Equal(DualSenseButtons.None, o.Buttons);
        Assert.False(Run(p, Pad(DualSenseButtons.Mute), 1.01).Actions.HasSystem(SystemAction.ToggleMicrophone));
    }

    [Fact]
    public void Virtual_bits_never_reach_the_virtual_pad()
    {
        var p = Processor(new ProcessingSettings { Touchpad = new TouchpadSettings { Gestures = true } });
        Run(p, Pad(touch: new TouchPoint(true, 1, 300, 500)), 1.0);
        Run(p, Pad(touch: new TouchPoint(true, 1, 1500, 500)), 1.1);
        Assert.Equal(DualSenseButtons.None, Run(p, Pad(), 1.15).Out.Buttons & DualSenseButtonGroups.Virtual);
    }

    [Fact]
    public void Key_bits_store_virtual_keys_and_scancodes()
    {
        var k = default(KeyBits);
        k.Set(0x41);
        k.Set(0x111);
        Assert.True(k.Has(0x41));
        Assert.True(k.Has(0x111));
        Assert.False(k.Has(0x42));
        Assert.False(k.IsEmpty);
    }
}
