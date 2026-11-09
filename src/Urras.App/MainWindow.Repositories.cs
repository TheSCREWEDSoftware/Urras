using System.Collections.ObjectModel;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Urras.Core.Git;
using Urras.Core.Settings;

namespace Urras.App;

// The repository tree: grouped by name, folder or owner, or a manual layout; status labels and group actions
public partial class MainWindow
{
    private const int SUMMARY_PARALLELISM = 4;

    private readonly Dictionary<string, RepositorySummary> _summaries =
        new Dictionary<string, RepositorySummary>(RepositoryLayout.PathComparer);
    private readonly Dictionary<string, RepositoryNode> _repositoryNodes =
        new Dictionary<string, RepositoryNode>(RepositoryLayout.PathComparer);
    private ObservableCollection<RepositoryNode> _rootNodes = new ObservableCollection<RepositoryNode>();
    private RepositoryNode? _contextNode;
    private bool _summariesRunning;
    private bool _summariesPending;
    private bool _rootsLearned;

    private void InitializeRepositoryTree()
    {
        GroupingBox.SelectedIndex = GroupingIndex();
        GroupingBox.SelectionChanged += async (_, _) => await OnGroupingChangedAsync();
        NewGroupButton.Click += async (_, _) => await NewGroupAsync(null);
        RepositoryTree.SelectionChanged += async (_, _) => await OnTreeSelectionChangedAsync();

        ContextMenu menu = new ContextMenu();
        menu.Opening += (_, _) => FillContextMenu(menu);
        RepositoryTree.ContextMenu = menu;

        // Right-clicking an item selects it, so the menu acts on what was clicked
        RepositoryTree.AddHandler(PointerPressedEvent, (_, e) =>
        {
            if (!e.GetCurrentPoint(RepositoryTree).Properties.IsRightButtonPressed)
                return;

            _contextNode = (e.Source as Control)?.DataContext as RepositoryNode;
            if (_contextNode != null)
                RepositoryTree.SelectedItem = _contextNode;
        }, RoutingStrategies.Tunnel);

        string? last = _settings.LastRepository;
        if (last != null && _settings.Repositories.Contains(last, RepositoryLayout.PathComparer))
        {
            _repository = new GitRepository(last);
            WatchRepository(last);
            _ = UpdateForkSafelyAsync(last, false);
        }

        BuildRepositoryTree();
    }

    // Building the tree

    private void BuildRepositoryTree()
    {
        List<RepositoryTreeNode> tree;
        if (_settings.GroupByFolder)
            tree = BuildAutomaticTree();
        else
        {
            _settings.ManualLayout ??= RepositoryLayout.FromTree(BuildAutomaticTree());
            RepositoryLayout.Reconcile(_settings.ManualLayout, _settings.Repositories);
            tree = RepositoryLayout.ToTree(_settings.ManualLayout);
        }

        _repositoryNodes.Clear();
        _rootNodes = new ObservableCollection<RepositoryNode>(tree.Select(node => CreateNode(node, null)));

        foreach (RepositoryNode node in _rootNodes.SelectMany(root => root.SelfAndDescendants()))
        {
            if (node.Path != null && _summaries.TryGetValue(RepositoryLayout.Normalize(node.Path),
                                                            out RepositorySummary? summary))
                node.ApplySummary(summary);
        }

        UpdateGroupDetails();

        _suppressSelection = true;
        RepositoryTree.ItemsSource = _rootNodes;
        _suppressSelection = false;

        SelectCurrentRepositoryNode();
        UpdatePinnedBar();
        UpdateHints();
        _ = RefreshSummariesAsync();
    }

    private RepositoryNode CreateNode(RepositoryTreeNode source, RepositoryNode? parent)
    {
        bool expanded = !source.IsGroup ||
                        (source.Item?.Group?.IsExpanded ??
                         !_settings.CollapsedFolders.Contains(source.Folder ?? string.Empty,
                                                              RepositoryLayout.PathComparer));
        RepositoryNode node = new RepositoryNode(source, parent, expanded, OnNodeExpandedChanged);

        foreach (RepositoryTreeNode child in source.Children)
            node.Children.Add(CreateNode(child, node));

        if (node.Path != null)
            _repositoryNodes[RepositoryLayout.Normalize(node.Path)] = node;

        return node;
    }

