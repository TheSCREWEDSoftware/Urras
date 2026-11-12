using Urras.Core.Git;

namespace Urras.Core.Tests;

public static class RemoteHealthTests
{
    private const string NOT_FOUND = "remote: Repository not found.\nfatal: repository " +
                                     "'https://github.com/TheSCREWEDSoftware/Y_Anarres.git/' not found";

    public static void SpotsADeletedRemoteRepository()
    {
        Assert.True(MissingRemote.IsMissing(NOT_FOUND), "GitHub's answer");
        Assert.Equal("https://github.com/TheSCREWEDSoftware/Y_Anarres.git", MissingRemote.UrlIn(NOT_FOUND), "url");
        Assert.True(!MissingRemote.IsMissing("fatal: Authentication failed"), "a different problem");

        Assert.Equal(("TheSCREWEDSoftware", "Y_Anarres"),
                     MissingRemote.OwnerAndName("https://github.com/TheSCREWEDSoftware/Y_Anarres.git"), "https");
        Assert.Equal(("team/games", "game"), MissingRemote.OwnerAndName("git@gitlab.com:team/games/game.git"),
                     "SSH with a GitLab subgroup");
        Assert.True(MissingRemote.OwnerAndName("C:/repos/local") is { Name: "local" }, "a folder still parses");

        RepositoryStatus status = new RepositoryStatus { Branch = "main", Upstream = "origin/main" };
        SyncState again = SyncState.For(status, null, DateTimeOffset.Now, true, false, true);
        Assert.Equal(SyncKind.PublishRepository, again.Kind, "offers to make it again");
        Assert.Equal("Publish repository again", again.Title, "says again");
    }

    public static async Task PublishingAgainPointsOriginAtTheNewRepository()
    {
        using TempRepository online = await TempRepository.CreateAsync();
        Assert.Success(await GitRunner.RunAsync(online.Path, "config", "receive.denyCurrentBranch", "ignore"), "push");
        using TempRepository local = await TempRepository.CreateAsync();
        local.Write("a.txt", "a");
        await local.CommitAllAsync("First");
        Assert.Success(await local.Repository.AddRemoteAsync("origin", "https://example.invalid/gone.git"), "old");

        Urras.Core.Accounts.PlatformRepository made = new("ryan/game", online.Path, null, true, false);
        Assert.Success(await RepositoryPublisher.ConnectAsync(local.Repository, made), "publish again");
        Assert.Equal(online.Path, await local.Repository.GetRemoteUrlAsync("origin"), "origin replaced");
        Assert.Equal("origin/main", (await local.Repository.GetStatusAsync()).Upstream, "pushed");
    }

    public static async Task TellsAGoneOriginFromAGoneFork()
    {
        using TempRepository local = await TempRepository.CreateAsync();
        Assert.Success(await local.Repository.AddRemoteAsync("origin", "https://github.com/ryan/game.git"), "origin");
        Assert.Success(await local.Repository.AddRemoteAsync("fork", "https://github.com/friend/game"), "fork");
        Assert.True(MissingRemote.SameAddress("https://github.com/Ryan/game/", "https://github.com/ryan/game.git"),
                    "same repository");

        string forkGone = "remote: Repository not found.\nfatal: repository 'https://github.com/friend/game/' " +
                          "not found";
        Assert.Equal("fork", await local.Repository.RemoteNamedInAsync(forkGone), "only the fork is gone");
        string originGone = "fatal: repository 'https://github.com/ryan/game.git/' not found";
        Assert.Equal("origin", await local.Repository.RemoteNamedInAsync(originGone), "origin is gone");
    }

    public static void ClearsOnlyLocksLeftOverByACrash()
    {
        string folder = Path.Combine(Path.GetTempPath(), "urras-lock-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(folder, ".git", "refs", "remotes"));
        string refLock = Path.Combine(folder, ".git", "refs", "remotes", "fork.lock");
        string indexLock = Path.Combine(folder, ".git", "index.lock");
        try
        {
            File.WriteAllText(refLock, string.Empty);
            File.WriteAllText(indexLock, string.Empty);
            string message = "error: cannot lock ref: Unable to create '" + refLock.Replace('\\', '/') +
                             "': File exists.";
            Assert.Equal(refLock, StaleLock.PathIn(message, folder), "the lock named by Git");

            Assert.True(!StaleLock.TryClear(message, folder), "too new: a Git may still be using it");
            Assert.True(StaleLock.TryClear(message, folder, DateTime.UtcNow + StaleLock.STALE_AGE), "left over");
            Assert.True(!File.Exists(refLock), "removed");

            string index = "fatal: Unable to create '" + indexLock + "': File exists.";
            Assert.True(!StaleLock.TryClear(index, folder, DateTime.UtcNow + StaleLock.STALE_AGE), "index.lock");
            Assert.True(File.Exists(indexLock), "a commit editor may hold it");
        }
        finally
        {
            TestFolders.Delete(folder);
        }
    }
}
