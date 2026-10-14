using System.Buffers.Binary;
using Se7en.DualSense.Hid;

namespace Se7en.DualSense.Output;

/// <summary>
/// Construit le rapport étendu DS4 (DS4_REPORT_EX, 63 octets) attendu par ViGEmBus à partir de l'état
/// de la DualSense. Le format étendu transporte aussi le pavé tactile, le gyroscope et le bouton PS.
///
/// Disposition : 0-3 sticks · 4-5 boutons · 6 spéciaux (PS, clic pavé) · 7-8 gâchettes · 9-10 horodatage
/// · 11 batterie · 12-17 gyro · 18-23 accéléro · 29 batterie (spécial) · 32 nb paquets tactiles
/// · 33 compteur · 34-37 doigt 1 · 38-41 doigt 2.
/// </summary>
public sealed class Ds4ReportBuilder
{
    public const int Length = 63;

    // La DualSense mesure 1920 × 1080 points tactiles, la DS4 1920 × 943
    private const int Ds4TouchMaxY = 942;

    private byte _touchPacketCounter;
    private ushort _timestamp;
    private bool _wasTouching;

    public void Build(in DualSenseState s, Span<byte> report)
    {
        if (report.Length < Length)
            throw new ArgumentException("Tampon trop petit pour un rapport DS4.", nameof(report));
        report.Slice(0, Length).Clear();

        report[0] = s.LeftX;
        report[1] = s.LeftY;
        report[2] = s.RightX;
        report[3] = s.RightY;

        BinaryPrimitives.WriteUInt16LittleEndian(report.Slice(4), EncodeButtons(s.Buttons));

        byte special = 0;
        if ((s.Buttons & DualSenseButtons.PS) != 0) special |= 0x01;
        if ((s.Buttons & DualSenseButtons.Touchpad) != 0) special |= 0x02;
        report[6] = special;

        report[7] = s.L2;
        report[8] = s.R2;

        // Horodatage en unités de 5,33 µs ; une progression régulière suffit aux jeux
        _timestamp = unchecked((ushort)(_timestamp + 188));
        BinaryPrimitives.WriteUInt16LittleEndian(report.Slice(9), _timestamp);

        byte battery = (byte)Math.Clamp(s.BatteryPercent * 255 / 100, 0, 255);
        report[11] = battery;

        BinaryPrimitives.WriteInt16LittleEndian(report.Slice(12), s.GyroX);
        BinaryPrimitives.WriteInt16LittleEndian(report.Slice(14), s.GyroY);
        BinaryPrimitives.WriteInt16LittleEndian(report.Slice(16), s.GyroZ);
        BinaryPrimitives.WriteInt16LittleEndian(report.Slice(18), s.AccelX);
        BinaryPrimitives.WriteInt16LittleEndian(report.Slice(20), s.AccelY);
        BinaryPrimitives.WriteInt16LittleEndian(report.Slice(22), s.AccelZ);

        // Octet 29 : niveau (0-10) + bit « câble branché »
        byte level = (byte)Math.Min(10, s.BatteryPercent / 10);
        report[29] = (byte)(level | (s.Charging is ChargingState.Charging or ChargingState.Full ? 0x10 : 0));

        bool touching = s.Touch0.Active || s.Touch1.Active;
        if (touching || _wasTouching)
            _touchPacketCounter++;
        _wasTouching = touching;

        report[32] = 1; // un paquet tactile courant
        report[33] = _touchPacketCounter;
        EncodeTouch(s.Touch0, report.Slice(34, 4));
        EncodeTouch(s.Touch1, report.Slice(38, 4));
    }

    public static ushort EncodeButtons(DualSenseButtons b)
    {
        bool up = (b & DualSenseButtons.DpadUp) != 0, down = (b & DualSenseButtons.DpadDown) != 0;
        bool left = (b & DualSenseButtons.DpadLeft) != 0, right = (b & DualSenseButtons.DpadRight) != 0;

        // Hat switch : 0 = haut, sens horaire, 8 = neutre
        int hat = (up, right, down, left) switch
        {
            (true, true, _, _) => 1,
            (_, true, true, _) => 3,
            (_, _, true, true) => 5,
            (true, _, _, true) => 7,
            (true, _, _, _) => 0,
            (_, true, _, _) => 2,
            (_, _, true, _) => 4,
            (_, _, _, true) => 6,
            _ => 8,
        };

        int v = hat;
        if ((b & DualSenseButtons.Square) != 0) v |= 1 << 4;
        if ((b & DualSenseButtons.Cross) != 0) v |= 1 << 5;
        if ((b & DualSenseButtons.Circle) != 0) v |= 1 << 6;
        if ((b & DualSenseButtons.Triangle) != 0) v |= 1 << 7;
        if ((b & DualSenseButtons.L1) != 0) v |= 1 << 8;
        if ((b & DualSenseButtons.R1) != 0) v |= 1 << 9;
        if ((b & DualSenseButtons.L2) != 0) v |= 1 << 10;
        if ((b & DualSenseButtons.R2) != 0) v |= 1 << 11;
        if ((b & DualSenseButtons.Create) != 0) v |= 1 << 12; // Share
        if ((b & DualSenseButtons.Options) != 0) v |= 1 << 13;
        if ((b & DualSenseButtons.L3) != 0) v |= 1 << 14;
        if ((b & DualSenseButtons.R3) != 0) v |= 1 << 15;
        return (ushort)v;
    }

    /// <summary>[bit7 = levé | id 7 bits][x lo][x hi 4 bits | y lo 4 bits][y hi 8 bits].</summary>
    private static void EncodeTouch(TouchPoint t, Span<byte> dst)
    {
        int x = Math.Clamp((int)t.X, 0, TouchPoint.MaxX);
        int y = Math.Clamp((int)t.Y * Ds4TouchMaxY / TouchPoint.MaxY, 0, Ds4TouchMaxY);
        dst[0] = (byte)((t.Active ? 0 : 0x80) | (t.Id & 0x7F));
        dst[1] = (byte)(x & 0xFF);
        dst[2] = (byte)(((x >> 8) & 0x0F) | ((y & 0x0F) << 4));
        dst[3] = (byte)(y >> 4);
    }
}
