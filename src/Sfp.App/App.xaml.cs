using System.Windows;
using Sfp.App.Composition;

namespace Sfp.App;

/// <summary>
/// WPF application entry. The only code here is the composition call: build the services, load settings, show the
/// window; save settings on exit. A startup failure (missing templates folder, unreadable manifest) is shown in a
/// message box and the process exits with the validation exit code.
/// </summary>
public partial class App : Application
{
    private AppServices? _services;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        try
        {
            _services = AppServices.Create(Dispatcher);
            _services.Main.Initialize();
            MainWindow window = new(_services.Main);
            MainWindow = window;
            window.Show();
        }
        catch (Exception ex)
        {
            // Top-level handler: nothing below the window exists yet, so the message box is the only place to report.
            MessageBox.Show(
                "SFP to OneNote could not start: " + ex.Message,
                ViewModels.MainViewModel.WindowTitle,
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            Shutdown(AppServices.StartupFailureExitCode);
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _services?.Main.SaveSettings();
        _services?.Dispose();
        base.OnExit(e);
    }
}
