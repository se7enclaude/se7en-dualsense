using System.Buffers.Binary;

namespace Se7en.DualSense.Hid;

/// <summary>État souhaité des retours de la manette (vibrations, barre lumineuse, LED).</summary>
public record struct DualSenseOutputState
{
    /// <summary>Moteur gauche (gros moteur, basses fréquences), 0-255.</summary>
    public byte HeavyMotor;
    /// <summary>Moteur droit (petit moteur, hautes fréquences), 0-255.</summary>
    public byte LightMotor;
    public byte LightbarRed;
    public byte LightbarGreen;
    public byte LightbarBlue;
    /// <summary>LED joueur (5 bits). 0x04 = LED centrale (joueur 1).</summary>
    public byte PlayerLeds;
    public bool MuteLed;
    /// <summary>Effets de gâchettes adaptatives (null = ne pas piloter les gâchettes).</summary>
    public TriggerEffect? LeftTrigger;
    public TriggerEffect? RightTrigger;
    /// <summary>
    /// Haptique HD : les moteurs ne sont plus pilotés par ce rapport mais par le son envoyé à la manette
    /// (canaux 3 et 4 de sa carte son USB).
    /// </summary>
    public bool AudioHaptics;

    public static DualSenseOutputState Default => new()
    {
        LightbarRed = 0,
        LightbarGreen = 64,
        LightbarBlue = 255,
        PlayerLeds = 0x04,
    };
}

/// <summary>
/// Effet de gâchette adaptative (modes simples largement documentés) :
/// 0x05 = aucun, 0x01 = résistance continue [début, force], 0x02 = section [début, fin, force],
/// 0x06 = vibration [fréquence, force, début].
/// </summary>
public readonly record struct TriggerEffect(byte Mode, byte P1 = 0, byte P2 = 0, byte P3 = 0)
{
    public static TriggerEffect Off => new(0x05);

    /// <summary>Résistance dès <paramref name="start"/> (0-1), d'intensité <paramref name="force"/> (0-1).</summary>
    public static TriggerEffect Resistance(double start, double force) =>
        new(0x01, (byte)(Math.Clamp(start, 0, 1) * 255), (byte)(Math.Clamp(force, 0, 1) * 255));

    /// <summary>Point dur entre <paramref name="start"/> et <paramref name="end"/> (0-1) : sensation d'arme, d'arc.</summary>
    public static TriggerEffect Section(double start, double end, double force) =>
        new(0x02, (byte)(Math.Clamp(start, 0, 1) * 255), (byte)(Math.Clamp(Math.Max(end, start), 0, 1) * 255), (byte)(Math.Clamp(force, 0, 1) * 255));

    /// <summary>Gâchette qui vibre à partir de <paramref name="start"/>.</summary>
    public static TriggerEffect Vibration(double frequency, double force, double start) =>
        new(0x06, (byte)(Math.Clamp(frequency, 0, 1) * 255), (byte)(Math.Clamp(force, 0, 1) * 255), (byte)(Math.Clamp(start, 0, 1) * 255));
}

/// <summary>
/// Construction des rapports de sortie.
/// USB 0x02 : [0x02][bloc commun 47 octets]                              = 48 octets
/// BT  0x31 : [0x31][seq&lt;&lt;4][0x10][bloc commun 47][réservé 24][CRC32] = 78 octets
/// </summary>
public static class DualSenseOutputReport
{
    public const int UsbLength = 48;
    public const int BtLength = 78;

    // Bloc commun
    private const int Flag0 = 0, Flag1 = 1, MotorRight = 2, MotorLeft = 3, MuteLed = 8;
    private const int RightTriggerBlock = 10, LeftTriggerBlock = 21;
    private const byte Flag0RightTrigger = 0x04, Flag0LeftTrigger = 0x08;
    private const int Flag2 = 38, LightbarSetup = 41, LedBrightness = 42, PlayerLeds = 43;
    private const int LightbarR = 44, LightbarG = 45, LightbarB = 46;

    private const byte Flag0CompatibleVibration = 0x01;
    private const byte Flag0HapticsSelect = 0x02;
    private const byte Flag1MicMuteLedEnable = 0x01;
    private const byte Flag1LightbarEnable = 0x04;
    private const byte Flag1PlayerLedEnable = 0x10;
    private const byte Flag2LightbarSetupEnable = 0x02;
    private const byte LightbarSetupLightOut = 0x02;

    /// <summary>
    /// Écrit le rapport dans <paramref name="buffer"/> (taille UsbLength ou BtLength selon la connexion).
    /// <paramref name="releaseLightbar"/> : à envoyer une fois après connexion pour arrêter l'animation
    /// bleue par défaut et prendre le contrôle de la barre lumineuse.
    /// </summary>
    public static int Build(Span<byte> buffer, ConnectionType connection, in DualSenseOutputState state,
        byte btSequence, bool releaseLightbar)
    {
        int length = connection == ConnectionType.Usb ? UsbLength : BtLength;
        if (buffer.Length < length)
            throw new ArgumentException("Tampon trop petit pour le rapport de sortie.", nameof(buffer));

        Span<byte> report = buffer.Slice(0, length);
        report.Clear();

        Span<byte> common;
        if (connection == ConnectionType.Usb)
        {
            report[0] = 0x02;
            common = report.Slice(1, 47);
        }
        else
        {
            report[0] = 0x31;
            report[1] = (byte)((btSequence & 0x0F) << 4);
            report[2] = 0x10;
            common = report.Slice(3, 47);
        }

        common[Flag0] = state.AudioHaptics ? (byte)0 : (byte)(Flag0CompatibleVibration | Flag0HapticsSelect);
        common[Flag1] = Flag1LightbarEnable | Flag1PlayerLedEnable | Flag1MicMuteLedEnable;
        common[MotorRight] = state.LightMotor;
        common[MotorLeft] = state.HeavyMotor;
        common[MuteLed] = state.MuteLed ? (byte)1 : (byte)0;

        if (state.RightTrigger is { } rt)
        {
            common[Flag0] |= Flag0RightTrigger;
            WriteTrigger(common.Slice(RightTriggerBlock, 11), rt);
        }
        if (state.LeftTrigger is { } lt)
        {
            common[Flag0] |= Flag0LeftTrigger;
            WriteTrigger(common.Slice(LeftTriggerBlock, 11), lt);
        }

        if (releaseLightbar)
        {
            common[Flag2] = Flag2LightbarSetupEnable;
            common[LightbarSetup] = LightbarSetupLightOut;
        }

        common[LedBrightness] = 0x00; // luminosité max
        common[PlayerLeds] = (byte)(state.PlayerLeds & 0x1F);
        common[LightbarR] = state.LightbarRed;
        common[LightbarG] = state.LightbarGreen;
        common[LightbarB] = state.LightbarBlue;

        if (connection == ConnectionType.Bluetooth)
        {
            uint crc = Crc32.Compute(Crc32.OutputSeed, report.Slice(0, BtLength - 4));
            BinaryPrimitives.WriteUInt32LittleEndian(report.Slice(BtLength - 4), crc);
        }

        return length;
    }

    private static void WriteTrigger(Span<byte> block, TriggerEffect e)
    {
        block.Clear();
        block[0] = e.Mode;
        block[1] = e.P1;
        block[2] = e.P2;
        block[3] = e.P3;
    }
}
