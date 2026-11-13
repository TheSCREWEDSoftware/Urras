using Avalonia.Controls;
using Urras.Core.Accounts;
using Urras.Core.Git;
using Urras.Core.Settings;

namespace Urras.App;

// Undoing the last commit, resetting to a commit, renaming branches, ignoring files and managing remotes
public partial class MainWindow
{
    // The commit just made, so the strip under Commit can offer to take it back while it's still the newest
    private sealed record MadeCommit(string Repository, string Hash, string Summary, string Description,
                                     IReadOnlyList<CoAuthor> CoAuthors);

    private MadeCommit? _madeCommit;

    private void InitializeManageTools()
    {
        UndoCommitButton.Click += async (_, _) => await UndoLastCommitAsync();
        CommitChecksPanel.PointerPressed += (_, e) =>
        {
            if (_checksUrl != null && e.GetCurrentPoint(CommitChecksPanel).Properties.IsLeftButtonPressed)
                _ = Report(SystemShell.OpenUrl(_checksUrl));
        };
    }

    // Called after a commit succeeds
    private async Task RememberCommitAsync(string summary, string description, IReadOnlyList<CoAuthor> coAuthors)
    {
        if (_repository == null || await _repository.GetHeadAsync() is not { } head)
            return;
        _madeCommit = new MadeCommit(_repository.Path, head, summary, description, coAuthors.ToList());
        UpdateUndoBar();
    }

    // The strip shows while the commit just made is still the one the branch is on
    private void UpdateUndoBar()
    {
        bool show = _madeCommit is { } made && _repository?.Path == made.Repository &&
                    _status?.HeadCommit == made.Hash;
        UndoCommitBar.IsVisible = show;
        if (show)
            UndoCommitText.Text = "Committed: " + _madeCommit!.Summary;
    }

    private async Task UndoLastCommitAsync()
    {
        if (_repository == null || _madeCommit is not { } made)
            return;

        // A pushed commit is on the remote too; taking it back here means the next push has to force
        bool pushed = _status?.Upstream != null && !(await _repository.GetUnpushedAsync(1)).Contains(made.Hash);
        if (pushed && !await Dialogs.ConfirmAsync(this, "Undo commit",
                                                  "This commit is already pushed. Taking it back here means the " +
                                                  "next push has to replace it on the remote (force push).",
                                                  "Undo anyway"))
            return;

        if (!await RunGitAsync("Undoing the commit…", repository => repository.UndoLastCommitAsync(),
                               "Took back \"" + made.Summary + "\"; its changes are staged."))
            return;

        _madeCommit = null;
        UpdateUndoBar();
        // The message comes back for committing again, unless something new has been written meanwhile
        if (string.IsNullOrWhiteSpace(SummaryBox.Text) && string.IsNullOrWhiteSpace(DescriptionBox.Text))
        {
            SummaryBox.Text = made.Summary;
            DescriptionBox.Text = made.Description;
            SetCoAuthors(made.CoAuthors);
        }
    }

    // Moves the current branch to a commit, keeping the changes staged or unstaged, or throwing them away
    private async Task ResetToCommitAsync(CommitInfo commit)
    {
        if (_repository == null || _status == null)
            return;

        string branch = _status.Branch ?? "HEAD";
        string pushedNote = _status.Upstream != null
            ? " If the commits after it were pushed, the next push has to replace them (force push)."
            : string.Empty;
        int choice = await Dialogs.PickActionAsync(this, "Reset to " + commit.ShortHash,
                                                   "Move " + branch + " back to " + commit.ShortHash + " \"" +
                                                   commit.Subject + "\". What happens to the changes in the " +
                                                   "commits after it?" + pushedNote, null,
                                                   "Keep them, staged", "Keep them, unstaged", "Throw them away…");
        ResetMode? mode = choice switch
        {
            0 => ResetMode.Soft,
            1 => ResetMode.Mixed,
            2 => ResetMode.Hard,
            _ => null
        };
        if (mode == null)
            return;

        string? backup = null;
        if (mode == ResetMode.Hard)
        {
            if (!await Dialogs.ConfirmAsync(this, "Throw the changes away",
                                            "The commits after " + commit.ShortHash + " and every uncommitted " +
                                            "change will be gone. Urras tags where " + branch + " is now first, " +
                                            "so the commits can be found again.", "Throw them away"))
                return;
            backup = GitRepository.BackupTagName(_status.Branch, DateTime.Now);
            GitResult tagged = await _repository.CreateTagAsync(backup, "HEAD", null);
            if (!ReportResult(tagged, null))
                return;
        }

        await RunGitAsync("Resetting to " + commit.ShortHash + "…",
                          repository => repository.ResetAsync(commit.Hash, mode.Value),
                          branch + " is at " + commit.ShortHash + " now." +
                          (backup == null ? string.Empty : " The old commits are kept in " + backup + "."));
    }

