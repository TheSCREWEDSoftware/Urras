using System.Reflection;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;
using Urras.Core.Git;
using Urras.Core.Settings;

namespace Urras.App;

// Everyday helpers: hints, file filter, commit message tools, open and copy menus, background fetch
public partial class MainWindow
{
    private const int RECOMMENDED_SUMMARY_LENGTH = 50;
    private const int MAXIMUM_SUMMARY_LENGTH = 72;
    private const int RECENT_MESSAGE_COUNT = 15;
    private const int MAX_WARNINGS_LISTED = 5;
    private const int AUTO_FETCH_START_DELAY_MS = 5000;

    private static IBrush WarningBrush => ThemeBrushes.Warning;

    private readonly DispatcherTimer _autoFetchTimer = new DispatcherTimer();
    private List<CommitInfo> _history = new List<CommitInfo>();
    private CommitTemplate? _template;
    private string? _templateRepository;
    private bool _autoFetching;

    // The folder an error message points to; clicking the status line opens it
    private string? _statusFolder;

    private void InitializeBasics()
    {
        StatusText.PointerPressed += (_, _) =>
        {
            if (_statusFolder != null)
                _ = Report(SystemShell.OpenFolder(_statusFolder));
        };

        HideUntrackedBox.IsChecked = _settings.HideUntracked;
        HideUntrackedBox.Click += (_, _) =>
        {
            _settings.HideUntracked = HideUntrackedBox.IsChecked == true;
            _settings.Save();
            ShowFileLists();
        };
        FileFilterBox.TextChanged += (_, _) => ShowFileLists();

        SummaryBox.TextChanged += (_, _) => UpdateSubjectCounter();
        RecentButton.Click += (_, _) => ShowRecentMessages();
        TemplateButton.Click += async (_, _) =>
        {
            if (_template != null)
                await Dialogs.ShowTextAsync(this, "Commit message template", TemplateText(_template));
        };

        AboutButton.Click += async (_, _) => await Dialogs.AboutAsync(this, AppVersion());
        SettingsButton.Click += async (_, _) => await OpenSettingsAsync();

        UnstagedList.ContextMenu = FileContextMenu(UnstagedList);
        StagedList.ContextMenu = FileContextMenu(StagedList);
        HistoryList.ContextMenu = HistoryContextMenu();

        _autoFetchTimer.Tick += async (_, _) => await AutoFetchAsync();
        RestartAutoFetch();
        _ = FetchSoonAfterStartAsync();

        UpdateSubjectCounter();
    }

    // Change lists, filter and line endings

    private async Task ApplyLineEndingWarningsAsync(RepositoryStatus status)
    {
        if (_repository == null)
            return;

        List<string> paths = status.Staged.Concat(status.Unstaged)
            .Where(change => change.Kind != ChangeKind.Deleted)
            .Select(change => change.Path)
            .Distinct(StringComparer.Ordinal)
            .ToList();
        Dictionary<string, LineEndingInfo> endings = await LineEndings.GetAsync(_repository.Path, paths);

        foreach (FileChange change in status.Staged)
            change.Warning = endings.GetValueOrDefault(change.Path)?.StagedWarning;
        foreach (FileChange change in status.Unstaged)
            change.Warning = endings.GetValueOrDefault(change.Path)?.WorkingTreeWarning;
    }

    // Shows the current status through the filter box and the untracked files setting
    private void ShowFileLists()
    {
        if (_status == null)
        {
            UnstagedList.ItemsSource = null;
            StagedList.ItemsSource = null;
            UpdateHints();
            return;
        }

        string? selectedUnstaged = (UnstagedList.SelectedItem as FileChange)?.Path;
        string? selectedStaged = (StagedList.SelectedItem as FileChange)?.Path;
        string filter = FileFilterBox.Text?.Trim() ?? string.Empty;

        bool Matches(FileChange change) =>
            filter.Length == 0 || change.DisplayPath.Contains(filter, StringComparison.OrdinalIgnoreCase);

        List<FileChange> unstaged = _status.Unstaged
            .Where(change => !_settings.HideUntracked || change.Kind != ChangeKind.Untracked)
            .Where(Matches)
            .ToList();
        List<FileChange> staged = _status.Staged.Where(Matches).ToList();

        // Unchanged lists are left alone, keeping their scroll position and selection
        string signature = FilesSignature(unstaged) + "\0" + FilesSignature(staged);
        if (signature != _filesSignature || UnstagedList.ItemsSource == null)
        {
            _filesSignature = signature;
            bool wasSuppressed = _suppressSelection;
            _suppressSelection = true;
            UnstagedList.ItemsSource = unstaged;
            StagedList.ItemsSource = staged;
            UnstagedList.SelectedItem = unstaged.FirstOrDefault(change => change.Path == selectedUnstaged);
            StagedList.SelectedItem = staged.FirstOrDefault(change => change.Path == selectedStaged);
            _suppressSelection = wasSuppressed;
        }

        int hidden = _status.Unstaged.Count - unstaged.Count;
        UnstagedHeader.Text = hidden > 0 ? $"Changes ({unstaged.Count}, {hidden} hidden)"
                                         : $"Changes ({unstaged.Count})";
        StagedHeader.Text = staged.Count < _status.Staged.Count ? $"Staged ({staged.Count} of {_status.Staged.Count})"
                                                                : $"Staged ({staged.Count})";
        UpdateHints();
    }

