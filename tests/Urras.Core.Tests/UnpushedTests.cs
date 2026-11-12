using Urras.Core.Accounts;
using Urras.Core.Git;

namespace Urras.Core.Tests;

public static class UnpushedTests
{
    public static async Task FindsTheCommitsAPushWouldSend()
    {
        using TempRepository online = await TempRepository.CreateAsync();
        Assert.Success(await GitRunner.RunAsync(online.Path, "config", "receive.denyCurrentBranch", "ignore"), "push");
        using TempRepository local = await TempRepository.CreateAsync();
        local.Write("a.txt", "a");
        await local.CommitAllAsync("First");
        Assert.Equal(1, (await local.Repository.GetUnpushedAsync()).Count, "no remote branch has it yet");

        PlatformRepository target = new PlatformRepository("ryan/game", online.Path, null, true, false);
        Assert.Success(await RepositoryPublisher.ConnectAsync(local.Repository, target), "publish");
        Assert.Equal(0, (await local.Repository.GetUnpushedAsync()).Count, "pushed");

        local.Write("b.txt", "b");
        await local.CommitAllAsync("Second");
        HashSet<string> unpushed = await local.Repository.GetUnpushedAsync();
        List<CommitInfo> history = await local.Repository.GetHistoryAsync();
        Assert.Equal(1, unpushed.Count, "only the new one");
        Assert.True(unpushed.Contains(history[0].Hash), "the newest commit");
    }

    public static void SaysLocalOnlyWithoutARemote()
    {
        CommitInfo commit = new CommitInfo("abc", "abc", "Ryan", "ryan@example.com", DateTimeOffset.Now, "First")
        {
            IsUnpushed = true
        };
        Assert.Equal("Not pushed", commit.UnpushedText, "a remote exists");
        commit.IsLocalOnly = true;
        Assert.Equal("Local only", commit.UnpushedText, "no remote at all");
        Assert.True(commit.UnpushedTip.Contains("Publish repository"), "says how to put it online");
    }
}