    private void OnNodeExpandedChanged(RepositoryNode node)
    {
        if (node.Source.Item?.Group is LayoutGroup group)
            group.IsExpanded = node.IsExpanded;
        else if (node.Source.Folder is string folder)
        {
            _settings.CollapsedFolders.RemoveAll(existing => RepositoryLayout.PathComparer.Equals(existing, folder));
            if (!node.IsExpanded)
                _settings.CollapsedFolders.Add(folder);
        }

        _settings.Save();
    }

    private void SelectCurrentRepositoryNode()
    {
        if (_repository == null ||
            !_repositoryNodes.TryGetValue(RepositoryLayout.Normalize(_repository.Path), out RepositoryNode? node))
            return;

        for (RepositoryNode? parent = node.Parent; parent != null; parent = parent.Parent)
            parent.IsExpanded = true;

        // Selected after layout, once the expanded groups have created their items
        Dispatcher.UIThread.Post(() =>
        {
            _suppressSelection = true;
            RepositoryTree.SelectedItem = node;
            _suppressSelection = false;
        }, DispatcherPriority.Loaded);
    }

    private async Task OnTreeSelectionChangedAsync()
    {
        if (_suppressSelection || RepositoryTree.SelectedItem is not RepositoryNode { Path: string path })
            return;

        if (_repository != null && RepositoryLayout.PathComparer.Equals(_repository.Path, path))
            return;

        await OpenRepositoryAsync(path);
    }

    private async Task OpenRepositoryAsync(string? path)
    {
        _repository = path == null ? null : new GitRepository(path);
        _status = null;
        _settings.LastRepository = path;
        _settings.Save();

        SummaryBox.Text = string.Empty;
        DescriptionBox.Text = string.Empty;
        AmendBox.IsChecked = false;
        ShowChangesDiff(null, string.Empty);
        ShowCommitDiff(null, string.Empty);
        UpdatePinnedBar();
        UpdateViewOnlineButton();
        ResetListSignatures();
        WatchRepository(path);
        await RefreshAsync();
        if (path != null)
            await UpdateForkSafelyAsync(path, true);
    }

    // Status labels

    private async Task RefreshSummariesAsync()
    {
        if (_summariesRunning)
        {
            _summariesPending = true;
            return;
        }

        _summariesRunning = true;
        _lastSummaries = DateTime.UtcNow;
        try
        {
            do
            {
                _summariesPending = false;
                using SemaphoreSlim slots = new SemaphoreSlim(SUMMARY_PARALLELISM);
                await Task.WhenAll(_settings.Repositories.ToList().Select(async path =>
                {
                    await slots.WaitAsync();
                    try
                    {
                        await UpdateSummaryAsync(path, false);
                    }
                    finally
                    {
                        slots.Release();
                    }
                }));
                UpdateGroupDetails();
            } while (_summariesPending);

            // Newly known first commits can bring forks together, or split unrelated repositories apart
            if (_rootsLearned)
            {
                _rootsLearned = false;
                _settings.Save();
                if (_settings.GroupByFolder && _settings.Grouping == RepositoryGrouping.Name)
                    BuildRepositoryTree();
            }
        }
        finally
        {
            _summariesRunning = false;
        }
    }

    private async Task UpdateSummaryAsync(string path, bool updateGroups)
    {
        RepositorySummary summary = await RepositorySummary.LoadAsync(path);
        string key = RepositoryLayout.Normalize(path);
        _summaries[key] = summary;
        if (summary.State != RepositoryState.Missing && !_settings.RootCommits.ContainsKey(key))
        {
            List<string> roots = await new GitRepository(path).GetRootCommitsAsync();
            if (roots.Count > 0)
            {
                _settings.RootCommits[key] = roots;
                _rootsLearned = true;
            }
        }

        if (_repositoryNodes.TryGetValue(key, out RepositoryNode? node))
            node.ApplySummary(summary);

        if (updateGroups)
            UpdateGroupDetails();
    }

    private void UpdateGroupDetails()
    {
        foreach (RepositoryNode node in _rootNodes.SelectMany(root => root.SelfAndDescendants()))
            node.UpdateGroupDetail(_summaries);
    }

    // Adding and removing

    private async Task<string?> PickFolderAsync(string title)
    {
        IReadOnlyList<IStorageFolder> folders = await StorageProvider.OpenFolderPickerAsync(
            new FolderPickerOpenOptions { Title = title, AllowMultiple = false });
        return folders.Count == 0 ? null : folders[0].TryGetLocalPath();
    }

