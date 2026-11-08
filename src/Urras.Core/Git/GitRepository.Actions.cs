namespace Urras.Core.Git;

// Tags, stashes, revert, cherry-pick, merge, rebase and conflict handling
public sealed partial class GitRepository
{
    // Continuing an operation must never open a text editor, which Urras could not show
    private static readonly IReadOnlyDictionary<string, string> NoEditorEnvironment =
        new Dictionary<string, string> { ["GIT_EDITOR"] = "true", ["GIT_SEQUENCE_EDITOR"] = "true" };

    // Tags

    public async Task<List<TagInfo>> GetTagsAsync()
    {
        GitResult result = await Run("for-each-ref", "--sort=-creatordate",
                                     "--format=%(refname:short)%1f%(*objectname)%(objectname)%1f%(subject)",
                                     "refs/tags");
        List<TagInfo> tags = new List<TagInfo>();
        if (!result.Success)
            return tags;

        foreach (string line in result.Output.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            string[] fields = line.TrimEnd('\r').Split(GitParsers.FIELD_SEPARATOR);
            if (fields.Length < 3)
                continue;

            // Annotated tags print the tagged commit first, then the tag object; the commit is the first 40 or 64
            string objects = fields[1];
            string commit = objects.Length >= 80 ? objects.Substring(0, objects.Length / 2) : objects;
            tags.Add(new TagInfo(fields[0], commit, fields[2]));
        }

        return tags;
    }

    // An annotated tag when a message is given, otherwise a lightweight one
    public Task<GitResult> CreateTagAsync(string name, string target, string? message)
    {
        return string.IsNullOrWhiteSpace(message) ? Run("tag", name, target)
                                                  : Run("tag", "-a", name, target, "-m", message);
    }

    public Task<GitResult> DeleteTagAsync(string name)
    {
        return Run("tag", "-d", name);
    }

    public async Task<GitResult> PushTagAsync(string name)
    {
        string? remote = await DefaultRemoteAsync();
        return remote == null ? NoRemote() : await Run("push", remote, "refs/tags/" + name);
    }

    public async Task<GitResult> PushAllTagsAsync()
    {
        string? remote = await DefaultRemoteAsync();
        return remote == null ? NoRemote() : await Run("push", remote, "--tags");
    }

    public async Task<GitResult> DeleteRemoteTagAsync(string name)
    {
        string? remote = await DefaultRemoteAsync();
        return remote == null ? NoRemote() : await Run("push", remote, "--delete", "refs/tags/" + name);
    }

    // Commits

    public Task<GitResult> CheckoutCommitAsync(string hash)
    {
        return Run("switch", "--detach", hash);
    }

    public Task<GitResult> RevertAsync(string hash)
    {
        return RunWithoutEditor("revert", "--no-edit", hash);
    }

    public Task<GitResult> CherryPickAsync(string hash)
    {
        return RunWithoutEditor("cherry-pick", hash);
    }

    // Stashes

    public async Task<List<StashInfo>> GetStashesAsync()
    {
        GitResult result = await Run("stash", "list", "--format=%gd%x1f%H%x1f%gs%x1f%aI%x1f%P%x1f%an");
        List<StashInfo> stashes = new List<StashInfo>();
        if (!result.Success)
            return stashes;

        foreach (string line in result.Output.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            string[] fields = line.TrimEnd('\r').Split(GitParsers.FIELD_SEPARATOR);
            if (fields.Length < 4)
                continue;

            DateTimeOffset.TryParse(fields[3], System.Globalization.CultureInfo.InvariantCulture,
                                    System.Globalization.DateTimeStyles.None, out DateTimeOffset date);
            stashes.Add(new StashInfo(fields[0], fields[1], fields[2], date)
            {
                // The first parent is the commit the stash was made on; the others hold the index and untracked files
                Parent = fields.Length > 4 ? fields[4].Split(' ')[0] : null,
                Author = fields.Length > 5 ? fields[5] : string.Empty
            });
        }

        return stashes;
    }

    public Task<GitResult> StashAsync(string? message, bool includeUntracked)
    {
        List<string> arguments = Arguments("stash", "push");
        if (includeUntracked)
            arguments.Add("--include-untracked");
        if (!string.IsNullOrWhiteSpace(message))
        {
            arguments.Add("-m");
            arguments.Add(message);
        }

        return Run(arguments);
    }

    public Task<GitResult> StashApplyAsync(string reference)
    {
        return Run("stash", "apply", "--index", reference);
    }

    public Task<GitResult> StashPopAsync(string reference)
    {
        return Run("stash", "pop", "--index", reference);
    }

    public Task<GitResult> StashDropAsync(string reference)
    {
        return Run("stash", "drop", reference);
    }

