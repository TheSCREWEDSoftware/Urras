using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Urras.Core.Settings;
using Xunit;

namespace Urras.App.Tests;

// The main window with real repositories: opening each one, and the Options window from it
public sealed class MainWindowTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "urras-ui-" + Guid.NewGuid().ToString("N"));

    // Git's object files are read-only, which Windows won't delete until that is cleared
    public void Dispose()
    {
        try
        {
            foreach (string file in Directory.EnumerateFiles(_root, "*", SearchOption.AllDirectories))
                File.SetAttributes(file, FileAttributes.Normal);
            Directory.Delete(_root, true);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // Left for the system to clean up
        }
    }

    // Two unrelated repositories share a name, and one has a clone, so grouping by name has work to do
    private MainWindow OpenWindow()
    {
        string first = Ui.Repository(Path.Combine(_root, "a"), "wiki");
        string second = Ui.Repository(Path.Combine(_root, "b"), "wiki");
        string clone = Path.Combine(_root, "c", "wiki");
        Ui.Git(_root, "clone", "-q", first, clone);

        AppSettings settings = new AppSettings();
        foreach (string path in new[] { first, second, clone })
            settings.AddRepository(path);
        settings.LastRepository = clone;
        settings.Save();

        MainWindow window = new MainWindow();
        window.Show();
        TreeView tree = window.FindControl<TreeView>("RepositoryTree")!;
        Ui.Pump(() => tree.ItemCount > 0);
        Ui.Pump(() => false, 1000);
        return window;
    }

    [AvaloniaFact]
    public void OpensEveryRepository()
    {
        MainWindow window = OpenWindow();
        TreeView tree = window.FindControl<TreeView>("RepositoryTree")!;
        List<RepositoryNode> repositories = tree.Items.OfType<RepositoryNode>()
                                                .SelectMany(node => node.SelfAndDescendants())
                                                .Where(node => node.Path != null).ToList();
        Assert.Equal(3, repositories.Count);

        foreach (RepositoryNode node in repositories)
        {
            tree.SelectedItem = node;
            Ui.Pump(() => window.FindControl<TextBlock>("RepositoryTitle")!.Text?.Contains("wiki") == true);
            Ui.Pump(() => false, 300);
        }

        window.Close();
    }

    [AvaloniaFact]
    public void OptionsOpenFromTheMainWindow()
    {
        MainWindow window = OpenWindow();
        Button settings = window.FindControl<Button>("SettingsButton")!;
        settings.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Ui.Pump(() => window.OwnedWindows.Count > 0);
        OptionsWindow options = Assert.IsType<OptionsWindow>(window.OwnedWindows[^1]);
        Ui.VisitEveryPage(options);
        options.Close(false);
        Ui.Pump();
        window.Close();
    }
}
