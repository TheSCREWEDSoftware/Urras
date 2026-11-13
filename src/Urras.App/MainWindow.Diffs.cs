using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Urras.Core.Git;
using Urras.Core.Settings;

namespace Urras.App;

// Diff options (whitespace, wrapping, per file or combined), the per-file list of a commit, and image diffs
public partial class MainWindow
{
    private DiffSource? _shownSource;
    // Lines of a long commit message shown before it's opened, and the height of one as a share of the text size
    private const int FOLDED_BODY_LINES = 6;
    private const double BODY_LINE_SPACING = 1.4;

    private void InitializeDiffOptions()
    {
        WhitespaceBox.IsChecked = _settings.ShowWhitespace;
        CommitWhitespaceBox.IsChecked = _settings.ShowWhitespace;
        WrapBox.IsChecked = _settings.WrapDiffLines;
        CommitWrapBox.IsChecked = _settings.WrapDiffLines;
        FileModeBox.SelectedIndex = (int)_settings.FileListMode;
        CommitFileModeBox.SelectedIndex = (int)_settings.FileListMode;
        CommitFilesList.IsVisible = _settings.FileListMode == FileListMode.PerFile;

        WhitespaceBox.Click += (_, _) => OnWhitespaceChanged(WhitespaceBox);
        CommitWhitespaceBox.Click += (_, _) => OnWhitespaceChanged(CommitWhitespaceBox);
        WrapBox.Click += (_, _) => OnWrapChanged(WrapBox);
        CommitWrapBox.Click += (_, _) => OnWrapChanged(CommitWrapBox);
        PreviewBox.IsChecked = _settings.MarkdownPreview;
        CommitPreviewBox.IsChecked = _settings.MarkdownPreview;
        PreviewBox.Click += async (_, _) => await OnPreviewToggledAsync(PreviewBox.IsChecked == true);
        CommitPreviewBox.Click += async (_, _) => await OnPreviewToggledAsync(CommitPreviewBox.IsChecked == true);
        FileModeBox.SelectionChanged += async (_, _) => await OnFileModeChangedAsync(FileModeBox);
        CommitFileModeBox.SelectionChanged += async (_, _) => await OnFileModeChangedAsync(CommitFileModeBox);
        CommitFilesList.SelectionChanged += async (_, _) => await OnCommitFileSelectedAsync();
        UpdateDiffOptionsState();
    }

    // Raw shows git's own output untouched, so Whitespace and Wrap don't apply to it
    private void UpdateDiffOptionsState()
    {
        bool formatted = _settings.DiffView != DiffViewMode.Raw;
        foreach (CheckBox box in new[] { WhitespaceBox, CommitWhitespaceBox, WrapBox, CommitWrapBox })
            box.IsEnabled = formatted;
    }

    // Options shared by both diff panes

    private void OnWhitespaceChanged(CheckBox source)
    {
        _settings.ShowWhitespace = source.IsChecked == true;
        _settings.Save();
        WhitespaceBox.IsChecked = _settings.ShowWhitespace;
        CommitWhitespaceBox.IsChecked = _settings.ShowWhitespace;
        RenderDiff(DiffList, _changesDiffText);
        RenderDiff(CommitDiffList, _commitDiffText);
    }

    private void OnWrapChanged(CheckBox source)
    {
        _settings.WrapDiffLines = source.IsChecked == true;
        _settings.Save();
        WrapBox.IsChecked = _settings.WrapDiffLines;
        CommitWrapBox.IsChecked = _settings.WrapDiffLines;
        RenderDiff(DiffList, _changesDiffText);
        RenderDiff(CommitDiffList, _commitDiffText);
    }

