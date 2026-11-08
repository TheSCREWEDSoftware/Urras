using Avalonia.Controls;
using Avalonia.Input;
using Urras.Core.Git;

namespace Urras.App;

// Searching the history, file history, blame, files at a commit, comparing revisions and the reflog
public partial class MainWindow
{
    private const int HISTORY_TAB = 1;

    private HistoryFilter? _historyFilter;

    private void InitializeHistoryTools()
    {
        SearchModeBox.SelectedIndex = 0;
        SearchBox.KeyDown += async (_, e) =>
        {
            if (e.Key != Key.Enter)
                return;

            e.Handled = true;
            await SearchHistoryAsync();
        };
        ClearSearchButton.Click += async (_, _) => await ClearHistorySearchAsync();
        AllBranchesBox.IsChecked = _settings.HistoryAllBranches;
        AllBranchesBox.Click += async (_, _) =>
        {
            _settings.HistoryAllBranches = AllBranchesBox.IsChecked == true;
            _settings.Save();
            await RefreshAsync();
        };
        CommitFilesList.ContextMenu = CommitFileContextMenu();
        UpdateSearchState();
    }

    // Search

    private async Task SearchHistoryAsync()
    {
        string text = SearchBox.Text?.Trim() ?? string.Empty;
        if (text.Length == 0)
        {
            await ClearHistorySearchAsync();
            return;
        }

        _historyFilter = new HistoryFilter((HistorySearch)Math.Max(0, SearchModeBox.SelectedIndex), text);
        UpdateSearchState();
        ClearCommitFiles();
        ShowCommitDiff(null, string.Empty);
        await RefreshAsync();
    }

    private async Task ClearHistorySearchAsync()
    {
        _historyFilter = null;
        SearchBox.Text = string.Empty;
        UpdateSearchState();
        await RefreshAsync();
    }

    private void UpdateSearchState()
    {
        ClearSearchButton.IsVisible = _historyFilter != null;
        HistoryFilterText.Text = _historyFilter == null ? string.Empty
                                                        : _historyFilter.Description + " · " + _history.Count +
                                                              (_history.Count == 1 ? " commit" : " commits");
        HistoryHint.Text = _historyFilter == null ? "No commits yet" : "No commits match the search";
    }

    private async Task ShowFileHistoryAsync(string path)
    {
        MainTabs.SelectedIndex = HISTORY_TAB;
        SearchModeBox.SelectedIndex = (int)HistorySearch.Path;
        SearchBox.Text = path;
        await SearchHistoryAsync();
    }

    // Two selected commits are compared, older to newer
    private async Task<bool> CompareSelectedCommitsAsync()
    {
        List<CommitInfo> selected = HistoryList.SelectedItems?.OfType<CommitInfo>().ToList() ?? new List<CommitInfo>();
        if (selected.Count != 2)
            return false;

        List<CommitInfo> ordered = selected.OrderByDescending(commit => _history.IndexOf(commit)).ToList();
        await ShowSourceAsync(new DiffSource("Compare " + ordered[0].ShortHash + " → " + ordered[1].ShortHash,
                                             ordered[1].Hash, ordered[0].Hash));
        return true;
    }

    private async Task CompareWithBranchAsync()
    {
        if (_repository == null)
            return;

        string? branch = await Dialogs.ChooseAsync(this, "Compare", "Compare the current branch with:",
                                                    OtherBranchNames());
        if (branch == null)
            return;

        MainTabs.SelectedIndex = HISTORY_TAB;
        await ShowSourceAsync(new DiffSource("Compare HEAD → " + branch, branch, "HEAD"));
    }

    // Blame and file views

    private async Task ShowBlameAsync(string path, string? revision)
    {
        if (_repository == null)
            return;

        try
        {
            List<BlameLine> lines = await _repository.GetBlameAsync(path, revision);
            string at = revision == null ? "working copy" : revision.Substring(0, Math.Min(8, revision.Length));
            BlameWindow window = new BlameWindow("Blame: " + path + " (" + at + ")", lines);
            window.CommitRequested += async hash => await ShowCommitByHashAsync(hash);
            window.Show(this);
        }
        catch (GitException exception)
        {
            SetStatus("Can't blame " + path + ": " + FirstLine(exception.Message), true);
        }
    }

