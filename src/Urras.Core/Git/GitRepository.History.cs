using System.Globalization;
using System.Text;

namespace Urras.Core.Git;

// Blame, files at a commit, comparing revisions and the reflog
public sealed partial class GitRepository
{
    public const int DEFAULT_REFLOG_LIMIT = 200;

    public async Task<List<BlameLine>> GetBlameAsync(string path, string? revision = null)
    {
        List<string> arguments = Arguments("blame", "--porcelain");
        if (revision != null)
            arguments.Add(revision);
        arguments.Add("--");
        arguments.Add(path);

        GitResult result = await Run(arguments);
        if (!result.Success)
            throw new GitException(result.Message);

        return GitParsers.ParseBlame(result.Output);
    }

    // Every file in the repository at a revision
    public async Task<List<string>> GetTreeFilesAsync(string revision)
    {
        GitResult result = await Run("ls-tree", "-r", "-z", "--name-only", revision);
        return result.Success ? result.Output.Split('\0', StringSplitOptions.RemoveEmptyEntries).ToList()
                              : new List<string>();
    }

    // A file's text at a revision; null when it doesn't exist there
    public async Task<string?> GetFileTextAsync(string revision, string path)
    {
        byte[]? bytes = await GetBlobAsync(revision, path);
        return bytes == null ? null : Encoding.UTF8.GetString(bytes);
    }

    public async Task<List<FileChange>> GetCompareFilesAsync(string from, string to)
    {
        GitResult result = await Run("diff", "--name-status", "-M", "-z", from, to);
        return result.Success ? GitParsers.ParseNameStatus(result.Output) : new List<FileChange>();
    }

    public async Task<string> GetCompareDiffAsync(string from, string to, FileChange? change = null)
    {
        List<string> arguments = Arguments("diff", "--no-color", "-M", from, to);
        if (change != null)
        {
            arguments.Add("--");
            if (change.OriginalPath != null)
                arguments.Add(change.OriginalPath);
            arguments.Add(change.Path);
        }

        GitResult result = await Run(arguments);
        return result.Success ? result.Output : result.Message;
    }

    public async Task<List<ReflogEntry>> GetReflogAsync(int limit = DEFAULT_REFLOG_LIMIT)
    {
        GitResult result = await Run("reflog", "-n", limit.ToString(CultureInfo.InvariantCulture),
                                     "--format=%H%x1f%h%x1f%gd%x1f%gs%x1f%cI");
        List<ReflogEntry> entries = new List<ReflogEntry>();
        if (!result.Success)
            return entries;

        foreach (string line in result.Output.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            string[] fields = line.TrimEnd('\r').Split(GitParsers.FIELD_SEPARATOR);
            if (fields.Length < 5)
                continue;

            DateTimeOffset.TryParse(fields[4], CultureInfo.InvariantCulture, DateTimeStyles.None,
                                    out DateTimeOffset date);
            entries.Add(new ReflogEntry(fields[0], fields[1], fields[2], fields[3], date));
        }

        return entries;
    }

    // A branch at any revision, used to bring back commits found in the reflog
    public Task<GitResult> CreateBranchAtAsync(string name, string revision)
    {
        GitResult? refused = RefNames.Refuse(name);
        return refused != null ? Task.FromResult(refused) : Run("branch", name, revision);
    }

    private const int KNOWN_PEOPLE_COMMITS = 3000;

    // People who have committed here, most frequent first, as co-author suggestions
    public async Task<List<CoAuthor>> GetKnownPeopleAsync(int commits = KNOWN_PEOPLE_COMMITS)
    {
        GitResult result = await Run("log", "--all", "-n", commits.ToString(), "--format=%an%x1f%ae%n%cn%x1f%ce");
        if (!result.Success)
            return new List<CoAuthor>();

        return result.Output.Split('\n')
                     .Select(line => line.TrimEnd('\r').Split('\u001f'))
                     .Where(fields => fields.Length == 2 && fields[1].Contains('@')
                                      && !fields[1].StartsWith("noreply@github.com", StringComparison.Ordinal))
                     .GroupBy(fields => fields[1].ToLowerInvariant())
                     .OrderByDescending(group => group.Count())
                     .Select(group => new CoAuthor(group.First()[0], group.First()[1]))
                     .ToList();
    }
}
