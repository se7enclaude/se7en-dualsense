using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Win32;
using Se7en.DualSense.App.Controls;
using Se7en.DualSense.App.Services;
using Se7en.DualSense.Output;

namespace Se7en.DualSense.App.Pages;

/// <summary>
/// Onglet Jeux &amp; profils : profils (un par jeu, avec son mode), jeux associés, import/export,
/// et bibliothèque détectée (Steam, Epic, Rockstar) pour créer un profil en un clic.
/// </summary>
public sealed class GamesPage : UserControl
{
    private readonly IShell _shell;
    private readonly StackPanel _root = new();
    private readonly StackPanel _list = new();
    private readonly ContentControl _editor = new();
    private readonly WrapPanel _library = new();
    private readonly TextBox _search = new() { Width = 260 };
    private List<InstalledGame>? _games;
    private string _selectedId;

    public GamesPage(IShell shell)
    {
        _shell = shell;
        _selectedId = App.Controller.ActiveProfile.Id;
        Content = _root;
        Build();
        App.Controller.Profiles.Changed += () => Dispatcher.BeginInvoke(RefreshList);
        App.Controller.ActiveProfileChanged += _ => RefreshList();
        _search.TextChanged += (_, _) => RenderLibrary();
        Loaded += async (_, _) => { if (_games == null) await LoadLibraryAsync(); };
    }

    private Profile? Selected => App.Controller.Profiles.Find(_selectedId);

    private void Build()
    {
        _root.Children.Clear();
        _root.Children.Add(new PageHeader
        {
            Overline = "JOUER",
            Title = "Jeux & profils",
            Subtitle = "Un profil par jeu, avec son mode et ses réglages. Quand un jeu associé démarre, son profil s'applique tout seul.",
        });

        // ── Profils ──
        var grid = new Grid { Margin = new Thickness(0, 0, 0, 16) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(300) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(16) });
        grid.ColumnDefinitions.Add(new ColumnDefinition());

        var left = new StackPanel();
        var head = new DockPanel { Margin = new Thickness(0, 0, 0, 12) };
        var buttons = new StackPanel { Orientation = Orientation.Horizontal };
        DockPanel.SetDock(buttons, Dock.Right);
        buttons.Children.Add(Ui.Button("Importer", "Button.Secondary", (_, _) => Import(), new Thickness(0, 0, 6, 0)));
        buttons.Children.Add(Ui.Button("Nouveau", "Button.Primary", (_, _) => Create(null, null)));
        head.Children.Add(buttons);
        head.Children.Add(Ui.Text("PROFILS", "Text.Overline"));
        left.Children.Add(head);
        left.Children.Add(_list);
        grid.Children.Add(Ui.Card(left, null, 16));

        Grid.SetColumn(_editor, 2);
        grid.Children.Add(_editor);
        _root.Children.Add(grid);

        // ── Bibliothèque ──
        var lib = new StackPanel();
        var libHead = new DockPanel { Margin = new Thickness(0, 0, 0, 6) };
        var libTools = new StackPanel { Orientation = Orientation.Horizontal };
        DockPanel.SetDock(libTools, Dock.Right);
        libTools.Children.Add(_search);
        libTools.Children.Add(Ui.Button("Actualiser", "Button.Secondary", async (_, _) => await LoadLibraryAsync(), new Thickness(8, 0, 0, 0)));
        libHead.Children.Add(libTools);
        libHead.Children.Add(Ui.Text("BIBLIOTHÈQUE", "Text.Overline"));
        lib.Children.Add(libHead);
        lib.Children.Add(Ui.Text("Jeux Steam, Epic Games et Rockstar Games détectés sur ce PC. Pour les jeux Steam, désactivez Steam Input dans les propriétés du jeu " +
                                 "si les touches semblent doublées.", "Text.Caption", new Thickness(0, 0, 0, 16)));
        lib.Children.Add(_library);
        _root.Children.Add(Ui.Card(lib, null, 20));