    private async Task RenameBranchAsync()
    {
        if (_repository == null)
            return;

        List<BranchInfo> locals = _branches.Where(branch => !branch.IsRemote).ToList();
        string? current = locals.FirstOrDefault(branch => branch.IsCurrent)?.Name;
        string? name = locals.Count == 1 ? locals[0].Name
            : await Dialogs.ChooseAsync(this, "Rename branch", "Branch to rename:",
                                        locals.Select(branch => branch.Name).OrderBy(item => item != current));
        if (name == null || locals.FirstOrDefault(branch => branch.Name == name) is not { } chosen)
            return;

        string? newName = await Dialogs.PromptAsync(this, "Rename branch", "New name for " + name + ":", name);
        if (newName == null || newName == name)
            return;

        bool remote = chosen.Upstream != null &&
                      await Dialogs.ConfirmAsync(this, "Rename on the remote too?",
                                                 name + " is also on " + chosen.Upstream + ". Rename it there " +
                                                 "too? Anyone else using it will need to switch to the new name.",
                                                 "Rename there too");
        await RunGitAsync("Renaming " + name + "…", repository => repository.RenameBranchAsync(name, newName, remote),
                          "Renamed " + name + " to " + newName + ".");
    }

    // Ignore this file, every file of its kind, or its folder, for a file Git isn't tracking yet
    private void AddIgnoreItems(ContextMenu menu, FileChange change)
    {
        if (change.Kind != ChangeKind.Untracked)
            return;

        (string file, string? extension, string? folder) = GitRepository.IgnorePatterns(change.Path);
        MenuItem ignore = new MenuItem { Header = "Ignore" };
        ignore.Items.Add(IgnoreItem("This file", file));
        if (extension != null)
            ignore.Items.Add(IgnoreItem("All " + extension[1..] + " files", extension));
        if (folder != null)
            ignore.Items.Add(IgnoreItem("Its folder (" + folder.Trim('/') + ")", folder));
        menu.Items.Add(ignore);
        menu.Items.Add(new Separator());
    }

    private MenuItem IgnoreItem(string header, string pattern)
    {
        MenuItem item = new MenuItem { Header = header };
        ToolTip.SetTip(item, "Adds " + pattern + " to .gitignore");
        item.Click += async (_, _) =>
        {
            if (_repository == null)
                return;
            if (ReportResult(await _repository.AddToGitIgnoreAsync(pattern), "Added " + pattern + " to .gitignore."))
                await RefreshAsync();
        };
        return item;
    }

    // Checks found per commit; finished ones are kept, running ones are asked again after a while
    private readonly Dictionary<string, (DateTime Asked, CommitChecks? Checks)> _checks = new();
    private static readonly TimeSpan RunningChecksAge = TimeSpan.FromSeconds(30);
    private string? _checksUrl;

    // The commit's CI result from the platform its origin is on, when you're signed in there
    private async Task ShowChecksAsync(CommitInfo commit)
    {
        CommitChecksPanel.IsVisible = false;
        if (_accounts == null || _repository == null || RemoteForChecks(_repository.Path) is not { } remote)
            return;

        CommitChecks? checks;
        bool known = _checks.TryGetValue(commit.Hash, out (DateTime Asked, CommitChecks? Checks) cached) &&
                     (cached.Checks?.State != CheckState.Running || DateTime.UtcNow - cached.Asked < RunningChecksAge);
        if (known)
            checks = cached.Checks;
        else
        {
            try
            {
                PlatformClient? client = await _accounts.ClientForHostAsync(remote.Host);
                if (client == null)
                    return;
                checks = await PlatformClient.SafelyAsync(() => client.GetChecksAsync(remote.Owner, remote.Name,
                                                                                      commit.Hash));
                _checks[commit.Hash] = (DateTime.UtcNow, checks);
            }
            catch (PlatformException)
            {
                // Offline, or a token without access to CI: the details simply don't mention checks
                return;
            }
        }

        if (checks == null || !ReferenceEquals(CommitDetails.DataContext, commit))
            return;
        CommitChecksDot.Fill = ThemeBrushes.Get(checks.State switch
        {
            CheckState.Passed => ThemeColours.ADDED,
            CheckState.Failed => ThemeColours.ERROR,
            CheckState.Running => ThemeColours.ATTENTION,
            _ => ThemeColours.MUTED_TEXT
        });
        CommitChecksText.Text = checks.Summary;
        ToolTip.SetTip(CommitChecksPanel, "Open the checks on the website");
        _checksUrl = checks.WebUrl;
        CommitChecksPanel.IsVisible = true;
    }

    // The commits live on origin; a repository without one uses its first remote
    private static RemoteIdentity? RemoteForChecks(string path)
    {
        Dictionary<string, RemoteIdentity> remotes = RemoteIdentity.ReadAll(path);
        return remotes.TryGetValue(RemoteIdentity.ORIGIN, out RemoteIdentity? origin) ? origin
             : remotes.Values.FirstOrDefault();
    }

    private async Task ManageRemotesAsync()
    {
        if (_repository != null)
            await Dialogs.RemotesAsync(this, _repository);
        await RefreshAsync();
    }
}
