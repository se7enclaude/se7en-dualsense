using System.Runtime.InteropServices;
using Se7en.DualSense.Processing;

namespace Se7en.DualSense.Output;

/// <summary>
/// Transforme les <see cref="ActionFrame"/> en vrais événements clavier / souris (SendInput).
/// Seuls les changements sont envoyés ; tout est relâché à la fermeture ou au changement de mode.
/// Les touches portent aussi leur code de balayage, lu par les jeux (Raw Input / DirectInput).
/// </summary>
public sealed class KeyboardMouseEmitter
{
    private KeyBits _down;
    private MouseButtonFlags _buttons;
    private double _fx, _fy, _wheel, _hwheel;
    private readonly INPUT[] _one = new INPUT[1];

    public void Apply(in ActionFrame f)
    {
        // Touches : comparaison par blocs de 64
        for (int block = 0; block < 8; block++)
        {
            ulong changed = _down[block] ^ f.Keys[block];
            if (changed == 0) continue;
            for (int bit = 0; bit < 64; bit++)
            {
                if ((changed & (1UL << bit)) == 0) continue;
                ushort code = (ushort)(block * 64 + bit);
                SendKey(code, (f.Keys[block] & (1UL << bit)) != 0);
            }
            _down[block] = f.Keys[block];
        }

        var b = f.MouseButtons;
        if (b != _buttons)
        {
            SendButton(MouseButtonFlags.Left, b, 0x0002, 0x0004, 0);
            SendButton(MouseButtonFlags.Right, b, 0x0008, 0x0010, 0);
            SendButton(MouseButtonFlags.Middle, b, 0x0020, 0x0040, 0);
            SendButton(MouseButtonFlags.Back, b, 0x0080, 0x0100, 1);
            SendButton(MouseButtonFlags.Forward, b, 0x0080, 0x0100, 2);
            _buttons = b;
        }

        _fx += f.MouseDx;
        _fy += f.MouseDy;
        int dx = (int)_fx, dy = (int)_fy;
        if (dx != 0 || dy != 0)
        {
            _fx -= dx;
            _fy -= dy;
            SendMouse(dx, dy, 0, 0x0001);
        }

        _wheel += f.Wheel * 120;
        int w = (int)_wheel;
        if (Math.Abs(w) >= 15)
        {
            _wheel -= w;
            SendMouse(0, 0, unchecked((uint)w), 0x0800);
        }
        _hwheel += f.HWheel * 120;
        int hw = (int)_hwheel;
        if (Math.Abs(hw) >= 15)
        {
            _hwheel -= hw;
            SendMouse(0, 0, unchecked((uint)hw), 0x1000);
        }
    }

    /// <summary>Relâche toutes les touches et boutons encore enfoncés.</summary>
    public void ReleaseAll() => Apply(default);

    /// <summary>Appui bref sur une touche virtuelle (volume, média, capture…), depuis n'importe quel thread.</summary>
    public static void Tap(params ushort[] vks)
    {
        var inputs = new List<INPUT>();
        foreach (var vk in vks) inputs.Add(KeyInput(vk, true));
        foreach (var vk in Enumerable.Reverse(vks)) inputs.Add(KeyInput(vk, false));
        SendInput((uint)inputs.Count, inputs.ToArray(), Marshal.SizeOf<INPUT>());
    }

    /// <summary>Tape un caractère quelle que soit la disposition du clavier (saisie Unicode).</summary>
    public static void TypeChar(char c)
    {
        const uint Unicode = 0x0004, KeyUp = 0x0002;
        var inputs = new[]
        {
            new INPUT { type = 1, U = new InputUnion { ki = new KEYBDINPUT { wScan = c, dwFlags = Unicode } } },
            new INPUT { type = 1, U = new InputUnion { ki = new KEYBDINPUT { wScan = c, dwFlags = Unicode | KeyUp } } },
        };
        SendInput(2, inputs, Marshal.SizeOf<INPUT>());
    }

    private void SendButton(MouseButtonFlags flag, MouseButtonFlags now, uint down, uint up, uint xButton)
    {
        bool was = (_buttons & flag) != 0, isDown = (now & flag) != 0;
        if (was == isDown) return;
        SendMouse(0, 0, xButton, isDown ? down : up);
    }

    private void SendMouse(int dx, int dy, uint data, uint flags)
    {
        _one[0] = new INPUT { type = 0, U = new InputUnion { mi = new MOUSEINPUT { dx = dx, dy = dy, mouseData = data, dwFlags = flags } } };
        SendInput(1, _one, Marshal.SizeOf<INPUT>());
    }

    private void SendKey(ushort code, bool down)
    {
        _one[0] = KeyInput(code, down);
        SendInput(1, _one, Marshal.SizeOf<INPUT>());
    }

    private static INPUT KeyInput(ushort code, bool down)
    {
        const uint ExtendedKey = 0x0001, KeyUp = 0x0002, ScanCode = 0x0008;
        uint flags = down ? 0 : KeyUp;
        ushort vk, scan;
        if (code >= 0x100)
        {
            // Code de balayage brut (position physique)
            vk = 0;
            scan = (ushort)(code & 0xFF);
            flags |= ScanCode;
        }
        else
        {
            vk = code;
            uint sc = MapVirtualKeyW(code, 4); // MAPVK_VK_TO_VSC_EX : 0xE0xx pour les touches étendues
            scan = (ushort)(sc & 0xFF);
            if ((sc & 0xFF00) == 0xE000 || IsExtendedVk(code)) flags |= ExtendedKey;
        }
        return new INPUT { type = 1, U = new InputUnion { ki = new KEYBDINPUT { wVk = vk, wScan = scan, dwFlags = flags } } };
    }

    private static bool IsExtendedVk(ushort vk) => vk is >= 0x21 and <= 0x2E or 0x5B or 0x5C or 0x5D or 0x6F or 0x90 or 0xA3 or 0xA5;

    // ───────────── Win32 ─────────────

    [StructLayout(LayoutKind.Sequential)]
    private struct INPUT
    {
        public uint type;
        public InputUnion U;
    }

    [StructLayout(LayoutKind.Explicit)]
    private struct InputUnion
    {
        [FieldOffset(0)] public MOUSEINPUT mi;
        [FieldOffset(0)] public KEYBDINPUT ki;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MOUSEINPUT
    {
        public int dx;
        public int dy;
        public uint mouseData;
        public uint dwFlags;
        public uint time;
        public IntPtr dwExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct KEYBDINPUT
    {
        public ushort wVk;
        public ushort wScan;
        public uint dwFlags;
        public uint time;
        public IntPtr dwExtraInfo;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SendInput(uint count, INPUT[] inputs, int size);

    [DllImport("user32.dll")]
    private static extern uint MapVirtualKeyW(uint code, uint mapType);
}
