namespace Urras.Core.Git;

// A multi-step Git operation that stopped halfway, usually because of conflicts
public enum OperationState
{
    None,
    Merge,
    Rebase,
    CherryPick,
    Revert
}

public sealed class TagInfo
{
    public TagInfo(string name, string commit, string subject)
    {
        Name = name;
        Commit = commit;
        Subject = subject;
    }

    public string Name { get; }
    public string Commit { get; }
    public string Subject { get; }

    public override string ToString()
    {
        return Subject.Length > 0 ? Name + "  (" + Subject + ")" : Name;
    }
}

public sealed class StashInfo
{
    public StashInfo(string reference, string hash, string message, DateTimeOffset date)
    {
        Reference = reference;
        Hash = hash;
        Message = message;
        Date = date;
    }

    // "stash@{0}"
    public string Reference { get; }
    public string Hash { get; }
    public string Message { get; }
    public DateTimeOffset Date { get; }
    public string? Parent { get; init; }
    public string Author { get; init; } = string.Empty;

    // The stash as a row in the history, branching off the commit it was made on
    public CommitInfo ToCommit()
    {
        return new CommitInfo(Hash, Hashes.Short(Hash), Author, string.Empty, Date, Message)
        {
            Parents = Parent == null ? Array.Empty<string>() : new[] { Parent },
            Refs = Reference,
            Stash = this
        };
    }

    // Puts stashes into a history listed newest first, each above the commit it was made on
    public static List<CommitInfo> Merge(IReadOnlyList<CommitInfo> history, IEnumerable<StashInfo> stashes)
    {
        List<CommitInfo> merged = new List<CommitInfo>(history);
        foreach (StashInfo stash in stashes.OrderBy(stash => stash.Date))
        {
            int parent = stash.Parent == null ? -1 : merged.FindIndex(commit => commit.Hash == stash.Parent);
            if (parent < 0)
                continue;

            int byDate = merged.FindIndex(commit => commit.Stash == null && commit.Date <= stash.Date);
            merged.Insert(byDate < 0 ? parent : Math.Min(byDate, parent), stash.ToCommit());
        }

        return merged;
    }

    public override string ToString()
    {
        return Reference + "  " + Message + "  (" + Date.LocalDateTime.ToString("yyyy-MM-dd HH:mm") + ")";
    }
}