        RefreshList();
    }

    // ───────────── Profils ─────────────

    private void RefreshList()
    {
        if (Selected == null) _selectedId = App.Controller.ActiveProfile.Id;
        _list.Children.Clear();
        foreach (var p in App.Controller.Profiles.All)
            _list.Children.Add(ProfileItem(p));
        _editor.Content = Editor(Selected!);
        RenderLibrary();
    }

    private FrameworkElement ProfileItem(Profile p)
    {
        bool selected = p.Id == _selectedId;
        bool active = p.Id == App.Controller.ActiveProfile.Id;
        var name = Ui.Text(p.Name, "Text.Label");
        name.FontSize = 14;
        var meta = Ui.Text(p.Mode.Label() + (p.Executables.Count > 0 ? $" · {p.Executables.Count} jeu(x)" : p.IsDefault ? " · tous les jeux" : ""), "Text.Caption", new Thickness(0, 3, 0, 0));
        var texts = new StackPanel();
        texts.Children.Add(name);
        texts.Children.Add(meta);
        var row = new DockPanel();
        if (active)
        {
            var badge = new Badge { Text = App.Controller.ActiveGame != null ? "En jeu" : "Actif", Tone = BadgeTone.Success, ShowDot = true, VerticalAlignment = VerticalAlignment.Center };
            DockPanel.SetDock(badge, Dock.Right);
            row.Children.Add(badge);
        }
        row.Children.Add(texts);

        var item = new Button { Content = row, HorizontalContentAlignment = HorizontalAlignment.Stretch, Margin = new Thickness(0, 0, 0, 6), Cursor = System.Windows.Input.Cursors.Hand };
        item.Template = ItemTemplate(selected);
        item.Click += (_, _) => { _selectedId = p.Id; RefreshList(); };
        return item;
    }

    private static ControlTemplate ItemTemplate(bool selected)
    {
        var t = new ControlTemplate(typeof(Button));
        var bd = new FrameworkElementFactory(typeof(Border));
        bd.SetValue(Border.CornerRadiusProperty, new CornerRadius(8));
        bd.SetValue(Border.PaddingProperty, new Thickness(12, 10, 12, 10));
        bd.SetValue(Border.BorderThicknessProperty, new Thickness(1));
        bd.SetResourceReference(Border.BackgroundProperty, selected ? "Surface.Raised" : "Surface.Card");
        bd.SetResourceReference(Border.BorderBrushProperty, selected ? "Ink" : "Border.Hairline");
        var cp = new FrameworkElementFactory(typeof(ContentPresenter));
        bd.AppendChild(cp);
        t.VisualTree = bd;
        return t;
    }

    private FrameworkElement Editor(Profile p)
    {
        var root = new StackPanel();
        root.Children.Add(Ui.Text(p.IsDefault ? "PROFIL PAR DÉFAUT" : "PROFIL", "Text.Overline", new Thickness(0, 0, 0, 10)));

        var name = new TextBox { Text = p.Name, IsEnabled = !p.IsDefault, Margin = new Thickness(0, 0, 0, 18) };
        name.LostFocus += (_, _) =>
        {
            var n = name.Text.Trim();
            if (n.Length > 0 && n != p.Name) App.Controller.UpdateProfile(Selected! with { Name = n });
        };
        root.Children.Add(name);

        root.Children.Add(Ui.Text("LES JEUX VOIENT", "Text.Overline", new Thickness(0, 0, 0, 8)));
        root.Children.Add(Ui.Segmented(new[] { "DualShock 4", "Xbox 360", "DualSense native" }, (int)p.Mode,
            i => App.Controller.UpdateProfile(Selected! with { Mode = (OutputMode)i })));

        root.Children.Add(Ui.Text("JEUX ASSOCIÉS", "Text.Overline", new Thickness(0, 22, 0, 8)));
        if (p.IsDefault)
        {
            root.Children.Add(Ui.Text("Le profil par défaut s'applique quand aucun jeu associé à un autre profil ne tourne.", "Text.Caption"));
        }
        else
        {
            var chips = new WrapPanel();
            foreach (var exe in p.Executables)
                chips.Children.Add(Chip(exe, () => App.Controller.UpdateProfile(Selected! with { Executables = Selected!.Executables.Where(e => e != exe).ToList() })));
            if (p.Executables.Count == 0)
                chips.Children.Add(Ui.Text("Aucun jeu : ajoutez l'exécutable du jeu pour l'activation automatique.", "Text.Caption"));
            root.Children.Add(chips);
            root.Children.Add(Ui.Button("Ajouter un exécutable…", "Button.Secondary", (_, _) => AddExecutable(), new Thickness(0, 10, 0, 0)));
            ((Button)root.Children[^1]).HorizontalAlignment = HorizontalAlignment.Left;
        }

        var actions = new WrapPanel { Margin = new Thickness(0, 26, 0, 0) };
        bool active = p.Id == App.Controller.ActiveProfile.Id;
        var use = Ui.Button(active ? "Profil actif" : "Utiliser ce profil", "Button.Primary", (_, _) =>
        {
            App.Controller.SelectProfile(Selected!);
            _shell.Toast($"Profil « {Selected!.Name} » activé", Selected!.Mode.Label(), NotifyKind.Success);
        }, new Thickness(0, 0, 8, 8));
        use.IsEnabled = !active;
        actions.Children.Add(use);
        actions.Children.Add(Ui.Button("Dupliquer", "Button.Secondary", (_, _) =>
        {
            var copy = Selected! with { Id = Guid.NewGuid().ToString("N"), Name = Selected!.Name + " (copie)", Executables = new() };
            App.Controller.Profiles.Save(copy);
            _selectedId = copy.Id;
            RefreshList();
        }, new Thickness(0, 0, 8, 8)));
        actions.Children.Add(Ui.Button("Exporter…", "Button.Secondary", (_, _) => Export(), new Thickness(0, 0, 8, 8)));
        if (!p.IsDefault)
            actions.Children.Add(Ui.Button("Supprimer", "Button.Secondary", (_, _) => Delete(), new Thickness(0, 0, 8, 8)));
        root.Children.Add(actions);

        return Ui.Card(root, null, 20);
    }

    private static Border Chip(string text, Action remove)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal };
        row.Children.Add(Ui.Text(Path.GetFileName(text), "Text.Readout"));
        var x = new Button { Content = "✕", Margin = new Thickness(8, 0, 0, 0), Cursor = System.Windows.Input.Cursors.Hand, ToolTip = "Retirer" };
        x.SetResourceReference(StyleProperty, "IconButton.Ghost");
        x.Width = 22;
        x.Height = 22;
        x.Click += (_, _) => remove();
        row.Children.Add(x);
        var chip = new Border { CornerRadius = new CornerRadius(14), Padding = new Thickness(12, 3, 4, 3), Margin = new Thickness(0, 0, 8, 8), Child = row };
        chip.SetResourceReference(Border.BackgroundProperty, "Surface.Inset");
        return chip;
    }

    private void Create(string? name, string? exe)
    {
        var baseProfile = App.Controller.Profiles.Default;
        var p = baseProfile with
        {
            Id = Guid.NewGuid().ToString("N"),
            Name = name ?? "Nouveau profil",
            Executables = exe != null ? new List<string> { Path.GetFileName(exe) } : new(),
        };
        App.Controller.Profiles.Save(p);
        _selectedId = p.Id;
        RefreshList();
        _shell.Toast($"Profil « {p.Name} » créé", exe != null ? $"S'appliquera au lancement de {Path.GetFileName(exe)}" : "Ajoutez-y l'exécutable d'un jeu.", NotifyKind.Success);
    }

    private void AddExecutable()
    {
        var dlg = new OpenFileDialog { Title = "Choisir l'exécutable du jeu", Filter = "Programmes (*.exe)|*.exe" };
        if (dlg.ShowDialog() != true) return;
        string exe = Path.GetFileName(dlg.FileName);
        var p = Selected!;
        if (!p.Executables.Contains(exe, StringComparer.OrdinalIgnoreCase))
            App.Controller.UpdateProfile(p with { Executables = p.Executables.Append(exe).ToList() });
    }

    private void Export()
    {
        var p = Selected!;
        var dlg = new SaveFileDialog { Title = "Exporter le profil", FileName = p.Name + ".se7en.json", Filter = "Profil se7en (*.json)|*.json" };
        if (dlg.ShowDialog() != true) return;
        App.Controller.Profiles.Export(p, dlg.FileName);
        _shell.Toast("Profil exporté", Path.GetFileName(dlg.FileName), NotifyKind.Success);
    }

    private void Import()
    {
        var dlg = new OpenFileDialog { Title = "Importer un profil", Filter = "Profil se7en (*.json)|*.json" };
        if (dlg.ShowDialog() != true) return;
        try
        {
            var p = App.Controller.Profiles.Import(dlg.FileName);
            _selectedId = p.Id;
            RefreshList();
            _shell.Toast($"Profil « {p.Name} » importé", "", NotifyKind.Success);
        }
        catch (Exception ex)
        {
            _shell.Toast("Import impossible", ex.Message, NotifyKind.Warning);
        }
    }

    private void Delete()
    {
        var p = Selected!;
        if (MessageBox.Show($"Supprimer le profil « {p.Name} » ?", "Supprimer", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
            return;
        if (App.Controller.ActiveProfile.Id == p.Id)
            App.Controller.SelectProfile(App.Controller.Profiles.Default);
        App.Controller.Profiles.Delete(p.Id);
        _selectedId = App.Controller.ActiveProfile.Id;
        RefreshList();
    }

    // ───────────── Bibliothèque ─────────────

    private async Task LoadLibraryAsync()
    {
        _library.Children.Clear();
        _library.Children.Add(Ui.Text("Recherche des jeux installés…", "Text.Caption"));
        _games = await Task.Run(GameLibrary.Detect);
        RenderLibrary();
    }

    private void RenderLibrary()
    {
        if (_games == null) return;
        _library.Children.Clear();
        string q = _search.Text.Trim();
        var games = _games.Where(g => q.Length == 0 || g.Name.Contains(q, StringComparison.CurrentCultureIgnoreCase)).ToList();
        if (games.Count == 0)
        {
            _library.Children.Add(Ui.Text(_games.Count == 0
                ? "Aucun jeu détecté. Ajoutez l'exécutable d'un jeu à un profil avec « Ajouter un exécutable… »."
                : $"Aucun jeu ne correspond à « {q} ».", "Text.Body"));
            return;
        }
        foreach (var g in games)
            _library.Children.Add(Tile(g));
    }

    private FrameworkElement Tile(InstalledGame g)
    {
        string? exe = g.Executable != null ? Path.GetFileName(g.Executable) : null;
        var owner = exe == null ? null : App.Controller.Profiles.All.FirstOrDefault(p => p.Executables.Contains(exe, StringComparer.OrdinalIgnoreCase));

        var cover = new Border { Height = 200, CornerRadius = new CornerRadius(8), ClipToBounds = true };
        if (g.CoverPath != null)
        {
            try
            {
                var img = new BitmapImage();
                img.BeginInit();
                img.UriSource = new Uri(g.CoverPath);
                img.DecodePixelWidth = 300;
                img.CacheOption = BitmapCacheOption.OnLoad;
                img.EndInit();
                cover.Background = new ImageBrush(img) { Stretch = Stretch.UniformToFill };
            }
            catch { /* jaquette illisible */ }
        }
        if (cover.Background == null)
        {
            cover.SetResourceReference(Border.BackgroundProperty, "Ink");
            var initial = new TextBlock { Text = g.Name[..1].ToUpperInvariant(), FontSize = 72, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
            initial.SetResourceReference(TextBlock.FontFamilyProperty, "Font.Display");
            initial.SetResourceReference(TextBlock.ForegroundProperty, "Highlight");
            cover.Child = initial;
        }

        var body = new StackPanel { Width = 150 };
        body.Children.Add(cover);
        var title = Ui.Text(g.Name, "Text.Label", new Thickness(0, 10, 0, 2));
        title.TextTrimming = TextTrimming.CharacterEllipsis;
        title.ToolTip = g.Name;
        body.Children.Add(title);
        body.Children.Add(Ui.Text(g.Store, "Text.Caption", new Thickness(0, 0, 0, 8)));
        if (owner != null)
            body.Children.Add(new Badge { Text = owner.Name, Tone = BadgeTone.Accent, HorizontalAlignment = HorizontalAlignment.Left, ToolTip = "Profil associé" });
        else if (exe != null)
            body.Children.Add(Ui.Button("Créer un profil", "Button.Secondary", (_, _) => Create(g.Name, exe)));
        else
            body.Children.Add(Ui.Text("Exécutable introuvable", "Text.Caption"));
        return new Border { Margin = new Thickness(0, 0, 16, 18), Child = body };
    }
}