    private async Task ShowCommitByHashAsync(string hash)
    {
        MainTabs.SelectedIndex = HISTORY_TAB;
        CommitInfo? loaded = _history.FirstOrDefault(commit => commit.Hash == hash);
        if (loaded != null)
        {
            HistoryList.SelectedItem = loaded;
            return;
        }

        // Older than the loaded history: shown without selecting a row
        string shortHash = hash.Substring(0, Math.Min(8, hash.Length));
        await ShowSourceAsync(new DiffSource(shortHash, hash, null));
    }

    private async Task ViewFileAsync(string revision, string path)
    {
        if (_repository == null)
            return;

        byte[]? contents = await _repository.GetBlobAsync(revision, path);
        string at = revision.Substring(0, Math.Min(8, revision.Length));
        new FileViewWindow(path + " (" + at + ")", path, contents).Show(this);
    }

    private async Task BrowseFilesAsync(CommitInfo commit)
    {
        if (_repository == null)
            return;

        List<string> files = await _repository.GetTreeFilesAsync(commit.Hash);
        string? file = await Dialogs.ChooseAsync(this, "Files at " + commit.ShortHash,
                                                  files.Count + " files at " + commit.ShortHash + " \"" +
                                                      commit.Subject + "\":",
                                                  files);
        if (file != null)
            await ViewFileAsync(commit.Hash, file);
    }

    // Reflog

    private async Task ShowReflogAsync()
    {
        if (_repository == null)
            return;

        List<ReflogEntry> entries = await _repository.GetReflogAsync();
        (ReflogEntry? entry, string? action) = await Dialogs.ListActionsAsync(
            this, "Reflog",
            "Everything HEAD pointed at recently, including commits no branch keeps any more. Create a branch to " +
                "bring one back.",
            entries, "Show changes", "Create branch here…", "Check out");
        if (entry == null || action == null)
            return;

        switch (action)
        {
            case "Show changes":
                MainTabs.SelectedIndex = HISTORY_TAB;
                await ShowSourceAsync(new DiffSource(entry.ShortHash + "  " + entry.Action, entry.Hash, null));
                break;
            case "Create branch here…":
            {
                string? name = await Dialogs.PromptAsync(this, "Create branch",
                                                         "Name of the new branch at " + entry.ShortHash + ":");
                if (name != null)
                    await RunGitAsync("Creating " + name + "…",
                                      repository => repository.CreateBranchAtAsync(name, entry.Hash),
                                      "Created " + name + " at " + entry.ShortHash + ".");
                break;
            }
            case "Check out":
                await RunGitAsync("Checking out " + entry.ShortHash + "…",
                                  repository => repository.CheckoutCommitAsync(entry.Hash),
                                  "Checked out " + entry.ShortHash + ".");
                break;
            default:
                break;
        }
    }

    // Menus

    // History and blame items for a file in the change lists
    private void AddFileHistoryItems(ContextMenu menu, FileChange change)
    {
        AddMenuItem(menu, "File history", () => ShowFileHistoryAsync(change.Path));
        if (change.Kind != ChangeKind.Untracked && change.Kind != ChangeKind.Deleted &&
            change.Kind != ChangeKind.Added)
            AddMenuItem(menu, "Blame", () => ShowBlameAsync(change.Path, null));
        menu.Items.Add(new Separator());
    }

    private ContextMenu CommitFileContextMenu()
    {
        ContextMenu menu = new ContextMenu();
        menu.Opening += (_, _) =>
        {
            menu.Items.Clear();
            if (_shownSource == null || CommitFilesList.SelectedItem is not FileChange file)
                return;

            string target = _shownSource.Target;
            AddMenuItem(menu, "File history", () => ShowFileHistoryAsync(file.Path));
            if (file.Kind != ChangeKind.Deleted)
            {
                AddMenuItem(menu, "Blame at this revision", () => ShowBlameAsync(file.Path, target));
                AddMenuItem(menu, "View file at this revision", () => ViewFileAsync(target, file.Path));
            }

            menu.Items.Add(new Separator());
            AddMenuItem(menu, "Copy path", () => CopyAsync(file.Path));
        };
        return menu;
    }

    private void AddHistoryBrowseItems(ContextMenu menu, CommitInfo commit)
    {
        AddMenuItem(menu, "Browse files at this commit…", () => BrowseFilesAsync(commit));
        if ((HistoryList.SelectedItems?.Count ?? 0) == 2)
            AddMenuItem(menu, "Compare the two selected commits", async () => await CompareSelectedCommitsAsync());
    }
}
