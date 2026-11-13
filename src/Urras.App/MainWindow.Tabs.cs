using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Urras.Core.Git;
using Urras.Core.Settings;

namespace Urras.App;

// Repository tabs, as Fork has: several repositories open, each keeping its place while another one shows
public partial class MainWindow
{
    // Where a tab was: the page, the selections, History's scroll and any unfinished commit message
    private sealed record TabPlace(int MainTab, string? Commit, Vector HistoryScroll, string? ChangedFile,
                                   bool StagedFile, string? CommitFile, string Summary, string Description);

    private const double TAB_MAX_WIDTH = 220;

    private RepositoryTabs _tabs = new RepositoryTabs(Array.Empty<string>(), 0, null);
    private readonly Dictionary<string, TabPlace> _places = new Dictionary<string, TabPlace>(
        RepositoryLayout.PathComparer);
    // The file to pick in the commit's file list once the commit it belongs to has loaded
    private string? _pendingCommitFile;
    // The place to go back to once the repository being opened has loaded
    private (string Path, TabPlace Place)? _pendingPlace;

    private void InitializeTabs()
    {
        List<string> known = _settings.OpenTabs.Where(path => _settings.Repositories.Contains(
                                                          path, RepositoryLayout.PathComparer)).ToList();
        _tabs = new RepositoryTabs(known, _settings.ActiveTab, _repository?.Path);
        ShowTabs();
    }

    // Called as a repository opens: the one leaving keeps its place, and the tabs follow
    private void LeaveTab()
    {
        if (_repository != null)
            _places[_repository.Path] = CurrentPlace();
    }

    private void EnterTab(string? path)
    {
        _pendingPlace = null;
        if (path != null)
        {
            _tabs.Show(path);
            if (_places.TryGetValue(path, out TabPlace? place))
                _pendingPlace = (path, place);
            SaveTabs();
        }
        ShowTabs();
    }

    // Once the repository has loaded, it goes back to where it was the last time it showed
    private void RestorePendingTab()
    {
        if (_pendingPlace is not { } pending || _repository == null ||
            !RepositoryLayout.PathComparer.Equals(pending.Path, _repository.Path))
            return;

        _pendingPlace = null;
        TabPlace place = pending.Place;

        MainTabs.SelectedIndex = place.MainTab;
        if (place.Summary.Length > 0 || place.Description.Length > 0)
        {
            SummaryBox.Text = place.Summary;
            DescriptionBox.Text = place.Description;
        }

        ListBox changes = place.StagedFile ? StagedList : UnstagedList;
        if (place.ChangedFile != null && changes.ItemsSource is IEnumerable<FileChange> files &&
            files.FirstOrDefault(change => change.Path == place.ChangedFile) is { } file)
            changes.SelectedItem = file;

        if (place.Commit != null && HistoryList.ItemsSource is IEnumerable<CommitInfo> commits &&
            commits.FirstOrDefault(commit => commit.Hash == place.Commit) is { } found)
        {
            _pendingCommitFile = place.CommitFile;
            HistoryList.SelectedItem = found;
        }

        Vector scroll = place.HistoryScroll;
        Dispatcher.UIThread.Post(() =>
        {
            if (HistoryList.Scroll is ScrollViewer viewer)
                viewer.Offset = scroll;
        }, DispatcherPriority.Loaded);
    }

    private TabPlace CurrentPlace()
    {
        FileChange? changed = UnstagedList.SelectedItem as FileChange ?? StagedList.SelectedItem as FileChange;
        return new TabPlace(MainTabs.SelectedIndex, (HistoryList.SelectedItem as CommitInfo)?.Hash,
                            HistoryList.Scroll is ScrollViewer viewer ? viewer.Offset : default, changed?.Path,
                            StagedList.SelectedItem != null, (CommitFilesList.SelectedItem as FileChange)?.Path,
                            SummaryBox.Text ?? string.Empty, DescriptionBox.Text ?? string.Empty);
    }

    // Takes the file to pick in a commit's file list, if one is waiting
    private FileChange? TakePendingCommitFile(IReadOnlyList<FileChange> files)
    {
        string? wanted = _pendingCommitFile;
        _pendingCommitFile = null;
        return wanted == null ? null : files.FirstOrDefault(file => file.Path == wanted);
    }

