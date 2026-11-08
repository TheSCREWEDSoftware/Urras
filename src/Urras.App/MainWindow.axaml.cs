using System.Text;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using Urras.Core.Git;
using Urras.Core.Settings;

namespace Urras.App;

public partial class MainWindow : Window
{
    private const int MAX_LOG_CHARACTERS = 200000;

    // Read-only commands run on every refresh; only log them when they fail
    private static readonly HashSet<string> QuietCommands = new HashSet<string>
    {
        "status", "for-each-ref", "log", "diff", "show", "rev-parse", "config", "remote"
    };

    private static readonly IBrush ErrorBrush = new SolidColorBrush(Color.Parse("#FF7B72"));
    private static readonly IBrush NormalBrush = new SolidColorBrush(Color.Parse("#E6EDF3"));

    private readonly AppSettings _settings;
    private readonly StringBuilder _log = new StringBuilder();
    private GitRepository? _repository;
    private RepositoryStatus? _status;
    private List<BranchInfo> _branches = new List<BranchInfo>();
    private bool _busy;
    private bool _refreshing;
    private bool _refreshPending;
    private bool _suppressSelection;
    private bool _updatingDiffMode;
    private string? _changesDiffText;
    private string? _commitDiffText;

    public MainWindow()
    {
        InitializeComponent();

        _settings = AppSettings.Load();
        GitRunner.CommandFinished += OnGitCommandFinished;

        AddRepositoryButton.Click += async (_, _) => await AddRepositoryAsync();
        CloneButton.Click += async (_, _) => await CloneRepositoryAsync();
        RemoveRepositoryButton.Click += async (_, _) => await RemoveSelectedAsync();

        FetchButton.Click += async (_, _) => await RunGitAsync("Fetching…", repository => repository.FetchAsync(),
                                                               "Fetched.");
        PullButton.Click += async (_, _) => await RunGitAsync("Pulling…", repository => repository.PullAsync(),
                                                              "Pulled.");
        PushButton.Click += async (_, _) => await RunGitAsync("Pushing…", repository => repository.PushAsync(),
                                                              "Pushed.");
        RefreshButton.Click += async (_, _) => await RefreshAsync();

        BranchBox.SelectionChanged += async (_, _) => await OnBranchSelectedAsync();

        UnstagedList.SelectionChanged += async (_, _) => await OnFileSelectedAsync(UnstagedList, StagedList, false);
        StagedList.SelectionChanged += async (_, _) => await OnFileSelectedAsync(StagedList, UnstagedList, true);
        UnstagedList.DoubleTapped += async (_, _) => await StageSelectedAsync();
        StagedList.DoubleTapped += async (_, _) => await UnstageSelectedAsync();

        StageAllButton.Click += async (_, _) => await RunGitAsync("Staging…", StageAllOrTrackedAsync);
        UnstageAllButton.Click += async (_, _) => await RunGitAsync("Unstaging…",
                                                                    repository => repository.UnstageAllAsync());
        StageSelectedButton.Click += async (_, _) => await StageSelectedAsync();
        UnstageSelectedButton.Click += async (_, _) => await UnstageSelectedAsync();
        DiscardButton.Click += async (_, _) => await DiscardSelectedAsync();

        AmendBox.Click += async (_, _) => await OnAmendToggledAsync();
        CommitButton.Click += async (_, _) => await CommitAsync();

        HistoryList.SelectionChanged += async (_, _) => await OnCommitSelectedAsync();
        LogToggle.Click += (_, _) => LogBox.IsVisible = LogToggle.IsChecked == true;

        DiffModeBox.SelectedIndex = (int)_settings.DiffView;
        CommitDiffModeBox.SelectedIndex = (int)_settings.DiffView;
        DiffModeBox.SelectionChanged += (_, _) => OnDiffModeChanged(DiffModeBox);
        CommitDiffModeBox.SelectionChanged += (_, _) => OnDiffModeChanged(CommitDiffModeBox);

        AddHandler(KeyDownEvent, OnPreviewKeyDown, RoutingStrategies.Tunnel);
        Activated += async (_, _) =>
        {
            await RefreshAsync();
            await RefreshSummariesAsync();
        };
        Closed += (_, _) => GitRunner.CommandFinished -= OnGitCommandFinished;

        InitializeBasics();
        InitializeDiffOptions();
        InitializeActions();
        InitializeHistoryTools();
        InitializeLineStaging();
        InitializeBisect();
        InitializeAccounts();
        InitializeCoAuthors();
        InitializeRepositoryTree();
        UpdateButtons();
    }

