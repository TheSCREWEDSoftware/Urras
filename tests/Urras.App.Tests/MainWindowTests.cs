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

    // A refresh with nothing new keeps the lists as they are; a new commit replaces the history
    [AvaloniaFact]
    public void RefreshKeepsUnchangedLists()
    {
        MainWindow window = OpenWindow();
        ListBox history = window.FindControl<ListBox>("HistoryList")!;
        Button refresh = window.FindControl<Button>("RefreshButton")!;
        Ui.Pump(() => history.ItemCount > 0);
        object? before = history.ItemsSource;

        refresh.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Ui.Pump(() => false, 1000);
        Assert.Same(before, history.ItemsSource);

        string clone = Path.Combine(_root, "c", "wiki");
        File.WriteAllText(Path.Combine(clone, "notes.txt"), "notes");
        Ui.Git(clone, "add", "notes.txt");
        Ui.Git(clone, "commit", "-q", "-m", "Notes");
        refresh.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Ui.Pump(() => !ReferenceEquals(before, history.ItemsSource));
        Assert.NotSame(before, history.ItemsSource);
        Assert.Equal(2, history.ItemCount);
        window.Close();
    }

    [AvaloniaFact]
    public void SearchesTheRepositoryList()
    {
        MainWindow window = OpenWindow();
        TreeView tree = window.FindControl<TreeView>("RepositoryTree")!;
        TextBox search = window.FindControl<TextBox>("RepositorySearchBox")!;
        int Repositories() => tree.Items.OfType<RepositoryNode>().SelectMany(node => node.SelfAndDescendants())
                                  .Count(node => node.Path != null);
        Assert.Equal(3, Repositories());

        search.Text = "nothing-matches-this";
        Ui.Pump();
        Assert.Equal(0, Repositories());
        Assert.True(window.FindControl<TextBlock>("RepositoryHint")!.IsVisible, "says nothing matched");

        search.Text = "wiki";
        Ui.Pump();
        Assert.Equal(3, Repositories());
        search.Text = string.Empty;
        Ui.Pump();
        Assert.Equal(3, Repositories());
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
