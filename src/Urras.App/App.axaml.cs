using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using Urras.Core.Git;
using Urras.Core.Settings;

namespace Urras.App;

public partial class App : Application
{
    private AskPass.Server? _askPass;

    // Git and SSH questions shown inside Urras; only as Urras's own program, which Git starts to ask
    private void StartAskPass(IClassicDesktopStyleApplicationLifetime desktop)
    {
        string? program = Environment.ProcessPath;
        if (program == null || Path.GetFileNameWithoutExtension(program).Equals("dotnet",
                                                                                StringComparison.OrdinalIgnoreCase))
            return;

        // Windows: Git runs a script that has Urras write the answer to a file, as it can't read a window's output
        string helper = program;
        if (OperatingSystem.IsWindows())
        {
            try
            {
                helper = Path.Combine(Path.GetDirectoryName(AppSettings.DefaultPath) ?? ".", "askpass.sh");
                Directory.CreateDirectory(Path.GetDirectoryName(helper)!);
                File.WriteAllText(helper, AskPass.WindowsScript(program));
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                return;
            }
        }

        _askPass = new AskPass.Server(helper, prompt => Dispatcher.UIThread.InvokeAsync(
            () => ((MainWindow)desktop.MainWindow!).AnswerGitAsync(prompt)));
        _askPass.Use();
        desktop.Exit += (_, _) => _askPass.Dispose();
    }

    // A crash .NET couldn't log is looked up in Windows' own records and added to crash.log first
    private static async Task ReportCrashedRunAsync(MainWindow window, DateTime started)
    {
        bool logged = RunMarker.CrashLogWrittenSince(started);
        if (!logged && await WindowsCrashReport.CollectAsync(started) is { } report)
            logged = CrashLog.WriteText(report, "Urras closed without logging an error; Windows recorded this") != null;
        window.ShowLastRunEnded(logged);
    }

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
        // Colours exist from the start; Options' choice replaces them once the settings are read
        ThemeBrushes.Use(Urras.Core.Settings.Theme.BuiltIn(ThemeBase.Dark), Resources);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            // A run that didn't close normally is reported once the window is up
            DateTime? crashedRun = RunMarker.Start();
            desktop.Exit += (_, _) => RunMarker.Stop();
            MainWindow window = new MainWindow();
            desktop.MainWindow = window;
            if (crashedRun is { } started)
                window.Opened += async (_, _) => await ReportCrashedRunAsync(window, started);
            StartAskPass(desktop);
            _ = CredentialSetup.UseManagerIfNeededAsync(Environment.CurrentDirectory);
            // Theme files from older builds get the colours this one added
            _ = Task.Run(() => ThemeStore.Default.LoadAll());

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
