namespace Urras.Core.Git;

public sealed partial class GitRepository
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

    // Clones into targetFolder, on branch when given; progress gets Git's progress lines as they come
    public static async Task<GitResult> CloneAsync(string url, string targetFolder, string? branch = null,
                                                   Action<string>? progress = null,
                                                   CancellationToken cancellationToken = default)
    {
        string? parent = System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(targetFolder));
        if (parent == null)
            return new GitResult(-1, string.Empty, "Invalid target folder: " + targetFolder);

        // A URL or branch starting with - would be read as an option (such as --upload-pack, which runs a program)
        if (url.TrimStart().StartsWith('-'))
            return new GitResult(-1, string.Empty, "That doesn't look like a repository address: " + url);
        if (branch?.StartsWith('-') == true)
            return new GitResult(-1, string.Empty, "That doesn't look like a branch name: " + branch);

        Directory.CreateDirectory(parent);
        List<string> arguments = new List<string> { "clone", "--progress" };
        if (!string.IsNullOrEmpty(branch))
            arguments.AddRange(new[] { "--branch", branch });
        arguments.AddRange(new[] { "--", url.Trim(), targetFolder });
        return await GitRunner.RunAsync(parent, arguments, null, cancellationToken, false, null, progress);
    }

    // The first candidate the remote has as a branch or tag; when the remote can't be asked, the shortest
    public static async Task<string?> ChooseBranchAsync(string url, IReadOnlyList<string> candidates,
                                                        CancellationToken cancellationToken = default)
    {
        if (candidates.Count == 0 || url.TrimStart().StartsWith('-'))
            return null;

        GitResult result = await GitRunner.RunAsync(System.IO.Path.GetTempPath(),
                                                    new[] { "ls-remote", "--heads", "--tags", "--", url.Trim() },
                                                    null, cancellationToken, true);
        if (!result.Success)
            return candidates[^1];

        HashSet<string> names = result.Output.Split('\n', StringSplitOptions.RemoveEmptyEntries)
                                      .Select(line => line.Split('\t').Last().Trim())
                                      .Select(name => StripPrefix(StripPrefix(name, HEADS), TAGS))
                                      .ToHashSet(StringComparer.Ordinal);
        return candidates.FirstOrDefault(names.Contains);
    }

    private const string HEADS = "refs/heads/";
    private const string TAGS = "refs/tags/";

    private static string StripPrefix(string name, string prefix)
    {
        return name.StartsWith(prefix, StringComparison.Ordinal) ? name[prefix.Length..] : name;
    }

    public async Task<RepositoryStatus> GetStatusAsync()
    {
        // Without optional locks, status doesn't rewrite the index, which would wake the folder watcher again
        GitResult result = await Run("--no-optional-locks", "status", "--porcelain=v2", "--branch", "-z",
                                     "--untracked-files=all");
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

    // Stages changes to tracked files only, leaving untracked files alone
    public Task<GitResult> StageTrackedAsync()
    {
        return Run("add", "-u");
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

    // Where the last discard's copies went, or null when nothing was copied
    public string? LastDiscardBackup { get; private set; }

    // Throws away unstaged changes, untracked files deleted, after copying the files to DiscardBackup
    public async Task<GitResult> DiscardAsync(IEnumerable<FileChange> changes)
    {
        List<FileChange> all = changes.ToList();
        LastDiscardBackup = Urras.Core.Settings.DiscardBackup.Save(Path, all.Select(change => change.Path));
        List<string> tracked = new List<string>();
        foreach (FileChange change in all)
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

    // The commit's first parent, or the empty tree for the first commit, so every commit can be diffed the same way
    public async Task<string> GetCommitBaseAsync(string hash)
    {
        GitResult parents = await Run("rev-list", "--parents", "-n", "1", hash);
        string[] ids = parents.Output.Split(' ',
                                            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (ids.Length > 1)
            return ids[1];

        GitResult emptyTree = await GitRunner.RunAsync(Path, new[] { "hash-object", "-t", "tree", "--stdin" },
                                                       string.Empty);
        return emptyTree.Output.Trim();
    }

    // The files a commit changed, compared with its first parent
    public async Task<List<FileChange>> GetCommitFilesAsync(string hash)
    {
        string commitBase = await GetCommitBaseAsync(hash);
        GitResult result = await Run("diff", "--name-status", "-M", "-z", commitBase, hash);
        return result.Success ? GitParsers.ParseNameStatus(result.Output) : new List<FileChange>();
    }

    public async Task<string> GetCommitFileDiffAsync(string hash, FileChange change)
    {
        string commitBase = await GetCommitBaseAsync(hash);
        List<string> arguments = Arguments("diff", "--no-color", "-M", commitBase, hash, "--");
        if (change.OriginalPath != null)
            arguments.Add(change.OriginalPath);
        arguments.Add(change.Path);

        GitResult result = await Run(arguments);
        return result.Success ? result.Output : result.Message;
    }

    // Every change in one diff, for the combined view of a change list
    public async Task<string> GetCombinedDiffAsync(IEnumerable<FileChange> changes, bool staged)
    {
        System.Text.StringBuilder diff = new System.Text.StringBuilder();
        foreach (FileChange change in changes)
            diff.Append(await GetDiffAsync(change, staged));
        return diff.ToString();
    }

    // A file's contents at a revision ("HEAD:path", ":path" for the staged copy); null when it doesn't exist there
    public Task<byte[]?> GetBlobAsync(string revision, string path)
    {
        return GitRunner.RunBytesAsync(Path, new[] { "show", revision + ":" + path.Replace('\\', '/') });
    }

    // The working copy of a file; null when it was deleted
    public async Task<byte[]?> GetWorkingFileAsync(string path)
    {
        string fullPath = System.IO.Path.Combine(Path, path);
        return File.Exists(fullPath) ? await File.ReadAllBytesAsync(fullPath) : null;
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

    public async Task<string?> GetCommitMessageAsync(string hash)
    {
        GitResult result = await Run("log", "-1", "--format=%B", hash);
        return result.Success ? result.Output.TrimEnd() : null;
    }

    public const string DEFAULT_TEMPLATE_FILE = ".git_commit_template.txt";
    public const string GIT_MESSAGE_FILE = ".gitmessage";

    // The commit.template setting, or a template file at the top of the repository
    public async Task<string?> GetCommitTemplateAsync()
    {
        List<string> candidates = new List<string>();
        GitResult setting = await Run("config", "--get", "commit.template");
        string configured = setting.Success ? setting.Output.Trim() : string.Empty;
        if (configured.Length > 0)
        {
            if (configured.StartsWith("~/", StringComparison.Ordinal))
                configured = System.IO.Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), configured.Substring(2));
            candidates.Add(System.IO.Path.IsPathRooted(configured) ? configured
                                                                    : System.IO.Path.Combine(Path, configured));
        }

        candidates.Add(System.IO.Path.Combine(Path, DEFAULT_TEMPLATE_FILE));
        candidates.Add(System.IO.Path.Combine(Path, GIT_MESSAGE_FILE));

        foreach (string candidate in candidates)
        {
            if (File.Exists(candidate))
                return await File.ReadAllTextAsync(candidate);
        }

        return null;
    }

    // Commits of the current branch that no remote branch has yet: the ones a push would send
    public async Task<HashSet<string>> GetUnpushedAsync(int limit = DEFAULT_HISTORY_LIMIT)
    {
        if (await IsUnbornAsync())
            return new HashSet<string>();

        GitResult result = await Run("rev-list", "-n", limit.ToString(), "HEAD", "--not", "--remotes");
        return result.Success
            ? result.Output.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                    .ToHashSet(StringComparer.Ordinal)
            : new HashSet<string>();
    }

    // allBranches adds every local and remote branch and tag, as the commit graph shows them
    public async Task<List<CommitInfo>> GetHistoryAsync(int limit = DEFAULT_HISTORY_LIMIT, HistoryFilter? filter = null,
                                                        bool allBranches = false)
    {
        if (await IsUnbornAsync())
            return new List<CommitInfo>();

        // Date order keeps every commit above its parents, which the graph needs
        List<string> arguments = Arguments("log", "-n", limit.ToString(), "--date-order", "--decorate=full",
                                           "--format=" + GitParsers.LOG_FORMAT);
        if (allBranches)
            arguments.AddRange(new[] { "--branches", "--remotes", "--tags", "HEAD" });
        if (filter != null)
            arguments.AddRange(filter.ToArguments(Path));

        GitResult result = await Run(arguments);
        if (!result.Success)
            throw new GitException(result.Message);

        List<CommitInfo> history = GitParsers.ParseLog(result.Output);
        return allBranches && filter == null ? StashInfo.Merge(history, await GetStashesAsync()) : history;
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
        GitResult? refused = RefNames.Refuse(name);
        if (refused != null)
            return Task.FromResult(refused);

        return switchTo ? Run("switch", "-c", name) : Run("branch", name);
    }

    public Task<GitResult> DeleteBranchAsync(string name, bool force)
    {
        return Run("branch", force ? "-D" : "-d", name);
    }

    // When this repository last fetched, from the time Git wrote FETCH_HEAD; null if it never has
    public async Task<DateTimeOffset?> GetLastFetchedAsync()
    {
        GitResult result = await GitRunner.RunAsync(Path, new[] { "rev-parse", "--git-path", "FETCH_HEAD" }, null,
                                                    CancellationToken.None, true);
        if (!result.Success)
            return null;

        string file = result.Output.Trim();
        if (!System.IO.Path.IsPathRooted(file))
            file = System.IO.Path.Combine(Path, file);
        return File.Exists(file) ? new DateTimeOffset(File.GetLastWriteTimeUtc(file), TimeSpan.Zero) : null;
    }

    // A background fetch is not logged when it succeeds and never opens a sign-in window
    public Task<GitResult> FetchAsync(bool background = false, Action<string>? progress = null)
    {
        if (!background)
            return RunWithProgress(progress, "fetch", "--all", "--prune");

        return GitRunner.RunAsync(Path, new[] { "fetch", "--all", "--prune", "--quiet" }, null,
                                  CancellationToken.None, true, BackgroundEnvironment);
    }

    // Git Credential Manager would otherwise show a sign-in window for a fetch nobody asked for
    private static readonly IReadOnlyDictionary<string, string> BackgroundEnvironment =
        new Dictionary<string, string> { ["GCM_INTERACTIVE"] = "Never" };

    public async Task<GitResult> PullAsync(Action<string>? progress = null)
    {
        // Respect a configured pull.rebase; otherwise merge, like GitHub Desktop
        GitResult rebaseSetting = await Run("config", "--get", "pull.rebase");
        if (rebaseSetting.Success && rebaseSetting.Output.Trim().Length > 0)
            return await RunWithProgress(progress, "pull");

        return await RunWithProgress(progress, "pull", "--no-rebase");
    }

    // Git only prints progress to a terminal unless asked, so --progress goes right after the command
    private Task<GitResult> RunWithProgress(Action<string>? progress, string command, params string[] arguments)
    {
        List<string> all = new List<string> { command };
        if (progress != null)
            all.Add("--progress");
        all.AddRange(arguments);
        return GitRunner.RunAsync(Path, all, null, CancellationToken.None, false, null, progress);
    }

    // progress hears Git's progress lines; force replaces the remote branch if it's still as last fetched
    public async Task<GitResult> PushAsync(Action<string>? progress = null, bool force = false)
    {
        RepositoryStatus status = await GetStatusAsync();
        if (status.IsDetached)
            return new GitResult(-1, string.Empty, "Cannot push: no branch is checked out.");
        if (force && status.Upstream == null)
            return new GitResult(-1, string.Empty, "Cannot force push: this branch isn't published yet.");
        if (force)
            return await RunWithProgress(progress, "push", "--force-with-lease");
        if (status.Upstream != null)
            return await RunWithProgress(progress, "push");

        List<string> remotes = await GetRemotesAsync();
        if (remotes.Count == 0)
            return new GitResult(-1, string.Empty,
                                 "Cannot push: this repository has no remote yet. Publish repository puts it online.");

        string remote = remotes.Contains("origin") ? "origin" : remotes[0];
        return await RunWithProgress(progress, "push", "--set-upstream", remote, status.Branch!);
    }

    // Diverged, but every remote-only commit is the same change as one here: rewritten after pushing (authors fixed)
    public async Task<bool> IsRewriteOfUpstreamAsync()
    {
        RepositoryStatus status = await GetStatusAsync();
        if (status.Upstream == null || status.Ahead == 0 || status.Behind == 0)
            return false;

        GitResult result = await Run("rev-list", "--left-right", "--cherry-mark", "@{upstream}...HEAD");
        if (!result.Success)
            return false;

        // '<' marks a commit only the remote has with no copy here: real work that a force push would lose
        string[] lines = result.Output.Split('\n',
                                             StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return lines.Length > 0 && !lines.Any(line => line.StartsWith('<'));
    }

    public async Task<List<string>> GetRemotesAsync()
    {
        GitResult result = await Run("remote");
        if (!result.Success)
            return new List<string>();

        return result.Output.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToList();
    }

    public Task<GitResult> AddRemoteAsync(string name, string url)
    {
        return Run("remote", "add", name, url);
    }

    public Task<GitResult> SetRemoteUrlAsync(string name, string url)
    {
        return Run("remote", "set-url", name, url);
    }

    // The remote whose address Git couldn't find, so only a deleted origin offers publishing again, not a fork's
    public async Task<string?> RemoteNamedInAsync(string message)
    {
        if (MissingRemote.UrlIn(message) is not { } missing)
            return null;

        foreach (string name in await GetRemotesAsync())
        {
            if (await GetRemoteUrlAsync(name) is { } url && MissingRemote.SameAddress(url, missing))
                return name;
        }

        return null;
    }

    // The remote's fetch address, or null when there's no such remote
    public async Task<string?> GetRemoteUrlAsync(string name)
    {
        GitResult result = await Run("remote", "get-url", name);
        return result.Success ? result.Output.Trim() : null;
    }

    // False in a new repository whose first commit hasn't been made, where there's nothing to push yet
    public async Task<bool> HasCommitsAsync()
    {
        return !await IsUnbornAsync();
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