    private string? _filesSignature;
    private string? _historySignature;

    // A new repository starts with fresh lists
    private void ResetListSignatures()
    {
        _filesSignature = null;
        _historySignature = null;
    }

    private static string FilesSignature(IEnumerable<FileChange> changes)
    {
        return string.Join("\n", changes.Select(change => change.Kind + "\t" + change.Path + "\t" +
                                                          change.OriginalPath));
    }

    // Commits, their labels (branches, tags) and stashes; a change in any of them redraws the history
    private static string HistorySignature(IEnumerable<CommitInfo> history)
    {
        return string.Join("\n", history.Select(commit => commit.Hash + "\t" + commit.Refs + "\t" +
                                                          commit.Stash?.Reference));
    }

    private void UpdateHints()
    {
        bool hasRepository = _repository != null;
        RepositoryHint.IsVisible = _settings.Repositories.Count == 0 || (_searching && _rootNodes.Count == 0);
        RepositoryHint.Text = _settings.Repositories.Count == 0 ? "Add a repository with Add… or Clone… to get started"
                                                                : "No repositories match the search";
        UnstagedHint.IsVisible = hasRepository && UnstagedList.ItemCount == 0;
        bool filtered = _status != null && _status.Unstaged.Count > 0;
        UnstagedHint.Text = filtered ? "No changes match the filter, or they are hidden untracked files" : "No changes";
        StagedHint.IsVisible = hasRepository && StagedList.ItemCount == 0;
        HistoryHint.IsVisible = hasRepository && HistoryList.ItemCount == 0;
        DiffHint.IsVisible =
            hasRepository && DiffList.ItemCount == 0 && !ImageView.IsVisible && !MarkdownView.IsVisible;
        CommitDiffHint.IsVisible = hasRepository && HistoryList.ItemCount > 0 && CommitDiffList.ItemCount == 0 &&
                                   !CommitImageView.IsVisible && !CommitMarkdownView.IsVisible;
    }

    private Task<GitResult> StageAllOrTrackedAsync(GitRepository repository)
    {
        return _settings.HideUntracked ? repository.StageTrackedAsync() : repository.StageAllAsync();
    }

    // Asks before committing files whose line endings would go in wrong; false cancels the commit
    private async Task<bool> ConfirmLineEndingsAsync()
    {
        if (_status == null)
            return true;

        List<FileChange> problems = _status.Staged.Where(change => change.HasWarning).ToList();
        if (problems.Count == 0)
            return true;

        string list = string.Join("\n", problems.Take(MAX_WARNINGS_LISTED)
                                               .Select(change => "• " + change.Path + ": " + change.Warning));
        if (problems.Count > MAX_WARNINGS_LISTED)
            list += "\n• and " + (problems.Count - MAX_WARNINGS_LISTED) + " more";

        string files = problems.Count == 1 ? "1 staged file has" : problems.Count + " staged files have";
        return await Dialogs.ConfirmAsync(this, "Line endings",
                                          files + " line-ending problems:\n\n" + list + "\n\nCommit anyway?",
                                          "Commit anyway");
    }

    // Commit message helpers

    private void UpdateSubjectCounter()
    {
        int length = SummaryBox.Text?.Length ?? 0;
        SubjectCounter.Text = length.ToString();
        SubjectCounter.Foreground = length > MAXIMUM_SUMMARY_LENGTH ? ErrorBrush :
                                    length > RECOMMENDED_SUMMARY_LENGTH ? WarningBrush : NormalBrush;
    }

