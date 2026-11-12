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
            return AskForGit(args);

        // Urras is already open: bring it forward instead of opening a second one
        if (SingleInstance.HandOver())
            return 0;

        StartupLog.Begin();
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

        StartupLog.Step("Starting the interface");
        int code = BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
        StartupLog.Step("Closed with code " + code);
        return code;
    }

    // With a second argument (on Windows, from the script Git runs), the answer goes to that file
    private static int AskForGit(string[] args)
    {
        string log = Path.Combine(Path.GetDirectoryName(AppSettings.DefaultPath) ?? ".", "askpass.log");
        void Report(string problem)
        {
            try
            {
                File.AppendAllText(log, DateTimeOffset.Now.ToString("u") + "  " + problem + Environment.NewLine);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                // Nowhere to write it down
            }
        }

        try
        {
            if (args.Length == 1)
                return AskPass.RunClient(args[0], Console.Out, report: Report);

            using StreamWriter file = new StreamWriter(args[1], false, new System.Text.UTF8Encoding(false));
            return AskPass.RunClient(args[0], file, report: Report);
        }
        catch (Exception exception)
        {
            Report("Unexpected: " + exception);
            return AskPass.NOT_ANSWERED;
        }
    }

    // Also used by the XAML previewer
    public static AppBuilder BuildAvaloniaApp()
    {
        return AppBuilder.Configure<App>().UsePlatformDetect().LogToTrace();
    }
}
