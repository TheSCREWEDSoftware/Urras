using System.Text.RegularExpressions;

namespace Urras.Core.Git;

// GitHub's "Permission to owner/repo.git denied to login." after a push, read so Urras can explain it
public sealed partial record PushRefusal(string Repository, string User)
{
    [GeneratedRegex(@"Permission to (?<repository>\S+?)(?:\.git)? denied to (?<user>[^\s.]+)")]
    private static partial Regex RefusalPattern();

    public static PushRefusal? Parse(string error)
    {
        Match match = RefusalPattern().Match(error);
        return match.Success ? new PushRefusal(match.Groups["repository"].Value, match.Groups["user"].Value) : null;
    }

    // The organisation or user the repository belongs to
    public string Owner => Repository.Split('/')[0];
}