    // Refresh

    private async Task RefreshAsync()
    {
        if (_refreshing)
        {
            _refreshPending = true;
            return;
        }

        _refreshing = true;
        try
        {
            do
            {
                _refreshPending = false;
                await RefreshOnceAsync();
            } while (_refreshPending);
        }
        finally
        {
            _refreshing = false;
        }
    }

    private async Task RefreshOnceAsync()
    {
        if (_repository == null)
        {
            ShowEmptyState();
            return;
        }

        if (!Directory.Exists(_repository.Path))
        {
            ShowEmptyState();
            SetStatus(_repository.Path + " no longer exists.", true);
            return;
        }

        try
        {
            string? selectedCommit = (HistoryList.SelectedItem as CommitInfo)?.Hash;

            RepositoryStatus status = await _repository.GetStatusAsync();
            List<BranchInfo> branches = await _repository.GetBranchesAsync();
            List<CommitInfo> history = await _repository.GetHistoryAsync(filter: _historyFilter,
                                                                         allBranches: _settings.HistoryAllBranches);

            // A search leaves gaps between commits, so the graph is only drawn for the full history
            if (_historyFilter == null)
                CommitGraph.Assign(history);
            await ApplyLineEndingWarningsAsync(status);
            await UpdateOperationBannerAsync(status);
            _status = status;
            _history = history;
            UpdateSearchState();
            await UpdateBisectBannerAsync();

            _suppressSelection = true;
            try
            {
                RepositoryTitle.Text = status.IsDetached ? _repository.Name + " (detached HEAD)" : _repository.Name;
                ToolTip.SetTip(RepositoryTitle, _repository.Path);
                ShowFileLists();

                List<BranchInfo> ordered = branches.Where(branch => !branch.IsRemote)
                    .Concat(branches.Where(branch => branch.IsRemote)).ToList();
                _branches = ordered;
                BranchBox.ItemsSource = ordered;
                BranchBox.SelectedItem = ordered.FirstOrDefault(branch => branch.IsCurrent);

                PullButton.Content = status.Behind > 0 ? $"Pull ({status.Behind})" : "Pull";
                PushButton.Content = status.Ahead > 0 ? $"Push ({status.Ahead})" :
                                     status.Upstream == null && !status.IsDetached ? "Publish" : "Push";

                HistoryList.ItemsSource = history;
                HistoryList.SelectedItem = history.FirstOrDefault(commit => commit.Hash == selectedCommit);
            }
            finally
            {
                _suppressSelection = false;
            }

            if (UnstagedList.SelectedItem is FileChange unstaged)
                await ShowFileDiffAsync(unstaged, false);
            else if (StagedList.SelectedItem is FileChange staged)
                await ShowFileDiffAsync(staged, true);
            else
                ShowChangesDiff(null, string.Empty);

            await UpdateSummaryAsync(_repository.Path, true);
            await LoadTemplateAsync();
            UpdateHints();

            if (!_busy && StatusText.Text == "Ready")
                SetStatus(DescribeStatus(status), false);
        }
        catch (GitException exception)
        {
            SetStatus(FirstLine(exception.Message), true);
        }

        UpdateButtons();
    }

    private void ShowEmptyState()
    {
        _suppressSelection = true;
        _status = null;
        _history = new List<CommitInfo>();
        ClearCommitFiles();
        OperationBanner.IsVisible = false;
        RepositoryTitle.Text = "No repository";
        ToolTip.SetTip(RepositoryTitle, null);
        UnstagedList.ItemsSource = null;
        StagedList.ItemsSource = null;
        HistoryList.ItemsSource = null;
        _branches = new List<BranchInfo>();
        BranchBox.ItemsSource = null;
        ShowChangesDiff(null, string.Empty);
        ShowCommitDiff(null, string.Empty);
        UnstagedHeader.Text = "Changes";
        StagedHeader.Text = "Staged";
        PullButton.Content = "Pull";
        PushButton.Content = "Push";
        _suppressSelection = false;
        UpdateHints();
        UpdateButtons();
    }

