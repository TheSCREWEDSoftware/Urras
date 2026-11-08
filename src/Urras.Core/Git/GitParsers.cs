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

    public const string LOG_FORMAT = "%H%x1f%h%x1f%an%x1f%ae%x1f%aI%x1f%s%x1e";

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
            commits.Add(new CommitInfo(fields[0], fields[1], fields[2], fields[3], date, fields[5]));
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
}
