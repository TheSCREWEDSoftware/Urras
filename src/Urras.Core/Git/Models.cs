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

    public string DateText => Date.LocalDateTime.ToString("yyyy-MM-dd HH:mm");

    // Branches and tags pointing at this commit, such as "HEAD -> main, tag: v0.1.0, origin/main"
    public string Refs { get; init; } = string.Empty;
    public bool HasRefs => Refs.Length > 0;
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
