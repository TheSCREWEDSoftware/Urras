using Urras.Core.Git;

namespace Urras.Core.Tests;

public static class RewriteTests
{
    // A clone whose last commit was pushed, then rewritten or added to on either side
    private static async Task<(TempRepository Online, TempRepository Local)> PushedAsync()
    {
        TempRepository online = await TempRepository.CreateAsync();
        online.Write("a.txt", "a");
        await online.CommitAllAsync("First");
        TempRepository local = await TempRepository.CreateAsync();
        Assert.Success(await GitRunner.RunAsync(local.Path, "remote", "add", "origin", online.Path), "remote");
        Assert.Success(await GitRunner.RunAsync(local.Path, "fetch", "origin"), "fetch");
        Assert.Success(await GitRunner.RunAsync(local.Path, "reset", "--hard", "origin/main"), "take it");
        Assert.Success(await GitRunner.RunAsync(local.Path, "branch", "-u", "origin/main"), "track it");
        return (online, local);
    }

    public static async Task SpotsABranchRewrittenAfterPushing()
    {
        (TempRepository online, TempRepository local) = await PushedAsync();
        using (online)
        using (local)
        {
            Assert.True(!await local.Repository.IsRewriteOfUpstreamAsync(), "up to date");

            // The same change with a fixed author email, as when commits are corrected after pushing
            Assert.Success(await GitRunner.RunAsync(local.Path, "-c", "user.email=fixed@example.com", "commit",
                                                    "--amend", "--no-edit", "--reset-author"), "rewrite");
            RepositoryStatus status = await local.Repository.GetStatusAsync();
            Assert.True(status.Ahead == 1 && status.Behind == 1, "diverged");
            Assert.True(await local.Repository.IsRewriteOfUpstreamAsync(), "only rewritten");
            Assert.Equal(SyncKind.ForcePush, SyncState.For(status, null, DateTimeOffset.Now, true, true).Kind,
                         "offers to replace the remote");
            Assert.Equal(SyncKind.Pull, SyncState.For(status, null, DateTimeOffset.Now).Kind, "otherwise pull");

            // Replacing the remote's copy leaves the branch in step, with nothing to pull back
            Assert.Success(await GitRunner.RunAsync(online.Path, "config", "receive.denyCurrentBranch", "ignore"),
                           "accept pushes");
            Assert.Success(await local.Repository.PushAsync(force: true), "force push");
            RepositoryStatus after = await local.Repository.GetStatusAsync();
            Assert.True(after.Ahead == 0 && after.Behind == 0, "in step");
        }
    }

    public static async Task DoesntOfferToLoseSomeoneElsesWork()
    {
        (TempRepository online, TempRepository local) = await PushedAsync();
        using (online)
        using (local)
        {
            // Someone pushed new work, and this branch has its own commit: that's a normal pull
            online.Write("b.txt", "b");
            await online.CommitAllAsync("Theirs");
            local.Write("c.txt", "c");
            await local.CommitAllAsync("Mine");
            Assert.Success(await GitRunner.RunAsync(local.Path, "fetch", "origin"), "fetch");
            Assert.True(!await local.Repository.IsRewriteOfUpstreamAsync(), "their commit has no copy here");
        }
    }
}
