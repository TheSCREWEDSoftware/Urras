using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Urras.Core.Git;
using Urras.Core.Settings;

namespace Urras.App;

// Diff options (whitespace, wrapping, per file or combined), the per-file list of a commit, and image diffs
public partial class MainWindow
{
    private DiffSource? _shownSource;

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
        if (ImageFiles.IsImage(change.Path))
        {
            (byte[]? oldImage, byte[]? newImage) = await LoadChangeImagesAsync(change, staged);
            _changesDiffText = null;
            DiffTitle.Text = title;
            ShowImage(ImageView, DiffList, oldImage, newImage);
            return;
        }

        string diff = await _repository.GetDiffAsync(change, staged);
        if (diff.Trim().Length == 0)
            diff = change.Kind == ChangeKind.Conflicted ? "Conflicted file: resolve it, then stage it."
                                                        : "No text changes.";
        ShowChangesDiff(diff, title);
    }

    // Staged: last commit against the staged copy. Unstaged: staged copy against the file on disk
    private async Task<(byte[]?, byte[]?)> LoadChangeImagesAsync(FileChange change, bool staged)
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
    private sealed record DiffSource(string Title, string Target, string? From);

    private Task ShowCommitAsync(CommitInfo commit)
    {
        return ShowSourceAsync(new DiffSource(commit.ShortHash + "  " + commit.Subject, commit.Hash, null));
    }

    private async Task ShowSourceAsync(DiffSource source)
    {
        if (_repository == null)
            return;

        _shownSource = source;
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
        if (ImageFiles.IsImage(file.Path))
        {
            string from = source.From ?? await _repository.GetCommitBaseAsync(source.Target);
            string oldPath = file.OriginalPath ?? file.Path;
            byte[]? before = file.Kind == ChangeKind.Added ? null : await _repository.GetBlobAsync(from, oldPath);
            byte[]? after = file.Kind == ChangeKind.Deleted ? null
                                                            : await _repository.GetBlobAsync(source.Target, file.Path);
            _commitDiffText = null;
            CommitDiffTitle.Text = title;
            ShowImage(CommitImageView, CommitDiffList, before, after);
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
    }

    // Images replace the text diff until a text file is shown again
    private void ShowImage(ImageDiffView view, ListBox list, byte[]? before, byte[]? after)
    {
        list.ItemsSource = null;
        list.IsVisible = false;
        view.IsVisible = true;
        view.Show(before, after);
        UpdateHints();
    }

    private static void ShowText(ImageDiffView view, ListBox list)
    {
        if (!view.IsVisible)
            return;

        view.Clear();
        view.IsVisible = false;
        list.IsVisible = true;
    }
}
