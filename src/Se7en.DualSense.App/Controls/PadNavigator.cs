using System.Diagnostics;
using Se7en.DualSense.App.Services;
using Se7en.DualSense.Hid;

namespace Se7en.DualSense.App.Controls;

/// <summary>
/// Navigation à la manette pour les écrans plein écran (lanceur, clavier virtuel) : croix ou stick gauche
/// avec répétition quand on maintient, et fronts montants des boutons.
/// </summary>
public sealed class PadNavigator
{
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private DualSenseButtons _last = (DualSenseButtons)uint.MaxValue;
    private (int X, int Y) _dir;
    private double _nextRepeat;

    /// <summary>Direction à appliquer cette image (-1, 0, 1), avec répétition.</summary>
    public (int X, int Y) Move { get; private set; }

    /// <summary>Boutons enfoncés à cette image (fronts montants).</summary>
    public DualSenseButtons Pressed { get; private set; }

    public void Update(in FrameData frame)
    {
        Move = (0, 0);
        Pressed = DualSenseButtons.None;
        if (!frame.IsConnected) return;
        var s = frame.State;
        var b = s.Buttons;
        // Première image : on ignore les boutons déjà enfoncés (celui qui a ouvert l'écran)
        Pressed = _last == (DualSenseButtons)uint.MaxValue ? DualSenseButtons.None : b & ~_last;
        _last = b;

        double lx = (s.LeftX - 127.5) / 127.5, ly = (s.LeftY - 127.5) / 127.5;
        int x = (b & DualSenseButtons.DpadLeft) != 0 || lx < -0.6 ? -1 : (b & DualSenseButtons.DpadRight) != 0 || lx > 0.6 ? 1 : 0;
        int y = (b & DualSenseButtons.DpadUp) != 0 || ly < -0.6 ? -1 : (b & DualSenseButtons.DpadDown) != 0 || ly > 0.6 ? 1 : 0;
        double now = _clock.Elapsed.TotalSeconds;
        if ((x, y) != _dir)
        {
            _dir = (x, y);
            if (x != 0 || y != 0)
            {
                Move = (x, y);
                _nextRepeat = now + 0.38;
            }
        }
        else if ((x != 0 || y != 0) && now >= _nextRepeat)
        {
            Move = (x, y);
            _nextRepeat = now + 0.11;
        }
        if (Move != (0, 0)) SoundService.Tick();
    }

    public bool Was(DualSenseButtons button) => (Pressed & button) != 0;
}
