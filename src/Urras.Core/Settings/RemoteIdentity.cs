namespace Urras.Core.Settings;

// Owner and name of a repository's origin, read from .git/config without starting git
public sealed record RemoteIdentity(string Owner, string Name, string Host)
{
    // Null for repositories without an origin remote
    public static RemoteIdentity? Read(string repository)
    {
        string? config = ConfigFile(repository);
        if (config == null)
            return null;

        string? url = null;
        bool inOrigin = false;
        try
        {
            foreach (string raw in File.ReadLines(config))
            {
                string line = raw.Trim();
                if (line.StartsWith('['))
                    inOrigin = line.Replace(" ", string.Empty).Equals("[remote\"origin\"]", StringComparison.Ordinal);
                else if (inOrigin && line.StartsWith("url", StringComparison.Ordinal) && line.Contains('='))
                {
                    url = line[(line.IndexOf('=') + 1)..].Trim();
                    break;
                }
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return null;
        }

        return url == null ? null : FromUrl(url);
    }

    // HTTPS (host/Owner/Name.git), scp-like SSH (git@host:Owner/Name.git), ssh:// with a port, and local paths
    public static RemoteIdentity? FromUrl(string url)
    {
        string text = url.Trim().TrimEnd('/');
        if (text.EndsWith(".git", StringComparison.OrdinalIgnoreCase))
            text = text[..^4];

        string host = string.Empty;
        string path;
        int scheme = text.IndexOf("://", StringComparison.Ordinal);
        if (scheme >= 0)
        {
            string rest = text[(scheme + 3)..];
            int slash = rest.IndexOf('/');
            if (slash < 0)
                return null;
            host = rest[..slash];
            path = rest[(slash + 1)..];
        }
        else if (text.Contains(':') && !LooksLikeWindowsPath(text))
        {
            // scp-like: git@github.com:Owner/Name
            int colon = text.IndexOf(':');
            host = text[..colon];
            path = text[(colon + 1)..];
        }
        else
            path = text.Replace('\\', '/');

        host = host[(host.LastIndexOf('@') + 1)..];
        int port = host.IndexOf(':');
        if (port >= 0)
            host = host[..port];

        string[] parts = path.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0)
            return null;
        return new RemoteIdentity(parts.Length > 1 ? parts[^2] : host, parts[^1], host);
    }

    private static bool LooksLikeWindowsPath(string text)
    {
        return text.Length > 2 && char.IsLetter(text[0]) && text[1] == ':' && text[2] is '\\' or '/';
    }

    // .git/config, following a .git file (worktrees, submodules) to the real git folder
    private static string? ConfigFile(string repository)
    {
        string git = Path.Combine(repository, ".git");
        if (Directory.Exists(git))
            return File.Exists(Path.Combine(git, "config")) ? Path.Combine(git, "config") : null;
        if (!File.Exists(git))
            return null;

        try
        {
            string line = File.ReadLines(git).FirstOrDefault() ?? string.Empty;
            if (!line.StartsWith("gitdir:", StringComparison.Ordinal))
                return null;

            string gitDir = Path.GetFullPath(Path.Combine(repository, line["gitdir:".Length..].Trim()));
            // A worktree's folder points at .git/worktrees/<name>; the config is two levels up
            string? common = File.Exists(Path.Combine(gitDir, "commondir"))
                ? Path.GetFullPath(Path.Combine(gitDir, File.ReadAllText(Path.Combine(gitDir, "commondir")).Trim()))
                : gitDir;
            string config = Path.Combine(common, "config");
            return File.Exists(config) ? config : null;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }
}