    private static string DescribeStatus(RepositoryStatus status)
    {
        string branch = status.IsDetached ? "detached HEAD" : status.Branch!;
        string upstream = status.Upstream == null ? "not published" :
                          status.Ahead == 0 && status.Behind == 0 ? "up to date with " + status.Upstream :
                          $"{status.Ahead} ahead, {status.Behind} behind {status.Upstream}";
        return $"On {branch}, {upstream}.";
    }

    // Changes

    private async Task OnFileSelectedAsync(ListBox list, ListBox otherList, bool staged)
    {
        if (_suppressSelection || _repository == null)
            return;

        if (list.SelectedItem is not FileChange change)
        {
            if (otherList.SelectedItem == null)
                ShowChangesDiff(null, string.Empty);
            return;
        }

        _suppressSelection = true;
        otherList.SelectedItems?.Clear();
        _suppressSelection = false;

        await ShowFileDiffAsync(change, staged);
    }

    private async Task ShowFileDiffAsync(FileChange change, bool staged)
    {
        if (_repository == null)
            return;

        await ShowSelectedChangeAsync(change, staged);
    }

    private static List<FileChange> Selected(ListBox list)
    {
        return list.SelectedItems?.OfType<FileChange>().ToList() ?? new List<FileChange>();
    }

    private async Task StageSelectedAsync()
    {
        List<FileChange> changes = Selected(UnstagedList);
        if (changes.Count == 0)
            return;

        await RunGitAsync("Staging…", repository => repository.StageAsync(changes.Select(change => change.Path)));
    }

    private async Task UnstageSelectedAsync()
    {
        List<FileChange> changes = Selected(StagedList);
        if (changes.Count == 0)
            return;

        // A rename is unstaged by resetting both its old and new paths
        IEnumerable<string> paths = changes.SelectMany(change =>
            change.OriginalPath == null ? new[] { change.Path } : new[] { change.OriginalPath, change.Path });
        await RunGitAsync("Unstaging…", repository => repository.UnstageAsync(paths));
    }

    private async Task DiscardSelectedAsync()
    {
        List<FileChange> changes = Selected(UnstagedList);
        if (changes.Count == 0)
        {
            SetStatus("Select unstaged changes to discard.", false);
            return;
        }

        string what = changes.Count == 1 ? changes[0].Path : changes.Count + " files";
        bool discard = await Dialogs.ConfirmAsync(this, "Discard changes",
                                                  "Discard the changes to " + what +
                                                  "? Untracked files are deleted. This cannot be undone.",
                                                  "Discard");
        if (!discard)
            return;

        await RunGitAsync("Discarding…", repository => repository.DiscardAsync(changes), "Discarded.");
    }

    private async Task OnAmendToggledAsync()
    {
        if (_repository == null || AmendBox.IsChecked != true || !string.IsNullOrWhiteSpace(SummaryBox.Text))
            return;

        string? message = await _repository.GetLastCommitMessageAsync();
        if (message == null)
            return;

        (string text, List<CoAuthor> coAuthors) = CoAuthor.Extract(message);
        string[] parts = text.Split('\n', 2);
        SummaryBox.Text = parts[0].Trim();
        DescriptionBox.Text = parts.Length > 1 ? parts[1].Trim('\n', '\r') : string.Empty;
        SetCoAuthors(coAuthors);
    }

    private async Task CommitAsync()
    {
        if (_repository == null || _status == null)
            return;

        string summary = SummaryBox.Text?.Trim() ?? string.Empty;
        string description = DescriptionBox.Text?.Trim() ?? string.Empty;
        bool amend = AmendBox.IsChecked == true;

        if (summary.Length == 0)
        {
            SetStatus("Write a summary first.", true);
            SummaryBox.Focus();
            return;
        }

        if (_status.Staged.Count == 0 && !amend)
        {
            SetStatus("Nothing is staged. Stage changes first.", true);
            return;
        }

        if (!await ConfirmLineEndingsAsync() || !await ConfirmWhitespaceAsync())
            return;

        string message = CoAuthor.Append(description.Length == 0 ? summary : summary + "\n\n" + description,
                                         _coAuthors);
        bool success = await RunGitAsync("Committing…", repository => repository.CommitAsync(message, amend),
                                         amend ? "Amended the last commit." : "Committed.");
        if (success)
        {
            SummaryBox.Text = string.Empty;
            DescriptionBox.Text = string.Empty;
            AmendBox.IsChecked = false;
            SetCoAuthors(Array.Empty<CoAuthor>());

            // The template prefills the next message again
            _templateRepository = null;
            await LoadTemplateAsync();
        }
    }

