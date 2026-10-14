using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using Se7en.DualSense.App.Controls;
using Se7en.DualSense.App.Services;
using Se7en.DualSense.Output;

namespace Se7en.DualSense.App.Pages;

/// <summary>Onglet Vibrations &amp; lumière : intensité et test des vibrations, barre lumineuse, LED joueur.</summary>
public sealed class FeedbackPage : UserControl
{
    private readonly Ui.Debouncer _save = new();
    private readonly StackPanel _root = new();
    private readonly DispatcherTimer _rumbleStop = new() { Interval = TimeSpan.FromMilliseconds(900) };
    private Border _preview = null!;

    public FeedbackPage(IShell shell)
    {
        Content = _root;
        _rumbleStop.Tick += (_, _) => { _rumbleStop.Stop(); App.Controller.Output.TestRumble(0, 0); };
        Build();
        App.Controller.ActiveProfileChanged += _ => Build();
    }

    private FeedbackSettings F => App.Controller.ActiveProfile.Feedback;

    private void Set(FeedbackSettings f)
    {
        App.Controller.PreviewFeedback(f);
        var profile = App.Controller.ActiveProfile with { Feedback = f };
        _save.Run(() => App.Controller.UpdateProfile(profile));
        UpdatePreview();
    }

    private void Build()
    {
        _root.Children.Clear();
        _root.Children.Add(new PageHeader
        {
            Overline = "RÉGLER · PROFIL « " + App.Controller.ActiveProfile.Name.ToUpperInvariant() + " »",
            Title = "Vibrations & lumière",
            Subtitle = "Les vibrations envoyées par le jeu sont transmises à la DualSense. Réglez leur intensité et la barre lumineuse.",
        });

        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(16) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.2, GridUnitType.Star) });

        // ── Vibrations ──
        var vib = new StackPanel();
        vib.Children.Add(Ui.Text("VIBRATIONS", "Text.Overline", new Thickness(0, 0, 0, 14)));
        vib.Children.Add(Ui.Slider("Intensité", 0, 2, F.RumbleIntensity, Ui.Percent, v => Set(F with { RumbleIntensity = v }), 0.05,
            "0 % coupe les vibrations · 200 % les double"));
        var tests = new WrapPanel { Margin = new Thickness(0, 6, 0, 0) };
        tests.Children.Add(Ui.Button("Tester le gros moteur", "Button.Secondary", (_, _) => Rumble(1, 0), new Thickness(0, 0, 8, 8)));
        tests.Children.Add(Ui.Button("Tester le petit moteur", "Button.Secondary", (_, _) => Rumble(0, 1), new Thickness(0, 0, 8, 8)));
        vib.Children.Add(tests);
        vib.Children.Add(Ui.Text("Les tests fonctionnent en modes DualShock 4 et Xbox 360 (en mode natif, la manette est en lecture seule).",
            "Text.Caption", new Thickness(0, 6, 0, 0)));

        vib.Children.Add(Ui.Text("LED JOUEUR", "Text.Overline", new Thickness(0, 26, 0, 8)));
        vib.Children.Add(Ui.Segmented(new[] { "Joueur 1", "Joueur 2", "Joueur 3", "Joueur 4" }, Math.Clamp(F.PlayerNumber - 1, 0, 3),
            i => Set(F with { PlayerNumber = i + 1 })));
        grid.Children.Add(Ui.Card(vib, null, 20));

        // ── Barre lumineuse ──
        var light = new StackPanel();
        var head = new DockPanel { Margin = new Thickness(0, 0, 0, 14) };
        _preview = new Border { Width = 92, Height = 10, CornerRadius = new CornerRadius(5), VerticalAlignment = VerticalAlignment.Center };
        DockPanel.SetDock(_preview, Dock.Right);
        head.Children.Add(_preview);
        head.Children.Add(Ui.Text("BARRE LUMINEUSE", "Text.Overline"));
        light.Children.Add(head);

        light.Children.Add(Ui.Segmented(new[] { "Couleur du jeu", "Personnalisée", "Batterie", "Éteinte" }, (int)F.Lightbar < 4 ? (int)F.Lightbar : -1,
            i => { Set(F with { Lightbar = (LightbarMode)i }); Build(); }));
        light.Children.Add(Ui.Text("EFFETS ANIMÉS", "Text.Overline", new Thickness(0, 14, 0, 8)));
        light.Children.Add(Ui.Segmented(new[] { "Arc-en-ciel", "Respiration", "Musique", "Écran" }, (int)F.Lightbar >= 4 ? (int)F.Lightbar - 4 : -1,
            i => { Set(F with { Lightbar = (LightbarMode)(i + 4) }); Build(); }));
        light.Children.Add(Ui.Text(F.Lightbar switch
        {
            LightbarMode.Music => "La barre pulse au rythme du son de Windows.",
            LightbarMode.Screen => "La barre reprend les couleurs du bord de l'écran (« ambilight »). Les jeux en plein écran exclusif peuvent rester noirs : préférez le fenêtré sans bordure.",
            LightbarMode.Breathing => "Votre couleur s'allume et s'éteint doucement.",
            _ => "« Couleur du jeu » utilise la couleur envoyée par le jeu en mode DualShock 4, sinon votre couleur.",
        }, "Text.Caption", new Thickness(0, 8, 0, 16)));
        if (Services.LightbarEffects.IsAnimated(F.Lightbar))
            light.Children.Add(Ui.Slider("Vitesse de l'effet", 0.2, 3, F.EffectSpeed, v => v.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture) + "×",
                v => Set(F with { EffectSpeed = v }), 0.1));

        var swatches = new WrapPanel { Margin = new Thickness(0, 0, 0, 16) };
        (string Name, Color C)[] palette =
        {
            ("Violet", Color.FromRgb(0x5B, 0x2D, 0xE0)), ("Bleu", Color.FromRgb(0x1A, 0x1A, 0xF0)), ("Cyan", Color.FromRgb(0x00, 0xC8, 0xF0)),
            ("Vert", Color.FromRgb(0x17, 0xC0, 0x6B)), ("Citron", Color.FromRgb(0xD4, 0xF5, 0x3C)), ("Orange", Color.FromRgb(0xF0, 0x8A, 0x1C)),
            ("Rouge", Color.FromRgb(0xE8, 0x24, 0x2B)), ("Rose", Color.FromRgb(0xF0, 0x4C, 0xB0)), ("Blanc", Colors.White),
        };
        foreach (var (name, c) in palette)
        {
            var b = new Button { Width = 34, Height = 34, Margin = new Thickness(0, 0, 10, 10), ToolTip = name, Cursor = System.Windows.Input.Cursors.Hand };
            b.Template = SwatchTemplate(c);
            b.Click += (_, _) => { Set(F with { Lightbar = LightbarMode.Custom, Red = c.R, Green = c.G, Blue = c.B }); Build(); };
            swatches.Children.Add(b);
        }
        light.Children.Add(swatches);
        light.Children.Add(Ui.Slider("Rouge", 0, 255, F.Red, v => ((int)v).ToString(), v => Set(F with { Red = (byte)v }), 1));
        light.Children.Add(Ui.Slider("Vert", 0, 255, F.Green, v => ((int)v).ToString(), v => Set(F with { Green = (byte)v }), 1));
        light.Children.Add(Ui.Slider("Bleu", 0, 255, F.Blue, v => ((int)v).ToString(), v => Set(F with { Blue = (byte)v }), 1));
        light.Children.Add(Ui.Slider("Luminosité", 0.05, 1, F.Brightness, Ui.Percent, v => Set(F with { Brightness = v }), 0.05));
        var lightCard = Ui.Card(light, null, 20);
        Grid.SetColumn(lightCard, 2);
        grid.Children.Add(lightCard);

        _root.Children.Add(grid);

        // ── Vibrations générées depuis le son ──
        var audio = new StackPanel();
        audio.Children.Add(Ui.Text("VIBRATIONS DEPUIS LE SON", "Text.Overline", new Thickness(0, 0, 0, 6)));
        audio.Children.Add(Ui.Text("La manette vibre au rythme du son de Windows : explosions, moteurs, basses de la musique. " +
                                   "Idéal pour les jeux qui ne font pas vibrer la manette ; s'ajoute aux vibrations du jeu.",
            "Text.Caption", new Thickness(0, 0, 0, 14)));
        audio.Children.Add(Ui.Segmented(new[] { "Désactivé", "Basses seulement", "Basses + médiums", "Haptique HD (USB)" }, (int)F.AudioRumble,
            i => { Set(F with { AudioRumble = (AudioRumbleMode)i }); Build(); }));
        if (F.AudioRumble == AudioRumbleMode.Haptics)
            audio.Children.Add(Ui.Text(AudioDevices.Controller().OutputId != null
                ? "Haptique HD active : les actionneurs de la DualSense reproduisent le son (pas, impacts, pluie…) et les vibrations du jeu, comme sur PS5."
                : "Branchez la manette en USB : l'haptique HD passe par sa carte son, indisponible en Bluetooth sous Windows.",
                "Text.Caption", new Thickness(0, 10, 0, 0)));
        audio.Children.Add(Ui.Slider("Intensité", 0.2, 2, F.AudioRumbleIntensity, Ui.Percent, v => Set(F with { AudioRumbleIntensity = v }), 0.05,
            null).WithMargin(new Thickness(0, 16, 0, 0)));
        audio.Children.Add(new SettingRow
        {
            Label = "Gâchettes réactives au son",
            Description = "Quand un son fort retentit (tir, explosion) pendant que vous pressez L2 ou R2, la gâchette vibre. Fonctionne dans tous les modes.",
            Control = Ui.Switch(F.AudioTriggers, v => Set(F with { AudioTriggers = v })),
            Last = true,
        });
        _root.Children.Add(Ui.Card(audio, new Thickness(0, 16, 0, 0), 20));

        // ── Son de la manette ──
        var (outputId, micId) = AudioDevices.Controller();
        var sound = new StackPanel();
        sound.Children.Add(Ui.Text("SON DE LA MANETTE", "Text.Overline", new Thickness(0, 0, 0, 6)));
        sound.Children.Add(Ui.Text(outputId == null
            ? "Branchez la DualSense en USB pour utiliser son haut-parleur, sa prise casque et son micro (indisponibles en Bluetooth sous Windows)."
            : "Haut-parleur et prise casque de la manette, micro intégré.", "Text.Caption", new Thickness(0, 0, 0, 6)));
        sound.Children.Add(new SettingRow
        {
            Label = "Copier le son du PC dans la manette",
            Description = "Le son continue sur vos haut-parleurs et sort aussi du haut-parleur (ou du casque branché) de la manette.",
            Control = Ui.Switch(AppSettings.Current.MirrorSoundToController, v =>
            {
                AppSettings.Current.MirrorSoundToController = v;
                AppSettings.Save();
                App.Controller.RefreshAudio();
            }).With(t => t.IsEnabled = outputId != null),
        });
        sound.Children.Add(Ui.Slider("Volume dans la manette", 0, 1, AppSettings.Current.MirrorVolume, Ui.Percent, v =>
        {
            AppSettings.Current.MirrorVolume = v;
            AppSettings.Save();
            App.Controller.RefreshAudio();
        }, 0.05).WithMargin(new Thickness(0, 12, 0, 0)));
        var defaults = new WrapPanel { Margin = new Thickness(0, 4, 0, 0) };
        defaults.Children.Add(Ui.Button("Manette = sortie audio de Windows", "Button.Secondary", (_, _) => SetDefault(outputId, "Le son de Windows sort maintenant de la manette."), new Thickness(0, 0, 8, 8))
            .With(b => b.IsEnabled = outputId != null));
        defaults.Children.Add(Ui.Button("Micro de la manette = micro de Windows", "Button.Secondary", (_, _) => SetDefault(micId, "Le micro de la manette est maintenant le micro de Windows."), new Thickness(0, 0, 8, 8))
            .With(b => b.IsEnabled = micId != null));
        sound.Children.Add(defaults);
        sound.Children.Add(Ui.Text("Pour revenir en arrière : choisissez votre autre périphérique dans le menu son de Windows (icône du haut-parleur).",
            "Text.Caption", new Thickness(0, 4, 0, 0)));
        _root.Children.Add(Ui.Card(sound, new Thickness(0, 16, 0, 0), 20));

        // ── Micro ──
        var mic = new StackPanel();
        mic.Children.Add(Ui.Text("BOUTON MICRO", "Text.Overline", new Thickness(0, 0, 0, 6)));
        mic.Children.Add(new SettingRow
        {
            Label = "Couper le micro de Windows",
            Description = "Le bouton micro de la manette coupe ou rétablit le micro de Windows (Discord, Teams…). Sa LED orange s'allume quand il est coupé.",
            Control = Ui.Switch(AppSettings.Current.MuteControlsMicrophone, v =>
            {
                AppSettings.Current.MuteControlsMicrophone = v;
                AppSettings.Save();
                App.Controller.ApplyGlobalOptions();
            }),
            Last = true,
        });
        _root.Children.Add(Ui.Card(mic, new Thickness(0, 16, 0, 0), 20));
        UpdatePreview();
    }

    private void SetDefault(string? id, string message)
    {
        if (id != null && AudioDevices.SetDefault(id))
        {
            App.Shell?.Toast("Son de Windows modifié", message, NotifyKind.Success);
            App.Controller.Audio.Refresh();
        }
        else
        {
            App.Shell?.Toast("Changement impossible", "Windows a refusé de changer le périphérique par défaut.", NotifyKind.Warning);
        }
    }

    private void UpdatePreview()
    {
        var f = F;
        var c = f.Lightbar == LightbarMode.Off ? Color.FromRgb(0x30, 0x30, 0x36) : Color.FromRgb((byte)(f.Red * f.Brightness), (byte)(f.Green * f.Brightness), (byte)(f.Blue * f.Brightness));
        _preview.Background = Services.LightbarEffects.IsAnimated(f.Lightbar)
            ? new LinearGradientBrush(new GradientStopCollection { new(Colors.Red, 0), new(Colors.Lime, 0.33), new(Colors.Blue, 0.66), new(Colors.Magenta, 1) }, 0)
            : new SolidColorBrush(c);
        _preview.Effect = new System.Windows.Media.Effects.DropShadowEffect { Color = c, ShadowDepth = 0, BlurRadius = 16, Opacity = 0.8 };
    }

    private void Rumble(double heavy, double light)
    {
        App.Controller.Output.TestRumble(heavy, light);
        _rumbleStop.Stop();
        _rumbleStop.Start();
    }

    private static ControlTemplate SwatchTemplate(Color c)
    {
        var t = new ControlTemplate(typeof(Button));
        var outer = new FrameworkElementFactory(typeof(Border));
        outer.SetValue(Border.CornerRadiusProperty, new CornerRadius(17));
        outer.SetValue(Border.BackgroundProperty, new SolidColorBrush(c));
        outer.SetResourceReference(Border.BorderBrushProperty, "Border.Default");
        outer.SetValue(Border.BorderThicknessProperty, new Thickness(1));
        t.VisualTree = outer;
        return t;
    }
}
