using Avalonia.Controls;
using Urras.Core.Git;

namespace Urras.App;

// Branch and stash menus, commit actions, merge and rebase with conflict handling, and repository settings
public partial class MainWindow
{
    private const int MAX_FILES_LISTED = 15;

    private OperationState _operation = OperationState.None;

    private void InitializeActions()
    {
        BranchMenuButton.Click += (_, _) => ShowBranchMenu();
        StashMenuButton.Click += (_, _) => ShowStashMenu();
        ContinueButton.Click += async (_, _) => await ContinueOperationAsync();
        AbortButton.Click += async (_, _) => await AbortOperationAsync();
    }

    // Menus

    private void ShowBranchMenu()
    {
        ContextMenu menu = new ContextMenu();
        AddMenuItem(menu, "New branch…", CreateBranchAsync);
        AddMenuItem(menu, "Delete branch…", DeleteBranchAsync);
        menu.Items.Add(new Separator());
        AddMenuItem(menu, "Merge a branch into the current one…", MergeBranchAsync);
        AddMenuItem(menu, "Rebase the current branch onto…", RebaseBranchAsync);
        AddMenuItem(menu, "Compare with a branch…", CompareWithBranchAsync);
        menu.Items.Add(new Separator());
        AddMenuItem(menu, "Tags…", ManageTagsAsync);
        AddMenuItem(menu, "Reflog (find lost commits)…", ShowReflogAsync);
        AddMenuItem(menu, "Submodules…", ManageSubmodulesAsync);
        AddMenuItem(menu, "Worktrees…", ManageWorktreesAsync);
        AddMenuItem(menu, "Git LFS…", ManageLfsAsync);
        AddMenuItem(menu, "Repository settings…", () => RepositorySettingsAsync(_repository?.Path));
        menu.Open(BranchMenuButton);
    }

    private void ShowStashMenu()
    {
        ContextMenu menu = new ContextMenu();
        AddMenuItem(menu, "Stash changes…", StashChangesAsync);
        AddMenuItem(menu, "Stashes…", ManageStashesAsync);
        menu.Open(StashMenuButton);
    }

    // Adds commit actions to the history menu
    private void AddCommitActions(ContextMenu menu, CommitInfo commit)
    {
        menu.Items.Add(new Separator());
        AddMenuItem(menu, "Create tag here…", () => CreateTagAsync(commit));
        AddMenuItem(menu, "Check out this commit…", () => CheckoutCommitAsync(commit));
        AddMenuItem(menu, "Revert this commit…", () => RevertCommitAsync(commit));
        AddMenuItem(menu, "Cherry-pick into the current branch…", () => CherryPickAsync(commit));
        AddMenuItem(menu, "Interactive rebase of the commits after this one…", () => InteractiveRebaseAsync(commit));
    }

    // Rewrites the current branch's commits after onto: reorder, reword, squash, fix up, edit or drop them
    private async Task InteractiveRebaseAsync(CommitInfo onto)
    {
        if (_repository == null || _status == null)
            return;

        if (_status.Staged.Count > 0 || _status.Unstaged.Any(change => change.Kind != ChangeKind.Untracked))
        {
            SetStatus("Commit or stash your changes before an interactive rebase.", true);
            return;
        }

        List<CommitInfo> commits = await _repository.GetRebaseCommitsAsync(onto.Hash);
        if (commits.Count == 0)
        {
            SetStatus("The current branch has no commits after " + onto.ShortHash + ".", false);
            return;
        }

        bool hasMerges = await _repository.HasMergesSinceAsync(onto.Hash);
        InteractiveRebaseWindow window = new InteractiveRebaseWindow(_status.Branch ?? "HEAD", onto, commits,
                                                                     hasMerges);
        if (!await window.ShowDialog<bool>(this))
            return;

        string? tag = window.CreateBackup ? GitRepository.BackupTagName(_status.Branch, DateTime.Now) : null;
        string done = "Rebase finished." + (tag == null ? string.Empty : " The old commits are kept in " + tag + ".");
        await RunGitAsync("Rebasing…", repository => repository.InteractiveRebaseAsync(onto.Hash, window.Plan, tag),
                          done);
    }

    // Commit actions

    private async Task CreateTagAsync(CommitInfo commit)
    {
        string? name = await Dialogs.PromptAsync(this, "Create tag", "Tag name for " + commit.ShortHash + ":");
        if (name == null)
            return;

        string? message = await Dialogs.PromptAsync(this, "Create tag",
                                                    "Message for an annotated tag (leave empty for a lightweight " +
                                                        "tag):");
        await RunGitAsync("Creating tag " + name + "…",
                          repository => repository.CreateTagAsync(name, commit.Hash, message),
                          "Created tag " + name + ".");
    }

