using Avalonia.Controls;
using Urras.Core.Git;

namespace Urras.App;

// Bisect: start from History, then mark each checked-out commit good or bad from the banner
public partial class MainWindow
{
    private BisectState _bisect = BisectState.Inactive;

    private void InitializeBisect()
    {
        BisectGoodButton.Click += async (_, _) => await MarkBisectAsync(BisectMark.Good);
        BisectBadButton.Click += async (_, _) => await MarkBisectAsync(BisectMark.Bad);
        BisectSkipButton.Click += async (_, _) => await MarkBisectAsync(BisectMark.Skip);
        BisectStopButton.Click += async (_, _) => await RunGitAsync("Stopping the bisect…",
                                                                    repository => repository.BisectResetAsync(),
                                                                    "Bisect stopped; back where it started.");
    }

    private async Task UpdateBisectBannerAsync()
    {
        _bisect = _repository == null ? BisectState.Inactive : await _repository.GetBisectStateAsync();
        BisectBanner.IsVisible = _bisect.IsActive;
        if (!_bisect.IsActive)
            return;

        string? badHash = _bisect.FirstBad;
        bool found = badHash != null;
        BisectGoodButton.IsVisible = !found;
        BisectBadButton.IsVisible = !found;
        BisectSkipButton.IsVisible = !found;
        BisectStopButton.Content = found ? "Finish" : "Stop bisect";
        CommitInfo? firstBad = _history.FirstOrDefault(commit => commit.Hash == badHash);
        string badText = firstBad == null ? (badHash == null ? string.Empty : Hashes.Short(badHash))
                                          : firstBad.ShortHash + "  " + firstBad.Subject;
        BisectText.Text = found
            ? "Found it: the first bad commit is " + badText + ". Finish goes back to where you started."
            : "Bisecting: test the checked-out commit, then mark it. About " + _bisect.Remaining +
                  (_bisect.Remaining == 1 ? " commit" : " commits") + " left to narrow down.";
    }

    private async Task MarkBisectAsync(BisectMark mark)
    {
        string name = mark.ToString().ToLowerInvariant();
        await RunGitAsync("Marking " + name + "…", repository => repository.BisectMarkAsync(mark),
                          "Marked " + name + ".");
    }

    // History menu: one commit (known good, with HEAD as bad) or two selected commits (older good, newer bad)
    private void AddBisectItems(ContextMenu menu, CommitInfo commit)
    {
        if (_bisect.IsActive || commit.IsStash)
            return;

        List<CommitInfo> selected = HistoryList.SelectedItems?.OfType<CommitInfo>().ToList()
                                    ?? new List<CommitInfo>();
        if (selected.Count == 2)
        {
            List<CommitInfo> ordered = selected.OrderBy(item => _history.IndexOf(item)).ToList();
            AddMenuItem(menu, "Bisect between the two selected commits…",
                        () => StartBisectAsync(ordered[0].Hash, ordered[1].Hash));
            return;
        }

        AddMenuItem(menu, "Bisect: this commit is good, the current one is bad…",
                    () => StartBisectAsync("HEAD", commit.Hash));
    }

    private async Task StartBisectAsync(string bad, string good)
    {
        if (_status != null && (_status.Staged.Count > 0 ||
                                _status.Unstaged.Any(change => change.Kind != ChangeKind.Untracked)))
        {
            SetStatus("Commit or stash your changes before bisecting; it checks out other commits.", true);
            return;
        }

        bool confirm = await Dialogs.ConfirmAsync(this, "Bisect",
                                                  "Urras checks out commits between the good and the bad one. Test " +
                                                      "each, then mark it Good or Bad in the banner until the first " +
                                                      "bad commit is found.", "Start");
        if (confirm)
            await RunGitAsync("Starting the bisect…", repository => repository.BisectStartAsync(bad, good),
                              "Bisect started: test the checked-out commit.");
    }
}
