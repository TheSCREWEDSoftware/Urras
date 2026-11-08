namespace Urras.Core.Git;

public enum RepositoryState
{
    // The folder is gone or is no longer a Git repository
    Missing,

    // No remote at all
    LocalOnly,

    // Has a remote, but the current branch has no upstream yet
    NotPublished,

    // The current branch tracks a remote branch
    Published
}

// What the repository list shows next to each repository
public sealed class RepositorySummary
{
    public RepositoryState State { get; init; }
    public string? Branch { get; init; }
    public bool IsDetached { get; init; }
    public string? Upstream { get; init; }
    public int Ahead { get; init; }
    public int Behind { get; init; }
    public int Changes { get; init; }
    public IReadOnlyList<string> Remotes { get; init; } = Array.Empty<string>();

    // "origin" for an upstream of "origin/main"
    public string? UpstreamRemote
    {
        get
        {
            if (Upstream == null)
                return null;

            int slash = Upstream.IndexOf('/');
            return slash > 0 ? Upstream.Substring(0, slash) : Upstream;
        }
    }

    public static async Task<RepositorySummary> LoadAsync(string path)
    {
        // A folder without its own .git would otherwise report the status of a parent repository
        string gitEntry = Path.Combine(path, ".git");
        if (!Directory.Exists(path) || (!Directory.Exists(gitEntry) && !File.Exists(gitEntry)))
            return new RepositorySummary { State = RepositoryState.Missing };

        GitResult statusResult = await GitRunner.RunAsync(path, "status", "--porcelain=v2", "--branch", "-z",
                                                          "--untracked-files=all");
        if (!statusResult.Success)
            return new RepositorySummary { State = RepositoryState.Missing };

        RepositoryStatus status = GitParsers.ParseStatus(statusResult.Output);
        List<string> remotes = await new GitRepository(path).GetRemotesAsync();

        RepositoryState state = remotes.Count == 0 ? RepositoryState.LocalOnly :
                                status.Upstream == null ? RepositoryState.NotPublished :
                                                          RepositoryState.Published;

        int changes = status.Staged.Select(change => change.Path)
            .Concat(status.Unstaged.Select(change => change.Path))
            .Distinct(StringComparer.Ordinal)
            .Count();

        return new RepositorySummary
        {
            State = state,
            Branch = status.Branch,
            IsDetached = status.IsDetached,
            Upstream = status.Upstream,
            Ahead = status.Ahead,
            Behind = status.Behind,
            Changes = changes,
            Remotes = remotes
        };
    }
}
