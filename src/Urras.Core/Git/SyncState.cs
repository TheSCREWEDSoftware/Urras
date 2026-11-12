using Urras.Core.Settings;

namespace Urras.Core.Git;

public enum SyncKind
{
    Fetch,
    Pull,
    Push,
    Publish,
    PublishRepository,
    ForcePush
}

// What the main sync button offers, like GitHub Desktop's: publish, pull, push, or fetch when there's nothing else
public sealed record SyncState(SyncKind Kind, string Title, string Detail, int Ahead, int Behind)
{
    private const string DEFAULT_REMOTE = "origin";

    // Offers Publish repository without a remote or again once it's deleted, and force push after a rewrite
    public static SyncState For(RepositoryStatus status, DateTimeOffset? lastFetched, DateTimeOffset now,
                                bool hasRemote = true, bool rewritten = false, bool remoteMissing = false)
    {
        if (!hasRemote)
            return new SyncState(SyncKind.PublishRepository, "Publish repository",
                                 "Publish this repository to GitHub", 0, 0);
        if (remoteMissing)
            return new SyncState(SyncKind.PublishRepository, "Publish repository again",
                                 "Its online repository is gone", 0, 0);

        string remote = Remote(status.Upstream) ?? DEFAULT_REMOTE;
        string fetched = lastFetched is { } when
            ? "Last fetched " + RelativeTime.Describe(when, now)
            : "Never fetched";

        if (!status.IsDetached && status.Upstream == null)
            return new SyncState(SyncKind.Publish, "Publish branch", "Publish this branch to " + remote, 0, 0);
        if (rewritten && status.Ahead > 0 && status.Behind > 0)
            return new SyncState(SyncKind.ForcePush, "Force push " + remote, "Replace the remote's old copies",
                                 status.Ahead, status.Behind);
        if (status.Behind > 0)
            return new SyncState(SyncKind.Pull, "Pull " + remote, fetched, status.Ahead, status.Behind);
        if (status.Ahead > 0)
            return new SyncState(SyncKind.Push, "Push " + remote, fetched, status.Ahead, 0);
        return new SyncState(SyncKind.Fetch, "Fetch " + remote, fetched, 0, 0);
    }

    // Beside the branch name: "↑13 ↓2", "up to date", "not published", or nothing for a detached HEAD
    public static string BranchCounts(RepositoryStatus status)
    {
        if (status.IsDetached)
            return string.Empty;
        if (status.Upstream == null)
            return "not published";
        if (status.Ahead == 0 && status.Behind == 0)
            return "up to date";

        List<string> parts = new List<string>();
        if (status.Ahead > 0)
            parts.Add("↑" + status.Ahead);
        if (status.Behind > 0)
            parts.Add("↓" + status.Behind);
        return string.Join(" ", parts);
    }

    // Above the branch name: "Current branch · fetched 5 minutes ago"
    public static string BranchCaption(DateTimeOffset? lastFetched, DateTimeOffset now)
    {
        return "Current branch · " + (lastFetched is { } when
            ? "fetched " + RelativeTime.Describe(when, now)
            : "never fetched");
    }

    // The remote part of an upstream: origin for origin/main
    public static string? Remote(string? upstream)
    {
        int slash = upstream?.IndexOf('/') ?? -1;
        return slash > 0 ? upstream![..slash] : null;
    }

    // The counts beside the title: "13 ↑", "164 ↓" or both; empty when there's nothing to move
    public string Badge
    {
        get
        {
            List<string> parts = new List<string>();
            if (Ahead > 0)
                parts.Add(Ahead + " ↑");
            if (Behind > 0)
                parts.Add(Behind + " ↓");
            return string.Join("  ", parts);
        }
    }
}
