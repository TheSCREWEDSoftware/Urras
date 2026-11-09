using Avalonia.Controls;
using Urras.Core.Git;
using Urras.Core.Settings;

namespace Urras.App;

// The worktree switcher beside the branch button, like GitHub Desktop's
public partial class MainWindow
{
    private void InitializeWorktreeSwitcher()
    {
        WorktreeButton.Click += async (_, _) => await ShowWorktreeMenuAsync();
    }

    private void UpdateWorktreeButton()
    {
        // Most repositories have one worktree; the button only takes toolbar space when there are others
        WorktreeButton.IsVisible = _repository != null && Directory.Exists(_repository.Path) &&
                                   GitRepository.HasLinkedWorktrees(_repository.Path);
        WorktreeButtonText.Text = _repository == null ? string.Empty
                                                      : Path.GetFileName(_repository.Path.TrimEnd('/', '\\'));
    }

    // Each worktree opens in Urras; the current one is marked, and missing ones can't be opened
    private async Task ShowWorktreeMenuAsync()
    {
        if (_repository == null)
            return;

        List<WorktreeInfo> worktrees = await _repository.GetWorktreesAsync();
        ContextMenu menu = new ContextMenu();
        foreach (WorktreeInfo worktree in worktrees)
        {
            bool current = RepositoryLayout.SamePath(worktree.Path, _repository.Path);
            string where = worktree.Branch ?? (worktree.Head == null ? "bare" : "detached");
            MenuItem item = new MenuItem
            {
                Header = Literal(Path.GetFileName(worktree.Path.TrimEnd('/', '\\')) + "  ·  " + where +
                                 (current ? "  (current)" : string.Empty) +
                                 (worktree.IsPrunable ? "  (missing)" : string.Empty)),
                IsEnabled = !current && !worktree.IsPrunable
            };
            ToolTip.SetTip(item, worktree.Path);
            string path = worktree.Path;
            item.Click += async (_, _) => await AddAndOpenRepositoryAsync(path);
            menu.Items.Add(item);
        }

        menu.Items.Add(new Separator());
        AddMenuItem(menu, "New worktree…", AddWorktreeAsync);
        AddMenuItem(menu, "Manage worktrees…", ManageWorktreesAsync);
        menu.Open(WorktreeButton);
    }
}
