using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Se7en.DualSense.App.Controls;
using Se7en.DualSense.App.Services;
using Se7en.DualSense.Hid;
using Se7en.DualSense.Processing;
using MouseAction = Se7en.DualSense.Processing.MouseAction;

namespace Se7en.DualSense.App.Pages;

/// <summary>
/// Onglet Mappage : chaque bouton peut envoyer un autre bouton, une touche du clavier, un clic, une macro
/// ou une action système ; couche secondaire (bouton maintenu), turbo, pavé tactile (zones, gestes, souris),
/// sticks en souris / molette / déplacement, macros enregistrées depuis la manette.
/// </summary>
public sealed class MappingPage : UserControl, ILivePage
{
    private readonly IShell _shell;
    private readonly Ui.Debouncer _save = new();
    private readonly StackPanel _root = new();
    private readonly List<(DualSenseButtons Flag, ButtonGlyph Glyph)> _glyphs = new();
    private int _layer;
    private MacroRecorder? _recorder;

    /// <summary>Compatibilité : liste des boutons de la manette.</summary>
    public static (DualSenseButtons Flag, string Glyph, string Name, GlyphShape Shape)[] Buttons => Labels.Buttons;

    public MappingPage(IShell shell)
    {
        _shell = shell;
        Content = _root;
        Build();
        App.Controller.ActiveProfileChanged += _ => Build();
    }

    private ProcessingSettings P => App.Controller.ActiveProfile.Processing;

    private void Apply(ProcessingSettings p, bool rebuild = false)
    {
        App.Controller.PreviewProcessing(p);
        var profile = App.Controller.ActiveProfile with { Processing = p };
        // Enregistrement immédiat si la page se reconstruit (sinon le profil relu serait l'ancien)
        if (rebuild)
        {
            App.Controller.UpdateProfile(profile);
            Build();
        }
        else
        {
            _save.Run(() => App.Controller.UpdateProfile(profile));
        }
    }

