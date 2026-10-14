using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Media;
using Se7en.DualSense.Output;
using Forms = System.Windows.Forms;

namespace Se7en.DualSense.App.Services;

/// <summary>
/// Démarrage avec Windows via une tâche planifiée « à l'ouverture de session, privilèges les plus élevés » :
/// l'application démarre directement en administrateur (HidHide) sans invite de contrôle de compte.
/// </summary>
public static class StartupService
{
    private const string TaskName = "se7en DualSense";

    public static bool Enable(bool enable, out string error, string? exePath = null)
    {
        error = "";
        string? exe = exePath ?? Environment.ProcessPath;
        if (exe == null) { error = "Chemin de l'application introuvable."; return false; }

        var args = enable
            ? $"/Create /F /TN \"{TaskName}\" /SC ONLOGON /RL HIGHEST /TR \"\\\"{exe}\\\" --minimized --skip-intro\""
            : $"/Delete /F /TN \"{TaskName}\"";
        try
        {
            var psi = new ProcessStartInfo("schtasks.exe", args)
            {
                UseShellExecute = false, CreateNoWindow = true, RedirectStandardError = true, RedirectStandardOutput = true,
            };
            using var p = Process.Start(psi)!;
            string err = p.StandardError.ReadToEnd();
            p.WaitForExit(10000);
            if (p.ExitCode == 0 || (!enable && err.Contains("n'existe pas", StringComparison.OrdinalIgnoreCase)))
                return true;
            error = OutputManager.IsElevated ? err.Trim() : "Droits administrateur requis pour créer la tâche de démarrage.";
            return false;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
        }
    }
}

/// <summary>Icône de la zone de notification : batterie au survol, menu (ouvrir, mode, profils, quitter).</summary>
public sealed class TrayService : IDisposable
{
    private readonly Forms.NotifyIcon _icon;
    private readonly Action _open;
    private readonly Action _quit;

    public TrayService(Action open, Action quit)
    {
        _open = open;
        _quit = quit;
        using var stream = Application.GetResourceStream(new Uri("pack://application:,,,/Assets/app.ico"))!.Stream;
        _icon = new Forms.NotifyIcon
        {
            Icon = new System.Drawing.Icon(stream),
            Text = "se7en DualSense",
            Visible = true,
            ContextMenuStrip = new Forms.ContextMenuStrip(),
        };
        _icon.DoubleClick += (_, _) => _open();
        _icon.ContextMenuStrip.Opening += (_, _) => BuildMenu();
        BuildMenu();
    }

    private void BuildMenu()
    {
        var menu = _icon.ContextMenuStrip!;
        menu.Items.Clear();
        var c = App.Controller;
        var slots = c.Slots;
        foreach (var slot in slots)
        {
            var device = slot.Device;
            var state = slot.Connection.Latest;
            string prefix = slots.Count > 1 ? slot.Label + " · " : "";
            string status = device == null ? prefix + "Aucune manette" :
                prefix + $"{device.DisplayName} · {device.ConnectionLabel}" + (state.IsFullReport ? $" · {state.BatteryPercent} %" : "");
            menu.Items.Add(new Forms.ToolStripMenuItem(status) { Enabled = false });
        }
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add("Ouvrir se7en DualSense", null, (_, _) => _open());

        var modes = new Forms.ToolStripMenuItem("Les jeux voient");
        foreach (var mode in new[] { OutputMode.DualShock4, OutputMode.Xbox360, OutputMode.Native, OutputMode.KeyboardMouse })
        {
            var m = mode;
            modes.DropDownItems.Add(new Forms.ToolStripMenuItem(m.Label(), null, (_, _) => c.SetOutputMode(m)) { Checked = c.ActiveProfile.Mode == m });
        }
        menu.Items.Add(modes);

        var profiles = new Forms.ToolStripMenuItem("Profil");
        foreach (var p in c.Profiles.All)
        {
            var profile = p;
            profiles.DropDownItems.Add(new Forms.ToolStripMenuItem(p.Name, null, (_, _) => c.SelectProfile(profile)) { Checked = c.ActiveProfile.Id == p.Id });
        }
        menu.Items.Add(profiles);
        menu.Items.Add(new Forms.ToolStripMenuItem("Mode bureau (clavier / souris)", null, (_, _) => c.ToggleDesktopMode())
        {
            Checked = c.ActiveProfile.Id == Profile.DesktopId,
        });
        if (slots.Any(s => s.Device?.Connection == Se7en.DualSense.Hid.ConnectionType.Bluetooth))
        {
            var off = new Forms.ToolStripMenuItem("Éteindre la manette");
            foreach (var slot in slots.Where(s => s.Device?.Connection == Se7en.DualSense.Hid.ConnectionType.Bluetooth))
            {
                var target = slot;
                off.DropDownItems.Add(slots.Count > 1 ? slot.Label : "DualSense", null, (_, _) => c.PowerOff(target));
            }
            if (off.DropDownItems.Count == 1)
            {
                var single = off.DropDownItems[0];
                off = new Forms.ToolStripMenuItem("Éteindre la manette", null, (_, _) => single.PerformClick());
            }
            menu.Items.Add(off);
        }
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add("Quitter", null, (_, _) => _quit());
    }

