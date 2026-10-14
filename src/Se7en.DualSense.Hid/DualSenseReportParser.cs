using System.Buffers.Binary;

namespace Se7en.DualSense.Hid;

/// <summary>
/// Décodage des rapports d'entrée de la DualSense.
/// Références : hid-playstation.c (Linux), DS4Windows.
///
/// USB 0x01 (64 octets)  : [0x01][données…]
/// BT  0x31 (78 octets)  : [0x31][seq][données…][CRC32 x4]
/// BT  0x01 (10 octets)  : rapport réduit envoyé tant que le mode complet n'est pas activé.
/// </summary>
public static class DualSenseReportParser
{
    public const int UsbInputReportLength = 64;
    public const int BtInputReportLength = 78;

    private const int UsbDataOffset = 1;
    private const int BtDataOffset = 2;

    // Offsets relatifs au début du bloc de données commun
    private const int OffLX = 0, OffLY = 1, OffRX = 2, OffRY = 3, OffL2 = 4, OffR2 = 5, OffSeq = 6;
    private const int OffButtons0 = 7, OffButtons1 = 8, OffButtons2 = 9;
    private const int OffGyro = 15, OffAccel = 21, OffTimestamp = 27;
    private const int OffTouch0 = 32, OffTouch1 = 36;
    private const int OffStatus = 52;

    /// <summary>Tente de décoder un rapport. Retourne false si le rapport est inconnu, tronqué ou corrompu.</summary>
    public static bool TryParse(ReadOnlySpan<byte> report, out DualSenseState state)
    {
        state = default;
        if (report.Length < 10)
            return false;

        switch (report[0])
        {
            case 0x01 when report.Length >= UsbInputReportLength:
                ParseFull(report.Slice(UsbDataOffset), ref state);
                return true;

            case 0x01:
                ParseBluetoothSimple(report, ref state);
                return true;

            case 0x31 when report.Length >= BtInputReportLength:
                if (!HasValidBluetoothCrc(report.Slice(0, BtInputReportLength)))
                    return false;
                ParseFull(report.Slice(BtDataOffset), ref state);
                return true;

            default:
                return false;
        }
    }

    public static bool HasValidBluetoothCrc(ReadOnlySpan<byte> report)
    {
        int len = report.Length;
        uint expected = BinaryPrimitives.ReadUInt32LittleEndian(report.Slice(len - 4));
        return Crc32.Compute(Crc32.InputSeed, report.Slice(0, len - 4)) == expected;
    }

    private static void ParseFull(ReadOnlySpan<byte> d, ref DualSenseState s)
    {
        s.IsFullReport = true;
        s.LeftX = d[OffLX];
        s.LeftY = d[OffLY];
        s.RightX = d[OffRX];
        s.RightY = d[OffRY];
        s.L2 = d[OffL2];
        s.R2 = d[OffR2];
        s.SequenceNumber = d[OffSeq];
        s.Buttons = DecodeButtons(d[OffButtons0], d[OffButtons1], d[OffButtons2]);

        s.GyroX = BinaryPrimitives.ReadInt16LittleEndian(d.Slice(OffGyro));
        s.GyroY = BinaryPrimitives.ReadInt16LittleEndian(d.Slice(OffGyro + 2));
        s.GyroZ = BinaryPrimitives.ReadInt16LittleEndian(d.Slice(OffGyro + 4));
        s.AccelX = BinaryPrimitives.ReadInt16LittleEndian(d.Slice(OffAccel));
        s.AccelY = BinaryPrimitives.ReadInt16LittleEndian(d.Slice(OffAccel + 2));
        s.AccelZ = BinaryPrimitives.ReadInt16LittleEndian(d.Slice(OffAccel + 4));
        s.SensorTimestamp = BinaryPrimitives.ReadUInt32LittleEndian(d.Slice(OffTimestamp));

        s.Touch0 = DecodeTouch(d.Slice(OffTouch0, 4));
        s.Touch1 = DecodeTouch(d.Slice(OffTouch1, 4));

        byte status = d[OffStatus];
        s.BatteryPercent = (byte)Math.Min(100, (status & 0x0F) * 10 + 5);
        s.Charging = (status >> 4) switch
        {
            0 => ChargingState.Discharging,
            1 => ChargingState.Charging,
            2 => ChargingState.Full,
            _ => ChargingState.Error,
        };
        if (s.Charging == ChargingState.Full)
            s.BatteryPercent = 100;
    }