    private void Build()
    {
        _root.Children.Clear();
        _glyphs.Clear();
        _root.Children.Add(new PageHeader
        {
            Overline = "RÉGLER · PROFIL « " + App.Controller.ActiveProfile.Name.ToUpperInvariant() + " »",
            Title = "Mappage",
            Subtitle = "Chaque bouton peut envoyer un autre bouton, une touche du clavier, un clic, une macro ou une action système. " +
                       "Appuyez sur une touche de la manette : sa ligne s'allume.",
        });

        // ── Couches ──
        var bar = new DockPanel { Margin = new Thickness(0, 0, 0, 16), LastChildFill = false };
        bar.Children.Add(Ui.Segmented(new[] { "Couche principale", "Couche secondaire" }, _layer, i => { _layer = i; Build(); }));
        var reset = Ui.Button("Tout réinitialiser", "Button.Secondary", (_, _) =>
        {
            Apply(P with
            {
                Remap = new(), Actions = new(), ShiftRemap = new(), ShiftActions = new(), ShiftButton = DualSenseButtons.None,
                Turbo = DualSenseButtons.None,
            }, rebuild: true);
            _shell.Toast("Mappage réinitialisé", "Chaque bouton retrouve sa fonction d'origine.");
        });
        DockPanel.SetDock(reset, Dock.Right);
        bar.Children.Add(reset);
        _root.Children.Add(bar);

        if (_layer == 1)
            _root.Children.Add(Ui.Card(LayerHeader(), new Thickness(0, 0, 0, 16), 20));

        // ── Tableau des boutons (2 colonnes) ──
        var rows = Labels.Buttons.ToList();
        bool edge = App.Controller.Slots.Any(s => s.Device?.IsEdge == true)
                    || P.Remap.Keys.Concat(P.Actions.Keys).Any(k => (k & DualSenseButtonGroups.Edge) != 0);
        if (edge) rows.AddRange(Labels.EdgeButtons);
        if (P.Touchpad.Mode == TouchpadMode.Zones)
            rows.AddRange(Labels.TouchInputs.Take(P.Touchpad.ZoneCount >= 4 ? 4 : 2));
        if (P.Touchpad.Gestures)
            rows.AddRange(Labels.TouchInputs.Skip(4));
        if (_layer == 1) rows.RemoveAll(r => r.Flag == P.ShiftButton);

        if (_layer == 0 || P.ShiftButton != DualSenseButtons.None)
        {
            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition());
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(16) });
            grid.ColumnDefinitions.Add(new ColumnDefinition());
            var col1 = new StackPanel();
            var col2 = new StackPanel();
            for (int i = 0; i < rows.Count; i++)
                (i < (rows.Count + 1) / 2 ? col1 : col2).Children.Add(Row(rows[i]));
            grid.Children.Add(Ui.Card(col1, null, 16));
            var c2 = Ui.Card(col2, null, 16);
            Grid.SetColumn(c2, 2);
            grid.Children.Add(c2);
            _root.Children.Add(grid);
        }

        if (_layer == 0)
        {
            _root.Children.Add(TouchpadCard());
            _root.Children.Add(SticksCard());
        }
        _root.Children.Add(MacrosCard());

        if (_layer == 0)
        {
            var turbo = new StackPanel();
            turbo.Children.Add(Ui.Text("TURBO", "Text.Overline", new Thickness(0, 0, 0, 6)));
            turbo.Children.Add(Ui.Text("Cochez « Turbo » sur une ligne : maintenir le bouton répète l'appui automatiquement.", "Text.Caption", new Thickness(0, 0, 0, 14)));
            turbo.Children.Add(Ui.Slider("Cadence", 2, 30, P.TurboRate, v => ((int)v).ToString(CultureInfo.InvariantCulture) + " appuis/s", v => Apply(P with { TurboRate = v }), 1));
            _root.Children.Add(Ui.Card(turbo, new Thickness(0, 16, 0, 0), 20));
        }
    }

    // ───────────── Couche secondaire ─────────────

    private FrameworkElement LayerHeader()
    {
        var panel = new StackPanel();
        panel.Children.Add(Ui.Text("COUCHE SECONDAIRE", "Text.Overline", new Thickness(0, 0, 0, 6)));
        panel.Children.Add(Ui.Text("Tant que le bouton de couche est maintenu, les boutons prennent les fonctions ci-dessous " +
                                   "(sans affectation, ils gardent celle de la couche principale). Le bouton de couche n'est plus envoyé au jeu.",
            "Text.Caption", new Thickness(0, 0, 0, 12)));
        var combo = new ComboBox { Width = 260, HorizontalAlignment = HorizontalAlignment.Left };
        combo.Items.Add(new ComboBoxItem { Content = "Aucune (désactivée)", Tag = DualSenseButtons.None });
        foreach (var b in Labels.Buttons.Concat(Labels.EdgeButtons))
            combo.Items.Add(new ComboBoxItem { Content = "Maintenir " + b.Name, Tag = b.Flag });
        combo.SelectedItem = combo.Items.Cast<ComboBoxItem>().FirstOrDefault(i => (DualSenseButtons)i.Tag == P.ShiftButton) ?? combo.Items[0];
        combo.SelectionChanged += (_, _) => Apply(P with { ShiftButton = (DualSenseButtons)((ComboBoxItem)combo.SelectedItem).Tag }, rebuild: true);
        panel.Children.Add(combo);
        return panel;
    }

    // ───────────── Une ligne : bouton → affectation ─────────────

    private Dictionary<DualSenseButtons, DualSenseButtons> RemapOf(ProcessingSettings p) => _layer == 1 ? p.ShiftRemap : p.Remap;
    private Dictionary<DualSenseButtons, InputAction> ActionsOf(ProcessingSettings p) => _layer == 1 ? p.ShiftActions : p.Actions;

    private ProcessingSettings With(ProcessingSettings p, Dictionary<DualSenseButtons, DualSenseButtons> remap, Dictionary<DualSenseButtons, InputAction> actions) =>
        _layer == 1 ? p with { ShiftRemap = remap, ShiftActions = actions } : p with { Remap = remap, Actions = actions };

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

        var combo = AssignmentCombo(b.Flag);
        Grid.SetColumn(combo, 2);
        row.Children.Add(combo);

        if (_layer == 0)
        {
            var turbo = new CheckBox { Content = "Turbo", IsChecked = (P.Turbo & b.Flag) != 0, VerticalAlignment = VerticalAlignment.Center };
            turbo.Checked += (_, _) => Apply(P with { Turbo = P.Turbo | b.Flag });
            turbo.Unchecked += (_, _) => Apply(P with { Turbo = P.Turbo & ~b.Flag });
            Grid.SetColumn(turbo, 3);
            row.Children.Add(turbo);
        }
        return row;
    }

    private ComboBox AssignmentCombo(DualSenseButtons flag)
    {
        var combo = new ComboBox { Margin = new Thickness(0, 0, 12, 0) };
        void Item(string text, string tag) => combo.Items.Add(new ComboBoxItem { Content = text, Tag = tag });
        void Header(string text)
        {
            var h = new ComboBoxItem { Content = text, IsEnabled = false, FontSize = 10, FontWeight = FontWeights.SemiBold, Padding = new Thickness(12, 10, 12, 4) };
            h.SetResourceReference(ForegroundProperty, "Text.Tertiary");
            combo.Items.Add(h);
        }

        var remap = RemapOf(P);
        var actions = ActionsOf(P);
        bool virtualInput = (flag & DualSenseButtonGroups.Virtual) != 0;

        Item(_layer == 1 ? "Comme la couche principale" : virtualInput ? "Aucune action" : "Inchangé", "keep");
        Item("Désactivé", "none");
        Header("BOUTON DE MANETTE");
        foreach (var t in Labels.Buttons.Where(t => t.Flag != flag))
            Item(t.Name, "btn:" + (uint)t.Flag);
        Header("CLAVIER");
        if (actions.TryGetValue(flag, out var current) && current.Kind == ActionKind.Key)
            Item(Labels.Describe(current, P.Macros), "key-current");
        Item("Choisir une touche…", "key-new");
        Header("SOURIS");
        foreach (var (m, name) in Labels.MouseActions)
            Item(name, "mouse:" + m);
        if (P.Macros.Count > 0)
        {
            Header("MACROS");
            foreach (var m in P.Macros)
                Item("Macro « " + m.Name + " »", "macro:" + m.Id);
        }
        Header("SYSTÈME");
        foreach (var (s, name) in Labels.SystemActions)
            Item(name, "sys:" + s);

        string selected = actions.TryGetValue(flag, out var a)
            ? a.Kind switch
            {
                ActionKind.Key => "key-current",
                ActionKind.Mouse => "mouse:" + a.Mouse,
                ActionKind.Macro => "macro:" + a.MacroId,
                _ => "sys:" + a.System,
            }
            : remap.TryGetValue(flag, out var r) ? (r == DualSenseButtons.None ? "none" : "btn:" + (uint)r) : "keep";
        combo.SelectedItem = combo.Items.Cast<ComboBoxItem>().FirstOrDefault(i => i.Tag as string == selected) ?? combo.Items[0];

        combo.SelectionChanged += (_, _) =>
        {
            if (combo.SelectedItem is not ComboBoxItem { Tag: string tag }) return;
            if (tag == "key-current") return;
            if (tag == "key-new")
            {
                CaptureKeys(flag);
                return;
            }
            var newRemap = new Dictionary<DualSenseButtons, DualSenseButtons>(RemapOf(P));
            var newActions = new Dictionary<DualSenseButtons, InputAction>(ActionsOf(P));
            newRemap.Remove(flag);
            newActions.Remove(flag);
            string[] parts = tag.Split(':', 2);
            switch (parts[0])
            {
                case "none": newRemap[flag] = DualSenseButtons.None; break;
                case "btn": newRemap[flag] = (DualSenseButtons)uint.Parse(parts[1]); break;
                case "mouse": newActions[flag] = InputAction.ForMouse(Enum.Parse<MouseAction>(parts[1])); break;
                case "sys": newActions[flag] = InputAction.ForSystem(Enum.Parse<SystemAction>(parts[1])); break;
                case "macro": newActions[flag] = InputAction.ForMacro(parts[1]); break;
            }
            Apply(With(P, newRemap, newActions));
        };
        return combo;
    }

    /// <summary>Fenêtre de capture : appuyez sur la touche (ou la combinaison) à envoyer.</summary>
    private void CaptureKeys(DualSenseButtons flag)
    {
        var keys = new List<ushort>();
        var body = new StackPanel { Width = 460 };
        body.Children.Add(Ui.Text("CLAVIER · " + Labels.ButtonName(flag).ToUpperInvariant(), "Text.Overline", new Thickness(0, 0, 0, 10)));
        body.Children.Add(Ui.Text("Appuyez sur une touche", "Text.H1", new Thickness(0, 0, 0, 8)));
        body.Children.Add(Ui.Text("Une combinaison est possible (Ctrl + C, Alt + Tab…). Les touches sont envoyées par leur position physique : " +
                                  "elles fonctionnent aussi dans les jeux.", "Text.Body", new Thickness(0, 0, 0, 18)));
        var display = new TextBlock { Text = "…", FontSize = 22, HorizontalAlignment = HorizontalAlignment.Center };
        display.SetResourceReference(TextBlock.FontFamilyProperty, "Font.Mono");
        display.SetResourceReference(TextBlock.ForegroundProperty, "Text.Primary");
        var zone = new Border { Height = 72, CornerRadius = new CornerRadius(12), Focusable = true, Child = display, BorderThickness = new Thickness(1.5) };
        zone.SetResourceReference(Border.BackgroundProperty, "Surface.Inset");
        zone.SetResourceReference(Border.BorderBrushProperty, "Accent");
        display.VerticalAlignment = VerticalAlignment.Center;
        zone.PreviewKeyDown += (_, e) =>
        {
            e.Handled = true;
            var key = e.Key == Key.System ? e.SystemKey : e.Key;
            if (key == Key.Escape && keys.Count == 0) { _shell.CloseModal(); return; }
            ushort vk = (ushort)KeyInterop.VirtualKeyFromKey(key);
            if (vk == 0) return;
            bool modifier = key is Key.LeftCtrl or Key.RightCtrl or Key.LeftShift or Key.RightShift or Key.LeftAlt or Key.RightAlt or Key.LWin or Key.RWin;
            // Nouvelle combinaison dès qu'une touche non modificatrice a déjà été saisie
            if (keys.Count > 0 && !IsModifier(keys[^1])) keys.Clear();
            if (!keys.Contains(vk)) keys.Add(vk);
            if (!modifier) keys.Sort((x, y) => IsModifier(y).CompareTo(IsModifier(x)));
            display.Text = string.Join(" + ", keys.Select(Labels.KeyName));
        };
        body.Children.Add(zone);

        var actions = new DockPanel { Margin = new Thickness(0, 24, 0, 0), LastChildFill = false };
        actions.Children.Add(Ui.Button("Annuler", "Button.Secondary", (_, _) => { _shell.CloseModal(); Build(); }));
        var ok = Ui.Button("Valider", "Button.Primary", (_, _) =>
        {
            _shell.CloseModal();
            if (keys.Count == 0) { Build(); return; }
            var newRemap = new Dictionary<DualSenseButtons, DualSenseButtons>(RemapOf(P));
            var newActions = new Dictionary<DualSenseButtons, InputAction>(ActionsOf(P));
            newRemap.Remove(flag);
            newActions[flag] = InputAction.ForKeys(keys.ToArray());
            Apply(With(P, newRemap, newActions), rebuild: true);
        });
        DockPanel.SetDock(ok, Dock.Right);
        actions.Children.Add(ok);
        body.Children.Add(actions);
        _shell.ShowModal(body);
        zone.Dispatcher.BeginInvoke(() => Keyboard.Focus(zone), System.Windows.Threading.DispatcherPriority.Input);
    }

    private static bool IsModifier(ushort vk) => vk is 0x10 or 0x11 or 0x12 or 0x5B or 0x5C or 0xA0 or 0xA1 or 0xA2 or 0xA3 or 0xA4 or 0xA5;

    // ───────────── Pavé tactile ─────────────

    private FrameworkElement TouchpadCard()
    {
        var t = P.Touchpad;
        var panel = new StackPanel();
        panel.Children.Add(Ui.Text("PAVÉ TACTILE", "Text.Overline", new Thickness(0, 0, 0, 12)));
        panel.Children.Add(Ui.Segmented(new[] { "Normal", "Zones cliquables", "Souris" }, (int)t.Mode,
            i => Apply(P with { Touchpad = P.Touchpad with { Mode = (TouchpadMode)i } }, rebuild: true)));
        panel.Children.Add(Ui.Text(t.Mode switch
        {
            TouchpadMode.Zones => "Le clic dépend de l'endroit touché : chaque zone devient un bouton (lignes Z1 à Z4 ci-dessus). Sans affectation, elle reste un clic de pavé.",
            TouchpadMode.Mouse => "Un doigt déplace le curseur, clic = clic gauche, clic à deux doigts = clic droit, deux doigts qui glissent = molette.",
            _ => "Le pavé est transmis au jeu tel quel (mode DualShock 4) ; le clic reste le bouton « pavé ».",
        }, "Text.Caption", new Thickness(0, 8, 0, 8)));

        if (t.Mode == TouchpadMode.Zones)
            panel.Children.Add(new SettingRow
            {
                Label = "Nombre de zones",
                Description = "2 : gauche / droite · 4 : un quart chacune",
                Control = Ui.Segmented(new[] { "2 zones", "4 zones" }, t.ZoneCount >= 4 ? 1 : 0,
                    i => Apply(P with { Touchpad = P.Touchpad with { ZoneCount = i == 1 ? 4 : 2 } }, rebuild: true)),
            });
        if (t.Mode == TouchpadMode.Mouse)
            panel.Children.Add(Ui.Slider("Vitesse du curseur", 0.2, 3, t.MouseSpeed, v => v.ToString("0.0", CultureInfo.InvariantCulture) + "×",
                v => Apply(P with { Touchpad = P.Touchpad with { MouseSpeed = v } }), 0.1));
        panel.Children.Add(new SettingRow
        {
            Label = "Gestes de glissement",
            Description = "Glisser vite un doigt vers le haut, le bas, la gauche ou la droite = 4 entrées de plus à affecter.",
            Control = Ui.Switch(t.Gestures, v => Apply(P with { Touchpad = P.Touchpad with { Gestures = v } }, rebuild: true)),
            Last = true,
        });
        return Ui.Card(panel, new Thickness(0, 16, 0, 0), 20);
    }

    // ───────────── Sticks souris ─────────────

    private FrameworkElement SticksCard()
    {
        var m = P.Mouse;
        var panel = new StackPanel();
        panel.Children.Add(Ui.Text("STICKS EN SOURIS", "Text.Overline", new Thickness(0, 0, 0, 6)));
        panel.Children.Add(Ui.Text("Pour les jeux sans manette ou le bureau : un stick peut piloter le curseur, la molette ou les touches de déplacement.",
            "Text.Caption", new Thickness(0, 0, 0, 8)));
        string[] roles = { "Manette", "Souris", "Molette", "Déplacement (ZQSD / WASD)" };
        panel.Children.Add(new SettingRow
        {
            Label = "Stick gauche",
            Control = Ui.Segmented(roles, (int)m.LeftStick, i => Apply(P with { Mouse = P.Mouse with { LeftStick = (StickRole)i } }, rebuild: true)),
        });
        panel.Children.Add(new SettingRow
        {
            Label = "Stick droit",
            Control = Ui.Segmented(roles, (int)m.RightStick, i => Apply(P with { Mouse = P.Mouse with { RightStick = (StickRole)i } }, rebuild: true)),
            Last = m.LeftStick != StickRole.Mouse && m.RightStick != StickRole.Mouse && m.LeftStick != StickRole.Scroll && m.RightStick != StickRole.Scroll,
        });
        if (m.LeftStick == StickRole.Mouse || m.RightStick == StickRole.Mouse)
            panel.Children.Add(Ui.Slider("Vitesse du curseur", 0.2, 3, m.Speed, v => v.ToString("0.0", CultureInfo.InvariantCulture) + "×",
                v => Apply(P with { Mouse = P.Mouse with { Speed = v } }), 0.1));
        if (m.LeftStick == StickRole.Scroll || m.RightStick == StickRole.Scroll)
            panel.Children.Add(Ui.Slider("Vitesse de défilement", 0.2, 3, m.ScrollSpeed, v => v.ToString("0.0", CultureInfo.InvariantCulture) + "×",
                v => Apply(P with { Mouse = P.Mouse with { ScrollSpeed = v } }), 0.1));
        return Ui.Card(panel, new Thickness(0, 16, 0, 0), 20);
    }

    // ───────────── Macros ─────────────

    private FrameworkElement MacrosCard()
    {
        var panel = new StackPanel();
        var head = new DockPanel { Margin = new Thickness(0, 0, 0, 6) };
        var add = Ui.Button("Nouvelle macro", "Button.Primary", (_, _) => OpenRecorder(null));
        DockPanel.SetDock(add, Dock.Right);
        head.Children.Add(add);
        var titles = new StackPanel();
        titles.Children.Add(Ui.Text("MACROS", "Text.Overline"));
        titles.Children.Add(Ui.Text("Enregistrez une suite d'appuis en la jouant sur la manette, puis affectez-la à un bouton (liste « Macros »).",
            "Text.Caption", new Thickness(0, 6, 24, 0)));
        head.Children.Add(titles);
        panel.Children.Add(head);

        if (P.Macros.Count == 0)
            panel.Children.Add(Ui.Text("Aucune macro pour ce profil.", "Text.Body", new Thickness(0, 12, 0, 0)));
        foreach (var macro in P.Macros)
        {
            var m = macro;
            var used = P.Actions.Concat(P.ShiftActions).Where(kv => kv.Value.Kind == ActionKind.Macro && kv.Value.MacroId == m.Id)
                .Select(kv => Labels.ButtonName(kv.Key)).ToList();
            var row = new SettingRow
            {
                Label = m.Name,
                Description = $"{m.Steps.Count} étape(s) · {m.TotalMs / 1000.0:0.0} s · " + m.Mode switch
                {
                    MacroMode.WhileHeld => "en boucle tant que maintenu",
                    MacroMode.Toggle => "bascule (appui = marche / arrêt)",
                    _ => "une fois par appui",
                } + (used.Count > 0 ? " · sur " + string.Join(", ", used) : " · non affectée"),
            };
            var buttons = new StackPanel { Orientation = Orientation.Horizontal };
            buttons.Children.Add(Ui.Button("Modifier", "Button.Secondary", (_, _) => OpenRecorder(m), new Thickness(0, 0, 8, 0)));
            buttons.Children.Add(Ui.Button("Supprimer", "Button.Secondary", (_, _) =>
            {
                var macros = P.Macros.Where(x => x.Id != m.Id).ToList();
                // Les boutons qui lançaient la macro redeviennent normaux
                var actions = P.Actions.Where(kv => kv.Value.MacroId != m.Id).ToDictionary(kv => kv.Key, kv => kv.Value);
                var shift = P.ShiftActions.Where(kv => kv.Value.MacroId != m.Id).ToDictionary(kv => kv.Key, kv => kv.Value);
                Apply(P with { Macros = macros, Actions = actions, ShiftActions = shift }, rebuild: true);
            }));
            row.Control = buttons;
            panel.Children.Add(row);
        }
        return Ui.Card(panel, new Thickness(0, 16, 0, 0), 20);
    }

    private void OpenRecorder(Macro? existing)
    {
        _recorder = new MacroRecorder(_shell, existing, macro =>
        {
            var macros = P.Macros.Where(x => x.Id != macro.Id).Append(macro).ToList();
            Apply(P with { Macros = macros }, rebuild: true);
            _shell.Toast("Macro enregistrée", "Affectez-la à un bouton dans le tableau (liste « Macros »).", NotifyKind.Success);
        }, () => _recorder = null);
        _recorder.Show();
    }

    public void Update(in FrameData frame)
    {
        var pressed = frame.IsConnected ? frame.State.Buttons : DualSenseButtons.None;
        foreach (var (flag, glyph) in _glyphs)
        {
            bool down = (pressed & flag) != 0;
            if (glyph.IsPressed != down) glyph.IsPressed = down;
        }
        _recorder?.Update(frame);
    }
}