    private async Task CheckoutCommitAsync(CommitInfo commit)
    {
        bool confirm = await Dialogs.ConfirmAsync(this, "Check out commit",
                                                  "Check out " + commit.ShortHash + " \"" + commit.Subject + "\"? " +
                                                      "You will not be on a branch (detached HEAD); create a branch " +
                                                      "to keep new commits.",
                                                  "Check out");
        if (confirm)
            await RunGitAsync("Checking out " + commit.ShortHash + "…",
                              repository => repository.CheckoutCommitAsync(commit.Hash),
                              "Checked out " + commit.ShortHash + ".");
    }

    private async Task RevertCommitAsync(CommitInfo commit)
    {
        bool confirm = await Dialogs.ConfirmAsync(this, "Revert commit",
                                                  "Create a new commit that undoes " + commit.ShortHash + " \"" +
                                                      commit.Subject + "\"?",
                                                  "Revert");
        if (confirm)
            await RunGitAsync("Reverting " + commit.ShortHash + "…", repository => repository.RevertAsync(commit.Hash),
                              "Reverted " + commit.ShortHash + ".");
    }

    private async Task CherryPickAsync(CommitInfo commit)
    {
        bool confirm = await Dialogs.ConfirmAsync(this, "Cherry-pick commit",
                                                  "Apply " + commit.ShortHash + " \"" + commit.Subject +
                                                      "\" to the current branch as a new commit?",
                                                  "Cherry-pick");
        if (confirm)
            await RunGitAsync("Cherry-picking " + commit.ShortHash + "…",
                              repository => repository.CherryPickAsync(commit.Hash),
                              "Cherry-picked " + commit.ShortHash + ".");
    }

    // Tags

    private async Task ManageTagsAsync()
    {
        if (_repository == null)
            return;

        List<TagInfo> tags = await _repository.GetTagsAsync();
        (TagInfo? tag, string? action) = await Dialogs.ListActionsAsync(
            this, "Tags", tags.Count == 0 ? "No tags yet. Create one from a commit in History." : "Tags:", tags,
            "Push", "Push all tags", "Delete", "Delete from remote");
        if (action == null)
            return;

        if (action == "Push all tags")
        {
            await RunGitAsync("Pushing tags…", repository => repository.PushAllTagsAsync(), "Pushed all tags.");
            return;
        }

        if (tag == null)
            return;

        switch (action)
        {
            case "Push":
                await RunGitAsync("Pushing " + tag.Name + "…", repository => repository.PushTagAsync(tag.Name),
                                  "Pushed " + tag.Name + ".");
                break;
            case "Delete":
                if (await Dialogs.ConfirmAsync(this, "Delete tag", "Delete the tag " + tag.Name + " here?", "Delete"))
                    await RunGitAsync("Deleting " + tag.Name + "…", repository => repository.DeleteTagAsync(tag.Name),
                                      "Deleted " + tag.Name + ".");
                break;
            case "Delete from remote":
                if (await Dialogs.ConfirmAsync(this, "Delete tag from remote",
                                               "Delete the tag " + tag.Name + " from the remote? Others will no " +
                                                   "longer get it.",
                                               "Delete from remote"))
                    await RunGitAsync("Deleting " + tag.Name + " from the remote…",
                                      repository => repository.DeleteRemoteTagAsync(tag.Name),
                                      "Deleted " + tag.Name + " from the remote.");
                break;
            default:
                break;
        }
    }

    // Stashes

    private async Task StashChangesAsync()
    {
        if (_status == null || !_status.HasChanges)
        {
            SetStatus("There are no changes to stash.", false);
            return;
        }

        (string Message, bool IncludeUntracked)? result = await Dialogs.StashAsync(this);
        if (result == null)
            return;

        await RunGitAsync("Stashing…",
                          repository => repository.StashAsync(result.Value.Message, result.Value.IncludeUntracked),
                          "Stashed your changes.");
    }

    private async Task ManageStashesAsync()
    {
        if (_repository == null)
            return;

        List<StashInfo> stashes = await _repository.GetStashesAsync();
        (StashInfo? stash, string? action) = await Dialogs.ListActionsAsync(
            this, "Stashes", stashes.Count == 0 ? "Nothing is stashed." : "Stashed changes:", stashes,
            "Apply", "Apply and drop", "Show changes", "Drop");
        if (stash == null || action == null)
            return;

        await RunStashActionAsync(stash, action);
    }

