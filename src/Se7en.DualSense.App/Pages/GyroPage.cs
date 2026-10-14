using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Se7en.DualSense.App.Controls;
using Se7en.DualSense.App.Services;
using Se7en.DualSense.Processing;

namespace Se7en.DualSense.App.Pages;

/// <summary>Onglet Gyroscope : visée par inclinaison de la manette, ajoutée au stick droit.</summary>
public sealed class GyroPage : UserControl, ILivePage
{
    private readonly Ui.Debouncer _save = new();
    private readonly StackPanel _root = new();
    private readonly GyroPreview _preview = new() { Height = 200 };
    private readonly StickVisualizer _stick = new() { Width = 150, Height = 150 };
    private readonly TextBlock _readout = new();

    public GyroPage()
    {
        _readout.SetResourceReference(StyleProperty, "Text.Readout");
        Content = _root;
        Build();
        App.Controller.ActiveProfileChanged += _ => Build();
    }

    private GyroSettings G => App.Controller.ActiveProfile.Processing.Gyro;

    private void Set(GyroSettings g)
    {
        var p = App.Controller.ActiveProfile.Processing with { Gyro = g };
        App.Controller.PreviewProcessing(p);
        var profile = App.Controller.ActiveProfile with { Processing = p };
        _save.Run(() => App.Controller.UpdateProfile(profile));
    }

