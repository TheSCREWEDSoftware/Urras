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
        string? firstBad = FirstBadFromLog(log.Output);
        int remaining = 0;
        if (firstBad == null)
        {
            // bisect-vars prints bisect_all=N among others: the commits still in question
            GitResult vars = await Run("rev-list", "--bisect-vars", "--bisect", "refs/bisect/bad", "--not",
                                       "--glob=refs/bisect/good-*");
            bool counted = false;
            foreach (string line in vars.Output.Split('\n'))
            {
                if (line.StartsWith("bisect_all=", StringComparison.Ordinal))
                    counted = int.TryParse(line["bisect_all=".Length..].Trim(), out remaining);
            }

            // Only the bad commit left in question means it is the first bad one; not every Git logs that
            if (vars.Success && counted && remaining <= 1)
            {
                GitResult bad = await Run("rev-parse", "refs/bisect/bad");
                if (bad.Success && bad.Output.Trim().Length > 0)
                    firstBad = bad.Output.Trim();
            }
        }

        return new BisectState(true, firstBad, Math.Max(0, remaining - 1));
    }

    // "# first bad commit: [hash] subject" in the bisect log, with or without the brackets
    public static string? FirstBadFromLog(string log)
    {
        const string MARKER = "first bad commit:";
        foreach (string line in log.Split('\n'))
        {
            int at = line.IndexOf(MARKER, StringComparison.Ordinal);
            if (!line.TrimStart().StartsWith('#') || at < 0)
                continue;

            string hash = new string(line[(at + MARKER.Length)..].Trim().TrimStart('[')
                                         .TakeWhile(Uri.IsHexDigit).ToArray());
            if (hash.Length >= Hashes.SHORT_LENGTH)
                return hash;
        }

        return null;
    }
}
