namespace Urras.Core.Git;

// A repository to clone, and the branch a web page pointed at as candidates longest first ("a/b/c", "a/b", "a")
public sealed record CloneSource(string Url, IReadOnlyList<string> BranchCandidates);

// What the clone box accepts: a full address, a page of a repository, or owner/name for GitHub
public static class CloneAddress
{
    public const string GITHUB_WEB = "https://github.com/";
    private const string GIT_SUFFIX = ".git";
    // GitLab puts "-" between a project and its pages, since projects can sit in nested groups
    private const string GITLAB_SEPARATOR = "-";
    // Hosts whose repositories are always owner/name, so anything after those two parts is a page
    private static readonly string[] OwnerNameHosts = { "github.com", "bitbucket.org", "codeberg.org" };
    // Page names that come before a branch: GitHub and GitLab tree/blob, Bitbucket and Gitea src
    private static readonly string[] BranchPages = { "tree", "blob", "src", "raw" };
    // Gitea's src/branch/name and src/tag/name
    private static readonly string[] GiteaKinds = { "branch", "tag" };

    // "azerothcore/wiki" and "github.com/azerothcore/wiki" become GitHub addresses; anything else is kept
    public static string Resolve(string input)
    {
        return Parse(input).Url;
    }

    // A web page such as github.com/owner/name/tree/master becomes the repository and the branch master
    public static CloneSource Parse(string input)
    {
        string text = input.Trim();
        if (text.StartsWith("github.com/", StringComparison.OrdinalIgnoreCase))
            text = "https://" + text;

        string[] parts = text.Split('/');
        bool ownerAndName = parts.Length == 2 && parts.All(part => part.Length > 0 && part.All(IsNameCharacter));
        if (ownerAndName && !Directory.Exists(text))
            return new CloneSource(GITHUB_WEB + text, Array.Empty<string>());

        if (!Uri.TryCreate(text, UriKind.Absolute, out Uri? uri) || uri.Scheme is not ("http" or "https"))
            return new CloneSource(text, Array.Empty<string>());

        List<string> path = uri.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries)
                               .Select(Uri.UnescapeDataString).ToList();
        int pageStart = PageStart(uri.Host, path);
        if (pageStart >= path.Count && uri.Query.Length == 0 && uri.Fragment.Length == 0)
            return new CloneSource(text.TrimEnd('/'), Array.Empty<string>());

        string repository = uri.GetLeftPart(UriPartial.Authority) + "/" + string.Join("/", path.Take(pageStart));
        return new CloneSource(repository, Candidates(path.Skip(pageStart).ToList()));
    }

    // The folder a clone goes into: the last part of the address, without .git
    public static string FolderName(string address)
    {
        string name = address.Trim().TrimEnd('/', '\\');
        name = name[(name.LastIndexOfAny(new[] { '/', '\\', ':' }) + 1)..];
        return name.EndsWith(GIT_SUFFIX, StringComparison.OrdinalIgnoreCase) ? name[..^GIT_SUFFIX.Length] : name;
    }

    // Where the repository's own path ends and a page within it begins
    private static int PageStart(string host, List<string> path)
    {
        int separator = path.IndexOf(GITLAB_SEPARATOR);
        if (separator > 0)
            return separator;
        if (OwnerNameHosts.Any(known => host.Equals(known, StringComparison.OrdinalIgnoreCase) ||
                                        host.EndsWith("." + known, StringComparison.OrdinalIgnoreCase)))
            return Math.Min(2, path.Count);

        int page = path.FindIndex(part => BranchPages.Contains(part, StringComparer.OrdinalIgnoreCase));
        return page > 0 ? page : path.Count;
    }

    // After tree/blob/src comes the branch and maybe a file; a branch may hold "/", so every length is a guess
    private static IReadOnlyList<string> Candidates(List<string> page)
    {
        if (page.Count > 0 && page[0] == GITLAB_SEPARATOR)
            page.RemoveAt(0);
        if (page.Count < 2 || !BranchPages.Contains(page[0], StringComparer.OrdinalIgnoreCase))
            return Array.Empty<string>();

        int start = page.Count > 2 && GiteaKinds.Contains(page[1], StringComparer.OrdinalIgnoreCase) ? 2 : 1;
        List<string> branch = page.Skip(start).ToList();
        return Enumerable.Range(1, branch.Count).Reverse()
                         .Select(length => string.Join("/", branch.Take(length))).ToList();
    }

    // GitHub owners and repositories use letters, digits, - _ and .
    private static bool IsNameCharacter(char character)
    {
        return char.IsAsciiLetterOrDigit(character) || character is '-' or '_' or '.';
    }
}
