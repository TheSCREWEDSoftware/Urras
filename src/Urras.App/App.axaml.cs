using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using Urras.Core.Settings;

namespace Urras.App;

public partial class App : Application
{
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.MainWindow = new MainWindow();

            // An error in a button or menu action is logged and shown, instead of closing the whole app
            Dispatcher.UIThread.UnhandledException += (_, e) =>
            {
                string? log = CrashLog.Write(e.Exception, "Error in an action; Urras kept running");
                (desktop.MainWindow as MainWindow)?.ShowUnexpectedError(e.Exception, log);
                e.Handled = true;
            };
        }

        base.OnFrameworkInitializationCompleted();
    }
}
