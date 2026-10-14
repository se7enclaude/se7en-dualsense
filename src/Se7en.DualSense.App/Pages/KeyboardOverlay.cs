using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using Se7en.DualSense.App.Controls;
using Se7en.DualSense.App.Services;
using Se7en.DualSense.Hid;
using Se7en.DualSense.Output;

namespace Se7en.DualSense.App.Pages;

/// <summary>
/// Clavier virtuel (AZERTY) piloté à la manette, par-dessus toutes les fenêtres sans prendre le focus :
/// les caractères arrivent dans l'application active. Croix / stick : choisir · ✕ : taper · □ : effacer ·
/// △ : espace · Options : Entrée · L1 : majuscules · ○ ou L3 : fermer.
/// </summary>
public sealed class KeyboardOverlay : Window
{
    private static KeyboardOverlay? _open;

    private static readonly string[][] Rows =
    {
        new[] { "1", "2", "3", "4", "5", "6", "7", "8", "9", "0" },
        new[] { "a", "z", "e", "r", "t", "y", "u", "i", "o", "p" },
        new[] { "q", "s", "d", "f", "g", "h", "j", "k", "l", "m" },
        new[] { "w", "x", "c", "v", "b", "n", ",", ".", "'", "@" },
        new[] { "MAJ", "ESPACE", "⌫", "ENTRÉE", "FERMER" },
    };

    private readonly List<List<Border>> _keys = new();
    private readonly PadNavigator _nav = new();
    private int _row = 1, _col;
    private bool _shift;

    public static void Toggle()
    {
        if (_open != null) { _open.Close(); return; }
        _open = new KeyboardOverlay();
        _open.Show();
    }

    private KeyboardOverlay()
    {
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        Topmost = true;
        ShowInTaskbar = false;
        ShowActivated = false;
        ResizeMode = ResizeMode.NoResize;
        SizeToContent = SizeToContent.WidthAndHeight;
        SetResourceReference(FontFamilyProperty, "Font.Sans");

        var panel = new StackPanel();
        foreach (var row in Rows)
        {
            var line = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center };
            var keys = new List<Border>();
            foreach (var k in row)
            {
                double width = k switch { "ESPACE" => 250, "ENTRÉE" => 110, "MAJ" or "FERMER" => 96, "⌫" => 70, _ => 54 };
                var key = new Border
                {
                    Width = width, Height = 54, Margin = new Thickness(4), CornerRadius = new CornerRadius(10),
                    Background = new SolidColorBrush(Color.FromArgb(0x30, 0xFF, 0xFF, 0xFF)),
                    Child = new TextBlock { Text = k, Foreground = Brushes.White, FontSize = k.Length > 1 ? 13 : 20, FontWeight = FontWeights.SemiBold, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center },
                    Tag = k,
                };
                keys.Add(key);
                line.Children.Add(key);
            }
            _keys.Add(keys);
            panel.Children.Add(line);
        }
        var hint = new TextBlock
        {
            Text = "✕ taper   □ effacer   △ espace   OPTIONS entrée   L1 majuscules   ○ fermer",
            Foreground = new SolidColorBrush(Color.FromArgb(0xB0, 0xFF, 0xFF, 0xFF)), FontSize = 12, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 10, 0, 0),
        };
        panel.Children.Add(hint);
        Content = new Border
        {
            CornerRadius = new CornerRadius(22), Padding = new Thickness(18), Child = panel,
            Background = new SolidColorBrush(Color.FromArgb(0xEE, 0x11, 0x11, 0x14)),
            Effect = new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 40, ShadowDepth = 10, Opacity = 0.5 },
        };

        SourceInitialized += (_, _) =>
        {
            // Ne jamais prendre le focus : les touches vont à l'application en cours
            var hwnd = new WindowInteropHelper(this).Handle;
            int style = GetWindowLong(hwnd, -20);
            SetWindowLong(hwnd, -20, style | 0x08000000 | 0x80);
        };
        Loaded += (_, _) =>
        {
            var area = SystemParameters.WorkArea;
            Left = area.Left + (area.Width - ActualWidth) / 2;
            Top = area.Bottom - ActualHeight - 30;
            Highlight();
        };
        App.Controller.Frame += OnFrame;
        App.Controller.SetActionsSuppressed(true);
        App.SuspendControllerNavigation = true;
        Closed += (_, _) =>
        {
            App.Controller.Frame -= OnFrame;
            App.Controller.SetActionsSuppressed(false);
            App.SuspendControllerNavigation = false;
            _open = null;
        };
        SoundService.Play(UiSound.ToggleOn);
    }

    private void OnFrame(FrameData frame)
    {
        _nav.Update(frame);
        if (_nav.Move != (0, 0))
        {
            int row = Math.Clamp(_row + _nav.Move.Y, 0, _keys.Count - 1);
            if (row != _row)
            {
                // Garde à peu près la même position horizontale d'une rangée à l'autre
                double ratio = (_col + 0.5) / _keys[_row].Count;
                _row = row;
                _col = Math.Clamp((int)(ratio * _keys[_row].Count), 0, _keys[_row].Count - 1);
            }
            _col = (_col + _nav.Move.X + _keys[_row].Count) % _keys[_row].Count;
            Highlight();
        }
        if (_nav.Was(DualSenseButtons.Cross)) Press((string)_keys[_row][_col].Tag);
        if (_nav.Was(DualSenseButtons.Square)) KeyboardMouseEmitter.Tap(0x08);
        if (_nav.Was(DualSenseButtons.Triangle)) KeyboardMouseEmitter.TypeChar(' ');
        if (_nav.Was(DualSenseButtons.Options)) KeyboardMouseEmitter.Tap(0x0D);
        if (_nav.Was(DualSenseButtons.L1)) { _shift = !_shift; Relabel(); }
        if (_nav.Was(DualSenseButtons.Circle) || _nav.Was(DualSenseButtons.L3)) Close();
    }

    private void Press(string key)
    {
        switch (key)
        {
            case "MAJ": _shift = !_shift; Relabel(); break;
            case "ESPACE": KeyboardMouseEmitter.TypeChar(' '); break;
            case "⌫": KeyboardMouseEmitter.Tap(0x08); break;
            case "ENTRÉE": KeyboardMouseEmitter.Tap(0x0D); break;
            case "FERMER": Close(); break;
            default:
                foreach (char c in _shift ? key.ToUpperInvariant() : key)
                    KeyboardMouseEmitter.TypeChar(c);
                if (_shift) { _shift = false; Relabel(); }
                break;
        }
        SoundService.Play(UiSound.Click);
    }

    private void Relabel()
    {
        foreach (var row in _keys)
            foreach (var k in row)
                if (k.Child is TextBlock t && ((string)k.Tag).Length == 1)
                    t.Text = _shift ? ((string)k.Tag).ToUpperInvariant() : (string)k.Tag;
        Highlight();
    }

    private void Highlight()
    {
        var accent = TryFindResource("Color.Accent") is Color c ? c : Color.FromRgb(0x5B, 0x2D, 0xE0);
        for (int r = 0; r < _keys.Count; r++)
            for (int i = 0; i < _keys[r].Count; i++)
            {
                bool sel = r == _row && i == _col;
                bool shiftKey = (string)_keys[r][i].Tag == "MAJ" && _shift;
                _keys[r][i].Background = new SolidColorBrush(sel ? accent : shiftKey ? Color.FromArgb(0x70, 0xFF, 0xFF, 0xFF) : Color.FromArgb(0x30, 0xFF, 0xFF, 0xFF));
            }
    }

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern int GetWindowLong(IntPtr hWnd, int nIndex);

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);
}
