using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Se7en.DualSense.App.Controls;
using Se7en.DualSense.App.Services;
using Se7en.DualSense.Processing;

namespace Se7en.DualSense.App.Pages;

/// <summary>
/// Onglet Sticks : calibration (centre + amplitude), zones mortes radiales, courbes, sensibilité, inversion,
/// échange des sticks. Valeurs brutes et corrigées côte à côte, détection automatique du drift.
/// </summary>
public sealed class SticksPage : UserControl, ILivePage
{
    private readonly IShell _shell;
    private readonly DriftDetector _drift = new();
    private readonly Ui.Debouncer _save = new();
    private readonly StackPanel _root = new();
    private StickPanel _left = null!, _right = null!;
    private TextBlock _calStatus = null!;

    public SticksPage(IShell shell)
    {
        _shell = shell;
        Content = _root;
        Build();
        App.Controller.ActiveProfileChanged += _ => Build();
    }

    private ProcessingSettings Settings => App.Controller.ActiveProfile.Processing;

    private void Apply(ProcessingSettings settings)
    {
        // Effet immédiat sur ce que reçoit le jeu, enregistrement du profil regroupé
        App.Controller.Output.Processor.Settings = settings;
        var profile = App.Controller.ActiveProfile with { Processing = settings };
        _save.Run(() => App.Controller.UpdateProfile(profile));
    }

