using System.Text.RegularExpressions;

namespace Urras.Core.Git;

// Windows treats "Fix" and "fix" as one file, so pruning branches differing only in capitals hits one lock twice
public static partial class RefNameClash
{
    // Only copies of remote branches are removed; the next fetch brings back any that still exist
    public const string REMOTE_REFS = "refs/remotes/";

    [GeneratedRegex(@"cannot lock ref '(?<ref>[^']+)'")]
    private static partial Regex RefPattern();

    // The remote branch Git couldn't lock, or null when the failure was about something else
    public static string? RefIn(string message)
    {
        Match match = RefPattern().Match(message);
        if (!match.Success || !match.Groups["ref"].Value.StartsWith(REMOTE_REFS, StringComparison.Ordinal))
            return null;
        return match.Groups["ref"].Value;
    }

    // Every ref named like the one that failed, ignoring capitals
    public static List<string> Matching(IEnumerable<string> refs, string name)
    {
        return refs.Where(other => string.Equals(other, name, StringComparison.OrdinalIgnoreCase)).ToList();
    }
}
