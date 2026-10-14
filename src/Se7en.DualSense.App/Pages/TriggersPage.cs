using System.Windows;
using System.Windows.Controls;
using Se7en.DualSense.App.Controls;
using Se7en.DualSense.App.Services;
using Se7en.DualSense.Output;
using Se7en.DualSense.Processing;

namespace Se7en.DualSense.App.Pages;

/// <summary>Onglet Gâchettes : zones mortes, tout-ou-rien et gâchettes adaptatives (avec préréglages).</summary>
public sealed class TriggersPage : UserControl, ILivePage
{
    private readonly Ui.Debouncer _save = new();
    private readonly StackPanel _root = new();
    private readonly TriggerMeter _l2Raw = new() { Width = 26, Height = 120 }, _l2Out = new() { Width = 26, Height = 120 };
    private readonly TriggerMeter _r2Raw = new() { Width = 26, Height = 120 }, _r2Out = new() { Width = 26, Height = 120 };

    public TriggersPage()
    {
        Content = _root;
        Build();
        App.Controller.ActiveProfileChanged += _ => Build();
    }

    private Profile Profile => App.Controller.ActiveProfile;

    private void ApplyProcessing(ProcessingSettings p)
    {
        App.Controller.Output.Processor.Settings = p;
        var profile = Profile with { Processing = p };
        _save.Run(() => App.Controller.UpdateProfile(profile));
    }

    private void ApplyFeedback(FeedbackSettings f)
    {
        App.Controller.Output.SetFeedback(f);
        var profile = Profile with { Feedback = f };
        _save.Run(() => App.Controller.UpdateProfile(profile));
    }

