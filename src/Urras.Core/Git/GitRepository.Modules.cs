namespace Urras.Core.Git;

public enum SubmoduleState
{
    UpToDate,
    NotInitialized,
    DifferentCommit,
    Conflicted
}

public sealed record SubmoduleInfo(string Path, string Commit, SubmoduleState State, string Description)
{
    public string StateText => State switch
    {
        SubmoduleState.NotInitialized => "not checked out",
        SubmoduleState.DifferentCommit => "at a different commit than recorded",
        SubmoduleState.Conflicted => "conflicted",
        _ => "up to date"
    };

    public override string ToString()
    {
        return Path + "  ·  " + StateText + (Description.Length > 0 ? "  ·  " + Description : string.Empty);
    }
}

public sealed record WorktreeInfo(string Path, string? Head, string? Branch, bool IsMain, bool IsLocked,
                                  bool IsPrunable)
{
    public override string ToString()
    {
        string where = Branch ?? (Head == null ? "bare" : "detached at " + Hashes.Short(Head));
        return Path + "  ·  " + where + (IsMain ? "  ·  main" : string.Empty)
               + (IsLocked ? "  ·  locked" : string.Empty) + (IsPrunable ? "  ·  missing" : string.Empty);
    }
}

// Submodules (repositories inside this one) and worktrees (more checkouts of this repository)
public partial class GitRepository
{
    // Other worktrees exist: listed in .git/worktrees, or this folder is one (its .git file points there)
    public static bool HasLinkedWorktrees(string folder)
    {
        string git = System.IO.Path.Combine(folder, ".git");
        try
        {
            if (Directory.Exists(git))
            {
                string worktrees = System.IO.Path.Combine(git, "worktrees");
                return Directory.Exists(worktrees) && Directory.EnumerateDirectories(worktrees).Any();
            }

            string line = File.Exists(git) ? File.ReadLines(git).FirstOrDefault() ?? string.Empty : string.Empty;
            return line.Replace('\\', '/').Contains("/worktrees/", StringComparison.Ordinal);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    public async Task<List<SubmoduleInfo>> GetSubmodulesAsync()
    {
        GitResult result = await Run("submodule", "status", "--recursive");
        return result.Success ? GitParsers.ParseSubmodules(result.Output) : new List<SubmoduleInfo>();
    }

    // Checks out every submodule at its recorded commit, cloning those not checked out yet
    public Task<GitResult> UpdateSubmodulesAsync()
    {
        return Run("submodule", "update", "--init", "--recursive");
    }

    public Task<GitResult> AddSubmoduleAsync(string url, string path)
    {
        return Run("submodule", "add", "--", url, path);
    }

    public async Task<List<WorktreeInfo>> GetWorktreesAsync()
    {
        GitResult result = await Run("worktree", "list", "--porcelain");
        return result.Success ? GitParsers.ParseWorktrees(result.Output) : new List<WorktreeInfo>();
    }

    // Checks out branch in a new folder; newBranch creates it from the current commit first
    public Task<GitResult> AddWorktreeAsync(string path, string branch, bool newBranch)
    {
        return newBranch ? Run("worktree", "add", "-b", branch, "--", path)
                         : Run("worktree", "add", "--", path, branch);
    }

    public Task<GitResult> RemoveWorktreeAsync(string path, bool force)
    {
        return force ? Run("worktree", "remove", "--force", "--", path) : Run("worktree", "remove", "--", path);
    }

    // Forgets worktrees whose folders were deleted by hand
    public Task<GitResult> PruneWorktreesAsync()
    {
        return Run("worktree", "prune");
    }
}