    private async Task OpenInNewTabAsync(string path)
    {
        if (_repository != null && RepositoryLayout.PathComparer.Equals(_repository.Path, path))
            return;
        _tabs.ShowInNewTab(path);
        await OpenRepositoryAsync(path);
        SelectCurrentRepositoryNode();
    }

    private async Task SwitchTabAsync(int index)
    {
        if (index < 0 || index >= _tabs.Paths.Count || index == _tabs.Active)
            return;
        await OpenRepositoryAsync(_tabs.Paths[index]);
        SelectCurrentRepositoryNode();
    }

    private async Task StepTabAsync(int direction)
    {
        if (_tabs.Paths.Count < 2)
            return;
        int target = ((_tabs.Active + direction) % _tabs.Paths.Count + _tabs.Paths.Count) % _tabs.Paths.Count;
        await SwitchTabAsync(target);
    }

    private async Task CloseTabAsync(int index)
    {
        bool showing = index == _tabs.Active;
        string closing = _tabs.Paths[index];
        if (!_tabs.Close(index))
            return;

        SaveTabs();
        if (showing && _tabs.ActivePath is { } next)
        {
            await OpenRepositoryAsync(next);
            SelectCurrentRepositoryNode();
        }
        else
            ShowTabs();
        _places.Remove(closing);
    }

    // A repository removed from Urras closes its tab
    private async Task ForgetTabAsync(string path)
    {
        bool showing = _tabs.ActivePath is { } active && RepositoryLayout.PathComparer.Equals(active, path);
        _tabs.Forget(path);
        SaveTabs();
        ShowTabs();
        if (showing)
            await OpenRepositoryAsync(_tabs.ActivePath);
        _places.Remove(path);
    }

    private void SaveTabs()
    {
        _settings.OpenTabs = _tabs.Paths.ToList();
        _settings.ActiveTab = Math.Max(0, _tabs.Active);
        _settings.Save();
    }

    // The strip shows once two or more repositories are open
    private void ShowTabs()
    {
        RepositoryTabsPanel.Children.Clear();
        RepositoryTabsBar.IsVisible = _tabs.Paths.Count > 1;
        for (int index = 0; index < _tabs.Paths.Count; index++)
            RepositoryTabsPanel.Children.Add(TabButton(index));
    }

    private Control TabButton(int index)
    {
        string path = _tabs.Paths[index];
        bool active = index == _tabs.Active;
        TextBlock name = new TextBlock
        {
            Text = Path.GetFileName(path.TrimEnd('/', '\\')),
            TextTrimming = TextTrimming.CharacterEllipsis,
            VerticalAlignment = VerticalAlignment.Center,
            FontWeight = active ? FontWeight.SemiBold : FontWeight.Normal
        };
        Button close = new Button
        {
            Content = "×", Classes = { "tabClose" }, VerticalAlignment = VerticalAlignment.Center
        };
        ToolTip.SetTip(close, "Close this tab (Ctrl+W)");
        close.Click += async (_, e) =>
        {
            e.Handled = true;
            await CloseTabAsync(index);
        };

        DockPanel content = new DockPanel();
        DockPanel.SetDock(close, Dock.Right);
        content.Children.Add(close);
        content.Children.Add(name);
        Border tab = new Border
        {
            Child = content,
            MaxWidth = TAB_MAX_WIDTH,
            Classes = { "repositoryTab" },
            Cursor = new Cursor(StandardCursorType.Hand)
        };
        if (active)
            tab.Classes.Add("active");
        ToolTip.SetTip(tab, path);
        // A click shows the tab; a middle click closes it, as in a browser
        tab.PointerPressed += async (_, e) =>
        {
            PointerPointProperties pressed = e.GetCurrentPoint(tab).Properties;
            if (pressed.IsMiddleButtonPressed)
                await CloseTabAsync(index);
            else if (pressed.IsLeftButtonPressed)
                await SwitchTabAsync(index);
        };
        return tab;
    }
}
