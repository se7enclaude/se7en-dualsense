using Nefarius.ViGEm.Client;
using Nefarius.ViGEm.Client.Targets;
using Nefarius.ViGEm.Client.Targets.Xbox360;
using Se7en.DualSense.Hid;

namespace Se7en.DualSense.Output;

/// <summary>Manette virtuelle branchée sur ViGEmBus.</summary>
public interface IVirtualPad : IDisposable
{
    OutputMode Mode { get; }

    /// <summary>Envoie l'état courant au jeu. Appelé sur le thread d'E/S : doit rester très rapide.</summary>
    void Submit(in DualSenseState state);

    /// <summary>Vibrations / barre lumineuse envoyées par le jeu.</summary>
    event Action<GameFeedback>? Feedback;
}

/// <summary>DualShock 4 virtuelle : rapports bruts étendus (pavé tactile, gyroscope, bouton PS).</summary>
public sealed class VirtualDs4 : IVirtualPad
{
    private readonly IDualShock4Controller _pad;
    private readonly Ds4ReportBuilder _builder = new();
    private readonly byte[] _report = new byte[Ds4ReportBuilder.Length];
    private readonly Thread _feedbackThread;
    private volatile bool _disposed;

    public OutputMode Mode => OutputMode.DualShock4;

    public event Action<GameFeedback>? Feedback;

    public VirtualDs4(ViGEmClient client)
    {
        _pad = client.CreateDualShock4Controller();
        _pad.AutoSubmitReport = false;
        _pad.Connect();

        // Lecture des rapports de sortie envoyés par le jeu (méthode recommandée par ViGEm)
        _feedbackThread = new Thread(FeedbackLoop) { Name = "DS4-Feedback", IsBackground = true };
        _feedbackThread.Start();
    }

    private void FeedbackLoop()
    {
        while (!_disposed)
        {
            byte[] buf;
            try
            {
                buf = _pad.AwaitRawOutputReport(250, out bool timedOut).ToArray();
                if (timedOut) continue;
            }
            catch
            {
                if (_disposed) return;
                Thread.Sleep(100);
                continue;
            }

            if (TryParseOutputReport(buf, out var feedback))
                Feedback?.Invoke(feedback);
        }
    }

    /// <summary>
    /// Rapport de sortie DS4 (USB 0x05) : [1] drapeaux (0x01 vibrations, 0x02 barre lumineuse),
    /// [4] petit moteur, [5] gros moteur, [6..8] RVB.
    /// </summary>
    public static bool TryParseOutputReport(ReadOnlySpan<byte> buf, out GameFeedback feedback)
    {
        feedback = default;
        if (buf.Length < 9 || buf[0] != 0x05)
            return false;
        byte flags = buf[1];
        bool rumble = (flags & 0x01) != 0;
        bool light = (flags & 0x02) != 0;
        if (!rumble && !light)
            return false;
        feedback = new GameFeedback(
            rumble ? buf[5] : (byte)0,
            rumble ? buf[4] : (byte)0,
            light ? buf[6] : null,
            light ? buf[7] : null,
            light ? buf[8] : null);
        return true;
    }

    public void Submit(in DualSenseState state)
    {
        _builder.Build(state, _report);
        _pad.SubmitRawReport(_report);
    }

    public void Dispose()
    {
        _disposed = true;
        try { _pad.Disconnect(); } catch { /* déjà débranchée */ }
        _feedbackThread.Join(500);
    }
}

/// <summary>Xbox 360 virtuelle.</summary>
public sealed class VirtualXbox360 : IVirtualPad
{
    private readonly IXbox360Controller _pad;

    public OutputMode Mode => OutputMode.Xbox360;

    public event Action<GameFeedback>? Feedback;

    public VirtualXbox360(ViGEmClient client)
    {
        _pad = client.CreateXbox360Controller();
        _pad.AutoSubmitReport = false;
        _pad.FeedbackReceived += (_, e) => Feedback?.Invoke(new GameFeedback(e.LargeMotor, e.SmallMotor, null, null, null));
        _pad.Connect();
    }

    public void Submit(in DualSenseState s)
    {
        var b = s.Buttons;
        ushort buttons = 0;
        // Positions équivalentes : ✕→A, ○→B, □→X, △→Y ; Create→Back, Options→Start, PS→Guide
        if ((b & DualSenseButtons.DpadUp) != 0) buttons |= 0x0001;
        if ((b & DualSenseButtons.DpadDown) != 0) buttons |= 0x0002;
        if ((b & DualSenseButtons.DpadLeft) != 0) buttons |= 0x0004;
        if ((b & DualSenseButtons.DpadRight) != 0) buttons |= 0x0008;
        if ((b & DualSenseButtons.Options) != 0) buttons |= 0x0010;
        if ((b & DualSenseButtons.Create) != 0) buttons |= 0x0020;
        if ((b & DualSenseButtons.L3) != 0) buttons |= 0x0040;
        if ((b & DualSenseButtons.R3) != 0) buttons |= 0x0080;
        if ((b & DualSenseButtons.L1) != 0) buttons |= 0x0100;
        if ((b & DualSenseButtons.R1) != 0) buttons |= 0x0200;
        if ((b & DualSenseButtons.PS) != 0) buttons |= 0x0400;
        if ((b & DualSenseButtons.Cross) != 0) buttons |= 0x1000;
        if ((b & DualSenseButtons.Circle) != 0) buttons |= 0x2000;
        if ((b & DualSenseButtons.Square) != 0) buttons |= 0x4000;
        if ((b & DualSenseButtons.Triangle) != 0) buttons |= 0x8000;
        _pad.SetButtonsFull(buttons);

        _pad.SetAxisValue(Xbox360Axis.LeftThumbX, ToXboxAxis(s.LeftX, invert: false));
        _pad.SetAxisValue(Xbox360Axis.LeftThumbY, ToXboxAxis(s.LeftY, invert: true));
        _pad.SetAxisValue(Xbox360Axis.RightThumbX, ToXboxAxis(s.RightX, invert: false));
        _pad.SetAxisValue(Xbox360Axis.RightThumbY, ToXboxAxis(s.RightY, invert: true));
        _pad.SetSliderValue(Xbox360Slider.LeftTrigger, s.L2);
        _pad.SetSliderValue(Xbox360Slider.RightTrigger, s.R2);
        _pad.SubmitReport();
    }

    /// <summary>0-255 (Y vers le bas) → -32768..32767 (Y vers le haut pour Xbox).</summary>
    public static short ToXboxAxis(byte value, bool invert)
    {
        int centered = value - 128;              // -128..127
        if (invert) centered = -centered - 1;    // -128..127, sens inversé
        int scaled = centered >= 0 ? centered * 32767 / 127 : centered * 32768 / 128;
        return (short)Math.Clamp(scaled, short.MinValue, short.MaxValue);
    }

    public void Dispose()
    {
        try { _pad.Disconnect(); } catch { /* déjà débranchée */ }
    }
}
