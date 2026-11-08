using Avalonia.Controls;
using Urras.Core.Settings;

namespace Urras.App;

// Toolbar buttons for pinned repositories and folders; right-click one to unpin it
public partial class MainWindow
{
    private void UpdatePinnedBar()
    {
        PinnedBar.Children.Clear();
        foreach (string path in _settings.PinnedRepositories.Where(Directory.Exists))
        {
            bool current = _repository != null && RepositoryLayout.PathComparer.Equals(_repository.Path, path);
            Button button = PinButton(Path.GetFileName(path), path, () => SetPinnedRepositoryAsync(path, false));
            if (current)
                button.Classes.Add("accent");
            button.Click += async (_, _) => await AddAndOpenRepositoryAsync(path);
            PinnedBar.Children.Add(button);
        }

        foreach (string folder in _settings.PinnedFolders.Where(Directory.Exists))
        {
            List<string> inside = RepositoriesInside(folder);
            bool current = _repository != null && inside.Contains(_repository.Path, RepositoryLayout.PathComparer);
            Button button = PinButton(Path.GetFileName(folder) + " ▾", folder, () => SetPinnedAsync(folder, false));
            if (current)
                button.Classes.Add("accent");
            button.Click += (_, _) => ShowPinnedFolderMenu(button, folder);
            PinnedBar.Children.Add(button);
        }
    }

    private Button PinButton(string text, string path, Func<Task> unpin)
    {
        Button button = new Button { Content = Literal(text), Padding = new Avalonia.Thickness(10, 3) };
        ToolTip.SetTip(button, path);
        ContextMenu menu = new ContextMenu();
        AddMenuItem(menu, "Unpin", unpin);
        button.ContextMenu = menu;
        return button;
    }

    private List<string> RepositoriesInside(string folder)
    {
        string prefix = folder.Replace('\\', '/').TrimEnd('/') + "/";
        return _settings.Repositories
                        .Where(path => path.Replace('\\', '/').StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                        .OrderBy(path => path, StringComparer.OrdinalIgnoreCase).ToList();
    }

    private void ShowPinnedFolderMenu(Button button, string folder)
    {
        ContextMenu menu = new ContextMenu();
        foreach (string path in RepositoriesInside(folder))
        {
            string label = Path.GetRelativePath(folder, path).Replace('\\', '/');
            AddMenuItem(menu, label, () => AddAndOpenRepositoryAsync(path));
        }

        if (menu.Items.Count == 0)
            AddMenuItem(menu, "No repositories in this folder", () => Task.CompletedTask);
        menu.Open(button);
    }
}
