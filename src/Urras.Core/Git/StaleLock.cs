using System.Text.RegularExpressions;

namespace Urras.Core.Git;

// A crashed Git leaves a .lock that fails every later command with "File exists"; this removes it when stale
public static partial class StaleLock
{
    // Ref and config locks last a moment; one this old belongs to no running Git
    public static readonly TimeSpan STALE_AGE = TimeSpan.FromMinutes(2);
    // index.lock stays while a "git commit" editor is open, so it's never removed automatically
    private const string INDEX_LOCK = "index.lock";

    [GeneratedRegex(@"Unable to create '(?<path>[^']+\.lock)': File exists")]
    private static partial Regex LockPattern();

    // The lock file Git couldn't create, or null when the failure was something else
    public static string? PathIn(string message, string workingDirectory)
    {
        Match match = LockPattern().Match(message);
        if (!match.Success)
            return null;

        string path = match.Groups["path"].Value.Replace('/', Path.DirectorySeparatorChar);
        return Path.IsPathRooted(path) ? path : Path.GetFullPath(Path.Combine(workingDirectory, path));
    }

    // Removes the lock named in the message if it's old enough and not index.lock; true when it was removed
    public static bool TryClear(string message, string workingDirectory, DateTime? now = null)
    {
        if (PathIn(message, workingDirectory) is not { } path ||
            string.Equals(Path.GetFileName(path), INDEX_LOCK, StringComparison.OrdinalIgnoreCase))
            return false;

        try
        {
            FileInfo file = new FileInfo(path);
            if (!file.Exists || (now ?? DateTime.UtcNow) - file.LastWriteTimeUtc < STALE_AGE)
                return false;

            file.Delete();
            return true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }
}
