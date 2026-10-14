using System.Diagnostics;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Se7en.DualSense.App.Controls;
using Se7en.DualSense.App.Services;
using Se7en.DualSense.Hid;

namespace Se7en.DualSense.App.Pages;

public partial class HomePage : UserControl
{
    private readonly Stopwatch _rateClock = Stopwatch.StartNew();
    private readonly Stopwatch _activityClock = Stopwatch.StartNew();
    private long _rateCount;
    private string _lastLeft = "", _lastRight = "", _lastTouch = "";
    private int _lastL2 = -1, _lastR2 = -1;
    private DualSenseButtons _lastButtons;
    private bool _activityLit, _charging;

    /// <summary>Demande d'ouverture de la page Calibration.</summary>
    public event Action? CalibrateRequested;

    public HomePage()
    {
        InitializeComponent();
    }

    private void OnCalibrateClick(object sender, RoutedEventArgs e) => CalibrateRequested?.Invoke();

    public void OnDeviceChanged(DualSenseDeviceInfo? device)
    {
        bool connected = device != null;
        StatusBadge.Text = connected ? "Connectée" : "Déconnectée";
        StatusBadge.Tone = connected ? BadgeTone.Success : BadgeTone.Neutral;
        Battery.Visibility = connected ? Visibility.Visible : Visibility.Collapsed;
        HeroLinkIcon.Visibility = connected ? Visibility.Visible : Visibility.Collapsed;
        SideLink.SetResourceReference(ForegroundProperty, connected ? "Accent" : "Text.Tertiary");

        if (connected)
        {
            var icon = (Geometry)FindResource(device!.Connection == ConnectionType.Usb ? "Icon.Usb" : "Icon.Bluetooth");
            HeroLinkIcon.Data = SideLink.Data = LinkIcon.Data = icon;
            SideLink.ToolTip = $"Connectée en {device.ConnectionLabel}";
            LinkValue.Text = device.ConnectionLabel;
            HeroHint.Text = $"{device.DisplayName} connectée en {device.ConnectionLabel}. " +
                            "Appuyez sur un bouton, bougez les sticks ou touchez le pavé : la manette réagit en direct.";
        }
        else
        {
            SideLink.ToolTip = "Aucune manette";
            LinkValue.Text = "Aucune";
            RateValue.Text = "—";
            HeroHint.Text = "En attente de la manette. Branchez-la en USB ou connectez-la en Bluetooth : elle est détectée automatiquement.";
            SetCharging(false);
        }
    }

    public void Update(in FrameData frame)
    {
        Controller.Update(frame);

        var s = frame.State;
        if (!frame.IsConnected)
        {
            LeftStickViz.SetPosition(0, 0);
            RightStickViz.SetPosition(0, 0);
            L2Meter.SetValue(0);
            R2Meter.SetValue(0);
            TouchViz.SetTouches(default, default, false);
            SetActivity(false);
            return;
        }

        if (frame.HasNewReport)
        {
            UpdateStick(s.LeftX, s.LeftY, LeftStickViz, LeftStickReadout, LeftStickRaw, ref _lastLeft);
            UpdateStick(s.RightX, s.RightY, RightStickViz, RightStickReadout, RightStickRaw, ref _lastRight);

            L2Meter.SetValue(s.L2 / 255.0);
            R2Meter.SetValue(s.R2 / 255.0);
            int l2 = (int)Math.Round(s.L2 / 2.55), r2 = (int)Math.Round(s.R2 / 2.55);
            if (l2 != _lastL2) { L2Readout.Text = l2 + "%"; _lastL2 = l2; }
            if (r2 != _lastR2) { R2Readout.Text = r2 + "%"; _lastR2 = r2; }

            TouchViz.SetTouches(s.Touch0, s.Touch1, s.IsPressed(DualSenseButtons.Touchpad));
            string touch = (s.Touch0.Active, s.Touch1.Active) switch
            {
                (false, false) => "Aucun contact",
                (true, false) => $"x {s.Touch0.X,4}  y {s.Touch0.Y,4}",
                (false, true) => $"x {s.Touch1.X,4}  y {s.Touch1.Y,4}",
                _ => "2 doigts",
            };
            if (touch != _lastTouch) { TouchReadout.Text = touch; _lastTouch = touch; }

            if (s.IsFullReport)
            {
                bool charging = s.Charging == ChargingState.Charging;
                Battery.SetLevel(s.BatteryPercent, charging);
                SetCharging(charging);
            }

            // L'icône « activité » s'allume brièvement à chaque action de l'utilisateur
            if (s.Buttons != _lastButtons || s.Touch0.Active || l2 > 2 || r2 > 2)
                _activityClock.Restart();
            _lastButtons = s.Buttons;
        }
        SetActivity(_activityClock.ElapsedMilliseconds < 400);

        if (_rateClock.ElapsedMilliseconds >= 1000)
        {
            long count = App.Controller.Connection.ReportCount;
            double hz = (count - _rateCount) * 1000.0 / _rateClock.ElapsedMilliseconds;
            _rateCount = count;
            _rateClock.Restart();
            RateValue.Text = hz.ToString("0", CultureInfo.InvariantCulture) + " Hz";
        }
    }

    private void SetCharging(bool charging)
    {
        if (charging == _charging) return;
        _charging = charging;
        SidePower.SetResourceReference(ForegroundProperty, charging ? "Success" : "Text.Tertiary");
        SidePower.ToolTip = charging ? "En charge" : "Sur batterie";
    }

    private void SetActivity(bool lit)
    {
        if (lit == _activityLit) return;
        _activityLit = lit;
        SideActivity.SetResourceReference(ForegroundProperty, lit ? "Accent" : "Text.Tertiary");
    }

    private static void UpdateStick(byte rawX, byte rawY, StickVisualizer viz, TextBlock readout, TextBlock raw, ref string last)
    {
        double x = Math.Clamp((rawX - 127.5) / 127.5, -1, 1);
        double y = Math.Clamp((rawY - 127.5) / 127.5, -1, 1);
        viz.SetPosition(x, y);

        string text = $"X {Fmt(x)}  Y {Fmt(-y)}";
        if (text == last) return;
        last = text;
        readout.Text = text;
        raw.Text = $"brut {rawX} · {rawY}";
    }

    private static string Fmt(double v) =>
        (v >= 0 ? "+" : "−") + Math.Abs(v).ToString("0.000", CultureInfo.InvariantCulture);
}