    private async Task AddRepositoryAsync()
    {
        string? folder = await PickFolderAsync("Add a repository");
        if (folder == null)
            return;

        string? root = await GitRepository.FindRootAsync(folder);
        if (root == null)
        {
            bool create = await Dialogs.ConfirmAsync(this, "Not a repository",
                                                     folder + " is not a Git repository. Create one here?",
                                                     "Create repository");
            if (!create)
                return;

            GitResult result = await GitRepository.InitAsync(folder);
            if (!ReportResult(result, "Created a repository."))
                return;
            root = folder;
        }

        await AddAndOpenRepositoryAsync(root);
    }

    private async Task CloneRepositoryAsync()
    {
        string? url = await Dialogs.PromptAsync(this, "Clone", "Repository URL:");
        if (url == null)
            return;

        string? parent = await PickFolderAsync("Choose where to clone into");
        if (parent == null)
            return;

        string name = url.TrimEnd('/');
        name = name.Substring(Math.Max(name.LastIndexOfAny(new[] { '/', '\\', ':' }) + 1, 0));
        if (name.EndsWith(".git", StringComparison.OrdinalIgnoreCase))
            name = name.Substring(0, name.Length - ".git".Length);

        string target = Path.Combine(parent, name);
        if (Directory.Exists(target) && Directory.EnumerateFileSystemEntries(target).Any())
        {
            SetStatus(target + " already exists and is not empty.", true);
            return;
        }

        SetBusy(true, "Cloning " + url + "…");
        GitResult result = await GitRepository.CloneAsync(url, target);
        SetBusy(false, null);

        if (ReportResult(result, "Cloned into " + target + "."))
            await AddAndOpenRepositoryAsync(target);
    }

    private async Task AddAndOpenRepositoryAsync(string path)
    {
        bool added = _settings.AddRepository(path);

        // In a manual layout, a new repository goes into the selected group
        if (added && !_settings.GroupByFolder && _settings.ManualLayout != null &&
            RepositoryTree.SelectedItem is RepositoryNode { Source.Item.Group: LayoutGroup group })
            group.Items.Add(LayoutItem.ForRepository(path));

        _settings.Save();
        _repository = new GitRepository(path);
        BuildRepositoryTree();
        await OpenRepositoryAsync(path);
    }

    private async Task RemoveSelectedAsync()
    {
        if (RepositoryTree.SelectedItem is not RepositoryNode node)
            return;

        if (node.IsGroup)
            await RemoveGroupAsync(node);
        else
            await RemoveRepositoryAsync(node);
    }

    private async Task RemoveRepositoryAsync(RepositoryNode node)
    {
        bool remove = await ConfirmIfAsync(_settings.Prompts.RemovingRepositories, "Remove repository",
                                           "Remove " + node.Name +
                                               " from the list? Files on disk are not touched.",
                                           "Remove");
        if (!remove || node.Path == null)
            return;

        _settings.RemoveRepository(node.Path);
        _settings.Save();

        bool wasOpen = _repository != null && RepositoryLayout.PathComparer.Equals(_repository.Path, node.Path);
        BuildRepositoryTree();
        if (wasOpen)
            await OpenRepositoryAsync(null);
    }

    // Context menu

    private void FillContextMenu(ContextMenu menu)
    {
        menu.Items.Clear();
        RepositoryNode? node = _contextNode ?? RepositoryTree.SelectedItem as RepositoryNode;
        _contextNode = null;

        if (node == null)
        {
            AddMenuItem(menu, "Add repository…", AddRepositoryAsync);
            AddMenuItem(menu, "New group…", () => NewGroupAsync(null));
            return;
        }

        AddOpenItems(menu, node);

        string scope = node.IsGroup ? " all" : string.Empty;
        AddMenuItem(menu, "Fetch" + scope, () => RunOnRepositoriesAsync(node, "Fetched", "Fetching",
                                                                        repository => repository.FetchAsync()));
        AddMenuItem(menu, "Pull" + scope, () => RunOnRepositoriesAsync(node, "Pulled", "Pulling",
                                                                       repository => repository.PullAsync()));
        menu.Items.Add(new Separator());

        if (node.IsGroup)
        {
            AddMenuItem(menu, "New group inside…", () => NewGroupAsync(node));
            AddMenuItem(menu, "Rename…", () => RenameGroupAsync(node));
        }

        AddPinItems(menu, node);
        AddMenuItem(menu, "Move up", () => MoveAsync(node, -1));
        AddMenuItem(menu, "Move down", () => MoveAsync(node, 1));
        AddMenuItem(menu, "Move to group…", () => MoveToGroupAsync(node));
        menu.Items.Add(new Separator());
        if (node.Path != null)
            AddMenuItem(menu, "Repository settings…", () => RepositorySettingsAsync(node.Path));
        AddMenuItem(menu, node.IsGroup ? "Remove group" : "Remove from list",
                    () => node.IsGroup ? RemoveGroupAsync(node) : RemoveRepositoryAsync(node));
    }

