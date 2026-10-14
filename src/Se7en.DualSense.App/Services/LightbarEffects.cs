using System.Runtime.InteropServices;
using Se7en.DualSense.Output;

namespace Se7en.DualSense.App.Services;

/// <summary>Effets animés de la barre lumineuse : arc-en-ciel, respiration, musique, couleurs de l'écran.</summary>
public static class LightbarEffects
{
    public static bool IsAnimated(LightbarMode m) => m is LightbarMode.Rainbow or LightbarMode.Breathing or LightbarMode.Music or LightbarMode.Screen;

    /// <summary>Couleur de l'effet à l'instant <paramref name="t"/> (secondes).</summary>
    public static (byte R, byte G, byte B) Compute(FeedbackSettings f, double t, double audioLevel)
    {
        double speed = Math.Clamp(f.EffectSpeed, 0.2, 3);
        switch (f.Lightbar)
        {
            case LightbarMode.Rainbow:
                return Hsv(t * 50 * speed % 360, 1, 1);
            case LightbarMode.Breathing:
            {
                double k = 0.08 + 0.92 * Math.Pow(Math.Sin(t * Math.PI * 0.35 * speed), 2);
                return ((byte)(f.Red * k), (byte)(f.Green * k), (byte)(f.Blue * k));
            }
            case LightbarMode.Music:
            {
                // La couleur glisse lentement, l'intensité suit le son
                var (r, g, b) = Hsv(t * 12 * speed % 360, 1, 1);
                double k = 0.06 + 0.94 * Math.Clamp(audioLevel * 1.6, 0, 1);
                return ((byte)(r * k), (byte)(g * k), (byte)(b * k));
            }
            case LightbarMode.Screen:
                return ScreenSampler.Latest;
            default:
                return (f.Red, f.Green, f.Blue);
        }
    }

    public static (byte, byte, byte) Hsv(double h, double s, double v)
    {
        double c = v * s, x = c * (1 - Math.Abs(h / 60 % 2 - 1)), m = v - c;
        var (r, g, b) = h switch
        {
            < 60 => (c, x, 0.0),
            < 120 => (x, c, 0.0),
            < 180 => (0.0, c, x),
            < 240 => (0.0, x, c),
            < 300 => (x, 0.0, c),
            _ => (c, 0.0, x),
        };
        return ((byte)((r + m) * 255), (byte)((g + m) * 255), (byte)((b + m) * 255));
    }
}

/// <summary>
/// « Ambilight » : couleur moyenne des bords de l'écran principal, échantillonnée ~12 fois par seconde
/// sur une image réduite (GDI). Les jeux en plein écran exclusif peuvent apparaître noirs : préférer le fenêtré sans bordure.
/// </summary>
public static class ScreenSampler
{
    private const int W = 48, H = 27;
    private static Timer? _timer;
    private static int _users;
    private static (double R, double G, double B) _smooth;

    public static (byte R, byte G, byte B) Latest { get; private set; }

    public static void Start()
    {
        if (Interlocked.Increment(ref _users) == 1)
            _timer = new Timer(_ => Sample(), null, 0, 80);
    }

    public static void Stop()
    {
        if (Interlocked.Decrement(ref _users) <= 0)
        {
            _users = 0;
            _timer?.Dispose();
            _timer = null;
        }
    }