    private void Build()
    {
        _root.Children.Clear();
        _root.Children.Add(new PageHeader
        {
            Overline = "RÉGLER · PROFIL « " + Profile.Name.ToUpperInvariant() + " »",
            Title = "Gâchettes",
            Subtitle = "Zones mortes de L2 et R2, mode tout-ou-rien pour tirer plus vite, et résistance des gâchettes adaptatives.",
        });

        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(16) });
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        var l = TriggerCard("L2", true, _l2Raw, _l2Out);
        var r = TriggerCard("R2", false, _r2Raw, _r2Out);
        Grid.SetColumn(r, 2);
        grid.Children.Add(l);
        grid.Children.Add(r);
        _root.Children.Add(grid);
    }

    private FrameworkElement TriggerCard(string name, bool left, TriggerMeter raw, TriggerMeter output)
    {
        TriggerSettings Get() => left ? Profile.Processing.L2 : Profile.Processing.R2;
        void Set(TriggerSettings t) => ApplyProcessing(left ? Profile.Processing with { L2 = t } : Profile.Processing with { R2 = t });
        TriggerEffectSettings GetFx() => left ? Profile.Feedback.LeftTrigger : Profile.Feedback.RightTrigger;
        void SetFx(TriggerEffectSettings e) => ApplyFeedback(left ? Profile.Feedback with { LeftTrigger = e } : Profile.Feedback with { RightTrigger = e });

        var root = new StackPanel();
        root.Children.Add(Ui.Text("GÂCHETTE " + name, "Text.Overline", new Thickness(0, 0, 0, 16)));

        var meters = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 0, 0, 20) };
        meters.Children.Add(Meter(raw, "BRUT"));
        meters.Children.Add(Meter(output, "JEU"));
        root.Children.Add(meters);

        var t = Get();
        root.Children.Add(Ui.Slider("Zone morte", 0, 0.5, t.Deadzone, Ui.Percent, v => Set(Get() with { Deadzone = v }), 0.01, "Course ignorée au début de l'appui"));
        root.Children.Add(Ui.Slider("Zone morte de fin", 0, 0.5, t.OuterDeadzone, Ui.Percent, v => Set(Get() with { OuterDeadzone = v }), 0.01, "Pression maximale atteinte avant la fin de course"));
        root.Children.Add(new SettingRow
        {
            Label = "Tout-ou-rien",
            Description = "La moindre pression envoie la gâchette à fond (tir plus rapide).",
            Control = Ui.Switch(t.HairTrigger, v => Set(Get() with { HairTrigger = v })),
            Last = true,
        });

        // ── Gâchette adaptative ──
        var fx = GetFx();
        var fxPanel = new StackPanel { Margin = new Thickness(0, 22, 0, 0) };
        fxPanel.Children.Add(Ui.Text("GÂCHETTE ADAPTATIVE", "Text.Overline", new Thickness(0, 0, 0, 4)));
        fxPanel.Children.Add(Ui.Text("Active en modes DualShock 4 et Xbox 360. En mode natif, c'est le jeu qui pilote la résistance.", "Text.Caption", new Thickness(0, 0, 0, 12)));

        var sliders = new StackPanel { Margin = new Thickness(0, 16, 0, 0) };
        void BuildSliders()
        {
            sliders.Children.Clear();
            var e = GetFx();
            if (e.Kind == TriggerEffectKind.Off) return;
            sliders.Children.Add(Ui.Slider(e.Kind == TriggerEffectKind.Section ? "Début du point dur" : "Début", 0, 1, e.Start, Ui.Percent, v => SetFx(GetFx() with { Start = v }), 0.01));
            if (e.Kind == TriggerEffectKind.Section)
                sliders.Children.Add(Ui.Slider("Fin du point dur", 0, 1, e.End, Ui.Percent, v => SetFx(GetFx() with { End = v }), 0.01));
            if (e.Kind == TriggerEffectKind.Vibration)
                sliders.Children.Add(Ui.Slider("Fréquence", 0, 1, e.Frequency, Ui.Percent, v => SetFx(GetFx() with { Frequency = v }), 0.01));
            sliders.Children.Add(Ui.Slider("Force", 0, 1, e.Force, Ui.Percent, v => SetFx(GetFx() with { Force = v }), 0.01));
        }

        fxPanel.Children.Add(Ui.Segmented(new[] { "Aucune", "Résistance", "Point dur", "Vibration" }, (int)fx.Kind, i =>
        {
            SetFx(GetFx() with { Kind = (TriggerEffectKind)i });
            BuildSliders();
        }));

        var presets = new WrapPanel { Margin = new Thickness(0, 12, 0, 0) };
        void Preset(string label, TriggerEffectSettings e) => presets.Children.Add(Ui.Button(label, "Button.Secondary", (_, _) =>
        {
            SetFx(e);
            Build();
        }, new Thickness(0, 0, 8, 8)));
        Preset("Arme", new TriggerEffectSettings { Kind = TriggerEffectKind.Section, Start = 0.25, End = 0.45, Force = 0.9 });
        Preset("Arc", new TriggerEffectSettings { Kind = TriggerEffectKind.Section, Start = 0.3, End = 0.9, Force = 0.65 });
        Preset("Frein", new TriggerEffectSettings { Kind = TriggerEffectKind.Resistance, Start = 0.1, Force = 0.8 });
        Preset("Accélérateur", new TriggerEffectSettings { Kind = TriggerEffectKind.Vibration, Start = 0.1, Force = 0.35, Frequency = 0.25 });
        fxPanel.Children.Add(presets);
        fxPanel.Children.Add(sliders);
        BuildSliders();
        root.Children.Add(fxPanel);

        return Ui.Card(root, null, 20);
    }

    private static StackPanel Meter(TriggerMeter meter, string label)
    {
        var sp = new StackPanel { Margin = new Thickness(14, 0, 14, 0) };
        sp.Children.Add(Ui.Detach(meter));
        var t = Ui.Text(label, "Text.Overline", new Thickness(0, 8, 0, 0));
        t.HorizontalAlignment = HorizontalAlignment.Center;
        sp.Children.Add(t);
        return sp;
    }

    public void Update(in FrameData frame)
    {
        bool c = frame.IsConnected;
        _l2Raw.SetValue(c ? frame.State.L2 / 255.0 : 0);
        _r2Raw.SetValue(c ? frame.State.R2 / 255.0 : 0);
        _l2Out.SetValue(c ? frame.Processed.L2 / 255.0 : 0);
        _r2Out.SetValue(c ? frame.Processed.R2 / 255.0 : 0);
    }
}
