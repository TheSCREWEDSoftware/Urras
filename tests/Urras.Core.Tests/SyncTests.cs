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

    public static void ReadsGitHubsPushRefusal()
    {
        PushRefusal? refusal = PushRefusal.Parse(
            "remote: Permission to cnc-exoduscommunity/cnc-exoduscommunity.github.io.git denied to " +
            "TheSCREWEDSoftware.\nfatal: unable to access 'https://github.com/...': The requested URL returned " +
            "error: 403");
        Assert.True(refusal != null, "recognised");
        Assert.Equal("cnc-exoduscommunity/cnc-exoduscommunity.github.io", refusal!.Repository, "without .git");
        Assert.Equal("TheSCREWEDSoftware", refusal.User, "without the full stop");
        Assert.Equal("cnc-exoduscommunity", refusal.Owner, "the organisation");
        Assert.Equal(null, PushRefusal.Parse("fatal: could not read Username"), "other errors are left alone");
    }

    public static void KeepsReadingCommandsOutOfTheLog()
    {
        Assert.True(GitCommandKind.IsReading("git --no-optional-locks status --porcelain=v2 --branch -z"),
                    "options before the command are skipped");
        Assert.True(GitCommandKind.IsReading("git lfs ls-files --long"), "listing LFS files reads");
        Assert.True(GitCommandKind.IsReading("git lfs track"), "listing LFS patterns reads");
        Assert.True(GitCommandKind.IsReading("git -C folder worktree list --porcelain"), "values of options skipped");
        Assert.True(!GitCommandKind.IsReading("git lfs track -- *.psd"), "tracking a pattern changes things");
        Assert.True(!GitCommandKind.IsReading("git push origin main"), "pushing is shown");
        Assert.True(!GitCommandKind.IsReading("git stash pop --index stash@{0}"), "popping a stash is shown");
        Assert.True(!GitCommandKind.IsReading("git"), "nothing to go on");
    }

    public static async Task AddsSettingsToEveryCommand()
    {
        using TempRepository repository = await TempRepository.CreateAsync();
        IReadOnlyList<string> before = GitRunner.ExtraSettings;
        try
        {
            GitRunner.ExtraSettings = new[] { "urras.test=yes" };
            GitResult result = await GitRunner.RunAsync(repository.Path, "config", "--get", "urras.test");
            Assert.Equal("yes", result.Output.Trim(), "passed to Git as -c");
        }
        finally
        {
            GitRunner.ExtraSettings = before;
        }
    }

    public static async Task LeavesAConfiguredCredentialHelperAlone()
    {
        using TempRepository repository = await TempRepository.CreateAsync();
        Assert.Success(await GitRunner.RunAsync(repository.Path, "config", "credential.helper", "store"), "helper");
        IReadOnlyList<string> before = GitRunner.ExtraSettings;
        try
        {
            Assert.True(!await CredentialSetup.UseManagerIfNeededAsync(repository.Path), "the user's helper wins");
            Assert.True(!GitRunner.ExtraSettings.Contains(CredentialSetup.MANAGER_SETTING), "nothing added");
        }
        finally
        {
            GitRunner.ExtraSettings = before;
        }
    }
}
