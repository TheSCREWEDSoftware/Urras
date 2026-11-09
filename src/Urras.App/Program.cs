using Avalonia;
using Urras.Core.Git;
using Urras.Core.Settings;

namespace Urras.App;

internal static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        // Started by Git or SSH to ask a question: pass it to the running Urras, print the answer, and stop
        if (AskPass.IsAskPassCall(args))
            return AskPass.RunClient(args[0], Console.Out);

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

        return BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    // Also used by the XAML previewer
    public static AppBuilder BuildAvaloniaApp()
    {
        return AppBuilder.Configure<App>().UsePlatformDetect().LogToTrace();
    }
}