    /// <summary>Rapport BT réduit : sticks, boutons et gâchettes uniquement.</summary>
    private static void ParseBluetoothSimple(ReadOnlySpan<byte> r, ref DualSenseState s)
    {
        s.IsFullReport = false;
        s.LeftX = r[1];
        s.LeftY = r[2];
        s.RightX = r[3];
        s.RightY = r[4];
        s.Buttons = DecodeButtons(r[5], r[6], r[7]);
        s.L2 = r[8];
        s.R2 = r[9];
    }

    public static DualSenseButtons DecodeButtons(byte b0, byte b1, byte b2)
    {
        var buttons = DualSenseButtons.None;

        // D-pad : hat switch sur les 4 bits de poids faible (0 = haut, sens horaire, 8 = neutre)
        buttons |= (b0 & 0x0F) switch
        {
            0 => DualSenseButtons.DpadUp,
            1 => DualSenseButtons.DpadUp | DualSenseButtons.DpadRight,
            2 => DualSenseButtons.DpadRight,
            3 => DualSenseButtons.DpadDown | DualSenseButtons.DpadRight,
            4 => DualSenseButtons.DpadDown,
            5 => DualSenseButtons.DpadDown | DualSenseButtons.DpadLeft,
            6 => DualSenseButtons.DpadLeft,
            7 => DualSenseButtons.DpadUp | DualSenseButtons.DpadLeft,
            _ => DualSenseButtons.None,
        };

        if ((b0 & 0x10) != 0) buttons |= DualSenseButtons.Square;
        if ((b0 & 0x20) != 0) buttons |= DualSenseButtons.Cross;
        if ((b0 & 0x40) != 0) buttons |= DualSenseButtons.Circle;
        if ((b0 & 0x80) != 0) buttons |= DualSenseButtons.Triangle;

        if ((b1 & 0x01) != 0) buttons |= DualSenseButtons.L1;
        if ((b1 & 0x02) != 0) buttons |= DualSenseButtons.R1;
        if ((b1 & 0x04) != 0) buttons |= DualSenseButtons.L2;
        if ((b1 & 0x08) != 0) buttons |= DualSenseButtons.R2;
        if ((b1 & 0x10) != 0) buttons |= DualSenseButtons.Create;
        if ((b1 & 0x20) != 0) buttons |= DualSenseButtons.Options;
        if ((b1 & 0x40) != 0) buttons |= DualSenseButtons.L3;
        if ((b1 & 0x80) != 0) buttons |= DualSenseButtons.R3;

        if ((b2 & 0x01) != 0) buttons |= DualSenseButtons.PS;
        if ((b2 & 0x02) != 0) buttons |= DualSenseButtons.Touchpad;
        if ((b2 & 0x04) != 0) buttons |= DualSenseButtons.Mute;
        if ((b2 & 0x10) != 0) buttons |= DualSenseButtons.FnLeft;
        if ((b2 & 0x20) != 0) buttons |= DualSenseButtons.FnRight;
        if ((b2 & 0x40) != 0) buttons |= DualSenseButtons.PaddleLeft;
        if ((b2 & 0x80) != 0) buttons |= DualSenseButtons.PaddleRight;

        return buttons;
    }

    /// <summary>Contact : [bit7 = pas de contact | id 7 bits][x lo][x hi 4 bits | y lo 4 bits][y hi].</summary>
    private static TouchPoint DecodeTouch(ReadOnlySpan<byte> t)
    {
        bool active = (t[0] & 0x80) == 0;
        byte id = (byte)(t[0] & 0x7F);
        ushort x = (ushort)(t[1] | ((t[2] & 0x0F) << 8));
        ushort y = (ushort)((t[2] >> 4) | (t[3] << 4));
        return new TouchPoint(active, id, x, y);
    }
}
