using Avalonia;
using Urras.Core.Settings;

namespace Urras.App;

internal static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        // Errors no handler caught are written down before the app closes
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
        {
            if (e.ExceptionObject is Exception exception)
                CrashLog.Write(exception, "Unhandled error; Urras closed");
        };
        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            CrashLog.Write(e.Exception, "Background task error");
            e.SetObserved();
        };

        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    // Also used by the XAML previewer
    public static AppBuilder BuildAvaloniaApp()
    {
        return AppBuilder.Configure<App>().UsePlatformDetect().LogToTrace();
    }
}
