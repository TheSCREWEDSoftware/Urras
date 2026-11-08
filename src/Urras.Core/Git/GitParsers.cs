using System.Globalization;

namespace Urras.Core.Git;

public static class GitParsers
{
    public const char FIELD_SEPARATOR = '\x1f';
    public const char RECORD_SEPARATOR = '\x1e';

    // Parses `git status --porcelain=v2 --branch -z --untracked-files=all`
    public static RepositoryStatus ParseStatus(string output)
    {
        RepositoryStatus status = new RepositoryStatus();
        string[] tokens = output.Split('\0');

        for (int i = 0; i < tokens.Length; ++i)
        {
            string token = tokens[i];
            if (token.Length == 0)
                continue;

            switch (token[0])
            {
                case '#':
                    ParseBranchHeader(status, token);
                    break;
                case '1':
                {
                    string[] fields = token.Split(' ', 9);
                    if (fields.Length == 9)
                        AddChange(status, fields[1], fields[8], null);
                    break;
                }
                case '2':
                {
                    // Renames and copies: the original path is the next NUL-separated token
                    string[] fields = token.Split(' ', 10);
                    string? originalPath = i + 1 < tokens.Length ? tokens[++i] : null;
                    if (fields.Length == 10)
                        AddChange(status, fields[1], fields[9], originalPath);
                    break;
                }
                case 'u':
                {
                    string[] fields = token.Split(' ', 11);
                    if (fields.Length == 11)
                        status.Unstaged.Add(new FileChange(fields[10], ChangeKind.Conflicted));
                    break;
                }
                case '?':
                    status.Unstaged.Add(new FileChange(token.Substring(2), ChangeKind.Untracked));
                    break;
                default:
                    break;
            }
        }

        return status;
    }

    private static void ParseBranchHeader(RepositoryStatus status, string token)
    {
        string[] parts = token.Split(' ', 3);
        if (parts.Length < 3)
            return;

        string value = parts[2];
        switch (parts[1])
        {
            case "branch.oid":
                status.IsUnborn = value == "(initial)";
                status.HeadCommit = status.IsUnborn ? null : value;
                break;
            case "branch.head":
                status.Branch = value == "(detached)" ? null : value;
                break;
            case "branch.upstream":
                status.Upstream = value;
                break;
            case "branch.ab":
            {
                string[] counts = value.Split(' ');
                if (counts.Length == 2)
                {
                    status.Ahead = int.Parse(counts[0].TrimStart('+'), CultureInfo.InvariantCulture);
                    status.Behind = int.Parse(counts[1].TrimStart('-'), CultureInfo.InvariantCulture);
                }
                break;
            }
            default:
                break;
        }
    }

    private static void AddChange(RepositoryStatus status, string xy, string path, string? originalPath)
    {
        if (xy.Length != 2)
            return;

        if (xy[0] != '.')
            status.Staged.Add(new FileChange(path, ToKind(xy[0]), originalPath));
        if (xy[1] != '.')
            status.Unstaged.Add(new FileChange(path, ToKind(xy[1]), xy[0] == '.' ? originalPath : null));
    }

