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
    public string DateTip => Urras.Core.Settings.DisplayFormats.Full(Date, DateTimeOffset.Now);

    // On the current branch but on no remote yet, marked "Not pushed" in History
    public bool IsUnpushed { get; set; }

    // The repository has no remote at all, so the mark says "Local only" instead
    public bool IsLocalOnly { get; set; }

    public string UnpushedText => IsLocalOnly ? "Local only" : "Not pushed";

    public string UnpushedTip => IsLocalOnly
        ? "Local only: this repository has no remote yet; Publish repository puts it online"
        : "Not pushed: on this branch but not on the remote yet; Push sends it";

    // Branches and tags pointing at this commit, such as "HEAD -> main, tag: v0.1.0, origin/main"
    public string Refs { get; init; } = string.Empty;

    // The summary split into text and code, worked out when first shown
    public IReadOnlyList<SubjectPart> SubjectParts => _subjectParts ??= CommitSubject.Parts(Subject);
    private IReadOnlyList<SubjectPart>? _subjectParts;
    public bool HasRefs => RefBadges.Count > 0;

    // Refs split into badges: the checked-out branch, other local branches, remote branches and tags
    public IReadOnlyList<RefBadge> RefBadges => _refBadges ??= RefBadge.Parse(Refs);
    private List<RefBadge>? _refBadges;

    // People named in "Co-authored-by:" lines, besides the author
    public IReadOnlyList<CommitPerson> CoAuthors { get; init; } = Array.Empty<CommitPerson>();

    // The author then the co-authors
    public IReadOnlyList<CommitPerson> People => _people ??= new[] { new CommitPerson(Author, Email) }
                                                                 .Concat(CoAuthors).ToList();
    private List<CommitPerson>? _people;

    // Up to three pictures, drawn overlapping
    public IReadOnlyList<CommitPerson> Pictures => People.Take(CommitPerson.MAX_PICTURES).ToList();

    // "Crow", "Crow, Claude Opus 5.5" or "3 people"
    public string PeopleText => CommitPerson.Describe(People);

    // For the narrow History column: the author, and "+2" for co-authors (named in the tooltip)
    public string ShortPeopleText => CoAuthors.Count == 0 ? Author : Author + "  +" + CoAuthors.Count;

    // Everyone by name and address, author first
    public string PeopleTip => CoAuthors.Count == 0
        ? "Open their profile, or this commit when the profile isn't known"
        : "Authored by " + People[0] + "\n\nCo-authored by\n" + string.Join("\n", CoAuthors) +
          "\n\nClick to open the author's profile";

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
    public string Text => Name;

    // A local branch whose remote branch is on the same commit shows as one badge, with the remote's name
    public string? Remote { get; init; }
    public bool HasRemote => Remote != null;
    public bool IsBranch => Kind is RefKind.Local or RefKind.Remote;

    // The graph lane of the commit, so the badge takes its colour; set when the graph is laid out
    public int Lane { get; set; }

    // %D with --decorate=full, so local branches like feature/x are told apart from remote ones
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

        return Merged(badges);
    }

    // "split/x" and "origin/split/x" on one commit become one "split/x" badge marked with origin
    private static List<RefBadge> Merged(List<RefBadge> badges)
    {
        HashSet<RefBadge> absorbed = new HashSet<RefBadge>();
        List<RefBadge> merged = new List<RefBadge>();
        foreach (RefBadge badge in badges)
        {
            if (badge.Kind is not (RefKind.Local or RefKind.Head) ||
                badge.Name.StartsWith("HEAD", StringComparison.Ordinal))
                continue;

            RefBadge? remote = badges.FirstOrDefault(other => other.Kind == RefKind.Remote &&
                                                              !absorbed.Contains(other) &&
                                                              RemoteOf(other.Name, badge.Name) != null);
            if (remote != null)
                absorbed.Add(remote);
        }

        foreach (RefBadge badge in badges)
        {
            if (absorbed.Contains(badge))
                continue;

            string? remote = badge.Kind is RefKind.Local or RefKind.Head
                ? absorbed.Select(other => RemoteOf(other.Name, badge.Name)).FirstOrDefault(name => name != null)
                : null;
            merged.Add(remote == null ? badge : badge with { Remote = remote });
        }

        return merged;
    }

    // "origin" when remoteBranch is exactly "origin/<branch>", the remote's name being one segment; otherwise null
    private static string? RemoteOf(string remoteBranch, string branch)
    {
        int slash = remoteBranch.IndexOf('/');
        return slash > 0 && remoteBranch.Length == slash + 1 + branch.Length &&
               remoteBranch.EndsWith(branch, StringComparison.Ordinal)
            ? remoteBranch[..slash]
            : null;
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
