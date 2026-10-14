using System.Runtime.InteropServices;

namespace Se7en.DualSense.Hid;

/// <summary>
/// Extinction d'une manette Bluetooth : Windows coupe la liaison (IOCTL_BTH_DISCONNECT_DEVICE sur la radio),
/// la DualSense s'éteint d'elle-même quelques secondes après.
/// </summary>
public static class Bluetooth
{
    private const uint IoctlBthDisconnectDevice = 0x41000C;

    [StructLayout(LayoutKind.Sequential)]
    private struct FindRadioParams
    {
        public int Size;
    }

    [DllImport("bthprops.cpl", SetLastError = true)]
    private static extern IntPtr BluetoothFindFirstRadio(ref FindRadioParams p, out IntPtr radio);

    [DllImport("bthprops.cpl", SetLastError = true)]
    private static extern bool BluetoothFindNextRadio(IntPtr find, out IntPtr radio);

    [DllImport("bthprops.cpl", SetLastError = true)]
    private static extern bool BluetoothFindRadioClose(IntPtr find);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool DeviceIoControl(IntPtr device, uint code, ref ulong input, int inputSize,
        IntPtr output, int outputSize, out int returned, IntPtr overlapped);

    [DllImport("kernel32.dll")]
    private static extern bool CloseHandle(IntPtr handle);

    /// <summary>« A0:AB:51:12:34:56 » → adresse 48 bits.</summary>
    public static bool TryParseAddress(string? mac, out ulong address)
    {
        address = 0;
        if (string.IsNullOrWhiteSpace(mac)) return false;
        var hex = mac.Replace(":", "").Replace("-", "");
        return hex.Length == 12 && ulong.TryParse(hex, System.Globalization.NumberStyles.HexNumber, null, out address);
    }

    /// <summary>Déconnecte (et donc éteint) la manette ; renvoie false si aucune radio ne l'a acceptée.</summary>
    public static bool Disconnect(string? mac)
    {
        if (!TryParseAddress(mac, out ulong address)) return false;
        var p = new FindRadioParams { Size = Marshal.SizeOf<FindRadioParams>() };
        IntPtr find;
        try
        {
            find = BluetoothFindFirstRadio(ref p, out IntPtr radio);
            if (find == IntPtr.Zero) return false;
            bool ok = false;
            do
            {
                ok |= DeviceIoControl(radio, IoctlBthDisconnectDevice, ref address, sizeof(ulong), IntPtr.Zero, 0, out _, IntPtr.Zero);
                CloseHandle(radio);
            } while (!ok && BluetoothFindNextRadio(find, out radio));
            BluetoothFindRadioClose(find);
            return ok;
        }
        catch (DllNotFoundException)
        {
            return false;
        }
    }
}
