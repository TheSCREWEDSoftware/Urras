using System.Text;
using System.Text.RegularExpressions;

namespace Urras.Core.Git;

// Finds repositories to add in one go: every repository under a folder, or the ones GitHub Desktop knows
public static partial class RepositoryImport
{
    public const int SEARCH_DEPTH = 4;

    // Folders not worth walking into: dependencies and build output hold no repositories of their own
    private static readonly HashSet<string> Skipped = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "node_modules", "bin", "obj", "build", "out", "target", ".vs", ".idea", "packages", "_deps", "vendor"
    };

    [GeneratedRegex(@"[A-Za-z]:\\(?:[^\\/:*?""<>|\x00-\x1f]+\\)*[^\\/:*?""<>|\x00-\x1f]+")]
    private static partial Regex WindowsPathRegex();

    // Repositories at most depth folders below root; a repository's own subfolders are not searched
    public static List<string> FindUnder(string root, int depth = SEARCH_DEPTH)
    {
        List<string> found = new List<string>();
        Walk(root, depth, found);
        found.Sort(StringComparer.OrdinalIgnoreCase);
        return found;
    }

    private static void Walk(string folder, int depth, List<string> found)
    {
        if (IsRepository(folder))
        {
            found.Add(folder);
            return;
        }

        if (depth <= 0)
            return;

        IEnumerable<string> children;
        try
        {
            children = Directory.EnumerateDirectories(folder).ToList();
        }
        catch (Exception exception) when (exception is UnauthorizedAccessException or IOException)
        {
            return;
        }

        foreach (string child in children)
        {
            string name = Path.GetFileName(child);
            if (!name.StartsWith('.') && !Skipped.Contains(name))
                Walk(child, depth - 1, found);
        }
    }

    // An owner or repository name from a platform, safe to use as one folder level
    public static bool IsPlainFolderName(string name)
    {
        return !string.IsNullOrWhiteSpace(name) && name != "." && name != ".." &&
               name.IndexOfAny(Path.GetInvalidFileNameChars()) < 0 && name.IndexOfAny(new[] { '/', '\\', ':' }) < 0;
    }

    // A .git folder, or a .git file (worktrees and submodules)
    public static bool IsRepository(string folder)
    {
        string git = Path.Combine(folder, ".git");
        return Directory.Exists(git) || File.Exists(git);
    }

    // GitHub Desktop keeps its list in a browser database (LevelDB); returns the listed paths that exist here
    public static List<string> FromGitHubDesktop(string? appData = null)
    {
        return GitHubDesktopPaths(appData).Where(path => Directory.Exists(path) && IsRepository(path))
                                          .OrderBy(path => path, StringComparer.OrdinalIgnoreCase).ToList();
    }

    // Every Windows path in GitHub Desktop's database, read through the compression older entries are stored with
    public static HashSet<string> GitHubDesktopPaths(string? appData = null)
    {
        appData ??= Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        string database = Path.Combine(appData, "GitHub Desktop", "IndexedDB");
        HashSet<string> paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (!Directory.Exists(database))
            return paths;

        foreach (string file in Directory.EnumerateFiles(database, "*", SearchOption.AllDirectories))
        {
            foreach (byte[] chunk in LevelDbText.Contents(file))
                AddPathsIn(chunk, paths);
        }

        return paths;
    }

    // Strings are stored as one byte per character, or two when they hold other characters; both are searched
    private static void AddPathsIn(byte[] bytes, HashSet<string> paths)
    {
        foreach (string text in new[] { Encoding.Latin1.GetString(bytes), Encoding.Unicode.GetString(bytes),
                                        Encoding.Unicode.GetString(bytes, 1, Math.Max(0, bytes.Length - 1)) })
        {
            foreach (Match match in WindowsPathRegex().Matches(text))
                paths.Add(match.Value.TrimEnd('\\', ' '));
        }
    }
}
