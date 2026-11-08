using Urras.Core.Git;

namespace Urras.Core.Tests;

public static class BisectTests
{
    public static async Task FindsTheFirstBadCommit()
    {
        using TempRepository temp = await TempRepository.CreateAsync();
        List<string> hashes = new List<string>();
        for (int i = 1; i <= 8; ++i)
        {
            // The bug arrives in commit 6
            temp.Write("value.txt", (i >= 6 ? "broken " : "fine ") + i + "\n");
            await temp.CommitAllAsync("Commit " + i);
            hashes.Add((await GitRunner.RunAsync(temp.Path, "rev-parse", "HEAD")).Output.Trim());
        }

        Assert.True(!(await temp.Repository.GetBisectStateAsync()).IsActive, "not bisecting yet");
        Assert.Success(await temp.Repository.BisectStartAsync(hashes[7], hashes[0]), "start");

        BisectState state = await temp.Repository.GetBisectStateAsync();
        Assert.True(state.IsActive && state.FirstBad == null && state.Remaining > 0, "testing commits");

        for (int step = 0; step < 10 && state.FirstBad == null; ++step)
        {
            bool broken = File.ReadAllText(Path.Combine(temp.Path, "value.txt")).StartsWith("broken");
            Assert.Success(await temp.Repository.BisectMarkAsync(broken ? BisectMark.Bad : BisectMark.Good), "mark");
            state = await temp.Repository.GetBisectStateAsync();
        }

        Assert.Equal(hashes[5], state.FirstBad, "commit 6 is the first bad one");
        Assert.Success(await temp.Repository.BisectResetAsync(), "reset");
        Assert.True(!(await temp.Repository.GetBisectStateAsync()).IsActive, "finished");
        RepositoryStatus status = await temp.Repository.GetStatusAsync();
        Assert.Equal("main", status.Branch, "back on the branch");
    }
}