    private async Task RunStashActionAsync(StashInfo stash, string action)
    {
        if (_repository == null)
            return;

        switch (action)
        {
            case "Apply":
                await RunGitAsync("Applying " + stash.Reference + "…",
                                  repository => repository.StashApplyAsync(stash.Reference),
                                  "Applied " + stash.Reference + "; it is still stashed.");
                break;
            case "Apply and drop":
                await RunGitAsync("Applying " + stash.Reference + "…",
                                  repository => repository.StashPopAsync(stash.Reference),
                                  "Applied and dropped " + stash.Reference + ".");
                break;
            case "Show changes":
                MainTabs.SelectedIndex = 1;
                ClearCommitFiles();
                ShowCommitDiff(await _repository.GetStashDiffAsync(stash.Reference),
                               stash.Reference + "  " + stash.Message);
                break;
            case "Drop":
                if (await Dialogs.ConfirmAsync(this, "Drop stash",
                                               "Drop " + stash.Reference + "? Its changes are lost.", "Drop"))
                    await RunGitAsync("Dropping " + stash.Reference + "…",
                                      repository => repository.StashDropAsync(stash.Reference),
                                      "Dropped " + stash.Reference + ".");
                break;
            default:
                break;
        }
    }

    // Merge and rebase

    private List<string> OtherBranchNames()
    {
        return _branches.Where(branch => !branch.IsCurrent).Select(branch => branch.Name).ToList();
    }

    private async Task MergeBranchAsync()
    {
        if (_status?.Branch == null)
        {
            SetStatus("Check out a branch first.", true);
            return;
        }

        string? branch = await Dialogs.ChooseAsync(this, "Merge", "Merge into " + _status.Branch + ":",
                                                    OtherBranchNames());
        if (branch == null)
            return;

        await RunGitAsync("Merging " + branch + "…", repository => repository.MergeAsync(branch),
                          "Merged " + branch + " into " + _status.Branch + ".");
    }

    private async Task RebaseBranchAsync()
    {
        if (_status?.Branch == null)
        {
            SetStatus("Check out a branch first.", true);
            return;
        }

        string current = _status.Branch;
        string? branch = await Dialogs.ChooseAsync(this, "Rebase", "Replay the commits of " + current + " on top of:",
                                                    OtherBranchNames());
        if (branch == null)
            return;

        bool confirm = await Dialogs.ConfirmAsync(this, "Rebase",
                                                  "Rebase " + current + " onto " + branch + "? Its commits are " +
                                                      "rewritten, so don't rebase commits others already pulled.",
                                                  "Rebase");
        if (confirm)
            await RunGitAsync("Rebasing " + current + "…", repository => repository.RebaseAsync(branch),
                              "Rebased " + current + " onto " + branch + ".");
    }

    // Operations stopped by conflicts

    private async Task UpdateOperationBannerAsync(RepositoryStatus status)
    {
        _operation = _repository == null ? OperationState.None : await _repository.GetOperationStateAsync();
        OperationBanner.IsVisible = _operation != OperationState.None;
        if (_operation == OperationState.None)
            return;

        int conflicts = status.Unstaged.Count(change => change.Kind == ChangeKind.Conflicted);
        string what = _operation switch
        {
            OperationState.Merge => "A merge",
            OperationState.Rebase => "A rebase",
            OperationState.CherryPick => "A cherry-pick",
            _ => "A revert"
        };

        OperationText.Text = conflicts > 0
            ? $"{what} stopped on {conflicts} conflicted {(conflicts == 1 ? "file" : "files")} (marked !). " +
                  "Right-click a file to take one side, or fix it and choose Mark as resolved."
            : $"{what} is waiting. All conflicts are resolved: Continue to finish, or Abort to undo it.";
        ContinueButton.IsEnabled = conflicts == 0 && !_busy;
    }

    private async Task ContinueOperationAsync()
    {
        OperationState operation = _operation;
        await RunGitAsync("Continuing…", repository => repository.ContinueAsync(operation), "Done.");
    }

    private async Task AbortOperationAsync()
    {
        OperationState operation = _operation;
        bool confirm = await Dialogs.ConfirmAsync(this, "Abort",
                                                  "Abort and go back to how things were before it started? " +
                                                      "Conflict fixes you made are lost.",
                                                  "Abort");
        if (confirm)
            await RunGitAsync("Aborting…", repository => repository.AbortAsync(operation), "Aborted.");
    }

