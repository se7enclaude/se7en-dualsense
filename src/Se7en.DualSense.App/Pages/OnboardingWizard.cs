using System.Windows;
using System.Windows.Controls;
using Se7en.DualSense.App.Controls;
using Se7en.DualSense.App.Services;
using Se7en.DualSense.Output;

namespace Se7en.DualSense.App.Pages;

/// <summary>
/// Assistant de premier lancement, en trois étapes : pilotes → calibration → mode de sortie.
/// Chaque étape peut être passée ; il ne s'affiche plus une fois terminé.
/// </summary>
public sealed class OnboardingWizard
{
    private readonly IShell _shell;
    private int _step;

    public OnboardingWizard(IShell shell) => _shell = shell;

    public void Start()
    {
        _step = 0;
        Show();
    }

    private void Show()
    {
        var body = new StackPanel { Width = 520 };
        body.Children.Add(Steps());

        switch (_step)
        {
            case 0:
                var drivers = DriverStatus.Detect();
                body.Children.Add(Ui.Text("Bienvenue sur se7en", "Text.H1", new Thickness(0, 18, 0, 10)));
                body.Children.Add(Ui.Text("Trois étapes pour jouer : les pilotes, la calibration de vos sticks, puis le choix de ce que voient les jeux.",
                    "Text.Body", new Thickness(0, 0, 0, 18)));
                body.Children.Add(Row("ViGEmBus", "Manettes virtuelles DualShock 4 et Xbox 360", drivers.ViGEmBusInstalled));
                body.Children.Add(Row("HidHide", "Évite que les jeux voient deux manettes", drivers.HidHideInstalled));
                if (!drivers.AllInstalled)
                    body.Children.Add(Ui.Text("Les pilotes manquants s'installent depuis l'accueil (bouton « Installer » du bandeau).", "Text.Caption", new Thickness(0, 12, 0, 0)));
                if (InstallService.CanInstall)
                    body.Children.Add(new SettingRow
                    {
                        Label = "Installer sur ce PC",
                        Description = "Copie l'application dans votre dossier Programmes, avec raccourcis Bureau et menu Démarrer. Elle redémarre depuis là.",
                        Control = Ui.Button("Installer", "Button.Accent", (_, _) =>
                        {
                            try { InstallService.InstallAndRestart(desktopShortcut: true); }
                            catch (Exception ex) { _shell.Toast("Installation impossible", ex.Message, NotifyKind.Warning); }
                        }),
                        Last = true,
                    });
                break;
            case 1:
                body.Children.Add(Ui.Text("Calibrer vos sticks", "Text.H1", new Thickness(0, 18, 0, 10)));
                body.Children.Add(Ui.Text("Si votre manette dérive (le personnage bouge seul), la calibration mesure le centre réel de chaque stick et le compense. " +
                                          "Elle se lance depuis l'onglet Sticks et prend une dizaine de secondes.", "Text.Body"));
                break;
            default:
                body.Children.Add(Ui.Text("Ce que voient les jeux", "Text.H1", new Thickness(0, 18, 0, 10)));
                body.Children.Add(Ui.Text("DualShock 4 affiche les icônes PlayStation dans la plupart des jeux. Xbox 360 offre la compatibilité la plus large. " +
                                          "DualSense native laisse le jeu gérer gâchettes adaptatives et haptique, sans anti-drift.", "Text.Body", new Thickness(0, 0, 0, 18)));
                body.Children.Add(Ui.Segmented(new[] { "DualShock 4", "Xbox 360", "DualSense native" }, (int)App.Controller.ActiveProfile.Mode,
                    i => App.Controller.SetOutputMode((OutputMode)i)));
                body.Children.Add(Ui.Text("Modifiable à tout moment depuis l'accueil, et par jeu dans Jeux & profils.", "Text.Caption", new Thickness(0, 12, 0, 0)));
                break;
        }

        var actions = new DockPanel { Margin = new Thickness(0, 28, 0, 0), LastChildFill = false };
        actions.Children.Add(Ui.Button("Passer", "Button.Secondary", (_, _) => Finish(null)));
        var next = Ui.Button(_step switch { 0 => "Continuer", 1 => "Calibrer maintenant", _ => "Commencer à jouer" }, "Button.Primary", (_, _) =>
        {
            if (_step == 1) { Finish("sticks"); return; }
            if (_step == 2) { Finish(null); return; }
            _step++;
            Show();
        });
        DockPanel.SetDock(next, Dock.Right);
        actions.Children.Add(next);
        if (_step == 1)
        {
            var later = Ui.Button("Plus tard", "Button.Secondary", (_, _) => { _step++; Show(); }, new Thickness(0, 0, 8, 0));
            DockPanel.SetDock(later, Dock.Right);
            actions.Children.Add(later);
        }
        body.Children.Add(actions);
        _shell.ShowModal(body);
    }

    private StackPanel Steps()
    {
        var sp = new StackPanel { Orientation = Orientation.Horizontal };
        string[] names = { "PILOTES", "CALIBRATION", "MODE" };
        for (int i = 0; i < names.Length; i++)
        {
            var t = Ui.Text($"{i + 1} · {names[i]}", "Text.Overline", new Thickness(0, 0, 18, 0));
            t.SetResourceReference(TextBlock.ForegroundProperty, i == _step ? "Accent" : "Text.Tertiary");
            sp.Children.Add(t);
        }
        return sp;
    }

    private static SettingRow Row(string name, string description, bool ok) => new()
    {
        Label = name,
        Description = description,
        Control = new Badge { ShowDot = true, Text = ok ? "Installé" : "À installer", Tone = ok ? BadgeTone.Success : BadgeTone.Warning },
    };

    private void Finish(string? page)
    {
        AppSettings.Current.OnboardingDone = true;
        AppSettings.Save();
        _shell.CloseModal();
        if (page != null) _shell.Navigate(page);
        _shell.Toast("Tout est prêt", "Bon jeu avec votre DualSense.", NotifyKind.Success);
    }
}