    private void ShowRecentMessages()
    {
        if (_repository == null)
            return;

        ContextMenu menu = new ContextMenu();
        foreach (CommitInfo commit in _history.DistinctBy(commit => commit.Subject).Take(RECENT_MESSAGE_COUNT))
        {
            MenuItem item = new MenuItem { Header = Literal(commit.Subject) };
            item.Click += async (_, _) => await UseMessageOfAsync(commit.Hash);
            menu.Items.Add(item);
        }

        if (menu.Items.Count == 0)
        {
            SetStatus("No commits yet.", false);
            return;
        }

        menu.Open(RecentButton);
    }

    private async Task UseMessageOfAsync(string hash)
    {
        if (_repository == null)
            return;

        string? message = await _repository.GetCommitMessageAsync(hash);
        if (message != null)
            FillMessage(message);
    }

    private void FillMessage(string message)
    {
        string[] parts = message.Replace("\r\n", "\n").Split('\n', 2);
        SummaryBox.Text = parts[0].Trim();
        DescriptionBox.Text = parts.Length > 1 ? parts[1].Trim('\n') : string.Empty;
    }

    // Loaded once per repository; prefills the message when the template has text besides its guidance
    private async Task LoadTemplateAsync()
    {
        if (_repository == null || _templateRepository == _repository.Path)
            return;

        _templateRepository = _repository.Path;
        string? text = await _repository.GetCommitTemplateAsync();
        _template = text == null ? null : CommitTemplate.Parse(text);

        TemplateButton.IsVisible = _template != null && !_template.IsEmpty;
        ToolTip.SetTip(SummaryBox, _template == null || _template.Guide.Length == 0 ? null : _template.Guide);

        if (_template != null && _template.Message.Length > 0 && string.IsNullOrWhiteSpace(SummaryBox.Text) &&
            string.IsNullOrWhiteSpace(DescriptionBox.Text))
            FillMessage(_template.Message);
    }

    private static string TemplateText(CommitTemplate template)
    {
        string text = template.Guide;
        if (template.Message.Length > 0)
            text += (text.Length > 0 ? "\n\n" : string.Empty) + "Prefilled message:\n" + template.Message;
        return text;
    }

    // Open and copy menus

    private ContextMenu FileContextMenu(ListBox list)
    {
        ContextMenu menu = new ContextMenu();
        menu.Opening += (_, _) =>
        {
            menu.Items.Clear();
            if (_repository == null || list.SelectedItem is not FileChange change)
                return;

            string fullPath = Path.Combine(_repository.Path, change.Path);
            AddConflictItems(menu, change);
            AddFileHistoryItems(menu, change);
            AddCustomCommandItems(menu, Urras.Core.Commands.CommandScope.File, CurrentContext(file: change.Path));
            if (change.Kind != ChangeKind.Deleted)
            {
                AddLocalFileItems(menu, fullPath);
                menu.Items.Add(new Separator());
            }

            AddMenuItem(menu, "Copy relative path", () => CopyAsync(change.Path));
            AddMenuItem(menu, "Copy full path", () => CopyAsync(fullPath));
            // New files aren't on the website yet
            if (change.Kind is not (ChangeKind.Untracked or ChangeKind.Added))
                AddViewFileOnlineItem(menu, change.Path, _status?.Branch ?? "HEAD");
        };
        return menu;
    }

    private ContextMenu HistoryContextMenu()
    {
        ContextMenu menu = new ContextMenu();
        menu.Opening += (_, _) =>
        {
            menu.Items.Clear();
            if (HistoryList.SelectedItem is not CommitInfo commit)
                return;

            if (commit.Stash is { } stash)
            {
                foreach (string action in new[] { "Apply", "Apply and drop", "Drop" })
                    AddMenuItem(menu, action, () => RunStashActionAsync(stash, action));
                return;
            }

            AddMenuItem(menu, "Copy SHA", () => CopyAsync(commit.Hash));
            AddMenuItem(menu, "Copy short SHA", () => CopyAsync(commit.ShortHash));
            AddMenuItem(menu, "Copy summary", () => CopyAsync(commit.Subject));
            AddMenuItem(menu, "Use this message", () => UseMessageOfAsync(commit.Hash));
            AddCommitActions(menu, commit);
            AddHistoryBrowseItems(menu, commit);
            AddBisectItems(menu, commit);
            AddCustomCommandItems(menu, Urras.Core.Commands.CommandScope.Commit, CurrentContext(commit: commit.Hash));
            if (_settings.Assistant.IsOn)
                AddMenuItem(menu, "Explain this commit (AI)", () => ExplainCommitAsync(commit));
        };
        return menu;
    }

