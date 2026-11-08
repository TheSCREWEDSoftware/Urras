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

    public override string ToString()
    {
        return Reference + "  " + Message + "  (" + Date.LocalDateTime.ToString("yyyy-MM-dd HH:mm") + ")";
    }
}