    private static void Sample()
    {
        IntPtr screen = IntPtr.Zero, mem = IntPtr.Zero, bmp = IntPtr.Zero, old = IntPtr.Zero;
        try
        {
            int sw = GetSystemMetrics(0), sh = GetSystemMetrics(1);
            screen = GetDC(IntPtr.Zero);
            mem = CreateCompatibleDC(screen);
            bmp = CreateCompatibleBitmap(screen, W, H);
            old = SelectObject(mem, bmp);
            SetStretchBltMode(mem, 4); // HALFTONE : moyenne des pixels
            StretchBlt(mem, 0, 0, W, H, screen, 0, 0, sw, sh, 0x00CC0020);
            SelectObject(mem, old);

            var info = new BITMAPINFOHEADER { biSize = 40, biWidth = W, biHeight = -H, biPlanes = 1, biBitCount = 32 };
            var px = new byte[W * H * 4];
            GetDIBits(mem, bmp, 0, H, px, ref info, 0);

            // Bords (3 pixels réduits) : comme un ambilight
            double r = 0, g = 0, b = 0, n = 0;
            for (int y = 0; y < H; y++)
                for (int x = 0; x < W; x++)
                {
                    if (x > 2 && x < W - 3 && y > 2 && y < H - 3) continue;
                    int i = (y * W + x) * 4;
                    // Les pixels saturés comptent davantage que les gris
                    double max = Math.Max(px[i], Math.Max(px[i + 1], px[i + 2])), min = Math.Min(px[i], Math.Min(px[i + 1], px[i + 2]));
                    double w = 0.2 + (max - min) / 255.0 * 2;
                    b += px[i] * w; g += px[i + 1] * w; r += px[i + 2] * w; n += w;
                }
            if (n <= 0) return;
            (double R, double G, double B) target = (r / n, g / n, b / n);
            _smooth = (_smooth.R + (target.R - _smooth.R) * 0.35, _smooth.G + (target.G - _smooth.G) * 0.35, _smooth.B + (target.B - _smooth.B) * 0.35);
            Latest = Saturate(_smooth);
        }
        catch
        {
            // Écran inaccessible (verrouillage…) : on garde la dernière couleur
        }
        finally
        {
            if (bmp != IntPtr.Zero) DeleteObject(bmp);
            if (mem != IntPtr.Zero) DeleteDC(mem);
            if (screen != IntPtr.Zero) ReleaseDC(IntPtr.Zero, screen);
        }
    }

    /// <summary>Renforce la saturation : une barre lumineuse fade ressemble à du blanc.</summary>
    private static (byte, byte, byte) Saturate((double R, double G, double B) c)
    {
        double avg = (c.R + c.G + c.B) / 3;
        double k = 1.8;
        byte Clamp(double v) => (byte)Math.Clamp(avg + (v - avg) * k, 0, 255);
        return (Clamp(c.R), Clamp(c.G), Clamp(c.B));
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BITMAPINFOHEADER
    {
        public int biSize, biWidth, biHeight;
        public short biPlanes, biBitCount;
        public int biCompression, biSizeImage, biXPelsPerMeter, biYPelsPerMeter, biClrUsed, biClrImportant;
    }

    [DllImport("user32.dll")] private static extern int GetSystemMetrics(int index);
    [DllImport("user32.dll")] private static extern IntPtr GetDC(IntPtr hwnd);
    [DllImport("user32.dll")] private static extern int ReleaseDC(IntPtr hwnd, IntPtr dc);
    [DllImport("gdi32.dll")] private static extern IntPtr CreateCompatibleDC(IntPtr dc);
    [DllImport("gdi32.dll")] private static extern IntPtr CreateCompatibleBitmap(IntPtr dc, int w, int h);
    [DllImport("gdi32.dll")] private static extern IntPtr SelectObject(IntPtr dc, IntPtr obj);
    [DllImport("gdi32.dll")] private static extern int SetStretchBltMode(IntPtr dc, int mode);
    [DllImport("gdi32.dll")] private static extern bool StretchBlt(IntPtr dest, int x, int y, int w, int h, IntPtr src, int sx, int sy, int sw, int sh, uint rop);
    [DllImport("gdi32.dll")] private static extern int GetDIBits(IntPtr dc, IntPtr bmp, uint start, int lines, byte[] bits, ref BITMAPINFOHEADER info, uint usage);
    [DllImport("gdi32.dll")] private static extern bool DeleteObject(IntPtr obj);
    [DllImport("gdi32.dll")] private static extern bool DeleteDC(IntPtr dc);
}
