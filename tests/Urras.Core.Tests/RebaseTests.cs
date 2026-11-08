using Urras.Core.Git;

namespace Urras.Core.Tests;

public static class RebaseTests
{
    private static async Task<(TempRepository Temp, string Base)> FourCommitsAsync()
    {
        TempRepository temp = await TempRepository.CreateAsync();
        temp.Write("base.txt", "base\n");
        await temp.CommitAllAsync("Base");
        string baseHash = (await GitRunner.RunAsync(temp.Path, "rev-parse", "HEAD")).Output.Trim();
        foreach (string name in new[] { "one", "two", "three", "four" })
        {
            temp.Write(name + ".txt", name + "\n");
            await temp.CommitAllAsync("Add " + name);
        }

        return (temp, baseHash);
    }

    private static async Task<List<string>> Subjects(TempRepository temp)
    {
        return (await temp.Repository.GetHistoryAsync()).Select(commit => commit.Subject).ToList();
    }

    public static async Task ReordersRewordsSquashesAndDrops()
    {
        (TempRepository temp, string baseHash) = await FourCommitsAsync();
        using TempRepository _ = temp;

        List<RebaseStep> steps = (await temp.Repository.GetRebaseCommitsAsync(baseHash))
            .Select(commit => new RebaseStep(commit)).ToList();
        Assert.Equal("Add one", steps[0].Commit.Subject, "oldest first");

        // four first, then one reworded, two squashed into it with a new message, three dropped
        RebaseStep four = steps[3];
        steps.RemoveAt(3);
        steps.Insert(0, four);
        steps[1].Action = RebaseAction.Reword;
        steps[1].Message = "Add one, reworded";
        steps[2].Action = RebaseAction.Squash;
        steps[2].Message = "Add one and two\n\nSquashed by Urras";
        steps[3].Action = RebaseAction.Drop;

        string tag = GitRepository.BackupTagName("main", new DateTime(2026, 11, 8, 10, 35, 0));
        Assert.Equal("urras-backup/main-20261108-103500", tag, "backup tag name");
        Assert.Success(await temp.Repository.InteractiveRebaseAsync(baseHash, steps, tag), "rebase");

        Assert.Equal("Add one and two|Add four|Base", string.Join("|", await Subjects(temp)), "new history");
        string body = (await GitRunner.RunAsync(temp.Path, "log", "-1", "--format=%B")).Output.Trim();
        Assert.Equal("Add one and two\n\nSquashed by Urras", body.Replace("\r", ""), "squash message");
        Assert.True(!File.Exists(Path.Combine(temp.Path, "three.txt")), "dropped commit's file is gone");
        Assert.True(File.Exists(Path.Combine(temp.Path, "two.txt")), "squashed commit's file stays");

        GitResult backup = await GitRunner.RunAsync(temp.Path, "log", "-1", "--format=%s", tag);
        Assert.Equal("Add four", backup.Output.Trim(), "backup tag points at the old tip");
    }

    public static async Task FixupKeepsTheFirstMessageAndRewordAlone()
    {
        (TempRepository temp, string baseHash) = await FourCommitsAsync();
        using TempRepository _ = temp;
        List<RebaseStep> steps = (await temp.Repository.GetRebaseCommitsAsync(baseHash))
            .Select(commit => new RebaseStep(commit)).ToList();
        steps[1].Action = RebaseAction.Fixup;
        steps[3].Action = RebaseAction.Reword;
        steps[3].Message = "Add \"four\" with quotes";

        Assert.Success(await temp.Repository.InteractiveRebaseAsync(baseHash, steps, null), "rebase");
        Assert.Equal("Add \"four\" with quotes|Add three|Add one|Base", string.Join("|", await Subjects(temp)),
                     "fixup folded into one, four reworded");
    }

    public static async Task RejectsImpossiblePlans()
    {
        (TempRepository temp, string baseHash) = await FourCommitsAsync();
        using TempRepository _ = temp;
        List<RebaseStep> steps = (await temp.Repository.GetRebaseCommitsAsync(baseHash))
            .Select(commit => new RebaseStep(commit)).ToList();

        steps[0].Action = RebaseAction.Squash;
        Assert.True(RebaseTodo.Validate(steps)!.Contains("first kept"), "squash with nothing before it");
        steps[0].Action = RebaseAction.Drop;
        steps[1].Action = RebaseAction.Fixup;
        Assert.True(RebaseTodo.Validate(steps) != null, "still the first kept commit");
        steps.ForEach(step => step.Action = RebaseAction.Drop);
        Assert.True(RebaseTodo.Validate(steps)!.Contains("dropped"), "dropping everything");
        steps.ForEach(step => step.Action = RebaseAction.Pick);
        steps[2].Action = RebaseAction.Reword;
        Assert.True(!(await temp.Repository.InteractiveRebaseAsync(baseHash, steps, null)).Success,
                    "reword without a message is refused");
        Assert.True(!await temp.Repository.HasMergesSinceAsync(baseHash), "no merges");
    }

    public static async Task StopsOnConflictsForTheBanner()
    {
        using TempRepository temp = await TempRepository.CreateAsync();
        temp.Write("f.txt", "a\n");
        await temp.CommitAllAsync("Base");
        string baseHash = (await GitRunner.RunAsync(temp.Path, "rev-parse", "HEAD")).Output.Trim();
        temp.Write("f.txt", "b\n");
        await temp.CommitAllAsync("B");
        temp.Write("f.txt", "c\n");
        await temp.CommitAllAsync("C");

        List<RebaseStep> steps = (await temp.Repository.GetRebaseCommitsAsync(baseHash))
            .Select(commit => new RebaseStep(commit)).ToList();
        steps.Reverse();
        Assert.True(!(await temp.Repository.InteractiveRebaseAsync(baseHash, steps, null)).Success,
                    "swapping two edits of one line conflicts");
        Assert.Equal(OperationState.Rebase, await temp.Repository.GetOperationStateAsync(), "rebase in progress");
        Assert.Success(await temp.Repository.AbortAsync(OperationState.Rebase), "abort");
        Assert.Equal("C|B|Base", string.Join("|", await Subjects(temp)), "history unchanged after abort");
    }
}
