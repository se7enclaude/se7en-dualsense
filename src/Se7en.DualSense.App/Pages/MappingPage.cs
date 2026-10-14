using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using Se7en.DualSense.App.Controls;
using Se7en.DualSense.App.Services;
using Se7en.DualSense.Hid;
using Se7en.DualSense.Processing;

namespace Se7en.DualSense.App.Pages;

/// <summary>Onglet Mappage : réattribution de chaque bouton et turbo, par profil.</summary>
public sealed class MappingPage : UserControl, ILivePage
{
    private readonly IShell _shell;
    private readonly Ui.Debouncer _save = new();
    private readonly StackPanel _root = new();
    private readonly List<(DualSenseButtons Flag, ButtonGlyph Glyph)> _glyphs = new();

    /// <summary>Boutons réattribuables, dans l'ordre de la manette.</summary>
    public static readonly (DualSenseButtons Flag, string Glyph, string Name, GlyphShape Shape)[] Buttons =
    {
        (DualSenseButtons.Cross, "✕", "Croix", GlyphShape.Circle),
        (DualSenseButtons.Circle, "○", "Rond", GlyphShape.Circle),
        (DualSenseButtons.Square, "□", "Carré", GlyphShape.Circle),
        (DualSenseButtons.Triangle, "△", "Triangle", GlyphShape.Circle),
        (DualSenseButtons.DpadUp, "↑", "Haut", GlyphShape.Rounded),
        (DualSenseButtons.DpadDown, "↓", "Bas", GlyphShape.Rounded),
        (DualSenseButtons.DpadLeft, "←", "Gauche", GlyphShape.Rounded),
        (DualSenseButtons.DpadRight, "→", "Droite", GlyphShape.Rounded),
        (DualSenseButtons.L1, "L1", "L1", GlyphShape.Pill),
        (DualSenseButtons.R1, "R1", "R1", GlyphShape.Pill),
        (DualSenseButtons.L2, "L2", "L2 (bouton)", GlyphShape.Pill),
        (DualSenseButtons.R2, "R2", "R2 (bouton)", GlyphShape.Pill),
        (DualSenseButtons.L3, "L3", "L3", GlyphShape.Pill),
        (DualSenseButtons.R3, "R3", "R3", GlyphShape.Pill),
        (DualSenseButtons.Create, "CREATE", "Create", GlyphShape.Pill),
        (DualSenseButtons.Options, "OPTIONS", "Options", GlyphShape.Pill),
        (DualSenseButtons.PS, "PS", "PS", GlyphShape.Pill),
        (DualSenseButtons.Touchpad, "PAVÉ", "Clic pavé", GlyphShape.Pill),
        (DualSenseButtons.Mute, "MICRO", "Micro", GlyphShape.Pill),
    };

    public MappingPage(IShell shell)
    {
        _shell = shell;
        Content = _root;
        Build();
        App.Controller.ActiveProfileChanged += _ => Build();
    }

    private ProcessingSettings P => App.Controller.ActiveProfile.Processing;

    private void Apply(ProcessingSettings p)
    {
        App.Controller.Output.Processor.Settings = p;
        var profile = App.Controller.ActiveProfile with { Processing = p };
        _save.Run(() => App.Controller.UpdateProfile(profile));
    }

