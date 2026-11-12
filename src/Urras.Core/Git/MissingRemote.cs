using System.Text.RegularExpressions;

namespace Urras.Core.Git;

// "remote: Repository not found": the remote's repository was deleted (or renamed, or made private to others)
public static partial class MissingRemote
{
    [GeneratedRegex(@"repository '(?<url>[^']+)' not found", RegexOptions.IgnoreCase)]
    private static partial Regex UrlPattern();

    public static bool IsMissing(string message)
    {
        return message.Contains("Repository not found", StringComparison.OrdinalIgnoreCase) ||
               UrlPattern().IsMatch(message);
    }

    // The address Git couldn't find, without the trailing slash Git adds
    public static string? UrlIn(string message)
    {
        Match match = UrlPattern().Match(message);
        return match.Success ? match.Groups["url"].Value.TrimEnd('/') : null;
    }

    // The same repository whatever the trailing slash, ".git" or letter case
    public static bool SameAddress(string first, string second)
    {
        return string.Equals(Normalised(first), Normalised(second), StringComparison.OrdinalIgnoreCase);
    }

    private static string Normalised(string url)
    {
        string trimmed = url.Trim().TrimEnd('/');
        return trimmed.EndsWith(".git", StringComparison.OrdinalIgnoreCase) ? trimmed[..^".git".Length] : trimmed;
    }

    // The owner and name in an HTTPS or SSH address, so publishing again can offer them; null if it has none
    public static (string Owner, string Name)? OwnerAndName(string url)
    {
        string path = url.Trim().TrimEnd('/');
        int scheme = path.IndexOf("://", StringComparison.Ordinal);
        if (scheme >= 0)
        {
            int slash = path.IndexOf('/', scheme + "://".Length);
            path = slash < 0 ? string.Empty : path[(slash + 1)..];
        }
        else if (path.IndexOf(':') is var colon and > 0)
        {
            path = path[(colon + 1)..];
        }

        if (path.EndsWith(".git", StringComparison.OrdinalIgnoreCase))
            path = path[..^".git".Length];
        string[] parts = path.Split('/', StringSplitOptions.RemoveEmptyEntries);
        return parts.Length >= 2 ? (string.Join('/', parts[..^1]), parts[^1]) : null;
    }
}