    // Menus and buttons treat _ as an Alt shortcut marker (so 0_Engine showed as 0Engine); doubling it shows it
    private static string Literal(string text)
    {
        return text.Replace("_", "__");
    }

    private static void AddMenuItem(ContextMenu menu, string header, Func<Task> action)
    {
        MenuItem item = new MenuItem { Header = Literal(header) };
        item.Click += async (_, _) => await action();
        menu.Items.Add(item);
    }

    // Organising

    // In the automatic layout, organising first turns the current folder layout into a manual one, after asking
    private async Task<bool> EnsureManualAsync(string change)
    {
        if (!_settings.GroupByFolder)
            return true;

        string replaced = _settings.ManualLayout == null ? string.Empty
                                                         : " Your previous manual layout will be replaced.";
        bool confirm = await Dialogs.ConfirmAsync(
            this, "Turn off automatic grouping",
            "This turns off automatic grouping. Groups you haven't touched keep their current layout, but " + change +
                "." + replaced,
            "Switch to manual");
        if (!confirm)
            return false;

        _settings.ManualLayout = RepositoryLayout.FromTree(BuildAutomaticTree());
        _settings.GroupByFolder = false;
        _suppressGrouping = true;
        GroupingBox.SelectedIndex = MANUAL_INDEX;
        _suppressGrouping = false;
        return true;
    }

    // The manual layout entry for a node, also right after an automatic layout was converted
    private LayoutItem? ResolveItem(RepositoryNode? node)
    {
        if (node == null || _settings.ManualLayout == null)
            return null;
        if (node.Source.Item != null)
            return node.Source.Item;
        if (node.Path != null)
            return RepositoryLayout.FindRepository(_settings.ManualLayout, node.Path);
        if (node.Source.Folder != null)
            return RepositoryLayout.FindGroupFromFolder(_settings.ManualLayout, node.Source.Folder);
        return null;
    }

    private void SaveLayout(string status)
    {
        _settings.Save();
        BuildRepositoryTree();
        SetStatus(status, false);
    }

    private async Task NewGroupAsync(RepositoryNode? parent)
    {
        string? name = await Dialogs.PromptAsync(this, "New group", "Name of the new group:");
        if (name == null)
            return;

        string where = parent == null ? string.Empty : " inside \"" + parent.Name + "\"";
        if (!await EnsureManualAsync("a new group \"" + name + "\" will be added" + where))
            return;

        LayoutGroup target = ResolveItem(parent)?.Group ?? _settings.ManualLayout!;
        RepositoryLayout.AddGroup(target, name);
        SaveLayout("Added the group " + name + ".");
    }

    private async Task RenameGroupAsync(RepositoryNode node)
    {
        string? name = await Dialogs.PromptAsync(this, "Rename group", "New name for \"" + node.Name + "\":",
                                                 node.Name);
        if (name == null || name == node.Name)
            return;

        if (!await EnsureManualAsync("\"" + node.Name + "\" will be renamed to \"" + name + "\""))
            return;

        if (ResolveItem(node)?.Group is LayoutGroup group)
            group.Name = name;
        SaveLayout("Renamed the group to " + name + ".");
    }

    private async Task MoveAsync(RepositoryNode node, int direction)
    {
        string where = direction < 0 ? "up" : "down";
        if (!await EnsureManualAsync("\"" + node.Name + "\" will move " + where))
            return;

        LayoutItem? item = ResolveItem(node);
        if (item == null || !RepositoryLayout.Move(_settings.ManualLayout!, item, direction))
        {
            SaveLayout(node.Name + " can't move further " + where + ".");
            return;
        }

        SaveLayout("Moved " + node.Name + " " + where + ".");
    }