    /// <summary>Notification Windows (bulle), visible même quand la fenêtre est masquée.</summary>
    public void ShowBalloon(string title, string text, bool warning)
    {
        _icon.BalloonTipTitle = title;
        _icon.BalloonTipText = text;
        _icon.BalloonTipIcon = warning ? Forms.ToolTipIcon.Warning : Forms.ToolTipIcon.Info;
        _icon.ShowBalloonTip(5000);
    }

    /// <summary>Infobulle de l'icône (63 caractères max).</summary>
    public void SetTooltip(string text) => _icon.Text = text.Length > 63 ? text[..63] : text;

    public void Dispose()
    {
        _icon.Visible = false;
        _icon.Dispose();
    }
}

/// <summary>
/// Overlay discret en jeu : petite capsule en bas à droite de l'écran (batterie, profil, mode).
/// Toujours au premier plan, transparente aux clics. Visible par-dessus les jeux en fenêtré sans bordure.
/// </summary>
public sealed class OverlayWindow : Window
{
    private readonly System.Windows.Controls.TextBlock _text = new()
    {
        FontSize = 12,
        Foreground = Brushes.White,
        VerticalAlignment = VerticalAlignment.Center,
    };

    public OverlayWindow()
    {
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        Topmost = true;
        ShowInTaskbar = false;
        ShowActivated = false;
        ResizeMode = ResizeMode.NoResize;
        SizeToContent = SizeToContent.WidthAndHeight;
        _text.SetResourceReference(System.Windows.Controls.TextBlock.FontFamilyProperty, "Font.Sans");

        var dot = new System.Windows.Shapes.Ellipse { Width = 8, Height = 8, Margin = new Thickness(0, 0, 10, 0), Fill = new SolidColorBrush(Color.FromRgb(0xD4, 0xF5, 0x3C)) };
        var row = new System.Windows.Controls.StackPanel { Orientation = System.Windows.Controls.Orientation.Horizontal };
        row.Children.Add(dot);
        row.Children.Add(_text);
        Content = new System.Windows.Controls.Border
        {
            CornerRadius = new CornerRadius(16),
            Background = new SolidColorBrush(Color.FromArgb(0xD8, 0x11, 0x11, 0x14)),
            Padding = new Thickness(14, 8, 16, 8),
            Child = row,
        };
        SourceInitialized += (_, _) => MakeClickThrough();
        Loaded += (_, _) => PlaceBottomRight();
    }

    public void SetText(string text) => _text.Text = text;

    private void PlaceBottomRight()
    {
        var area = SystemParameters.WorkArea;
        Left = area.Right - ActualWidth - 24;
        Top = area.Bottom - ActualHeight - 24;
    }

    private void MakeClickThrough()
    {
        var hwnd = new System.Windows.Interop.WindowInteropHelper(this).Handle;
        const int GWL_EXSTYLE = -20, WS_EX_TRANSPARENT = 0x20, WS_EX_TOOLWINDOW = 0x80, WS_EX_NOACTIVATE = 0x08000000;
        int style = NativeMethods.GetWindowLong(hwnd, GWL_EXSTYLE);
        NativeMethods.SetWindowLong(hwnd, GWL_EXSTYLE, style | WS_EX_TRANSPARENT | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE);
    }

    private static class NativeMethods
    {
        [System.Runtime.InteropServices.DllImport("user32.dll")]
        public static extern int GetWindowLong(IntPtr hWnd, int nIndex);

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        public static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);
    }
}