    // "ours" and "theirs" mean different branches in a merge and in a rebase, so the menu names them
    private void AddConflictItems(ContextMenu menu, FileChange change)
    {
        if (change.Kind != ChangeKind.Conflicted)
            return;

        (string ours, string theirs) = _operation == OperationState.Rebase
            ? ("Take the version being rebased onto", "Take your commit's version")
            : ("Take the current branch's version", "Take the incoming version");

        AddMenuItem(menu, "Resolve conflicts…", () => ResolveInConflictViewAsync(change.Path));
        AddMenuItem(menu, ours, () => RunGitAsync("Resolving…",
                                                  repository => repository.ResolveWithSideAsync(
                                                      new[] { change.Path }, true),
                                                  "Resolved " + change.Path + "."));
        AddMenuItem(menu, theirs, () => RunGitAsync("Resolving…",
                                                    repository => repository.ResolveWithSideAsync(
                                                        new[] { change.Path }, false),
                                                    "Resolved " + change.Path + "."));
        AddMenuItem(menu, "Mark as resolved", () => RunGitAsync("Resolving…",
                                                                repository => repository.MarkResolvedAsync(
                                                                    new[] { change.Path }),
                                                                "Marked " + change.Path + " as resolved."));
        menu.Items.Add(new Separator());
    }

    private async Task ResolveInConflictViewAsync(string path)
    {
        if (_repository == null)
            return;

        ConflictFile? file = await _repository.GetConflictAsync(path);
        if (file == null)
        {
            SetStatus(path + " has no conflict markers left; mark it as resolved, or use one side.", false);
            return;
        }

        ConflictWindow window = new ConflictWindow(path, file, _operation == OperationState.Rebase);
        if (await window.ShowDialog<bool>(this))
            await RunGitAsync("Saving " + path + "…", repository => repository.SaveResolvedAsync(path, file),
                              "Resolved " + path + ".");
    }

    // Asks before committing staged whitespace problems, when that check is on; false cancels the commit
    private async Task<bool> ConfirmWhitespaceAsync()
    {
        if (!_settings.CheckWhitespaceBeforeCommit || _repository == null)
            return true;

        List<string> problems = await _repository.CheckStagedWhitespaceAsync();
        if (problems.Count == 0)
            return true;

        string list = string.Join("\n", problems.Take(MAX_WARNINGS_LISTED).Select(problem => "• " + problem));
        if (problems.Count > MAX_WARNINGS_LISTED)
            list += "\n• and " + (problems.Count - MAX_WARNINGS_LISTED) + " more";

        return await Dialogs.ConfirmAsync(this, "Whitespace", "The staged changes have whitespace problems:\n\n" +
                                                                  list + "\n\nCommit anyway?",
                                          "Commit anyway");
    }

    // Repository settings

    private async Task RepositorySettingsAsync(string? path)
    {
        if (path == null || !Directory.Exists(path))
            return;

        GitRepository repository = new GitRepository(path);
        LineEndingPolicy current = await LineEndingRules.ReadAsync(path);
        Dialogs.RepositorySettingsResult? result =
            await Dialogs.RepositorySettingsAsync(this, Path.GetFileName(path), current);
        if (result == null)
            return;

        await LineEndingRules.WriteAsync(path, result.Policy, result.EditorConfig);
        string saved = result.Policy == LineEndingPolicy.Custom ? "Line-ending rules unchanged."
                                                                 : "Line-ending rules saved; commit them to share.";

        if (result.Normalize)
        {
            List<string> files = await repository.PreviewNormalizeAsync();
            if (files.Count == 0)
                saved += " No files need normalizing.";
            else
            {
                string list = string.Join("\n", files.Take(MAX_FILES_LISTED).Select(file => "• " + file));
                if (files.Count > MAX_FILES_LISTED)
                    list += "\n• and " + (files.Count - MAX_FILES_LISTED) + " more";

                bool confirm = await Dialogs.ConfirmAsync(this, "Normalize line endings",
                                                          files.Count + " files will be staged with normalized " +
                                                              "line endings:\n\n" + list,
                                                          "Normalize");
                if (confirm)
                {
                    GitResult normalized = await repository.NormalizeAsync();
                    saved += normalized.Success ? " Normalized files are staged; commit them."
                                                : " Normalizing failed: " + FirstLine(normalized.Message);
                }
            }
        }

        SetStatus(saved, false);
        await RefreshAsync();
    }
}