/// <summary>Enregistreur de macro : on joue la séquence sur la manette, chaque changement devient une étape.</summary>
public sealed class MacroRecorder
{
    private readonly IShell _shell;
    private readonly Macro _macro;
    private readonly Action<Macro> _save;
    private readonly Action _closed;
    private readonly List<MacroStep> _steps;
    private readonly System.Diagnostics.Stopwatch _clock = new();
    private bool _recording;
    private DualSenseButtons _current;
    private double _since;
    private TextBlock _status = null!, _timeline = null!;
    private Button _recordButton = null!;
    private TextBox _name = null!;
    private MacroMode _mode;

    public MacroRecorder(IShell shell, Macro? existing, Action<Macro> save, Action closed)
    {
        _shell = shell;
        _macro = existing ?? new Macro { Name = "Macro " + DateTime.Now.ToString("HH:mm") };
        _steps = existing?.Steps.ToList() ?? new List<MacroStep>();
        _mode = _macro.Mode;
        _save = save;
        _closed = closed;
    }

    public void Show()
    {
        var body = new StackPanel { Width = 560 };
        body.Children.Add(Ui.Text("MACRO", "Text.Overline", new Thickness(0, 0, 0, 10)));
        body.Children.Add(Ui.Text("Enregistrer une séquence", "Text.H1", new Thickness(0, 0, 0, 8)));
        body.Children.Add(Ui.Text("Appuyez sur « Enregistrer », jouez la séquence sur la manette (les temps sont respectés), puis « Arrêter ». " +
                                  "La navigation à la manette est suspendue pendant l'enregistrement.", "Text.Body", new Thickness(0, 0, 0, 16)));

        body.Children.Add(Ui.Text("NOM", "Text.Overline", new Thickness(0, 0, 0, 6)));
        _name = new TextBox { Text = _macro.Name, Margin = new Thickness(0, 0, 0, 16) };
        body.Children.Add(_name);

        body.Children.Add(Ui.Text("LECTURE", "Text.Overline", new Thickness(0, 0, 0, 6)));
        body.Children.Add(Ui.Segmented(new[] { "Une fois", "Tant que maintenu", "Bascule" }, (int)_mode, i => _mode = (MacroMode)i));

        var rec = new DockPanel { Margin = new Thickness(0, 20, 0, 8) };
        _recordButton = Ui.Button("Enregistrer", "Button.Accent", (_, _) => ToggleRecording());
        DockPanel.SetDock(_recordButton, Dock.Right);
        rec.Children.Add(_recordButton);
        _status = Ui.Text("", "Text.Label");
        _status.VerticalAlignment = VerticalAlignment.Center;
        rec.Children.Add(_status);
        body.Children.Add(rec);

        _timeline = new TextBlock { TextWrapping = TextWrapping.Wrap, MinHeight = 60, FontSize = 12 };
        _timeline.SetResourceReference(TextBlock.FontFamilyProperty, "Font.Mono");
        _timeline.SetResourceReference(TextBlock.ForegroundProperty, "Text.Secondary");
        body.Children.Add(new Border { Child = _timeline, Padding = new Thickness(14), CornerRadius = new CornerRadius(10) }
            .WithResource(Border.BackgroundProperty, "Surface.Inset"));

        var actions = new DockPanel { Margin = new Thickness(0, 24, 0, 0), LastChildFill = false };
        actions.Children.Add(Ui.Button("Annuler", "Button.Secondary", (_, _) => Close()));
        var ok = Ui.Button("Enregistrer la macro", "Button.Primary", (_, _) =>
        {
            if (_recording) StopRecording();
            if (_steps.Count == 0) { _shell.Toast("Macro vide", "Enregistrez au moins un appui.", NotifyKind.Warning); return; }
            var macro = _macro with { Name = string.IsNullOrWhiteSpace(_name.Text) ? _macro.Name : _name.Text.Trim(), Steps = _steps.ToList(), Mode = _mode };
            Close();
            _save(macro);
        });
        DockPanel.SetDock(ok, Dock.Right);
        actions.Children.Add(ok);
        body.Children.Add(actions);

        Refresh();
        _shell.ShowModal(body);
    }

