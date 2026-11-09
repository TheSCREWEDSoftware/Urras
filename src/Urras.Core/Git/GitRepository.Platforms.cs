namespace Urras.Core.Git;

// Git steps behind platform features: checking out a pull request, finding the main branch
public partial class GitRepository
{
    // Fetches a pull request's commits into a local branch and switches; source may be a remote or an address
    public async Task<GitResult> CheckOutPullRequestAsync(string pullRequestRef, string localBranch,
                                                          string source = "origin")
    {
        GitResult fetch = await Run("fetch", source, "+" + pullRequestRef + ":" + localBranch);
        return fetch.Success ? await Run("switch", localBranch) : fetch;
    }

    // The remote's main branch (origin/HEAD), falling back to main or master
    public async Task<string> GetDefaultBranchAsync()
    {
        GitResult head = await Run("symbolic-ref", "--quiet", "--short", "refs/remotes/origin/HEAD");
        if (head.Success && head.Output.Trim().StartsWith("origin/"))
            return head.Output.Trim()["origin/".Length..];

        GitResult main = await Run("rev-parse", "--verify", "--quiet", "refs/remotes/origin/main");
        return main.Success ? "main" : "master";
    }

    // When each branch last changed (its latest commit), by name as the branch list shows it
    public async Task<Dictionary<string, DateTimeOffset>> GetBranchDatesAsync()
    {
        Dictionary<string, DateTimeOffset> dates = new Dictionary<string, DateTimeOffset>(StringComparer.Ordinal);
        GitResult result = await Run("for-each-ref", "--format=%(refname:short)%1f%(committerdate:iso-strict)",
                                     "refs/heads", "refs/remotes");
        foreach (string line in result.Output.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            string[] fields = line.Split(GitParsers.FIELD_SEPARATOR);
            if (fields.Length == 2 && DateTimeOffset.TryParse(fields[1].Trim(),
                                                              System.Globalization.CultureInfo.InvariantCulture,
                                                              System.Globalization.DateTimeStyles.None,
                                                              out DateTimeOffset date))
                dates[fields[0]] = date;
        }

        return dates;
    }

    // Branches checked out lately, newest first, read from HEAD's reflog ("checkout: moving from a to b")
    public async Task<List<string>> GetRecentBranchesAsync(int limit = BranchLists.RECENT_COUNT * 2)
    {
        const string MOVE = "checkout: moving from ";
        const string TO = " to ";
        const int REFLOG_ENTRIES = 300;
        GitResult result = await Run("reflog", "show", "-n", REFLOG_ENTRIES.ToString(), "--format=%gs", "HEAD");
        List<string> recent = new List<string>();
        foreach (string line in result.Output.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            int to = line.LastIndexOf(TO, StringComparison.Ordinal);
            if (!line.StartsWith(MOVE, StringComparison.Ordinal) || to < 0)
                continue;

            string branch = line[(to + TO.Length)..].Trim();
            if (!recent.Contains(branch))
                recent.Add(branch);
            if (recent.Count >= limit)
                break;
        }

        return recent;
    }
}