    private void Build()
    {
        _root.Children.Clear();
        _root.Children.Add(new PageHeader
        {
            Overline = "RÉGLER · PROFIL « " + App.Controller.ActiveProfile.Name.ToUpperInvariant() + " »",
            Title = "Sticks",
            Subtitle = "Corrigez le drift : calibrez le centre réel de chaque stick, puis réglez zones mortes et courbes. " +
                       "À gauche la valeur brute, à droite ce que reçoit le jeu.",
        });

        // ── Calibration ──
        var cal = new DockPanel();
        var calButtons = new StackPanel { Orientation = Orientation.Horizontal };
        DockPanel.SetDock(calButtons, Dock.Right);
        calButtons.Children.Add(Ui.Button("Réinitialiser", "Button.Secondary", (_, _) =>
        {
            App.Controller.ResetCalibration();
            UpdateCalStatus();
            _shell.Toast("Calibration réinitialisée", "Les sticks utilisent les valeurs d'usine.");
        }, new Thickness(0, 0, 8, 0)));
        calButtons.Children.Add(Ui.Button("Lancer la calibration", "Button.Primary", (_, _) => StartCalibration()));
        cal.Children.Add(calButtons);
        var calText = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        calText.Children.Add(Ui.Text("CALIBRATION", "Text.Overline"));
        _calStatus = Ui.Text("", "Text.Body", new Thickness(0, 6, 24, 0));
        calText.Children.Add(_calStatus);
        cal.Children.Add(calText);
        _root.Children.Add(Ui.Card(cal, new Thickness(0, 0, 0, 16), 20));
        UpdateCalStatus();

        // ── Sticks ──
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(16) });
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        _left = new StickPanel("STICK GAUCHE", Settings.LeftStick, s => Apply(Settings with { LeftStick = s }));
        _right = new StickPanel("STICK DROIT", Settings.RightStick, s => Apply(Settings with { RightStick = s }));
        Grid.SetColumn(_right.Card, 2);
        grid.Children.Add(_left.Card);
        grid.Children.Add(_right.Card);
        _root.Children.Add(grid);

        // ── Options globales ──
        var options = new StackPanel();
        options.Children.Add(new SettingRow
        {
            Label = "Échanger les sticks",
            Description = "Le stick gauche devient le droit et inversement (joueurs gauchers).",
            Control = Ui.Switch(Settings.SwapSticks, v => Apply(Settings with { SwapSticks = v })),
            Last = true,
        });
        _root.Children.Add(Ui.Card(options, new Thickness(0, 16, 0, 0), 20));
    }

    private void UpdateCalStatus()
    {
        var l = App.Controller.Output.Processor.LeftCalibration;
        var r = App.Controller.Output.Processor.RightCalibration;
        bool calibrated = l != StickCalibration.Identity || r != StickCalibration.Identity;
        _calStatus.Text = calibrated
            ? $"Centre corrigé : gauche {Fmt(l.CenterX)} / {Fmt(-l.CenterY)} · droit {Fmt(r.CenterX)} / {Fmt(-r.CenterY)}. Calibration mémorisée pour cette manette."
            : "Aucune calibration. Posez la manette à plat : une mesure de quelques secondes suffit à compenser le drift.";
    }

    private static string Fmt(double v) => (v >= 0 ? "+" : "−") + Math.Abs(v).ToString("0.000", CultureInfo.InvariantCulture);

    public void Update(in FrameData frame)
    {
        if (!frame.IsConnected)
        {
            _left.Update(0, 0, 0, 0);
            _right.Update(0, 0, 0, 0);
            return;
        }
        var s = frame.State;
        var p = frame.Processed;
        bool swap = Settings.SwapSticks;
        _left.Update(StickMath.Normalize(swap ? s.RightX : s.LeftX), StickMath.Normalize(swap ? s.RightY : s.LeftY),
                     StickMath.Normalize(p.LeftX), StickMath.Normalize(p.LeftY));
        _right.Update(StickMath.Normalize(swap ? s.LeftX : s.RightX), StickMath.Normalize(swap ? s.LeftY : s.RightY),
                      StickMath.Normalize(p.RightX), StickMath.Normalize(p.RightY));

        var proc = App.Controller.Output.Processor;
        _drift.Add(s, proc.LeftCalibration, proc.RightCalibration);
        _left.SetDrift(_drift.LeftDrift, Settings.LeftStick.InnerDeadzone);
        _right.SetDrift(_drift.RightDrift, Settings.RightStick.InnerDeadzone);
        _shell.SetDriftAlert(_drift.LeftDrift > Settings.LeftStick.InnerDeadzone || _drift.RightDrift > Settings.RightStick.InnerDeadzone);
    }

    // ───────────── Assistant de calibration ─────────────

    private void StartCalibration()
    {
        if (App.Controller.Device == null)
        {
            _shell.Toast("Aucune manette", "Branchez la DualSense avant de calibrer.", NotifyKind.Warning);
            return;
        }

        var sampler = App.Controller.Output.Calibration;
        var title = Ui.Text("Ne touchez pas aux sticks", "Text.H1");
        var text = Ui.Text("Posez la manette sur une surface plane. La position de repos de chaque stick est mesurée pendant 3 secondes.",
            "Text.Body", new Thickness(0, 10, 0, 20));
        var bar = new TriggerBar { Height = 8, Margin = new Thickness(0, 0, 0, 8) };
        var barsRange = new StackPanel { Visibility = Visibility.Collapsed };
        var leftBar = new TriggerBar { Height = 8, Margin = new Thickness(0, 6, 0, 12) };
        var rightBar = new TriggerBar { Height = 8, Margin = new Thickness(0, 6, 0, 0) };
        barsRange.Children.Add(Ui.Text("STICK GAUCHE", "Text.Overline"));
        barsRange.Children.Add(leftBar);
        barsRange.Children.Add(Ui.Text("STICK DROIT", "Text.Overline"));
        barsRange.Children.Add(rightBar);

        var actions = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 24, 0, 0) };
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(50) };
        var cancel = Ui.Button("Annuler", "Button.Secondary", (_, _) => { timer.Stop(); sampler.Stop(); _shell.CloseModal(); });
        var finish = Ui.Button("Terminer", "Button.Primary", (_, _) => { }, new Thickness(8, 0, 0, 0));
        finish.Visibility = Visibility.Collapsed;
        actions.Children.Add(cancel);
        actions.Children.Add(finish);

        var body = new StackPanel { Width = 460 };
        body.Children.Add(Ui.Text("CALIBRATION", "Text.Overline", new Thickness(0, 0, 0, 10)));
        body.Children.Add(title);
        body.Children.Add(text);
        body.Children.Add(bar);
        body.Children.Add(barsRange);
        body.Children.Add(actions);
        _shell.ShowModal(body);

        var start = DateTime.UtcNow;
        int stage = 1;
        sampler.BeginCenter();

        void Complete()
        {
            timer.Stop();
            sampler.Stop();
            var (l, r) = sampler.Result(App.Controller.Output.Processor.LeftCalibration, App.Controller.Output.Processor.RightCalibration, includeRange: true);
            App.Controller.SaveCalibration(l, r);
            _shell.CloseModal();
            UpdateCalStatus();
            double fixedDrift = Math.Max(Math.Sqrt(l.CenterX * l.CenterX + l.CenterY * l.CenterY), Math.Sqrt(r.CenterX * r.CenterX + r.CenterY * r.CenterY));
            _shell.Toast("Calibration enregistrée", fixedDrift > 0.02 ? $"Dérive compensée (jusqu'à {fixedDrift:0.000})." : "Les sticks étaient déjà bien centrés.", NotifyKind.Success);
        }
        finish.Click += (_, _) => Complete();

        timer.Tick += (_, _) =>
        {
            double elapsed = (DateTime.UtcNow - start).TotalSeconds;
            if (stage == 1)
            {
                bar.Value = Math.Min(1, elapsed / 3);
                if (elapsed < 3) return;
                if (sampler.CenterNoise > 0.03)
                {
                    text.Text = "La manette a bougé pendant la mesure. Laissez-la immobile : nouvelle mesure…";
                    start = DateTime.UtcNow;
                    sampler.BeginCenter();
                    return;
                }
                stage = 2;
                start = DateTime.UtcNow;
                sampler.BeginRange();
                title.Text = "Faites tourner les deux sticks";
                text.Text = "Poussez chaque stick à fond et faites-lui faire 2 ou 3 tours complets, lentement. L'amplitude réelle est mesurée.";
                bar.Visibility = Visibility.Collapsed;
                barsRange.Visibility = Visibility.Visible;
                finish.Visibility = Visibility.Visible;
                finish.Content = "Ignorer cette étape";
            }
            else
            {
                var (cl, cr) = sampler.RangeCoverage;
                leftBar.Value = cl;
                rightBar.Value = cr;
                if (cl > 0.5 || cr > 0.5)
                    finish.Content = "Terminer";
                if (cl >= 0.98 && cr >= 0.98 && elapsed > 2)
                    Complete();
            }
        };
        timer.Start();
    }

    /// <summary>Carte de réglage d'un stick.</summary>
    private sealed class StickPanel
    {
        private readonly StickVisualizer _raw = new() { Width = 132, Height = 132 };
        private readonly StickVisualizer _processed = new() { Width = 132, Height = 132 };
        private readonly CurveGraph _curve = new() { Height = 96, Margin = new Thickness(0, 0, 0, 18) };
        private readonly Badge _badge = new() { ShowDot = true, Text = "Stable", Tone = BadgeTone.Success };
        private readonly TextBlock _readout;
        private StickSettings _s;
        private readonly Action<StickSettings> _changed;
        private FrameworkElement? _exponentRow;

        public ContentControl Card { get; }

        public StickPanel(string title, StickSettings settings, Action<StickSettings> changed)
        {
            _s = settings;
            _changed = changed;
            var root = new StackPanel();

            var head = new DockPanel { Margin = new Thickness(0, 0, 0, 16) };
            DockPanel.SetDock(_badge, Dock.Right);
            head.Children.Add(_badge);
            head.Children.Add(Ui.Text(title, "Text.Overline"));
            root.Children.Add(head);

            var vis = new Grid { Margin = new Thickness(0, 0, 0, 8) };
            vis.ColumnDefinitions.Add(new ColumnDefinition());
            vis.ColumnDefinitions.Add(new ColumnDefinition());
            vis.Children.Add(Labeled(_raw, "BRUT"));
            var p = Labeled(_processed, "CORRIGÉ");
            Grid.SetColumn(p, 1);
            vis.Children.Add(p);
            root.Children.Add(vis);
            _readout = Ui.Text("", "Text.Readout", new Thickness(0, 0, 0, 16));
            _readout.HorizontalAlignment = HorizontalAlignment.Center;
            root.Children.Add(_readout);

            root.Children.Add(Ui.Text("COURBE DE RÉPONSE", "Text.Overline", new Thickness(0, 0, 0, 8)));
            root.Children.Add(_curve);
            root.Children.Add(Ui.Segmented(new[] { "Linéaire", "Douce", "Agressive", "Perso" }, (int)_s.Curve, i =>
            {
                Set(_s with { Curve = (CurveType)i });
                if (_exponentRow != null) _exponentRow.Visibility = i == 3 ? Visibility.Visible : Visibility.Collapsed;
            }));
            _exponentRow = Ui.Slider("Exposant", 0.3, 3, _s.CustomExponent, v => v.ToString("0.00", CultureInfo.InvariantCulture),
                v => Set(_s with { CustomExponent = v }), 0.05, "Moins de 1 : réponse rapide · plus de 1 : précision au centre");
            _exponentRow.Margin = new Thickness(0, 14, 0, 0);
            _exponentRow.Visibility = _s.Curve == CurveType.Custom ? Visibility.Visible : Visibility.Collapsed;
            root.Children.Add(_exponentRow);

            var sliders = new StackPanel { Margin = new Thickness(0, 18, 0, 0) };
            sliders.Children.Add(Ui.Slider("Zone morte", 0, 0.4, _s.InnerDeadzone, Ui.Percent, v => Set(_s with { InnerDeadzone = v }), 0.01,
                "Ignore les petits mouvements au centre (le drift)"));
            sliders.Children.Add(Ui.Slider("Zone morte externe", 0, 0.3, _s.OuterDeadzone, Ui.Percent, v => Set(_s with { OuterDeadzone = v }), 0.01,
                "Atteint 100 % avant le bord physique"));
            sliders.Children.Add(Ui.Slider("Anti-zone morte", 0, 0.4, _s.AntiDeadzone, Ui.Percent, v => Set(_s with { AntiDeadzone = v }), 0.01,
                "Compense la zone morte ajoutée par le jeu"));
            sliders.Children.Add(Ui.Slider("Sensibilité horizontale", 0.2, 2, _s.SensitivityX, Ui.Percent, v => Set(_s with { SensitivityX = v }), 0.05));
            sliders.Children.Add(Ui.Slider("Sensibilité verticale", 0.2, 2, _s.SensitivityY, Ui.Percent, v => Set(_s with { SensitivityY = v }), 0.05));
            root.Children.Add(sliders);

            var invert = new StackPanel { Orientation = Orientation.Horizontal };
            var ix = new CheckBox { Content = "Inverser X", IsChecked = _s.InvertX, Margin = new Thickness(0, 0, 24, 0) };
            ix.Checked += (_, _) => Set(_s with { InvertX = true });
            ix.Unchecked += (_, _) => Set(_s with { InvertX = false });
            var iy = new CheckBox { Content = "Inverser Y", IsChecked = _s.InvertY };
            iy.Checked += (_, _) => Set(_s with { InvertY = true });
            iy.Unchecked += (_, _) => Set(_s with { InvertY = false });
            invert.Children.Add(ix);
            invert.Children.Add(iy);
            root.Children.Add(invert);

            _curve.SetSettings(_s);
            _raw.Deadzone = _s.InnerDeadzone;
            Card = Ui.Card(root, null, 20);
        }

        private static StackPanel Labeled(FrameworkElement viz, string label)
        {
            var sp = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center };
            sp.Children.Add(viz);
            var t = Ui.Text(label, "Text.Overline", new Thickness(0, 8, 0, 0));
            t.HorizontalAlignment = HorizontalAlignment.Center;
            sp.Children.Add(t);
            return sp;
        }

        private void Set(StickSettings s)
        {
            _s = s;
            _curve.SetSettings(s);
            _raw.Deadzone = s.InnerDeadzone;
            _changed(s);
        }

        public void Update(double rx, double ry, double px, double py)
        {
            _raw.SetPosition(rx, ry);
            _processed.SetPosition(px, py);
            _curve.SetInput(Math.Min(1, Math.Sqrt(rx * rx + ry * ry)));
            _readout.Text = $"X {Fmt(rx)} → {Fmt(px)}   Y {Fmt(-ry)} → {Fmt(-py)}";
        }

        public void SetDrift(double drift, double deadzone)
        {
            bool bad = drift > deadzone;
            _badge.Text = drift <= 0 ? "Stable" : bad ? $"Drift {drift:0.00}" : $"Dérive absorbée {drift:0.00}";
            _badge.Tone = drift <= 0 ? BadgeTone.Success : bad ? BadgeTone.Danger : BadgeTone.Warning;
        }
    }
}

/// <summary>Barre de progression horizontale fine (pilule violette).</summary>
public sealed class TriggerBar : LiveVisual
{
    private double _value;

    public double Value
    {
        get => _value;
        set { _value = Math.Clamp(value, 0, 1); InvalidateVisual(); }
    }

    protected override void OnRender(System.Windows.Media.DrawingContext dc)
    {
        double w = ActualWidth, h = ActualHeight;
        if (w <= 0 || h <= 0) return;
        dc.DrawRoundedRectangle(Res("Surface.Inset"), null, new Rect(0, 0, w, h), h / 2, h / 2);
        if (_value > 0)
            dc.DrawRoundedRectangle(Res(_value >= 0.98 ? "Success" : "Accent"), null, new Rect(0, 0, Math.Max(h, w * _value), h), h / 2, h / 2);
    }
}