    // Parses `git blame --porcelain`: a header per line ("<hash> <old> <new> [<count>]"), commit details the first
    // time a commit appears, then the line itself after a tab
    public static List<BlameLine> ParseBlame(string output)
    {
        List<BlameLine> lines = new List<BlameLine>();
        Dictionary<string, (string Author, DateTimeOffset Date, string Summary)> commits =
            new Dictionary<string, (string, DateTimeOffset, string)>();

        string hash = string.Empty;
        int number = 0;
        string author = string.Empty;
        long time = 0;
        string zone = "+0000";
        string summary = string.Empty;

        foreach (string rawLine in output.Split('\n'))
        {
            string line = rawLine.TrimEnd('\r');
            if (line.StartsWith('\t'))
            {
                if (!commits.ContainsKey(hash))
                    commits[hash] = (author, ToDate(time, zone), summary);

                (string Author, DateTimeOffset Date, string Summary) commit = commits[hash];
                lines.Add(new BlameLine(number, hash, commit.Author, commit.Date, commit.Summary, line.Substring(1)));
                continue;
            }

            string[] parts = line.Split(' ', 2);
            string key = parts[0];
            string value = parts.Length > 1 ? parts[1] : string.Empty;

            if (key.Length >= 40 && key.All(Uri.IsHexDigit))
            {
                hash = key;
                string[] numbers = value.Split(' ');
                number = numbers.Length > 1 ? int.Parse(numbers[1], CultureInfo.InvariantCulture) : number + 1;
                continue;
            }

            switch (key)
            {
                case "author":
                    author = value;
                    break;
                case "author-time":
                    time = long.Parse(value, CultureInfo.InvariantCulture);
                    break;
                case "author-tz":
                    zone = value;
                    break;
                case "summary":
                    summary = value;
                    break;
                default:
                    break;
            }
        }

        // Shade alternate blocks of consecutive lines from the same commit
        bool shaded = false;
        for (int i = 0; i < lines.Count; ++i)
        {
            if (i > 0 && lines[i].Hash != lines[i - 1].Hash)
                shaded = !shaded;
            lines[i].IsShaded = shaded;
        }

        return lines;
    }

    private static DateTimeOffset ToDate(long unixSeconds, string zone)
    {
        TimeSpan offset = TimeSpan.Zero;
        if (zone.Length == 5 && int.TryParse(zone.AsSpan(1, 2), out int hours) &&
            int.TryParse(zone.AsSpan(3, 2), out int minutes))
            offset = new TimeSpan(hours, minutes, 0) * (zone[0] == '-' ? -1 : 1);

        return DateTimeOffset.FromUnixTimeSeconds(unixSeconds).ToOffset(offset);
    }

    // Parses `--name-status -z`: "M\0path\0", or "R100\0old\0new\0" for renames and copies
    public static List<FileChange> ParseNameStatus(string output)
    {
        List<FileChange> changes = new List<FileChange>();
        string[] tokens = output.Split('\0');
        int i = 0;
        while (i < tokens.Length && tokens[i].Length > 0)
        {
            char code = tokens[i][0];
            if ((code == 'R' || code == 'C') && i + 2 < tokens.Length)
            {
                changes.Add(new FileChange(tokens[i + 2], ToKind(code), tokens[i + 1]));
                i += 3;
            }
            else if (i + 1 < tokens.Length)
            {
                changes.Add(new FileChange(tokens[i + 1], ToKind(code)));
                i += 2;
            }
            else
                break;
        }

        return changes;
    }

    private static ChangeKind ToKind(char code)
    {
        return code switch
        {
            'A' => ChangeKind.Added,
            'D' => ChangeKind.Deleted,
            'R' => ChangeKind.Renamed,
            'C' => ChangeKind.Copied,
            'T' => ChangeKind.TypeChanged,
            _ => ChangeKind.Modified
        };
    }

    public const string LOG_FORMAT = "%H%x1f%h%x1f%an%x1f%ae%x1f%aI%x1f%s%x1f%D%x1f%P%x1e";

    public static List<CommitInfo> ParseLog(string output)
    {
        List<CommitInfo> commits = new List<CommitInfo>();

        foreach (string record in output.Split(RECORD_SEPARATOR))
        {
            string trimmed = record.Trim('\n', '\r');
            if (trimmed.Length == 0)
                continue;

            string[] fields = trimmed.Split(FIELD_SEPARATOR);
            if (fields.Length < 6)
                continue;

            DateTimeOffset.TryParse(fields[4], CultureInfo.InvariantCulture, DateTimeStyles.None,
                                    out DateTimeOffset date);
            commits.Add(new CommitInfo(fields[0], fields[1], fields[2], fields[3], date, fields[5])
            {
                Refs = fields.Length > 6 ? fields[6] : string.Empty,
                Parents = fields.Length > 7 ? fields[7].Split(' ', StringSplitOptions.RemoveEmptyEntries)
                                            : Array.Empty<string>()
            });
        }

        return commits;
    }

