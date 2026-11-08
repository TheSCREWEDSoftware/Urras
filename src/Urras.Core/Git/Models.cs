namespace Urras.Core.Git;

public enum ChangeKind
{
    Modified,
    TypeChanged,
    Added,
    Deleted,
    Renamed,
    Copied,
    Untracked,
    Conflicted
}

public sealed class FileChange
{
    public FileChange(string path, ChangeKind kind, string? originalPath = null)
    {
        Path = path;
        Kind = kind;
        OriginalPath = originalPath;
    }

    public string Path { get; }
    public ChangeKind Kind { get; }
    public string? OriginalPath { get; }

    public string KindLetter => Kind switch
    {
        ChangeKind.Modified => "M",
        ChangeKind.TypeChanged => "T",
        ChangeKind.Added => "A",
        ChangeKind.Deleted => "D",
        ChangeKind.Renamed => "R",
        ChangeKind.Copied => "C",
        ChangeKind.Untracked => "?",
        ChangeKind.Conflicted => "!",
        _ => " "
    };

    public string KindName => Kind switch
    {
        ChangeKind.Modified => "Modified",
        ChangeKind.TypeChanged => "Type changed",
        ChangeKind.Added => "Added",
        ChangeKind.Deleted => "Deleted",
        ChangeKind.Renamed => "Renamed",
        ChangeKind.Copied => "Copied",
        ChangeKind.Untracked => "Untracked",
        ChangeKind.Conflicted => "Conflicted",
        _ => "Unchanged"
    };

    // Shown when hovering the change icon
    public string KindDescription => Kind switch
    {
        ChangeKind.Added => "New",
        ChangeKind.Untracked => "New (not tracked yet)",
        ChangeKind.TypeChanged => "Type changed (for example a file became a link)",
        ChangeKind.Renamed or ChangeKind.Copied when OriginalPath != null => KindName + " from " + OriginalPath,
        ChangeKind.Conflicted => "Conflicted (resolve before continuing)",
        _ => KindName
    };

    public string DisplayPath => OriginalPath == null ? Path : OriginalPath + " → " + Path;

    // A line-ending problem to show next to the file, filled in by the change list
    public string? Warning { get; set; }
    public bool HasWarning => Warning != null;
}

public sealed class RepositoryStatus
{
    public string? Branch { get; set; }
    public string? HeadCommit { get; set; }
    public bool IsUnborn { get; set; }
    public bool IsDetached => Branch == null;
    public string? Upstream { get; set; }
    public int Ahead { get; set; }
    public int Behind { get; set; }
    public List<FileChange> Staged { get; } = new List<FileChange>();
    public List<FileChange> Unstaged { get; } = new List<FileChange>();

    public bool HasChanges => Staged.Count > 0 || Unstaged.Count > 0;
}

public sealed class CommitInfo
{
    public CommitInfo(string hash, string shortHash, string author, string email, DateTimeOffset date, string subject)
    {
        Hash = hash;
        ShortHash = shortHash;
        Author = author;
        Email = email;
        Date = date;
        Subject = subject;
    }

    public string Hash { get; }
    public string ShortHash { get; }
    public string Author { get; }
    public string Email { get; }
    public DateTimeOffset Date { get; }
    public string Subject { get; }

    public string DateText => Urras.Core.Settings.DisplayFormats.DateAndTime(Date);

    // Branches and tags pointing at this commit, such as "HEAD -> main, tag: v0.1.0, origin/main"
    public string Refs { get; init; } = string.Empty;
    public bool HasRefs => RefBadges.Count > 0;

    // Refs split into badges: the checked-out branch, other local branches, remote branches and tags
    public IReadOnlyList<RefBadge> RefBadges => _refBadges ??= RefBadge.Parse(Refs);
    private List<RefBadge>? _refBadges;

    // Full hashes of the parent commits: none for the first commit, two or more for a merge
    public IReadOnlyList<string> Parents { get; init; } = Array.Empty<string>();

    // Set when this row is a stash rather than a commit
    public StashInfo? Stash { get; init; }
    public bool IsStash => Stash != null;
    public bool IsMerge => Parents.Count > 1;

    // Lanes and lines drawn beside the commit; set by the history view
    public GraphRow? Graph { get; set; }
}

public sealed class BranchInfo
{
    public BranchInfo(string name, bool isCurrent, bool isRemote, string? upstream, string? tracking)
    {
        Name = name;
        IsCurrent = isCurrent;
        IsRemote = isRemote;
        Upstream = upstream;
        Tracking = tracking;
    }

    public string Name { get; }
    public bool IsCurrent { get; }
    public bool IsRemote { get; }
    public string? Upstream { get; }
    public string? Tracking { get; }

    public override string ToString()
    {
        return Name;
    }
}

public enum RefKind
{
    Head,
    Local,
    Remote,
    Tag,
    Stash
}

public sealed record RefBadge(string Name, RefKind Kind)
{
    public bool IsHead => Kind == RefKind.Head;
    public bool IsRemote => Kind == RefKind.Remote;
    public bool IsTag => Kind == RefKind.Tag;
    public bool IsStash => Kind == RefKind.Stash;
    public string Text => Kind == RefKind.Head ? "✓ " + Name : Name;

    // git log's %D with --decorate=full, which tells local branches named like feature/x apart from remote ones:
    // "HEAD -> refs/heads/main, tag: refs/tags/v0.1.0, refs/remotes/origin/main, refs/remotes/origin/HEAD"
    public static List<RefBadge> Parse(string refs)
    {
        List<RefBadge> badges = new List<RefBadge>();
        StringSplitOptions options = StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries;
        foreach (string part in refs.Split(',', options))
        {
            if (part.StartsWith("HEAD -> ", StringComparison.Ordinal))
                badges.Add(new RefBadge(Short(part["HEAD -> ".Length..]), RefKind.Head));
            else if (part == "HEAD")
                badges.Add(new RefBadge("HEAD (detached)", RefKind.Head));
            else if (part.StartsWith("tag: ", StringComparison.Ordinal))
                badges.Add(new RefBadge(Short(part["tag: ".Length..]), RefKind.Tag));
            else if (part.StartsWith("stash@{", StringComparison.Ordinal))
                badges.Add(new RefBadge(part, RefKind.Stash));
            else if (part.EndsWith("/HEAD", StringComparison.Ordinal) || part.StartsWith("refs/stash"))
                continue;
            else if (part.StartsWith("refs/remotes/", StringComparison.Ordinal))
                badges.Add(new RefBadge(Short(part), RefKind.Remote));
            else
                badges.Add(new RefBadge(Short(part), RefKind.Local));
        }

        return badges;
    }

    private static string Short(string name)
    {
        foreach (string prefix in new[] { "refs/heads/", "refs/remotes/", "refs/tags/" })
        {
            if (name.StartsWith(prefix, StringComparison.Ordinal))
                return name[prefix.Length..];
        }

        return name;
    }
}
