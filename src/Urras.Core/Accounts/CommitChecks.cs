namespace Urras.Core.Accounts;

public enum CheckState
{
    Passed,
    Failed,
    Running,
    Skipped
}

// What CI says about a commit (GitHub's checks and statuses, GitLab's pipeline jobs), and where to read more
public sealed record CommitChecks(int Passed, int Failed, int Running, int Skipped, string WebUrl)
{
    public int Total => Passed + Failed + Running + Skipped;

    // Any failure decides, then anything still going
    public CheckState State => Failed > 0 ? CheckState.Failed
                             : Running > 0 ? CheckState.Running
                             : Passed > 0 ? CheckState.Passed
                             : CheckState.Skipped;

    // "12 checks passed", "2 of 12 checks failed", "3 of 12 checks running"
    public string Summary => State switch
    {
        CheckState.Failed => Failed + " of " + Total + Checks(Total) + " failed",
        CheckState.Running => Running + " of " + Total + Checks(Total) + " running",
        CheckState.Passed => Passed + Checks(Passed) + " passed" + (Skipped > 0 ? ", " + Skipped + " skipped" : ""),
        _ => Total + Checks(Total) + " skipped"
    };

    // Adds up the checks; null when there are none, so commits without CI show nothing
    public static CommitChecks? Count(IEnumerable<CheckState> states, string webUrl)
    {
        List<CheckState> all = states.ToList();
        if (all.Count == 0)
            return null;
        return new CommitChecks(all.Count(state => state == CheckState.Passed),
                                all.Count(state => state == CheckState.Failed),
                                all.Count(state => state == CheckState.Running),
                                all.Count(state => state == CheckState.Skipped), webUrl);
    }

    private static string Checks(int count)
    {
        return count == 1 ? " check" : " checks";
    }
}
