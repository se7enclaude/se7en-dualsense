namespace Se7en.DualSense.Hid;

/// <summary>Identifiants USB/BT des manettes Sony prises en charge.</summary>
public static class DualSenseIds
{
    public const int SonyVendorId = 0x054C;
    public const int DualSenseProductId = 0x0CE6;
    public const int DualSenseEdgeProductId = 0x0DF2;

    public static bool IsSupported(int vendorId, int productId) =>
        vendorId == SonyVendorId && (productId == DualSenseProductId || productId == DualSenseEdgeProductId);
}

public enum ConnectionType
{
    Usb,
    Bluetooth,
}

/// <summary>Boutons physiques de la DualSense (le D-pad est décomposé en 4 bits).</summary>
[Flags]
public enum DualSenseButtons : uint
{
    None = 0,
    Square = 1u << 0,
    Cross = 1u << 1,
    Circle = 1u << 2,
    Triangle = 1u << 3,
    L1 = 1u << 4,
    R1 = 1u << 5,
    L2 = 1u << 6,
    R2 = 1u << 7,
    Create = 1u << 8,
    Options = 1u << 9,
    L3 = 1u << 10,
    R3 = 1u << 11,
    PS = 1u << 12,
    Touchpad = 1u << 13,
    Mute = 1u << 14,
    DpadUp = 1u << 15,
    DpadDown = 1u << 16,
    DpadLeft = 1u << 17,
    DpadRight = 1u << 18,
    // DualSense Edge uniquement
    FnLeft = 1u << 19,
    FnRight = 1u << 20,
    PaddleLeft = 1u << 21,
    PaddleRight = 1u << 22,
}

public enum ChargingState : byte
{
    Discharging = 0,
    Charging = 1,
    Full = 2,
    Error = 0x0F,
}

/// <summary>Point de contact sur le pavé tactile (X : 0-1919, Y : 0-1079).</summary>
public readonly record struct TouchPoint(bool Active, byte Id, ushort X, ushort Y)
{
    public const int MaxX = 1919;
    public const int MaxY = 1079;
}

/// <summary>Instantané complet d'un rapport d'entrée. Struct pour éviter toute allocation dans la boucle.</summary>
public struct DualSenseState
{
    /// <summary>Sticks : 0-255, centre théorique 128. Y : 0 = haut.</summary>
    public byte LeftX, LeftY, RightX, RightY;
    /// <summary>Gâchettes analogiques : 0-255.</summary>
    public byte L2, R2;
    public DualSenseButtons Buttons;
    public TouchPoint Touch0, Touch1;
    public short GyroX, GyroY, GyroZ;
    public short AccelX, AccelY, AccelZ;
    public uint SensorTimestamp;
    /// <summary>Niveau de batterie en % (0-100).</summary>
    public byte BatteryPercent;
    public ChargingState Charging;
    /// <summary>Vrai si le rapport est le rapport complet (USB 0x01 ou BT 0x31).</summary>
    public bool IsFullReport;
    public byte SequenceNumber;

    public readonly bool IsPressed(DualSenseButtons button) => (Buttons & button) == button;
}