    private void Close()
    {
        _recording = false;
        App.SuspendControllerNavigation = false;
        _shell.CloseModal();
        _closed();
    }

    private void ToggleRecording()
    {
        if (_recording) StopRecording();
        else
        {
            _steps.Clear();
            _recording = true;
            _current = DualSenseButtons.None;
            _clock.Restart();
            _since = 0;
            App.SuspendControllerNavigation = true;
            _recordButton.Content = "Arrêter";
        }
        Refresh();
    }

    private void StopRecording()
    {
        _recording = false;
        App.SuspendControllerNavigation = false;
        double now = _clock.Elapsed.TotalMilliseconds;
        if (_current != DualSenseButtons.None)
            _steps.Add(new MacroStep(_current, (int)Math.Max(16, now - _since)));
        // La pause avant le premier appui ne fait pas partie de la macro
        while (_steps.Count > 0 && _steps[0].Buttons == DualSenseButtons.None) _steps.RemoveAt(0);
        _recordButton.Content = "Enregistrer";
        Refresh();
    }

    public void Update(in FrameData frame)
    {
        if (!_recording || !frame.IsConnected) return;
        // Seuls les boutons comptent (pas les bits virtuels du pavé)
        var buttons = frame.State.Buttons & ~DualSenseButtonGroups.Virtual;
        if (buttons == _current) return;
        double now = _clock.Elapsed.TotalMilliseconds;
        if (_since > 0 || _current != DualSenseButtons.None || _steps.Count > 0)
            _steps.Add(new MacroStep(_current, (int)Math.Max(16, now - _since)));
        _current = buttons;
        _since = now;
        Refresh();
    }

    private void Refresh()
    {
        _status.Text = _recording ? "● Enregistrement… jouez la séquence sur la manette" : _steps.Count == 0 ? "Aucune étape" : $"{_steps.Count} étape(s) · {_steps.Sum(s => s.DurationMs) / 1000.0:0.00} s";
        _status.SetResourceReference(TextBlock.ForegroundProperty, _recording ? "Danger" : "Text.Secondary");
        _timeline.Text = _steps.Count == 0 ? "—" : string.Join("   ", _steps.Select(s => $"{Labels.Combo(s.Buttons)} {s.DurationMs} ms"));
    }
}

internal static class ElementExtensions
{
    public static T WithResource<T>(this T element, DependencyProperty property, string key) where T : FrameworkElement
    {
        element.SetResourceReference(property, key);
        return element;
    }
}