    public const string BRANCH_FORMAT =
        "%(refname)%1f%(refname:short)%1f%(HEAD)%1f%(upstream:short)%1f%(upstream:track)";

    public static List<BranchInfo> ParseBranches(string output)
    {
        List<BranchInfo> branches = new List<BranchInfo>();

        foreach (string line in output.Split('\n'))
        {
            string trimmed = line.TrimEnd('\r');
            if (trimmed.Length == 0)
                continue;

            string[] fields = trimmed.Split(FIELD_SEPARATOR);
            if (fields.Length < 5)
                continue;

            bool isRemote = fields[0].StartsWith("refs/remotes/", StringComparison.Ordinal);

            // Skip the symbolic origin/HEAD entry
            if (isRemote && fields[0].EndsWith("/HEAD", StringComparison.Ordinal))
                continue;

            string? upstream = fields[3].Length > 0 ? fields[3] : null;
            string? tracking = fields[4].Length > 0 ? fields[4] : null;
            branches.Add(new BranchInfo(fields[1], fields[2] == "*", isRemote, upstream, tracking));
        }

        return branches;
    }

    // "-a1b2c3 libs/raylib", " a1b2c3 libs/raylib (v5.0)", "+a1b2c3 libs/raylib (heads/main)"
    public static List<SubmoduleInfo> ParseSubmodules(string output)
    {
        List<SubmoduleInfo> modules = new List<SubmoduleInfo>();
        foreach (string raw in output.Split('\n'))
        {
            string line = raw.TrimEnd('\r');
            if (line.Length < 3)
                continue;

            SubmoduleState state = line[0] switch
            {
                '-' => SubmoduleState.NotInitialized,
                '+' => SubmoduleState.DifferentCommit,
                'U' => SubmoduleState.Conflicted,
                _ => SubmoduleState.UpToDate
            };
            string rest = line[1..];
            int space = rest.IndexOf(' ');
            if (space < 0)
                continue;

            string commit = rest[..space];
            string pathAndDescription = rest[(space + 1)..];
            string description = string.Empty;
            if (pathAndDescription.EndsWith(')') && pathAndDescription.LastIndexOf(" (", StringComparison.Ordinal)
                    is var open and > 0)
            {
                description = pathAndDescription[(open + 2)..^1];
                pathAndDescription = pathAndDescription[..open];
            }

            modules.Add(new SubmoduleInfo(pathAndDescription, commit, state, description));
        }

        return modules;
    }

    // Blocks separated by blank lines: "worktree <path>", "HEAD <sha>", "branch refs/heads/x" or "detached",
    // "bare", "locked [reason]", "prunable [reason]". The first block is the main worktree.
    public static List<WorktreeInfo> ParseWorktrees(string output)
    {
        List<WorktreeInfo> worktrees = new List<WorktreeInfo>();
        string? path = null;
        string? head = null;
        string? branch = null;
        bool locked = false;
        bool prunable = false;

        void Flush()
        {
            if (path != null)
                worktrees.Add(new WorktreeInfo(path, head, branch, worktrees.Count == 0, locked, prunable));
            path = head = branch = null;
            locked = prunable = false;
        }

        foreach (string raw in output.Split('\n'))
        {
            string line = raw.TrimEnd('\r');
            if (line.Length == 0)
                Flush();
            else if (line.StartsWith("worktree ", StringComparison.Ordinal))
            {
                Flush();
                path = line["worktree ".Length..];
            }
            else if (line.StartsWith("HEAD ", StringComparison.Ordinal))
                head = line["HEAD ".Length..];
            else if (line.StartsWith("branch ", StringComparison.Ordinal))
                branch = line["branch ".Length..].Replace("refs/heads/", string.Empty);
            else if (line.StartsWith("locked", StringComparison.Ordinal))
                locked = true;
            else if (line.StartsWith("prunable", StringComparison.Ordinal))
                prunable = true;
        }

        Flush();
        return worktrees;
    }
}