    private async Task OnFileModeChangedAsync(ComboBox source)
    {
        if (_updatingDiffMode || source.SelectedIndex < 0)
            return;
        if ((FileListMode)source.SelectedIndex == _settings.FileListMode)
            return;

        _settings.FileListMode = (FileListMode)source.SelectedIndex;
        _settings.Save();

        _updatingDiffMode = true;
        FileModeBox.SelectedIndex = source.SelectedIndex;
        CommitFileModeBox.SelectedIndex = source.SelectedIndex;
        _updatingDiffMode = false;
        CommitFilesList.IsVisible = _settings.FileListMode == FileListMode.PerFile;

        if (UnstagedList.SelectedItem is FileChange unstaged)
            await ShowFileDiffAsync(unstaged, false);
        else if (StagedList.SelectedItem is FileChange staged)
            await ShowFileDiffAsync(staged, true);

        if (HistoryList.SelectedItem is CommitInfo commit)
            await ShowCommitAsync(commit);
    }

    // Applies the view, whitespace and wrapping settings to a diff list
    private void ApplyDiffLayout(ListBox list)
    {
        bool wrap = _settings.WrapDiffLines;
        bool scroll = !wrap && _settings.DiffView != DiffViewMode.Split;
        SetClass(list, "wrap", wrap);
        SetClass(list, "scroll", scroll);
        ScrollViewer.SetHorizontalScrollBarVisibility(list, scroll ? ScrollBarVisibility.Auto
                                                                   : ScrollBarVisibility.Disabled);
    }

    private static void SetClass(Control control, string name, bool enabled)
    {
        if (enabled && !control.Classes.Contains(name))
            control.Classes.Add(name);
        else if (!enabled)
            control.Classes.Remove(name);
    }

    // Changes tab

    private async Task ShowSelectedChangeAsync(FileChange change, bool staged)
    {
        if (_repository == null)
            return;

        _changesDiffStaged = staged;

        if (_settings.FileListMode == FileListMode.Combined)
        {
            List<FileChange> files = ((staged ? StagedList : UnstagedList).ItemsSource as IEnumerable<FileChange>)
                                         ?.ToList() ?? new List<FileChange> { change };
            string combined = await _repository.GetCombinedDiffAsync(files, staged);
            string what = staged ? "All staged changes" : "All changes";
            ShowChangesDiff(combined.Length > 0 ? combined : "No text changes.",
                            $"{what} ({files.Count} {(files.Count == 1 ? "file" : "files")})");
            return;
        }

        string title = change.DisplayPath + (staged ? "  (staged)" : string.Empty);
        bool markdown = Urras.Core.Markdown.PreviewFiles.CanPreview(change.Path);
        PreviewBox.IsVisible = markdown;
        if (ImageFiles.IsImage(change.Path) || (markdown && _settings.MarkdownPreview))
        {
            (byte[]? before, byte[]? after) = await LoadChangeContentsAsync(change, staged);
            _changesDiffText = null;
            DiffTitle.Text = title;
            DiffStats.Show(markdown ? await _repository.GetDiffAsync(change, staged) : null);
            if (markdown)
                ShowMarkdown(MarkdownView, ImageView, DiffList, before, after, change.Path,
                             ChangePictures(change, staged));
            else
                ShowImage(ImageView, MarkdownView, DiffList, before, after);
            return;
        }

        string diff = await _repository.GetDiffAsync(change, staged);
        if (diff.Trim().Length == 0)
            diff = change.Kind == ChangeKind.Conflicted ? "Conflicted file: resolve it, then stage it."
                                                        : "No text changes.";
        ShowChangesDiff(diff, title);
    }

    // Staged: last commit against the staged copy. Unstaged: staged copy against the file on disk
    private async Task<(byte[]?, byte[]?)> LoadChangeContentsAsync(FileChange change, bool staged)
    {
        GitRepository repository = _repository!;
        if (staged)
        {
            byte[]? before = change.Kind == ChangeKind.Added ? null
                                                             : await repository.GetBlobAsync(
                                                                   "HEAD", change.OriginalPath ?? change.Path);
            byte[]? after = change.Kind == ChangeKind.Deleted ? null : await repository.GetBlobAsync(string.Empty,
                                                                                                    change.Path);
            return (before, after);
        }

        byte[]? staging = change.Kind == ChangeKind.Untracked ? null
                                                             : await repository.GetBlobAsync(string.Empty, change.Path);
        return (staging, await repository.GetWorkingFileAsync(change.Path));
    }