    // Branches

    private async Task OnBranchSelectedAsync()
    {
        if (_suppressSelection || _repository == null || BranchBox.SelectedItem is not BranchInfo branch ||
            branch.IsCurrent)
            return;

        if (!branch.IsRemote)
        {
            await RunGitAsync("Switching to " + branch.Name + "…", repository => repository.SwitchAsync(branch.Name),
                              "Switched to " + branch.Name + ".");
            return;
        }

        // Use an existing local branch with the same name, otherwise create one that tracks the remote
        string localName = branch.Name.Substring(branch.Name.IndexOf('/') + 1);
        bool localExists = _branches.Any(other => !other.IsRemote && other.Name == localName);
        await RunGitAsync("Switching to " + localName + "…",
                          repository => localExists ? repository.SwitchAsync(localName)
                                                    : repository.SwitchToRemoteAsync(branch.Name),
                          "Switched to " + localName + ".");
    }

    private async Task CreateBranchAsync()
    {
        if (_repository == null)
            return;

        string? name = await Dialogs.PromptAsync(this, "New branch",
                                                 "Name of the new branch (created from the current one):");
        if (name == null)
            return;

        await RunGitAsync("Creating " + name + "…", repository => repository.CreateBranchAsync(name, true),
                          "Created and switched to " + name + ".");
    }

    private async Task DeleteBranchAsync()
    {
        if (_repository == null)
            return;

        List<string> candidates = _branches
            .Where(branch => !branch.IsRemote && !branch.IsCurrent).Select(branch => branch.Name).ToList();
        if (candidates.Count == 0)
        {
            SetStatus("There are no other local branches to delete.", false);
            return;
        }

        string? name = await Dialogs.ChooseAsync(this, "Delete branch", "Local branch to delete:", candidates);
        if (name == null)
            return;

        GitResult result = await _repository.DeleteBranchAsync(name, false);
        if (!result.Success)
        {
            bool force = await Dialogs.ConfirmAsync(this, "Branch not merged",
                                                    name + " has commits that are not merged anywhere. " +
                                                        "Delete it anyway? " +
                                                    "Those commits may be lost.",
                                                    "Delete anyway");
            if (!force)
                return;

            result = await _repository.DeleteBranchAsync(name, true);
        }

        ReportResult(result, "Deleted " + name + ".");
        await RefreshAsync();
    }

    // History

    private async Task OnCommitSelectedAsync()
    {
        if (_suppressSelection || _repository == null)
            return;

        if (await CompareSelectedCommitsAsync())
            return;

        if (HistoryList.SelectedItem is not CommitInfo commit)
        {
            ClearCommitFiles();
            ShowCommitDiff(null, string.Empty);
            return;
        }

        await ShowCommitAsync(commit);
    }

    // Diff views

    private void ShowChangesDiff(string? text, string title)
    {
        _changesDiffText = text;
        DiffTitle.Text = title;
        ShowText(ImageView, DiffList);
        RenderDiff(DiffList, text);
    }

    private void ShowCommitDiff(string? text, string title)
    {
        _commitDiffText = text;
        CommitDiffTitle.Text = title;
        ShowText(CommitImageView, CommitDiffList);
        RenderDiff(CommitDiffList, text);
    }

    private void RenderDiff(ListBox list, string? text)
    {
        if (text == null)
        {
            list.ItemsSource = null;
            UpdateHints();
            return;
        }

        DiffViewMode mode = _settings.DiffView;
        string templateKey = mode switch
        {
            DiffViewMode.Split => "SplitDiffTemplate",
            DiffViewMode.Raw => "RawDiffTemplate",
            _ => "UnifiedDiffTemplate"
        };
        // Cleared first so no row is reused with the previous view's template
        list.ItemsSource = null;
        list.ItemTemplate = Resources[templateKey] as IDataTemplate;
        ApplyDiffLayout(list);
        bool whitespace = _settings.ShowWhitespace;
        list.ItemsSource = mode switch
        {
            DiffViewMode.Split => DiffRow.Split(text, whitespace),
            DiffViewMode.Raw => DiffRow.Raw(text),
            _ => DiffRow.Unified(text, whitespace)
        };
        UpdateHints();
    }