    // Open and copy items for the repository tree menu
    private void AddOpenItems(ContextMenu menu, RepositoryNode node)
    {
        string? folder = node.Path ?? node.Source.Folder;
        if (folder == null || !Directory.Exists(folder))
            return;

        AddMenuItem(menu, "Open in file explorer", () => Report(SystemShell.OpenFolder(folder)));
        if (node.Path != null)
        {
            AddMenuItem(menu, "Open in terminal", () => Report(SystemShell.OpenShell(_settings.Shell, folder)));
            AddMenuItem(menu, "Open in " + EditorName,
                        () => Report(SystemShell.OpenInEditor(_settings.Editor, folder)));
        }

        AddMenuItem(menu, "Copy path", () => CopyAsync(folder));
        if (node.Path != null)
            AddViewOnlineItems(menu, node.Path);
        menu.Items.Add(new Separator());
    }

    // Something went wrong that no action handled; the details are in the crash log
    public void ShowUnexpectedError(Exception exception, string? log)
    {
        SetBusy(false, null);
        SetStatus("Something went wrong: " + FirstLine(exception.Message) +
                  (log == null ? string.Empty : " Click here to open crash.log's folder."), true);
        if (log != null)
        {
            _statusFolder = Path.GetDirectoryName(log);
            StatusText.Cursor = new Avalonia.Input.Cursor(Avalonia.Input.StandardCursorType.Hand);
            ToolTip.SetTip(StatusText, log);
        }
    }

    // The last run ended without closing normally: say so, and where its error is if it left one
    public void ShowLastRunEnded(bool logged)
    {
        string folder = Path.GetDirectoryName(CrashLog.DefaultPath) ?? string.Empty;
        SetStatus(logged
                      ? "Urras closed unexpectedly last time. Click here to open crash.log's folder."
                      : "Urras closed unexpectedly last time without logging an error. Windows' Event Viewer may " +
                        "have it under Windows Logs > Application.", true);
        if (!logged)
            return;

        _statusFolder = folder;
        StatusText.Cursor = new Avalonia.Input.Cursor(Avalonia.Input.StandardCursorType.Hand);
        ToolTip.SetTip(StatusText, CrashLog.DefaultPath);
    }

    // Show in file explorer, open in the editor, open with the program the system uses for the file
    private void AddLocalFileItems(ContextMenu menu, string fullPath)
    {
        AddMenuItem(menu, "Show in file explorer", () => Report(SystemShell.ShowFile(fullPath)));
        AddMenuItem(menu, "Open in " + EditorName, () => Report(SystemShell.OpenInEditor(_settings.Editor, fullPath)));
        AddMenuItem(menu, "Open with default program", () => Report(SystemShell.OpenFile(fullPath)));
    }

    private Task Report(string? error)
    {
        if (error != null)
            SetStatus(error, true);
        return Task.CompletedTask;
    }

    private async Task CopyAsync(string text)
    {
        if (Clipboard == null)
            return;

        await Clipboard.SetTextAsync(text);
        SetStatus("Copied " + text, false);
    }

    // Settings, About and background fetching

    private static string AppVersion()
    {
        string? version = typeof(MainWindow).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        return version ?? typeof(MainWindow).Assembly.GetName().Version?.ToString() ?? "unknown";
    }

    private void RestartAutoFetch()
    {
        _autoFetchTimer.Stop();
        if (_settings.AutoFetchMinutes <= 0)
        {
            AutoFetchText.Text = string.Empty;
            return;
        }

        _autoFetchTimer.Interval = TimeSpan.FromMinutes(_settings.AutoFetchMinutes);
        _autoFetchTimer.Start();
    }

    private async Task FetchSoonAfterStartAsync()
    {
        await Task.Delay(AUTO_FETCH_START_DELAY_MS);
        await UpdateNotificationCountAsync();
        await AutoFetchAsync();
    }

    // Fetches every repository quietly, then refreshes the counts of commits to pull
    private async Task AutoFetchAsync()
    {
        if (_autoFetching || _busy || _settings.AutoFetchMinutes <= 0)
            return;

        _autoFetching = true;
        try
        {
            foreach (string path in _settings.Repositories.ToList())
            {
                if (Directory.Exists(path))
                    await new GitRepository(path).FetchAsync(true);
            }

            AutoFetchText.Text = "Fetched " + DateTime.Now.ToString(DisplayFormats.TimeFormat);
            await RefreshSummariesAsync();
            await RefreshAsync();
            await UpdateNotificationCountAsync();
        }
        finally
        {
            _autoFetching = false;
        }
    }
}