    // History tab

    // What the history diff pane shows: one commit (From is null), or the difference between two revisions
    private sealed record DiffSource(string Title, string Target, string? From, CommitInfo? Commit = null);

    private Task ShowCommitAsync(CommitInfo commit)
    {
        return ShowSourceAsync(new DiffSource(commit.ShortHash + "  " + commit.Subject, commit.Hash, null, commit));
    }

    // The details above the diff: summary, the rest of the message, its people and hash; hidden for comparisons
    private async Task ShowCommitDetailsAsync(CommitInfo? commit)
    {
        CommitDetails.IsVisible = commit is { IsStash: false } && _repository != null;
        if (commit is not { IsStash: false } || _repository == null)
            return;

        string message = await _repository.GetCommitMessageAsync(commit.Hash) ?? string.Empty;
        int firstBreak = message.IndexOf('\n');
        string body = firstBreak < 0 ? string.Empty : message[(firstBreak + 1)..].Trim('\n', '\r');
        CommitDetails.DataContext = commit;
        CommitBody.Text = body;
        CommitBodyScroll.IsVisible = body.Length > 0;
        int lines = body.Split('\n').Length;
        CommitBodyToggle.IsVisible = lines > FOLDED_BODY_LINES;
        CommitBodyToggle.IsChecked = false;
        FoldCommitBody();
    }

    // A long message shows its first few lines until the toggle opens it all
    private void FoldCommitBody()
    {
        double lineHeight = CommitBody.FontSize * BODY_LINE_SPACING;
        CommitBodyScroll.MaxHeight = CommitBodyToggle.IsChecked == true ? double.PositiveInfinity
                                                                         : lineHeight * FOLDED_BODY_LINES;
    }

    private async Task ShowSourceAsync(DiffSource source)
    {
        if (_repository == null)
            return;

        _shownSource = source;
        await ShowCommitDetailsAsync(source.From == null ? source.Commit : null);
        if (_settings.FileListMode == FileListMode.Combined)
        {
            string diff = source.From == null ? await _repository.GetCommitDiffAsync(source.Target)
                                              : await _repository.GetCompareDiffAsync(source.From, source.Target);
            ShowCommitDiff(diff.Length > 0 ? diff : "No differences.", source.Title);
            return;
        }

        List<FileChange> files = source.From == null ? await _repository.GetCommitFilesAsync(source.Target)
                                                     : await _repository.GetCompareFilesAsync(source.From,
                                                                                              source.Target);
        _suppressSelection = true;
        CommitFilesHeader.Text = files.Count == 1 ? "1 changed file" : files.Count.ToString("N0") + " changed files";
        CommitFilesList.ItemsSource = files;
        CommitFilesList.SelectedItem = files.FirstOrDefault();
        _suppressSelection = false;

        if (files.Count == 0)
            ShowCommitDiff("No file changes.", source.Title);
        else
            await ShowSourceFileAsync(source, files[0]);
    }

    private async Task OnCommitFileSelectedAsync()
    {
        if (_suppressSelection || _shownSource == null || CommitFilesList.SelectedItem is not FileChange file)
            return;

        await ShowSourceFileAsync(_shownSource, file);
    }