    private void Build()
    {
        _root.Children.Clear();
        _glyphs.Clear();
        _root.Children.Add(new PageHeader
        {
            Overline = "RÉGLER · PROFIL « " + App.Controller.ActiveProfile.Name.ToUpperInvariant() + " »",
            Title = "Mappage",
            Subtitle = "Réattribuez n'importe quel bouton et activez le turbo. Appuyez sur une touche de la manette : sa ligne s'allume.",
        });

        var actions = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 16) };
        actions.Children.Add(Ui.Button("Tout réinitialiser", "Button.Secondary", (_, _) =>
        {
            Apply(P with { Remap = new(), Turbo = DualSenseButtons.None });
            Build();
            _shell.Toast("Mappage réinitialisé", "Chaque bouton retrouve sa fonction d'origine.");
        }));
        _root.Children.Add(actions);

        // ── Tableau de réattribution (2 colonnes) ──
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(16) });
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        var col1 = new StackPanel();
        var col2 = new StackPanel();
        for (int i = 0; i < Buttons.Length; i++)
            (i < (Buttons.Length + 1) / 2 ? col1 : col2).Children.Add(Row(Buttons[i]));
        grid.Children.Add(Ui.Card(col1, null, 16));
        var c2 = Ui.Card(col2, null, 16);
        Grid.SetColumn(c2, 2);
        grid.Children.Add(c2);
        _root.Children.Add(grid);

        // ── Turbo ──
        var turbo = new StackPanel();
        turbo.Children.Add(Ui.Text("TURBO", "Text.Overline", new Thickness(0, 0, 0, 6)));
        turbo.Children.Add(Ui.Text("Cochez « Turbo » sur une ligne : maintenir le bouton répète l'appui automatiquement.", "Text.Caption", new Thickness(0, 0, 0, 14)));
        turbo.Children.Add(Ui.Slider("Cadence", 2, 30, P.TurboRate, v => ((int)v).ToString(CultureInfo.InvariantCulture) + " appuis/s", v => Apply(P with { TurboRate = v }), 1));
        _root.Children.Add(Ui.Card(turbo, new Thickness(0, 16, 0, 0), 20));

        // ── À venir ──
        var soon = new StackPanel();
        soon.Children.Add(Ui.Text("BIENTÔT", "Text.Overline", new Thickness(0, 0, 0, 8)));
        soon.Children.Add(Ui.Text("Couche secondaire (un bouton maintenu change toutes les fonctions) · macros · pavé tactile en zones et gestes · " +
                                  "mode clavier / souris · raccourcis système (PS + bouton).", "Text.Body"));
        _root.Children.Add(Ui.Card(soon, new Thickness(0, 16, 0, 0), 20));
    }

    private FrameworkElement Row((DualSenseButtons Flag, string Glyph, string Name, GlyphShape Shape) b)
    {
        var row = new Grid { Margin = new Thickness(0, 4, 0, 4) };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(96) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(28) });
        row.ColumnDefinitions.Add(new ColumnDefinition());
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var glyph = new ButtonGlyph { Text = b.Glyph, Shape = b.Shape, HorizontalAlignment = HorizontalAlignment.Left, ToolTip = b.Name };
        _glyphs.Add((b.Flag, glyph));
        row.Children.Add(glyph);

        var arrow = new AxisIcon { Width = 16, Height = 16 };
        arrow.SetResourceReference(AxisIcon.DataProperty, "Icon.ArrowRight");
        arrow.SetResourceReference(ForegroundProperty, "Text.Tertiary");
        Grid.SetColumn(arrow, 1);
        row.Children.Add(arrow);

        var combo = new ComboBox { Margin = new Thickness(0, 0, 12, 0) };
        combo.Items.Add(new ComboBoxItem { Content = "Inchangé", Tag = b.Flag });
        combo.Items.Add(new ComboBoxItem { Content = "Désactivé", Tag = DualSenseButtons.None });
        foreach (var t in Buttons.Where(t => t.Flag != b.Flag))
            combo.Items.Add(new ComboBoxItem { Content = t.Name, Tag = t.Flag });
        combo.SelectedIndex = !P.Remap.TryGetValue(b.Flag, out var current) ? 0
            : current == DualSenseButtons.None ? 1
            : Math.Max(0, combo.Items.Cast<ComboBoxItem>().ToList().FindIndex(i => (DualSenseButtons)i.Tag == current && i.Content as string != "Inchangé"));
        combo.SelectionChanged += (_, _) =>
        {
            var target = (DualSenseButtons)((ComboBoxItem)combo.SelectedItem).Tag;
            var map = new Dictionary<DualSenseButtons, DualSenseButtons>(P.Remap);
            if (combo.SelectedIndex == 0) map.Remove(b.Flag); else map[b.Flag] = target;
            Apply(P with { Remap = map });
        };
        Grid.SetColumn(combo, 2);
        row.Children.Add(combo);

        var turbo = new CheckBox { Content = "Turbo", IsChecked = (P.Turbo & b.Flag) != 0, VerticalAlignment = VerticalAlignment.Center };
        turbo.Checked += (_, _) => Apply(P with { Turbo = P.Turbo | b.Flag });
        turbo.Unchecked += (_, _) => Apply(P with { Turbo = P.Turbo & ~b.Flag });
        Grid.SetColumn(turbo, 3);
        row.Children.Add(turbo);
        return row;
    }

    public void Update(in FrameData frame)
    {
        var pressed = frame.IsConnected ? frame.State.Buttons : DualSenseButtons.None;
        foreach (var (flag, glyph) in _glyphs)
        {
            bool down = (pressed & flag) != 0;
            if (glyph.IsPressed != down) glyph.IsPressed = down;
        }
    }
}
