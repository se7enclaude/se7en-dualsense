using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Se7en.DualSense.App.Controls;
using Se7en.DualSense.App.Services;
using Se7en.DualSense.Hid;
using Se7en.DualSense.Output;
using Se7en.DualSense.Processing;

namespace Se7en.DualSense.App.Pages;

/// <summary>Onglet Diagnostic : informations matérielles, fréquence, test de circularité, course des gâchettes, rapport brut.</summary>
public sealed class DiagnosticPage : UserControl, ILivePage
{
    private readonly Dictionary<string, TextBlock> _info = new();
    private readonly CircularityPlot _left = new() { Width = 180, Height = 180 }, _right = new() { Width = 180, Height = 180 };
    private readonly TextBlock _leftScore, _rightScore, _triggers, _raw;
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private long _count;
    private byte _maxL2, _maxR2;
    private int _frame;

    public DiagnosticPage()
    {
        var root = new StackPanel();
        root.Children.Add(new PageHeader
        {
            Overline = "SYSTÈME",
            Title = "Diagnostic",
            Subtitle = "Testez l'état de votre manette : usure des sticks, course des gâchettes, fréquence de communication.",
        });

        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(16) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.3, GridUnitType.Star) });

        // ── Informations ──
        var info = new StackPanel();
        info.Children.Add(Ui.Text("MANETTE", "Text.Overline", new Thickness(0, 0, 0, 8)));
        foreach (var key in new[] { "Modèle", "Connexion", "Numéro de série", "Fréquence", "Intervalle", "Batterie", "Sortie", "ViGEmBus", "HidHide", "Administrateur" })
        {
            var value = Ui.Text("—", "Text.Readout");
            _info[key] = value;
            info.Children.Add(new SettingRow { Label = key, Control = value });
        }
        grid.Children.Add(Ui.Card(info, null, 20));

        // ── Circularité ──
        var circ = new StackPanel();
        var head = new DockPanel { Margin = new Thickness(0, 0, 0, 6) };
        var reset = Ui.Button("Recommencer", "Button.Secondary", (_, _) => { _left.Reset(); _right.Reset(); _maxL2 = _maxR2 = 0; });
        DockPanel.SetDock(reset, Dock.Right);
        head.Children.Add(reset);
        head.Children.Add(Ui.Text("TEST DE CIRCULARITÉ", "Text.Overline"));
        circ.Children.Add(head);
        circ.Children.Add(Ui.Text("Faites tourner chaque stick à fond, plusieurs tours. Un stick sain trace un cercle régulier ; un écart élevé révèle de l'usure.",
            "Text.Caption", new Thickness(0, 0, 0, 16)));
        var plots = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center };
        _leftScore = Ui.Text("—", "Text.Readout");
        _rightScore = Ui.Text("—", "Text.Readout");
        plots.Children.Add(Plot(_left, "STICK GAUCHE", _leftScore));
        plots.Children.Add(Plot(_right, "STICK DROIT", _rightScore));
        circ.Children.Add(plots);
        _triggers = Ui.Text("Course maximale · L2 0 % · R2 0 %", "Text.Readout", new Thickness(0, 18, 0, 0));
        _triggers.HorizontalAlignment = HorizontalAlignment.Center;
        circ.Children.Add(_triggers);
        var circCard = Ui.Card(circ, null, 20);
        Grid.SetColumn(circCard, 2);
        grid.Children.Add(circCard);
        root.Children.Add(grid);

        // ── Historique de l'usure ──
        var history = new StackPanel();
        var hhead = new DockPanel { Margin = new Thickness(0, 0, 0, 6) };
        var legend = new StackPanel { Orientation = Orientation.Horizontal };
        legend.Children.Add(LegendDot("Accent", "Stick gauche"));
        legend.Children.Add(LegendDot("Ink", "Stick droit"));
        legend.Children.Add(LegendDot("Danger", "Zone morte par défaut", dashed: true));
        DockPanel.SetDock(legend, Dock.Right);
        hhead.Children.Add(legend);
        hhead.Children.Add(Ui.Text("HISTORIQUE DU DRIFT", "Text.Overline"));
        history.Children.Add(hhead);
        _historyCaption = Ui.Text("", "Text.Caption", new Thickness(0, 0, 0, 14));
        history.Children.Add(_historyCaption);
        history.Children.Add(_history);
        root.Children.Add(Ui.Card(history, new Thickness(0, 16, 0, 0), 20));
        Loaded += (_, _) => RefreshHistory();

        // ── Batterie ──
        var battery = new StackPanel();
        var bhead = new DockPanel { Margin = new Thickness(0, 0, 0, 6) };
        _batteryLeft = Ui.Text("—", "Text.Readout");
        DockPanel.SetDock(_batteryLeft, Dock.Right);
        bhead.Children.Add(_batteryLeft);
        bhead.Children.Add(Ui.Text("BATTERIE · 24 H", "Text.Overline"));
        battery.Children.Add(bhead);
        battery.Children.Add(Ui.Text("Charge de la manette au fil du temps (vert : en charge). L'autonomie restante est estimée d'après la décharge en cours.",
            "Text.Caption", new Thickness(0, 0, 0, 14)));
        battery.Children.Add(_battery);
        root.Children.Add(Ui.Card(battery, new Thickness(0, 16, 0, 0), 20));

        // ── Rapport brut ──
        var raw = new StackPanel();
        raw.Children.Add(Ui.Text("RAPPORT BRUT", "Text.Overline", new Thickness(0, 0, 0, 10)));
        _raw = Ui.Text("", "Text.Readout");
        _raw.FontSize = 11;
        _raw.LineHeight = 17;
        raw.Children.Add(_raw);
        root.Children.Add(Ui.Card(raw, new Thickness(0, 16, 0, 0), 20));

        Content = root;
    }

    private readonly DriftHistoryChart _history = new() { Height = 200 };
    private TextBlock _historyCaption = null!;

    private readonly BatteryChart _battery = new() { Height = 150 };
    private TextBlock _batteryLeft = null!;

    private void RefreshHistory()
    {
        _battery.SetData(BatteryTracker.Get(App.Controller.Device?.Identity));
        var left = App.Controller.BatteryRemaining();
        _batteryLeft.Text = left is { } t ? "autonomie ≈ " + BatteryTracker.Format(t) : "autonomie : mesure en cours";
        var days = DriftHistory.Get(App.Controller.Device?.Identity);
        _history.SetData(days);
        _historyCaption.Text = days.Count == 0
            ? "Décalage moyen de chaque stick au repos, jour après jour (valeurs brutes, avant calibration). Les mesures se font toutes seules quand la manette est posée : revenez dans quelques jours."
            : $"Décalage moyen au repos, avant calibration · {days.Count} jour(s) suivis depuis le {days[0].Date:dd/MM/yyyy}. " +
              "Une courbe qui monte révèle une usure ; au-dessus de la ligne rouge, calibrez ou augmentez la zone morte.";
    }

    private static StackPanel LegendDot(string brush, string label, bool dashed = false)
    {
        var sp = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(16, 0, 0, 0) };
        var line = new System.Windows.Shapes.Rectangle { Width = 14, Height = dashed ? 1.5 : 3, RadiusX = 1.5, RadiusY = 1.5, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 0) };
        line.SetResourceReference(System.Windows.Shapes.Shape.FillProperty, brush);
        sp.Children.Add(line);
        sp.Children.Add(Ui.Text(label, "Text.Caption"));
        return sp;
    }

    private static StackPanel Plot(CircularityPlot plot, string label, TextBlock score)
    {
        var sp = new StackPanel { Margin = new Thickness(16, 0, 16, 0) };
        sp.Children.Add(plot);
        var t = Ui.Text(label, "Text.Overline", new Thickness(0, 10, 0, 4));
        t.HorizontalAlignment = HorizontalAlignment.Center;
        sp.Children.Add(t);
        score.HorizontalAlignment = HorizontalAlignment.Center;
        sp.Children.Add(score);
        return sp;
    }

    public void Update(in FrameData frame)
    {
        var s = frame.State;
        var d = frame.Device;
        if (d != null)
        {
            _left.Add(StickMath.Normalize(s.LeftX), StickMath.Normalize(s.LeftY));
            _right.Add(StickMath.Normalize(s.RightX), StickMath.Normalize(s.RightY));
            _maxL2 = Math.Max(_maxL2, s.L2);
            _maxR2 = Math.Max(_maxR2, s.R2);
        }

        if (++_frame % 15 != 0) return; // 4 rafraîchissements par seconde suffisent pour le texte

        _leftScore.Text = _left.Score;
        _rightScore.Text = _right.Score;
        _triggers.Text = $"Course maximale · L2 {_maxL2 * 100 / 255} % · R2 {_maxR2 * 100 / 255} %";

        double elapsed = _clock.Elapsed.TotalSeconds;
        long count = App.Controller.Connection.ReportCount;
        double hz = elapsed > 0 ? (count - _count) / elapsed : 0;
        if (elapsed > 1) { _count = count; _clock.Restart(); }

        Set("Modèle", d?.DisplayName ?? "Aucune manette");
        Set("Connexion", d?.ConnectionLabel ?? "—");
        Set("Numéro de série", string.IsNullOrEmpty(d?.SerialNumber) ? "—" : d!.SerialNumber!);
        if (elapsed > 1)
        {
            Set("Fréquence", d == null ? "—" : hz.ToString("0", CultureInfo.InvariantCulture) + " Hz");
            Set("Intervalle", d == null || hz < 1 ? "—" : (1000 / hz).ToString("0.0", CultureInfo.InvariantCulture) + " ms");
        }
        Set("Batterie", d == null || !s.IsFullReport ? "—" : $"{s.BatteryPercent} %" + (s.Charging == ChargingState.Charging ? " · en charge" : s.Charging == ChargingState.Full ? " · chargée" : ""));
        var status = App.Controller.Output.Status;
        Set("Sortie", status.Mode.Label() + (status.DeviceHidden ? " · masquée" : ""));
        Set("ViGEmBus", _drivers.ViGEmBusInstalled ? "installé" : "absent");
        Set("HidHide", _drivers.HidHideInstalled ? "installé" : "absent");
        Set("Administrateur", OutputManager.IsElevated ? "oui" : "non");

        var bytes = ControllerService.IsDemo ? Array.Empty<byte>() : App.Controller.Connection.CopyLastReport();
        var sb = new StringBuilder();
        for (int i = 0; i < bytes.Length; i++)
        {
            if (i % 16 == 0) sb.Append(i == 0 ? "" : "\n").Append(i.ToString("D2")).Append("  ");
            sb.Append(bytes[i].ToString("X2")).Append(' ');
        }
        _raw.Text = bytes.Length == 0 ? "Aucun rapport (manette absente ou mode démo)." : sb.ToString();
    }

    private readonly DriverStatus _drivers = DriverStatus.Detect();

    private void Set(string key, string value)
    {
        if (_info[key].Text != value) _info[key].Text = value;
    }

    /// <summary>Trace l'enveloppe atteinte par le stick (72 secteurs) et mesure l'écart à un cercle parfait.</summary>
    private sealed class CircularityPlot : LiveVisual
    {
        private readonly double[] _max = new double[72];
        private int _dirty;

        public void Reset()
        {
            Array.Clear(_max);
            InvalidateVisual();
        }

        public void Add(double x, double y)
        {
            double r = Math.Sqrt(x * x + y * y);
            if (r < 0.3) return;
            int bin = (int)((Math.Atan2(y, x) + Math.PI) / (2 * Math.PI) * 72) % 72;
            if (r > _max[bin])
            {
                _max[bin] = Math.Min(r, 1.5);
                if (++_dirty % 4 == 0) InvalidateVisual();
            }
        }

        /// <summary>Écart moyen au cercle de rayon 1 (en %), une fois assez de secteurs couverts.</summary>
        public string Score
        {
            get
            {
                var covered = _max.Where(v => v > 0).ToArray();
                if (covered.Length < 60) return $"couverture {covered.Length * 100 / 72} %";
                double err = covered.Average(v => Math.Abs(v - 1)) * 100;
                string verdict = err < 6 ? "excellent" : err < 12 ? "correct" : "usure probable";
                return $"écart {err:0.0} % · {verdict}";
            }
        }

        protected override void OnRender(DrawingContext dc)
        {
            double size = Math.Min(ActualWidth, ActualHeight);
            if (size <= 0) return;
            var c = new Point(ActualWidth / 2, ActualHeight / 2);
            double r = size / 2 - 6;
            dc.DrawEllipse(Res("Surface.Inset"), null, c, r + 4, r + 4);
            var dashed = new Pen(Res("Border.Dashed"), 1) { DashStyle = new DashStyle(new double[] { 3, 3 }, 0) };
            dc.DrawEllipse(null, dashed, c, r / 1.0 * (1 / 1.12), r / 1.0 * (1 / 1.12));

            var geo = new StreamGeometry();
            using (var ctx = geo.Open())
            {
                bool started = false;
                for (int i = 0; i <= 72; i++)
                {
                    double v = _max[i % 72];
                    if (v <= 0) { started = false; continue; }
                    double a = (i % 72 + 0.5) / 72 * 2 * Math.PI - Math.PI;
                    var p = new Point(c.X + Math.Cos(a) * v / 1.12 * r, c.Y + Math.Sin(a) * v / 1.12 * r);
                    if (!started) { ctx.BeginFigure(p, false, false); started = true; }
                    else ctx.LineTo(p, true, true);
                }
            }
            geo.Freeze();
            dc.DrawGeometry(null, new Pen(Res("Accent"), 2) { LineJoin = PenLineJoin.Round }, geo);
        }
    }
}
