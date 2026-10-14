using System.Windows;
using System.Windows.Threading;
using Se7en.DualSense.App.Services;

namespace Se7en.DualSense.App;

public partial class App : Application
{
    public static ControllerService Controller { get; private set; } = null!;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        DispatcherUnhandledException += OnDispatcherUnhandledException;

        var ui = UiSettings.Load();
        if (ui.Theme != AppTheme.Light)
            ThemeManager.Apply(ui.Theme);

        Controller = new ControllerService();
        Controller.Start();

        MainWindow = new MainWindow();
        MainWindow.Show();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        Controller?.Dispose();
        base.OnExit(e);
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        MessageBox.Show(
            "Une erreur inattendue s'est produite :\n\n" + e.Exception.Message,
            "se7en DualSense", MessageBoxButton.OK, MessageBoxImage.Warning);
        e.Handled = true;
    }
}
