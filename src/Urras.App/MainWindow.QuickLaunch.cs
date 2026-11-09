using Urras.Core.Git;

namespace Urras.App;

// Quick Launch (Ctrl+P): repositories, branches and commands in one searchable list
public partial class MainWindow
{
    private const int CHANGES_TAB = 0;

    private async Task ShowQuickLaunchAsync()
    {
        QuickItem? item = await new QuickLaunchWindow(QuickItems()).ShowDialog<QuickItem?>(this);
        if (item != null)
            await item.Run();
    }

    private List<QuickItem> QuickItems()
    {
        List<QuickItem> items = new List<QuickItem>();
        foreach (string path in _settings.Repositories)
        {
            if (_repository == null || !string.Equals(path, _repository.Path, StringComparison.OrdinalIgnoreCase))
                items.Add(new QuickItem(Path.GetFileName(path) + "  ·  " + path, "repository",
                                        () => AddAndOpenRepositoryAsync(path)));
        }

        if (_repository != null)
        {
            foreach (BranchInfo branch in _branches.Where(branch => !branch.IsCurrent))
            {
                items.Add(new QuickItem("Switch to " + branch.Name, branch.IsRemote ? "remote branch" : "branch",
                                        () => SwitchFromQuickLaunchAsync(branch)));
            }

            AddCommand(items, "Fetch", () => RunGitAsync("Fetching…", repository => repository.FetchAsync(),
                                                         "Fetched."));
            AddCommand(items, "Pull", () => RunGitAsync("Pulling…", repository => repository.PullAsync(), "Pulled."));
            AddCommand(items, "Push", () => RunGitAsync("Pushing…", repository => repository.PushAsync(), "Pushed."));
            AddCommand(items, "Commit…", () =>
            {
                MainTabs.SelectedIndex = CHANGES_TAB;
                SummaryBox.Focus();
                return Task.CompletedTask;
            });
            AddCommand(items, "History", () =>
            {
                MainTabs.SelectedIndex = HISTORY_TAB;
                return Task.CompletedTask;
            });
            AddCommand(items, "Search the history…", () =>
            {
                MainTabs.SelectedIndex = HISTORY_TAB;
                SearchBox.Focus();
                return Task.CompletedTask;
            });
            AddCommand(items, "New branch…", CreateBranchAsync);
            AddCommand(items, "Delete branch…", DeleteBranchAsync);
            AddCommand(items, "Merge a branch into the current one…", MergeBranchAsync);
            AddCommand(items, "Rebase the current branch onto…", RebaseBranchAsync);
            AddCommand(items, "Compare with a branch…", CompareWithBranchAsync);
            AddCommand(items, "Pull requests…", ShowPullRequestsAsync);
            AddCommand(items, "Stash changes…", StashChangesAsync);
            AddCommand(items, "Stashes…", ManageStashesAsync);
            AddCommand(items, "Tags…", ManageTagsAsync);
            AddCommand(items, "Reflog (find lost commits)…", ShowReflogAsync);
            AddCommand(items, "Submodules…", ManageSubmodulesAsync);
            AddCommand(items, "Worktrees…", ManageWorktreesAsync);
            AddCommand(items, "Git LFS…", ManageLfsAsync);
            AddCommand(items, "Repository settings…", () => RepositorySettingsAsync(_repository.Path));
            string folder = _repository.Path;
            AddCommand(items, "Open in file explorer", () => Report(SystemShell.OpenFolder(folder)));
            AddCommand(items, "Open in terminal", () => Report(SystemShell.OpenShell(_settings.Shell, folder)));
            AddCommand(items, "Open in " + EditorName,
                       () => Report(SystemShell.OpenInEditor(_settings.Editor, folder)));
            AddCommand(items, "Refresh", RefreshAsync);
            AddCommand(items, "Commands…", ManageCommandsAsync);
            AddCommand(items, "Statistics…", ShowStatisticsAsync);
            foreach (Urras.Core.Commands.CustomCommand command in _settings.CustomCommands
                         .Where(command => command.Scope == Urras.Core.Commands.CommandScope.Repository))
                items.Add(new QuickItem(command.Name, "your command",
                                        () => RunCustomCommandAsync(command, CurrentContext())));
        }

        AddCommand(items, "Add repository…", AddRepositoryAsync);
        AddCommand(items, "Clone…", CloneRepositoryAsync);
        AddCommand(items, "Import all repositories in a folder…", ImportFromFolderAsync);
        AddCommand(items, "Import repositories from GitHub Desktop", ImportFromGitHubDesktopAsync);
        AddCommand(items, "Clone from your accounts…", CloneFromAccountAsync);
        AddCommand(items, "Accounts…", ManageAccountsAsync);
        AddCommand(items, "Notifications…", ShowNotificationsAsync);
        AddCommand(items, "Settings…", OpenSettingsAsync);
        return items;
    }

    private static void AddCommand(List<QuickItem> items, string title, Func<Task> run)
    {
        items.Add(new QuickItem(title, "command", run));
    }

    private Task SwitchFromQuickLaunchAsync(BranchInfo branch)
    {
        return SwitchToBranchAsync(branch);
    }
}
