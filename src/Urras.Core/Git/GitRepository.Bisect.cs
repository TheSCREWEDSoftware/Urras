namespace Urras.Core.Git;

public enum BisectMark
{
    Good,
    Bad,
    Skip
}

// Where a bisect stands: not running, testing a commit, or finished with the first bad commit found
public sealed class BisectState
{
    public static readonly BisectState Inactive = new BisectState(false, null, 0);

    public BisectState(bool active, string? firstBad, int remaining)
    {
        IsActive = active;
        FirstBad = firstBad;
        Remaining = remaining;
    }

    public bool IsActive { get; }

    // Set once git has narrowed it down to one commit
    public string? FirstBad { get; }

    // Commits still to test after the one checked out, roughly
    public int Remaining { get; }
}

// Bisect: find the commit that introduced a problem by testing commits between a good and a bad one
public partial class GitRepository
{
    public Task<GitResult> BisectStartAsync(string bad, string good)
    {
        return Run("bisect", "start", bad, good);
    }

    public Task<GitResult> BisectMarkAsync(BisectMark mark)
    {
        return Run("bisect", mark.ToString().ToLowerInvariant());
    }

    // Ends the bisect and goes back to the branch it started on
    public Task<GitResult> BisectResetAsync()
    {
        return Run("bisect", "reset");
    }

    public async Task<BisectState> GetBisectStateAsync()
    {
        if (!await GitPathExistsAsync("BISECT_LOG"))
            return BisectState.Inactive;

        GitResult log = await Run("bisect", "log");
        string? firstBad = null;
        foreach (string line in log.Output.Split('\n'))
        {
            // "# first bad commit: [hash] subject"
            const string PREFIX = "# first bad commit: [";
            if (line.StartsWith(PREFIX, StringComparison.Ordinal))
                firstBad = line[PREFIX.Length..].Split(']')[0];
        }

        int remaining = 0;
        if (firstBad == null)
        {
            // bisect-vars prints bisect_all=N among others: the commits still in question
            GitResult vars = await Run("rev-list", "--bisect-vars", "--bisect", "refs/bisect/bad", "--not",
                                       "--glob=refs/bisect/good-*");
            foreach (string line in vars.Output.Split('\n'))
            {
                if (line.StartsWith("bisect_all=", StringComparison.Ordinal))
                    int.TryParse(line["bisect_all=".Length..].Trim(), out remaining);
            }
        }

        return new BisectState(true, firstBad, Math.Max(0, remaining - 1));
    }
}