    private async Task MoveToGroupAsync(RepositoryNode node)
    {
        // Every group except the node itself and its own groups, plus the top level
        List<RepositoryNode> groups = _rootNodes.SelectMany(root => root.SelfAndDescendants())
            .Where(candidate => candidate.IsGroup && !node.SelfAndDescendants().Contains(candidate))
            .ToList();
        List<string> labels = new List<string> { "(Top level)" };
        labels.AddRange(groups.Select(group => group.FullName));

        string? choice = await Dialogs.ChooseAsync(this, "Move to group", "Move \"" + node.Name + "\" to:", labels);
        if (choice == null)
            return;

        int index = labels.IndexOf(choice);
        RepositoryNode? target = index > 0 ? groups[index - 1] : null;
        string targetName = target?.FullName ?? "the top level";

        if (!await EnsureManualAsync("\"" + node.Name + "\" will move to " + targetName))
            return;

        LayoutItem? item = ResolveItem(node);
        LayoutGroup? targetGroup = target == null ? null : ResolveItem(target)?.Group;
        if (item == null || (target != null && targetGroup == null) ||
            !RepositoryLayout.MoveInto(_settings.ManualLayout!, item, targetGroup))
        {
            SaveLayout(node.Name + " is already there.");
            return;
        }

        SaveLayout("Moved " + node.Name + " to " + targetName + ".");
    }

    private async Task RemoveGroupAsync(RepositoryNode node)
    {
        // Switching to manual already asked, so only a manual layout asks here
        bool wasAutomatic = _settings.GroupByFolder;
        if (!await EnsureManualAsync("the group \"" + node.Name + "\" will be removed (its repositories stay)"))
            return;

        bool confirm = wasAutomatic ||
                       await Dialogs.ConfirmAsync(this, "Remove group",
                                                  "Remove the group \"" + node.Name +
                                                      "\"? Its repositories and groups move up a level.",
                                                  "Remove group");
        if (!confirm)
            return;

        if (ResolveItem(node) is LayoutItem item)
            RepositoryLayout.RemoveGroup(_settings.ManualLayout!, item);
        SaveLayout("Removed the group " + node.Name + ".");
    }

    private const int MANUAL_INDEX = 3;
    private bool _suppressGrouping;

    // Pinned items first, folders laid out as on disk; the rest follows the chosen grouping
    private List<RepositoryTreeNode> BuildAutomaticTree()
    {
        return RepositoryLayout.BuildWithPins(_settings.Repositories, _settings.PinnedFolders, rest =>
            _settings.Grouping switch
            {
                RepositoryGrouping.Folder => RepositoryLayout.BuildByFolder(rest),
                RepositoryGrouping.Owner => RepositoryLayout.BuildByOwner(rest, RemoteIdentity.Read),
                _ => RepositoryLayout.BuildByName(rest, RemoteIdentity.Read, Kin)
            }, _settings.PinnedRepositories);
    }

    // What relates copies of a repository: its remotes and its first commits
    private IReadOnlyCollection<string> Kin(string path)
    {
        List<string> keys = RemoteIdentity.ReadAll(path).Values.Select(remote => remote.Key).ToList();
        if (_settings.RootCommits.TryGetValue(RepositoryLayout.Normalize(path), out List<string>? roots))
            keys.AddRange(roots.Select(root => "root:" + root));
        return keys;
    }

    // Pin a folder group or a repository to the top of the list and to the toolbar
    private void AddPinItems(ContextMenu menu, RepositoryNode node)
    {
        string? folder = node.Source.Folder;
        if (node.IsGroup && folder != null && Directory.Exists(folder))
        {
            bool pinned = _settings.PinnedFolders.Contains(folder, RepositoryLayout.PathComparer);
            AddMenuItem(menu, pinned ? "Unpin" : "Pin", () => SetPinnedAsync(folder, !pinned));
        }
        else if (node.Path is { } path)
        {
            bool pinned = _settings.PinnedRepositories.Contains(path, RepositoryLayout.PathComparer);
            AddMenuItem(menu, pinned ? "Unpin" : "Pin", () => SetPinnedRepositoryAsync(path, !pinned));
            AddMenuItem(menu, "Pin its folder…", () => PinFolderOfAsync(path));
        }
    }

