using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Urras.Core.Git;
using Urras.Core.Settings;
using Xunit;

[assembly: AvaloniaTestApplication(typeof(Urras.App.Tests.TestApp))]
// The windows share one settings file, so tests take turns
[assembly: CollectionBehavior(DisableTestParallelization = true)]

namespace Urras.App.Tests;

// Urras's own App, on Avalonia's headless platform, with its settings in a temporary folder
public static class TestApp
{
    public static readonly string SettingsFolder =
        Path.Combine(Path.GetTempPath(), "urras-ui-tests-" + Environment.ProcessId);

    public static AppBuilder BuildAvaloniaApp()
    {
        Environment.SetEnvironmentVariable(AppSettings.FOLDER_VARIABLE, SettingsFolder);
        return AppBuilder.Configure<App>().UseHeadless(new AvaloniaHeadlessPlatformOptions());
    }
}

internal static class Ui
{
    private const int WAIT_MILLISECONDS = 5000;
    private const int STEP_MILLISECONDS = 10;

    // Runs queued interface work, and the continuations of background Git commands, until done or out of time
    public static void Pump(Func<bool>? until = null, int milliseconds = WAIT_MILLISECONDS)
    {
        Stopwatch watch = Stopwatch.StartNew();
        do
        {
            Dispatcher.UIThread.RunJobs();
            if (until?.Invoke() == true)
                return;
            Thread.Sleep(STEP_MILLISECONDS);
        } while (until != null && watch.ElapsedMilliseconds < milliseconds);

        Dispatcher.UIThread.RunJobs();
    }

    public static T Find<T>(Visual root, Func<T, bool>? match = null) where T : Visual
    {
        return root.GetVisualDescendants().OfType<T>().First(control => match?.Invoke(control) ?? true);
    }

    // Opens a dialog, lets it build and lay out, then closes it; any error while building fails the test
    public static T? OpenAndClose<T>(Window owner, Func<Task<T>> show)
    {
        int before = owner.OwnedWindows.Count;
        Task<T> task = show();
        Pump(() => owner.OwnedWindows.Count > before || task.IsCompleted);
        Assert.True(owner.OwnedWindows.Count > before, "the dialog opened");
        owner.OwnedWindows[^1].Close();
        Pump(() => task.IsCompleted);
        Assert.True(task.IsCompleted, "the dialog closed");
        return task.GetAwaiter().GetResult();
    }

    // Visits every page of the Options window, twice, the way someone clicking around would
    public static void VisitEveryPage(OptionsWindow window)
    {
        Pump();
        ListBox sidebar = Find<ListBox>(window, list => list.ItemsSource is string[]);
        for (int round = 0; round < 2; ++round)
        {
            for (int page = 0; page < sidebar.ItemCount; ++page)
            {
                sidebar.SelectedIndex = page;
                Pump();
            }
        }
    }

    // A repository with one commit, in a temporary folder
    public static string Repository(string root, string name)
    {
        string path = Path.Combine(root, name);
        Directory.CreateDirectory(path);
        Git(path, "init", "-q", "-b", "main");
        Git(path, "config", "user.name", "Urras Test");
        Git(path, "config", "user.email", "test@example.com");
        Git(path, "config", "commit.gpgsign", "false");
        File.WriteAllText(Path.Combine(path, "README.md"), "# " + name + "\n");
        Git(path, "add", "README.md");
        Git(path, "commit", "-q", "-m", "First");
        return path;
    }

    public static void Git(string path, params string[] arguments)
    {
        // Off the interface thread, which the test itself is holding
        GitResult result = Task.Run(() => GitRunner.RunAsync(path, arguments)).GetAwaiter().GetResult();
        Assert.True(result.Success, "git " + string.Join(' ', arguments) + ": " + result.Error);
    }
}
