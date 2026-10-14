using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using Se7en.DualSense.Output;

namespace Se7en.DualSense.App.Services;

/// <summary>
/// Suit la fenêtre au premier plan pour repérer un retour dans le jeu (Alt + Tab, clic sur la barre des tâches).
/// Un jeu = un exe associé à un profil, un exe installé par Steam / Epic / Rockstar, ou une fenêtre plein écran
/// (hors navigateurs et lecteurs vidéo). Doit être créé sur le thread de l'interface (boucle de messages).
/// </summary>
public sealed class GameFocusWatcher : IDisposable
{
    private const uint EventSystemForeground = 0x0003;
    private const uint WinEventOutOfContext = 0x0000;
    private static readonly TimeSpan MinAway = TimeSpan.FromMilliseconds(400);

    private static readonly HashSet<string> NotGames = new(StringComparer.OrdinalIgnoreCase)
    {
        "explorer", "chrome", "msedge", "firefox", "opera", "brave", "vivaldi", "vlc", "mpc-hc64", "mpc-be64",
        "PotPlayerMini64", "Video.UI", "Microsoft.Media.Player", "ApplicationFrameHost", "steamwebhelper",
        "Discord", "obs64", "AfterFX", "Photoshop", "Premiere Pro",
    };

    private readonly ProfileStore _profiles;
    private readonly WinEventDelegate _callback;
    private readonly IntPtr _hook;
    private readonly int _ownPid = Environment.ProcessId;
    private List<string>? _gameDirs;
    private int _gamePid;
    private string _gameName = "";
    private DateTime? _leftAt;

    /// <summary>Le jeu revient au premier plan après en être sorti (nom de l'exe).</summary>
    public event Action<string>? GameRefocused;

    public GameFocusWatcher(ProfileStore profiles)
    {
        _profiles = profiles;
        _callback = OnForeground;
        _hook = SetWinEventHook(EventSystemForeground, EventSystemForeground, IntPtr.Zero, _callback, 0, 0, WinEventOutOfContext);
        // Dossiers des jeux installés : lus une fois, hors du thread de l'interface
        Task.Run(() =>
        {
            try { _gameDirs = GameLibrary.Detect().Select(g => g.InstallDir.TrimEnd('\\') + "\\").ToList(); }
            catch { _gameDirs = new(); }
        });
    }

    private void OnForeground(IntPtr hook, uint evt, IntPtr hwnd, int idObject, int idChild, uint thread, uint time)
    {
        try
        {
            if (hwnd == IntPtr.Zero) return;
            GetWindowThreadProcessId(hwnd, out int pid);
            if (pid == 0) return;

            if (pid == _gamePid)
            {
                if (_leftAt is { } left && DateTime.UtcNow - left >= MinAway)
                    GameRefocused?.Invoke(_gameName);
                _leftAt = null;
                return;
            }

            // Fenêtre de se7en DualSense elle-même : on est sorti du jeu, sans en changer
            if (pid != _ownPid && IsGame(hwnd, pid, out var name))
            {
                _gamePid = pid;
                _gameName = name;
                _leftAt = null;
                return;
            }

            if (_gamePid != 0 && _leftAt == null)
                _leftAt = DateTime.UtcNow;
        }
        catch
        {
            // Fenêtre fermée entre-temps
        }
    }

    private bool IsGame(IntPtr hwnd, int pid, out string name)
    {
        string? path = ProcessPath(pid);
        name = path != null ? Path.GetFileName(path) : "";
        if (path == null) return false;
        string bare = Path.GetFileNameWithoutExtension(path);
        if (NotGames.Contains(bare)) return false;

        string exe = name;
        foreach (var p in _profiles.All)
            if (p.Executables.Any(e => string.Equals(Path.GetFileName(e), exe, StringComparison.OrdinalIgnoreCase)))
                return true;
        if (_gameDirs is { } dirs && dirs.Any(d => path.StartsWith(d, StringComparison.OrdinalIgnoreCase)))
            return true;
        return IsFullscreen(hwnd);
    }

    private static bool IsFullscreen(IntPtr hwnd)
    {
        if (!GetWindowRect(hwnd, out var r)) return false;
        var monitor = MonitorFromWindow(hwnd, 2 /* MONITOR_DEFAULTTONEAREST */);
        var info = new MONITORINFO { cbSize = Marshal.SizeOf<MONITORINFO>() };
        if (!GetMonitorInfo(monitor, ref info)) return false;
        var m = info.rcMonitor;
        return r.Left <= m.Left && r.Top <= m.Top && r.Right >= m.Right && r.Bottom >= m.Bottom;
    }

    private static string? ProcessPath(int pid)
    {
        IntPtr h = OpenProcess(0x1000 /* PROCESS_QUERY_LIMITED_INFORMATION */, false, pid);
        if (h == IntPtr.Zero) return null;
        try
        {
            var sb = new StringBuilder(1024);
            int size = sb.Capacity;
            return QueryFullProcessImageName(h, 0, sb, ref size) ? sb.ToString() : null;
        }
        finally
        {
            CloseHandle(h);
        }
    }

    public void Dispose()
    {
        if (_hook != IntPtr.Zero) UnhookWinEvent(_hook);
    }

    private delegate void WinEventDelegate(IntPtr hook, uint evt, IntPtr hwnd, int idObject, int idChild, uint thread, uint time);

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT { public int Left, Top, Right, Bottom; }

    [StructLayout(LayoutKind.Sequential)]
    private struct MONITORINFO { public int cbSize; public RECT rcMonitor, rcWork; public uint dwFlags; }

    [DllImport("user32.dll")] private static extern IntPtr SetWinEventHook(uint min, uint max, IntPtr module, WinEventDelegate callback, uint pid, uint thread, uint flags);
    [DllImport("user32.dll")] private static extern bool UnhookWinEvent(IntPtr hook);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr hwnd, out int pid);
    [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr hwnd, out RECT rect);
    [DllImport("user32.dll")] private static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint flags);
    [DllImport("user32.dll")] private static extern bool GetMonitorInfo(IntPtr monitor, ref MONITORINFO info);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern IntPtr OpenProcess(uint access, bool inherit, int pid);
    [DllImport("kernel32.dll")] private static extern bool CloseHandle(IntPtr handle);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] private static extern bool QueryFullProcessImageName(IntPtr process, int flags, StringBuilder name, ref int size);
}