    private void Build()
    {
        _root.Children.Clear();
        _root.Children.Add(new PageHeader
        {
            Overline = "RÉGLER · PROFIL « " + App.Controller.ActiveProfile.Name.ToUpperInvariant() + " »",
            Title = "Gyroscope",
            Subtitle = "Visez en inclinant la manette : le mouvement s'ajoute au stick droit, pour une précision au pixel dans les jeux de tir.",
        });

        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.2, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(16) });
        grid.ColumnDefinitions.Add(new ColumnDefinition());

        var settings = new StackPanel();
        settings.Children.Add(new SettingRow
        {
            Label = "Visée gyroscopique",
            Description = "Incliner la manette déplace la caméra, en plus du stick droit.",
            Control = Ui.Switch(G.Enabled, v => Set(G with { Enabled = v })),
        });
        settings.Children.Add(Ui.Text("ACTIVATION", "Text.Overline", new Thickness(0, 18, 0, 8)));
        settings.Children.Add(Ui.Segmented(new[] { "Toujours", "En visant (L2)", "Avec R1" }, (int)G.Activation, i => Set(G with { Activation = (GyroActivation)i })));
        var sliders = new StackPanel { Margin = new Thickness(0, 20, 0, 0) };
        sliders.Children.Add(Ui.Slider("Sensibilité", 0.2, 5, G.Sensitivity, v => v.ToString("0.0", CultureInfo.InvariantCulture) + " ×", v => Set(G with { Sensitivity = v }), 0.1));
        sliders.Children.Add(Ui.Slider("Zone morte", 0, 6, G.Deadzone, v => v.ToString("0.0", CultureInfo.InvariantCulture) + " °/s", v => Set(G with { Deadzone = v }), 0.1,
            "Ignore les tremblements de la main"));
        settings.Children.Add(sliders);
        var inv = new StackPanel { Orientation = Orientation.Horizontal };
        var ix = new CheckBox { Content = "Inverser l'horizontale", IsChecked = G.InvertX, Margin = new Thickness(0, 0, 24, 0) };
        ix.Checked += (_, _) => Set(G with { InvertX = true });
        ix.Unchecked += (_, _) => Set(G with { InvertX = false });
        var iy = new CheckBox { Content = "Inverser la verticale", IsChecked = G.InvertY };
        iy.Checked += (_, _) => Set(G with { InvertY = true });
        iy.Unchecked += (_, _) => Set(G with { InvertY = false });
        inv.Children.Add(ix);
        inv.Children.Add(iy);
        settings.Children.Add(inv);
        grid.Children.Add(Ui.Card(settings, null, 20));

        var live = new StackPanel();
        live.Children.Add(Ui.Text("EN DIRECT", "Text.Overline", new Thickness(0, 0, 0, 12)));
        live.Children.Add(Ui.Detach(_preview));
        var stickBox = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 16, 0, 0) };
        stickBox.Children.Add(Ui.Detach(_stick));
        var lbl = Ui.Text("STICK DROIT ENVOYÉ AU JEU", "Text.Overline", new Thickness(0, 8, 0, 0));
        lbl.HorizontalAlignment = HorizontalAlignment.Center;
        stickBox.Children.Add(lbl);
        live.Children.Add(stickBox);
        _readout.HorizontalAlignment = HorizontalAlignment.Center;
        _readout.Margin = new Thickness(0, 12, 0, 0);
        live.Children.Add(Ui.Detach(_readout));
        var liveCard = Ui.Card(live, null, 20);
        Grid.SetColumn(liveCard, 2);
        grid.Children.Add(liveCard);

        _root.Children.Add(grid);
    }

    public void Update(in FrameData frame)
    {
        if (!frame.IsConnected) return;
        var s = frame.State;
        _preview.Push(s.GyroX / 16.0, s.GyroY / 16.0, s.AccelX, s.AccelY, s.AccelZ);
        _stick.SetPosition(StickMath.Normalize(frame.Processed.RightX), StickMath.Normalize(frame.Processed.RightY));
        _readout.Text = $"lacet {-s.GyroY / 16.0,6:0} °/s   tangage {-s.GyroX / 16.0,6:0} °/s";
    }

    /// <summary>Silhouette de manette qui s'incline avec la vraie manette, et trace du mouvement.</summary>
    private sealed class GyroPreview : LiveVisual
    {
        private double _roll, _pitch, _yawTrace;
        private readonly Queue<double> _trace = new();

        public void Push(double pitchRate, double yawRate, short ax, short ay, short az)
        {
            double norm = Math.Sqrt((double)ay * ay + (double)az * az);
            if (norm > 1000)
            {
                _roll += (Math.Atan2(ax, norm) * 180 / Math.PI - _roll) * 0.2;
                _pitch += (Math.Atan2(az, ay) * 180 / Math.PI - _pitch) * 0.2;
            }
            _yawTrace = -yawRate;
            _trace.Enqueue(_yawTrace);
            while (_trace.Count > 120) _trace.Dequeue();
            InvalidateVisual();
        }

        protected override void OnRender(DrawingContext dc)
        {
            double w = ActualWidth, h = ActualHeight;
            if (w <= 0 || h <= 0) return;
            dc.DrawRoundedRectangle(Res("Surface.Inset"), null, new Rect(0, 0, w, h), 8, 8);

            // Trace du lacet (°/s)
            var pen = new Pen(Res("Accent.Line"), 1.5);
            var pts = _trace.ToArray();
            for (int i = 1; i < pts.Length; i++)
            {
                double x0 = (i - 1) / 120.0 * w, x1 = i / 120.0 * w;
                double y0 = h / 2 - Math.Clamp(pts[i - 1] / 300, -1, 1) * (h / 2 - 8);
                double y1 = h / 2 - Math.Clamp(pts[i] / 300, -1, 1) * (h / 2 - 8);
                dc.DrawLine(pen, new Point(x0, y0), new Point(x1, y1));
            }

            // Silhouette inclinée
            dc.PushTransform(new TranslateTransform(w / 2, h / 2));
            dc.PushTransform(new RotateTransform(Math.Clamp(_roll, -60, 60)));
            dc.PushTransform(new ScaleTransform(1, Math.Max(0.35, Math.Cos(Math.Clamp(_pitch, -70, 70) * Math.PI / 180))));
            var body = new RectangleGeometry(new Rect(-70, -26, 140, 52), 26, 26);
            dc.DrawGeometry(Res("Surface.Raised"), new Pen(Res("Ink"), 2), body);
            dc.DrawEllipse(Res("Ink"), null, new Point(-26, 6), 8, 8);
            dc.DrawEllipse(Res("Ink"), null, new Point(26, 6), 8, 8);
            dc.DrawRoundedRectangle(Res("Accent"), null, new Rect(-24, -20, 48, 14), 4, 4);
            dc.Pop(); dc.Pop(); dc.Pop();
        }
    }
}
