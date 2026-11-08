namespace Urras.Core.Git;

// A file stored with Git LFS; Downloaded is false while only the small pointer file is checked out
public sealed record LfsFile(string Path, string Oid, bool Downloaded)
{
    public override string ToString()
    {
        return Path + (Downloaded ? string.Empty : "  ·  not downloaded");
    }
}

public sealed record LfsLock(string Id, string Path, string Owner, DateTimeOffset LockedAt)
{
    public override string ToString()
    {
        return Path + "  ·  locked by " + Owner + "  ·  " + LockedAt.LocalDateTime.ToString("yyyy-MM-dd HH:mm");
    }
}

// Git LFS: large files kept outside the repository's history, with optional file locking
public partial class GitRepository
{
    public static async Task<bool> IsLfsInstalledAsync()
    {
        return (await GitRunner.RunAsync(Environment.CurrentDirectory, "lfs", "version")).Success;
    }

    // Installs the LFS hooks for this repository only
    public Task<GitResult> SetUpLfsAsync()
    {
        return Run("lfs", "install", "--local");
    }

    public async Task<List<string>> GetLfsPatternsAsync()
    {
        GitResult result = await Run("lfs", "track");
        return result.Success ? GitParsers.ParseLfsPatterns(result.Output) : new List<string>();
    }

    public Task<GitResult> LfsTrackAsync(string pattern)
    {
        return Run("lfs", "track", "--", pattern);
    }

    public Task<GitResult> LfsUntrackAsync(string pattern)
    {
        return Run("lfs", "untrack", "--", pattern);
    }

    public async Task<List<LfsFile>> GetLfsFilesAsync()
    {
        GitResult result = await Run("lfs", "ls-files", "--long");
        return result.Success ? GitParsers.ParseLfsFiles(result.Output) : new List<LfsFile>();
    }

    // Downloads the LFS files of the current checkout
    public Task<GitResult> LfsPullAsync()
    {
        return Run("lfs", "pull");
    }

    public async Task<(List<LfsLock> Locks, string? Error)> GetLfsLocksAsync()
    {
        GitResult result = await Run("lfs", "locks", "--json");
        return result.Success ? (GitParsers.ParseLfsLocks(result.Output), null)
                              : (new List<LfsLock>(), result.Message);
    }

    public Task<GitResult> LfsLockAsync(string path)
    {
        return Run("lfs", "lock", "--", path);
    }

    // force unlocks someone else's lock, when the server allows it
    public Task<GitResult> LfsUnlockAsync(string path, bool force)
    {
        return force ? Run("lfs", "unlock", "--force", "--", path) : Run("lfs", "unlock", "--", path);
    }
}
