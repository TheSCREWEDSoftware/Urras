namespace Urras.Core.Git;

// Tells commands that only read the repository from ones that change it, so the Git log shows what matters
public static class GitCommandKind
{
    // Options before the command that take a value as the next word
    private static readonly HashSet<string> OptionsWithValue = new HashSet<string>(StringComparer.Ordinal)
    {
        "-c", "-C", "--git-dir", "--work-tree", "--namespace"
    };

    private static readonly HashSet<string> ReadingCommands = new HashSet<string>(StringComparer.Ordinal)
    {
        "status", "log", "diff", "show", "rev-parse", "rev-list", "for-each-ref", "ls-files", "ls-tree", "ls-remote",
        "cat-file", "merge-base", "symbolic-ref", "check-attr", "check-ignore", "hash-object", "var", "version",
        "blame", "shortlog", "describe", "name-rev", "grep", "count-objects", "config", "remote", "reflog"
    };

    // Commands that read or change depending on the word after them: these are the reading ones
    private static readonly Dictionary<string, HashSet<string>> ReadingSubcommands =
        new Dictionary<string, HashSet<string>>(StringComparer.Ordinal)
        {
            ["lfs"] = new HashSet<string>(StringComparer.Ordinal) { "ls-files", "locks", "env", "status", "version" },
            ["worktree"] = new HashSet<string>(StringComparer.Ordinal) { "list" },
            ["stash"] = new HashSet<string>(StringComparer.Ordinal) { "list", "show" },
            ["submodule"] = new HashSet<string>(StringComparer.Ordinal) { "status" }
        };

    // "git --no-optional-locks status ..." reads; "git push" doesn't. Words are split on spaces, as logged
    public static bool IsReading(string commandLine)
    {
        string[] words = commandLine.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        int index = words.Length > 0 && words[0] == "git" ? 1 : 0;
        while (index < words.Length && words[index].StartsWith('-'))
            index += OptionsWithValue.Contains(words[index]) ? 2 : 1;
        if (index >= words.Length)
            return false;

        string command = words[index];
        if (ReadingCommands.Contains(command))
            return true;
        if (!ReadingSubcommands.TryGetValue(command, out HashSet<string>? reading))
            return false;

        // lfs track with nothing after it only lists the tracked patterns
        string? next = index + 1 < words.Length ? words[index + 1] : null;
        return (next != null && reading.Contains(next)) || (command == "lfs" && next == "track" &&
                                                           index + 2 >= words.Length);
    }
}