    private void OnDiffModeChanged(ComboBox source)
    {
        if (_updatingDiffMode || source.SelectedIndex < 0)
            return;

        _settings.DiffView = (DiffViewMode)source.SelectedIndex;
        _settings.Save();

        // Both selectors show the same setting
        _updatingDiffMode = true;
        DiffModeBox.SelectedIndex = source.SelectedIndex;
        CommitDiffModeBox.SelectedIndex = source.SelectedIndex;
        _updatingDiffMode = false;
        UpdateDiffOptionsState();

        RenderDiff(DiffList, _changesDiffText);
        RenderDiff(CommitDiffList, _commitDiffText);
    }

    // Running commands

    private async Task<bool> RunGitAsync(string busyText, Func<GitRepository, Task<GitResult>> action,
                                         string? successText = null)
    {
        if (_repository == null || _busy)
            return false;

        bool success = false;
        SetBusy(true, busyText);
        try
        {
            GitResult result = await action(_repository);
            success = ReportResult(result, successText);
        }
        catch (Exception exception) when (exception is GitException or IOException or UnauthorizedAccessException)
        {
            SetStatus(FirstLine(exception.Message), true);
        }
        finally
        {
            SetBusy(false, null);
        }

        await RefreshAsync();
        return success;
    }

    private bool ReportResult(GitResult result, string? successText)
    {
        if (result.Success)
        {
            SetStatus(successText ?? "Done.", false);
            return true;
        }

        SetStatus(FirstLine(result.Message) + "  (see Git log)", true);
        return false;
    }

    private void SetBusy(bool busy, string? text)
    {
        _busy = busy;
        if (text != null)
            SetStatus(text, false);
        UpdateButtons();
        Cursor = busy ? new Cursor(StandardCursorType.Wait) : Cursor.Default;
    }

    private void SetStatus(string text, bool isError)
    {
        StatusText.Text = text;
        StatusText.Foreground = isError ? ErrorBrush : NormalBrush;
    }

    private void UpdateButtons()
    {
        bool ready = _repository != null && !_busy;
        foreach (Control control in new Control[]
                 {
                     FetchButton, PullButton, PushButton, RefreshButton, BranchBox, BranchMenuButton, StashMenuButton,
                     StageAllButton, UnstageAllButton, StageSelectedButton, UnstageSelectedButton, DiscardButton,
                     CommitButton
                 })
        {
            control.IsEnabled = ready;
        }

        AddRepositoryButton.IsEnabled = !_busy;
        CloneButton.IsEnabled = !_busy;
        NewGroupButton.IsEnabled = !_busy;
        RemoveRepositoryButton.IsEnabled = !_busy;
    }

    private void OnPreviewKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.F5)
        {
            e.Handled = true;
            _ = RefreshAsync();
        }
        else if (e.Key == Key.P && e.KeyModifiers.HasFlag(KeyModifiers.Control))
        {
            e.Handled = true;
            _ = ShowQuickLaunchAsync();
        }
        else if (e.Key == Key.Enter && e.KeyModifiers.HasFlag(KeyModifiers.Control) &&
                 (SummaryBox.IsFocused || DescriptionBox.IsFocused))
        {
            e.Handled = true;
            _ = CommitAsync();
        }
    }

    private void OnGitCommandFinished(string commandLine, GitResult result, bool quiet)
    {
        string[] words = commandLine.Split(' ', 3);
        string verb = words.Length > 1 ? words[1] : string.Empty;
        if (result.Success && (quiet || QuietCommands.Contains(verb)))
            return;

        Dispatcher.UIThread.Post(() => AppendLog(commandLine, result));
    }

    private void AppendLog(string commandLine, GitResult result)
    {
        _log.Append("> ").Append(commandLine).Append(result.Success ? string.Empty : $"   [exit {result.ExitCode}]")
            .AppendLine();
        string details = (result.Output + result.Error).Trim();
        if (details.Length > 0)
            _log.AppendLine(details);

        if (_log.Length > MAX_LOG_CHARACTERS)
            _log.Remove(0, _log.Length - MAX_LOG_CHARACTERS);

        LogBox.Text = _log.ToString();
        LogBox.CaretIndex = LogBox.Text.Length;
    }

    private static string FirstLine(string text)
    {
        string trimmed = text.Trim();
        int newline = trimmed.IndexOf('\n');
        return newline < 0 ? trimmed : trimmed.Substring(0, newline).Trim();
    }
}
