namespace Urras.Core.Git;

public sealed class GitRepository
{
    public const int DEFAULT_HISTORY_LIMIT = 300;

    public GitRepository(string path)
    {
        Path = path;
    }

    public string Path { get; }
    public string Name => System.IO.Path.GetFileName(Path.TrimEnd('/', '\\'));

    // Returns the repository root for a folder inside a work tree, or null if it isn't one
    public static async Task<string?> FindRootAsync(string folder)
    {
        if (!Directory.Exists(folder))
            return null;

        GitResult result = await GitRunner.RunAsync(folder, "rev-parse", "--show-toplevel");
        if (!result.Success)
            return null;

        string root = result.Output.Trim();
        return root.Length == 0 ? null : System.IO.Path.GetFullPath(root);
    }

    public static async Task<GitResult> InitAsync(string folder)
    {
        Directory.CreateDirectory(folder);
        return await GitRunner.RunAsync(folder, "init");
    }

    public static async Task<GitResult> CloneAsync(string url, string targetFolder)
    {
        string? parent = System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(targetFolder));
        if (parent == null)
            return new GitResult(-1, string.Empty, "Invalid target folder: " + targetFolder);

        Directory.CreateDirectory(parent);
        return await GitRunner.RunAsync(parent, "clone", "--progress", url, targetFolder);
    }

    public async Task<RepositoryStatus> GetStatusAsync()
    {
        GitResult result = await Run("status", "--porcelain=v2", "--branch", "-z", "--untracked-files=all");
        if (!result.Success)
            throw new GitException(result.Message);

        return GitParsers.ParseStatus(result.Output);
    }

    public Task<GitResult> StageAsync(IEnumerable<string> paths)
    {
        return Run(Arguments("add", "-A", "--").Concat(paths));
    }

    public Task<GitResult> StageAllAsync()
    {
        return Run("add", "-A");
    }

    public async Task<GitResult> UnstageAsync(IEnumerable<string> paths)
    {
        // Before the first commit there is no HEAD to reset to
        if (await IsUnbornAsync())
            return await Run(Arguments("rm", "--cached", "-r", "-q", "--").Concat(paths));

        return await Run(Arguments("reset", "-q", "--").Concat(paths));
    }

    public async Task<GitResult> UnstageAllAsync()
    {
        if (await IsUnbornAsync())
            return await Run("rm", "--cached", "-r", "-q", ".");

        return await Run("reset", "-q");
    }

    // Throws away unstaged changes; untracked files are deleted
    public async Task<GitResult> DiscardAsync(IEnumerable<FileChange> changes)
    {
        List<string> tracked = new List<string>();
        foreach (FileChange change in changes)
        {
            if (change.Kind == ChangeKind.Untracked)
            {
                string fullPath = System.IO.Path.Combine(Path, change.Path);
                if (File.Exists(fullPath))
                    File.Delete(fullPath);
            }
            else
                tracked.Add(change.Path);
        }

        if (tracked.Count == 0)
            return new GitResult(0, string.Empty, string.Empty);

        return await Run(Arguments("checkout", "--").Concat(tracked));
    }

    public async Task<string> GetDiffAsync(FileChange change, bool staged)
    {
        GitResult result;
        if (change.Kind == ChangeKind.Untracked)
        {
            // --no-index exits with 1 when the files differ, which is the normal case here
            result = await Run("diff", "--no-color", "--no-index", "--", "/dev/null", change.Path);
            return result.ExitCode <= 1 ? result.Output : result.Message;
        }

        List<string> arguments = Arguments("diff", "--no-color", "--find-renames");
        if (staged)
            arguments.Add("--cached");
        arguments.Add("--");
        if (change.OriginalPath != null)
            arguments.Add(change.OriginalPath);
        arguments.Add(change.Path);

        result = await Run(arguments);
        return result.Success ? result.Output : result.Message;
    }

    public async Task<string> GetCommitDiffAsync(string hash)
    {
        GitResult result = await Run("show", "--no-color", "--find-renames", "--stat", "--patch", "--format=fuller",
                                     hash);
        return result.Success ? result.Output : result.Message;
    }

    public Task<GitResult> CommitAsync(string message, bool amend)
    {
        List<string> arguments = Arguments("commit", "--file=-", "--cleanup=strip");
        if (amend)
            arguments.Add("--amend");

        return GitRunner.RunAsync(Path, arguments, message);
    }

    public async Task<string?> GetLastCommitMessageAsync()
    {
        GitResult result = await Run("log", "-1", "--format=%B");
        return result.Success ? result.Output.TrimEnd() : null;
    }

    public async Task<List<CommitInfo>> GetHistoryAsync(int limit = DEFAULT_HISTORY_LIMIT)
    {
        if (await IsUnbornAsync())
            return new List<CommitInfo>();

        GitResult result = await Run("log", "-n", limit.ToString(), "--format=" + GitParsers.LOG_FORMAT);
        if (!result.Success)
            throw new GitException(result.Message);

        return GitParsers.ParseLog(result.Output);
    }

    public async Task<List<BranchInfo>> GetBranchesAsync()
    {
        GitResult result = await Run("for-each-ref", "--format=" + GitParsers.BRANCH_FORMAT, "refs/heads",
                                     "refs/remotes");
        if (!result.Success)
            throw new GitException(result.Message);

        return GitParsers.ParseBranches(result.Output);
    }

    public Task<GitResult> SwitchAsync(string branch)
    {
        return Run("switch", branch);
    }

    // Creates a local branch that tracks a remote one, such as origin/feature
    public Task<GitResult> SwitchToRemoteAsync(string remoteBranch)
    {
        int slash = remoteBranch.IndexOf('/');
        string localName = slash >= 0 ? remoteBranch.Substring(slash + 1) : remoteBranch;
        return Run("switch", "-c", localName, "--track", remoteBranch);
    }

    public Task<GitResult> CreateBranchAsync(string name, bool switchTo)
    {
        return switchTo ? Run("switch", "-c", name) : Run("branch", name);
    }

    public Task<GitResult> DeleteBranchAsync(string name, bool force)
    {
        return Run("branch", force ? "-D" : "-d", name);
    }

    public Task<GitResult> FetchAsync()
    {
        return Run("fetch", "--all", "--prune");
    }

    public async Task<GitResult> PullAsync()
    {
        // Respect a configured pull.rebase; otherwise merge, like GitHub Desktop
        GitResult rebaseSetting = await Run("config", "--get", "pull.rebase");
        if (rebaseSetting.Success && rebaseSetting.Output.Trim().Length > 0)
            return await Run("pull");

        return await Run("pull", "--no-rebase");
    }

    public async Task<GitResult> PushAsync()
    {
        RepositoryStatus status = await GetStatusAsync();
        if (status.IsDetached)
            return new GitResult(-1, string.Empty, "Cannot push: no branch is checked out.");
        if (status.Upstream != null)
            return await Run("push");

        List<string> remotes = await GetRemotesAsync();
        if (remotes.Count == 0)
            return new GitResult(-1, string.Empty, "Cannot push: this repository has no remote.");

        string remote = remotes.Contains("origin") ? "origin" : remotes[0];
        return await Run("push", "--set-upstream", remote, status.Branch!);
    }

    public async Task<List<string>> GetRemotesAsync()
    {
        GitResult result = await Run("remote");
        if (!result.Success)
            return new List<string>();

        return result.Output.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToList();
    }

    private async Task<bool> IsUnbornAsync()
    {
        GitResult result = await Run("rev-parse", "--verify", "--quiet", "HEAD");
        return !result.Success;
    }

    private Task<GitResult> Run(params string[] arguments)
    {
        return GitRunner.RunAsync(Path, arguments);
    }

    private Task<GitResult> Run(IEnumerable<string> arguments)
    {
        return GitRunner.RunAsync(Path, arguments);
    }

    private static List<string> Arguments(params string[] arguments)
    {
        return new List<string>(arguments);
    }
}

public sealed class GitException : Exception
{
    public GitException(string message) : base(message)
    {
    }
}
