using Urras.Core.Git;

namespace Urras.Core.Tests;

public static class SyncTests
{
    private static readonly DateTimeOffset Now = new DateTimeOffset(2026, 11, 9, 14, 0, 0, TimeSpan.Zero);

    private static RepositoryStatus Status(string? upstream, int ahead, int behind, string? branch = "main")
    {
        return new RepositoryStatus { Branch = branch, Upstream = upstream, Ahead = ahead, Behind = behind };
    }

    public static void OffersWhatGitHubDesktopOffers()
    {
        SyncState fetch = SyncState.For(Status("origin/main", 0, 0), Now.AddMinutes(-5), Now);
        Assert.Equal(SyncKind.Fetch, fetch.Kind, "nothing to move: fetch");
        Assert.Equal("Fetch origin", fetch.Title, "names the remote");
        Assert.Equal("Last fetched 5 minutes ago", fetch.Detail, "when it last fetched");
        Assert.Equal(string.Empty, fetch.Badge, "no counts");

        SyncState push = SyncState.For(Status("upstream/dev", 13, 0), Now, Now);
        Assert.Equal(SyncKind.Push, push.Kind, "ahead: push");
        Assert.Equal("Push upstream", push.Title, "the upstream's remote");
        Assert.Equal("13 ↑", push.Badge, "commits to push");

        SyncState pull = SyncState.For(Status("origin/master", 2, 164), null, Now);
        Assert.Equal(SyncKind.Pull, pull.Kind, "behind comes first: pull");
        Assert.Equal("Never fetched", pull.Detail, "no FETCH_HEAD yet");
        Assert.Equal("2 ↑  164 ↓", pull.Badge, "both counts");

        SyncState publish = SyncState.For(Status(null, 0, 0), null, Now);
        Assert.Equal(SyncKind.Publish, publish.Kind, "no upstream: publish");
        Assert.Equal("Publish this branch to origin", publish.Detail, "says where");

        SyncState detached = SyncState.For(Status(null, 0, 0, null), null, Now);
        Assert.Equal(SyncKind.Fetch, detached.Kind, "a detached HEAD can't be published");
    }

    public static async Task KnowsWhenItLastFetched()
    {
        using TempRepository remote = await TempRepository.CreateAsync();
        remote.Write("a.txt", "a\n");
        await remote.CommitAllAsync("Initial");
        using TempRepository local = await TempRepository.CreateAsync();
        Assert.Success(await GitRunner.RunAsync(local.Path, "remote", "add", "origin", remote.Path), "remote");

        Assert.Equal(null, await local.Repository.GetLastFetchedAsync(), "never fetched");
        DateTimeOffset before = DateTimeOffset.UtcNow.AddMinutes(-1);
        Assert.Success(await local.Repository.FetchAsync(true), "fetch");
        DateTimeOffset? fetched = await local.Repository.GetLastFetchedAsync();
        Assert.True(fetched != null && fetched > before, "just fetched: " + fetched);
    }
}