    private async Task ShowSourceFileAsync(DiffSource source, FileChange file)
    {
        if (_repository == null)
            return;

        string title = source.Title + "  ·  " + file.DisplayPath;
        bool markdown = Urras.Core.Markdown.PreviewFiles.CanPreview(file.Path);
        CommitPreviewBox.IsVisible = markdown;
        if (ImageFiles.IsImage(file.Path) || (markdown && _settings.MarkdownPreview))
        {
            string from = source.From ?? await _repository.GetCommitBaseAsync(source.Target);
            string oldPath = file.OriginalPath ?? file.Path;
            byte[]? before = file.Kind == ChangeKind.Added ? null : await _repository.GetBlobAsync(from, oldPath);
            byte[]? after = file.Kind == ChangeKind.Deleted ? null
                                                            : await _repository.GetBlobAsync(source.Target, file.Path);
            _commitDiffText = null;
            CommitDiffTitle.Text = title;
            CommitDiffStats.Show(!markdown ? null
                                 : source.From == null ? await _repository.GetCommitFileDiffAsync(source.Target, file)
                                                       : await _repository.GetCompareDiffAsync(source.From,
                                                                                               source.Target, file));
            if (markdown)
                ShowMarkdown(CommitMarkdownView, CommitImageView, CommitDiffList, before, after, file.Path,
                             PreviewPictures.For(_settings, file.Path, path => _repository.GetBlobAsync(from, path),
                                                 path => _repository.GetBlobAsync(source.Target, path)));
            else
                ShowImage(CommitImageView, CommitMarkdownView, CommitDiffList, before, after);
            return;
        }

        string diff = source.From == null ? await _repository.GetCommitFileDiffAsync(source.Target, file)
                                          : await _repository.GetCompareDiffAsync(source.From, source.Target, file);
        ShowCommitDiff(diff, title);
    }

    private void ClearCommitFiles()
    {
        _shownSource = null;
        CommitFilesList.ItemsSource = null;
        CommitDetails.IsVisible = false;
    }

    // Images and rendered Markdown replace the text diff until a text file is shown again
    private void ShowImage(ImageDiffView view, MarkdownDiffView markdown, ListBox list, byte[]? before,
                           byte[]? after)
    {
        HideMarkdown(markdown);
        list.ItemsSource = null;
        list.IsVisible = false;
        view.IsVisible = true;
        view.Show(before, after);
        UpdateHints();
    }

    // Pictures for a change: the staged side reads the staging area, the other the last commit or the files on disk
    private MarkdownDiffView.ImageLoader? ChangePictures(FileChange change, bool staged)
    {
        GitRepository repository = _repository!;
        return PreviewPictures.For(_settings, change.Path,
                                   path => repository.GetBlobAsync(staged ? "HEAD" : string.Empty, path),
                                   path => staged ? repository.GetBlobAsync(string.Empty, path)
                                                  : repository.GetWorkingFileAsync(path));
    }

    private void ShowMarkdown(MarkdownDiffView view, ImageDiffView image, ListBox list, byte[]? before,
                              byte[]? after, string path, MarkdownDiffView.ImageLoader? pictures)
    {
        HideImage(image);
        list.ItemsSource = null;
        list.IsVisible = false;
        view.IsVisible = true;
        view.JumpToFirstChange = _settings.JumpToFirstChange;
        view.Show(before, after, path, pictures);
        UpdateHints();
    }

    private static void ShowText(ImageDiffView image, MarkdownDiffView markdown, ListBox list)
    {
        HideImage(image);
        HideMarkdown(markdown);
        list.IsVisible = true;
    }

    private static void HideImage(ImageDiffView view)
    {
        if (!view.IsVisible)
            return;
        view.Clear();
        view.IsVisible = false;
    }

    private static void HideMarkdown(MarkdownDiffView view)
    {
        if (!view.IsVisible)
            return;
        view.Clear();
        view.IsVisible = false;
    }

    // The Preview switch shows Markdown files rendered or as a text diff, in both tabs
    private async Task OnPreviewToggledAsync(bool preview)
    {
        _settings.MarkdownPreview = preview;
        _settings.Save();
        PreviewBox.IsChecked = preview;
        CommitPreviewBox.IsChecked = preview;
        await ReloadChangesDiffAsync();
        if (_shownSource != null && CommitFilesList.SelectedItem is FileChange file)
            await ShowSourceFileAsync(_shownSource, file);
    }
}