    public async Task<string> GetStashDiffAsync(string reference)
    {
        GitResult result = await Run("stash", "show", "--patch", "--include-untracked", "--no-color", reference);
        if (!result.Success)
            result = await Run("stash", "show", "--patch", "--no-color", reference);
        return result.Success ? result.Output : result.Message;
    }

    // Merge and rebase

    public Task<GitResult> MergeAsync(string branch)
    {
        return RunWithoutEditor("merge", "--no-edit", branch);
    }

    public Task<GitResult> RebaseAsync(string branch)
    {
        return RunWithoutEditor("rebase", branch);
    }

    public async Task<OperationState> GetOperationStateAsync()
    {
        if (await GitPathExistsAsync("rebase-merge") || await GitPathExistsAsync("rebase-apply"))
            return OperationState.Rebase;
        if (await GitPathExistsAsync("MERGE_HEAD"))
            return OperationState.Merge;
        if (await GitPathExistsAsync("CHERRY_PICK_HEAD"))
            return OperationState.CherryPick;
        if (await GitPathExistsAsync("REVERT_HEAD"))
            return OperationState.Revert;
        return OperationState.None;
    }

    public Task<GitResult> AbortAsync(OperationState state)
    {
        return state switch
        {
            OperationState.Merge => Run("merge", "--abort"),
            OperationState.Rebase => Run("rebase", "--abort"),
            OperationState.CherryPick => Run("cherry-pick", "--abort"),
            OperationState.Revert => Run("revert", "--abort"),
            _ => Task.FromResult(new GitResult(0, string.Empty, string.Empty))
        };
    }

    public Task<GitResult> ContinueAsync(OperationState state)
    {
        return state switch
        {
            OperationState.Merge => RunWithoutEditor("commit", "--no-edit"),
            OperationState.Rebase => RunWithoutEditor("rebase", "--continue"),
            OperationState.CherryPick => RunWithoutEditor("cherry-pick", "--continue"),
            OperationState.Revert => RunWithoutEditor("revert", "--continue"),
            _ => Task.FromResult(new GitResult(0, string.Empty, string.Empty))
        };
    }

    // "ours" is the current branch in a merge but the base in a rebase, so the UI names the sides itself
    public async Task<GitResult> ResolveWithSideAsync(IEnumerable<string> paths, bool ours)
    {
        List<string> list = paths.ToList();
        GitResult checkout = await Run(Arguments("checkout", ours ? "--ours" : "--theirs", "--").Concat(list));
        if (!checkout.Success)
            return checkout;

        return await Run(Arguments("add", "--").Concat(list));
    }

    public Task<GitResult> MarkResolvedAsync(IEnumerable<string> paths)
    {
        return Run(Arguments("add", "--").Concat(paths));
    }

    // Whitespace problems in the staged changes, such as trailing spaces, one per line; empty when there are none
    public async Task<List<string>> CheckStagedWhitespaceAsync()
    {
        GitResult result = await Run("diff", "--cached", "--check", "--no-color");
        return result.Output.Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(line => line.TrimEnd('\r'))
            .Where(line => !line.StartsWith('+'))
            .ToList();
    }

    // Line-ending normalization

    // Files whose staged copy would change if their line endings were normalized by .gitattributes
    public async Task<List<string>> PreviewNormalizeAsync()
    {
        GitResult result = await Run("add", "--renormalize", "--dry-run", ".");
        return result.Output.Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(line => line.TrimEnd('\r'))
            .Where(line => line.StartsWith("add '", StringComparison.Ordinal))
            .Select(line => line.Substring("add '".Length).TrimEnd('\''))
            .ToList();
    }

    public Task<GitResult> NormalizeAsync()
    {
        return Run("add", "--renormalize", ".");
    }

    private async Task<bool> GitPathExistsAsync(string name)
    {
        GitResult result = await Run("rev-parse", "--git-path", name);
        if (!result.Success)
            return false;

        string path = result.Output.Trim();
        string full = System.IO.Path.IsPathRooted(path) ? path : System.IO.Path.Combine(Path, path);
        return File.Exists(full) || Directory.Exists(full);
    }

    private async Task<string?> DefaultRemoteAsync()
    {
        List<string> remotes = await GetRemotesAsync();
        return remotes.Contains("origin") ? "origin" : remotes.FirstOrDefault();
    }

    private static GitResult NoRemote()
    {
        return new GitResult(-1, string.Empty, "This repository has no remote.");
    }

    private Task<GitResult> RunWithoutEditor(params string[] arguments)
    {
        return GitRunner.RunAsync(Path, arguments, null, CancellationToken.None, false, NoEditorEnvironment);
    }
}