    private Task SetPinnedRepositoryAsync(string path, bool pinned)
    {
        _settings.PinnedRepositories.RemoveAll(existing => RepositoryLayout.PathComparer.Equals(existing, path));
        if (pinned)
            _settings.PinnedRepositories.Add(path);

        SaveLayout(Path.GetFileName(path) + (pinned ? " is pinned." : " is unpinned."));
        return Task.CompletedTask;
    }

    private async Task PinFolderOfAsync(string repository)
    {
        // The folders above the repository, nearest first, up to (not including) the drive
        List<string> folders = new List<string>();
        for (string? folder = Path.GetDirectoryName(repository);
             folder != null && Path.GetDirectoryName(folder) != null;
             folder = Path.GetDirectoryName(folder))
            folders.Add(folder);

        if (folders.Count == 0)
            return;

        string? chosen = await Dialogs.ChooseAsync(this, "Pin a folder",
                                                   "Which folder should be pinned? It goes to the top of the list, " +
                                                       "laid out as on disk, and to the toolbar.", folders);
        if (chosen != null)
            await SetPinnedAsync(chosen, true);
    }

    private Task SetPinnedAsync(string folder, bool pinned)
    {
        _settings.PinnedFolders.RemoveAll(existing => RepositoryLayout.PathComparer.Equals(existing, folder));
        if (pinned)
            _settings.PinnedFolders.Add(folder);

        SaveLayout(Path.GetFileName(folder) + (pinned ? " is pinned." : " is unpinned."));
        return Task.CompletedTask;
    }

    private int GroupingIndex()
    {
        return _settings.GroupByFolder ? (int)_settings.Grouping : MANUAL_INDEX;
    }

    private async Task OnGroupingChangedAsync()
    {
        int index = GroupingBox.SelectedIndex;
        if (_suppressGrouping || index < 0 || index == GroupingIndex())
            return;

        // Leaving the manual layout keeps it, so choosing Manual again brings it back
        if (!_settings.GroupByFolder && index != MANUAL_INDEX)
        {
            bool confirm = await Dialogs.ConfirmAsync(this, "Automatic grouping",
                                                      "Group repositories automatically? Your manual layout is " +
                                                          "kept and comes back if you choose Manual again.",
                                                      "Group automatically");
            if (!confirm)
            {
                _suppressGrouping = true;
                GroupingBox.SelectedIndex = MANUAL_INDEX;
                _suppressGrouping = false;
                return;
            }
        }

        _settings.GroupByFolder = index != MANUAL_INDEX;
        if (index != MANUAL_INDEX)
            _settings.Grouping = (RepositoryGrouping)index;
        SaveLayout(index switch
        {
            0 => "Grouping repositories by name, with forks and clones inside.",
            1 => "Grouping repositories by folder.",
            2 => "Grouping repositories by owner.",
            _ => "Manual layout: organise it from the right-click menu."
        });
    }

    // Group actions

    private async Task RunOnRepositoriesAsync(RepositoryNode node, string doneVerb, string busyVerb,
                                              Func<GitRepository, Task<GitResult>> action)
    {
        if (_busy)
            return;

        List<string> paths = RepositoryLayout.AllRepositories(node.Source).Where(Directory.Exists).ToList();
        if (paths.Count == 0)
        {
            SetStatus("No repositories to update in " + node.Name + ".", false);
            return;
        }

        List<string> failed = new List<string>();
        SetBusy(true, busyVerb + "…");
        try
        {
            for (int i = 0; i < paths.Count; ++i)
            {
                string name = Path.GetFileName(paths[i]);
                SetStatus($"{busyVerb} {i + 1}/{paths.Count}: {name}…", false);
                GitResult result = await action(new GitRepository(paths[i]));
                if (!result.Success)
                    failed.Add(name);
            }
        }
        finally
        {
            SetBusy(false, null);
        }

        string count = paths.Count == 1 ? Path.GetFileName(paths[0]) : paths.Count + " repositories";
        if (failed.Count == 0)
            SetStatus(doneVerb + " " + count + ".", false);
        else
            SetStatus($"{doneVerb} {count}, but {failed.Count} failed: {string.Join(", ", failed)}  (see Git log)",
                      true);

        await RefreshSummariesAsync();
        await RefreshAsync();
    }
}
