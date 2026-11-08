using Urras.Core.Git;

namespace Urras.Core.Tests;

public static class ActionsTests
{
    private static async Task<TempRepository> RepositoryWithCommitsAsync()
    {
        TempRepository temp = await TempRepository.CreateAsync();
        temp.Write("file.txt", "one\n");
        await temp.CommitAllAsync("First");
        temp.Write("file.txt", "one\ntwo\n");
        await temp.CommitAllAsync("Second");
        return temp;
    }

    public static async Task TagsCreateListDelete()
    {
        using TempRepository temp = await RepositoryWithCommitsAsync();
        List<CommitInfo> history = await temp.Repository.GetHistoryAsync();

        Assert.Success(await temp.Repository.CreateTagAsync("v1.0", history[1].Hash, null), "lightweight tag");
        Assert.Success(await temp.Repository.CreateTagAsync("v2.0", history[0].Hash, "Release two"), "annotated tag");

        List<TagInfo> tags = await temp.Repository.GetTagsAsync();
        Assert.Equal(history[1].Hash, tags.Single(tag => tag.Name == "v1.0").Commit, "lightweight tag commit");
        TagInfo annotated = tags.Single(tag => tag.Name == "v2.0");
        Assert.Equal(history[0].Hash, annotated.Commit, "annotated tag points at the commit, not the tag object");
        Assert.Equal("Release two", annotated.Subject, "tag message");

        List<CommitInfo> decorated = await temp.Repository.GetHistoryAsync();
        Assert.True(decorated[0].Refs.Contains("tag: v2.0") && decorated[0].Refs.Contains("main"), "refs in history");

        Assert.Success(await temp.Repository.DeleteTagAsync("v1.0"), "delete tag");
        Assert.Equal(1, (await temp.Repository.GetTagsAsync()).Count, "one tag left");
        Assert.True(!(await temp.Repository.PushTagAsync("v2.0")).Success, "push without a remote fails");
    }

    public static async Task TagsPushToRemote()
    {
        using TempRepository remote = await TempRepository.CreateAsync();
        using TempRepository local = await RepositoryWithCommitsAsync();
        Assert.Success(await GitRunner.RunAsync(local.Path, "remote", "add", "origin", remote.Path), "remote");
        List<CommitInfo> history = await local.Repository.GetHistoryAsync();
        Assert.Success(await local.Repository.CreateTagAsync("v1", history[0].Hash, null), "tag");

        Assert.Success(await local.Repository.PushTagAsync("v1"), "push tag");
        Assert.Equal("v1", (await remote.Repository.GetTagsAsync()).Single().Name, "remote has the tag");
        Assert.Success(await local.Repository.DeleteRemoteTagAsync("v1"), "delete remote tag");
        Assert.Equal(0, (await remote.Repository.GetTagsAsync()).Count, "remote tag gone");
        Assert.Success(await local.Repository.PushAllTagsAsync(), "push all tags");
    }

    public static async Task CheckoutRevertAndCherryPick()
    {
        using TempRepository temp = await RepositoryWithCommitsAsync();
        List<CommitInfo> history = await temp.Repository.GetHistoryAsync();

        Assert.Success(await temp.Repository.RevertAsync(history[0].Hash), "revert");
        Assert.Equal("one\n", File.ReadAllText(Path.Combine(temp.Path, "file.txt")), "change undone");
        Assert.Equal(3, (await temp.Repository.GetHistoryAsync()).Count, "revert is a new commit");

        Assert.Success(await temp.Repository.CreateBranchAsync("side", true), "branch");
        temp.Write("side.txt", "side\n");
        await temp.CommitAllAsync("Side work");
        string sideCommit = (await temp.Repository.GetHistoryAsync())[0].Hash;
        Assert.Success(await temp.Repository.SwitchAsync("main"), "back to main");
        Assert.Success(await temp.Repository.CherryPickAsync(sideCommit), "cherry-pick");
        Assert.True(File.Exists(Path.Combine(temp.Path, "side.txt")), "picked change applied");

        Assert.Success(await temp.Repository.CheckoutCommitAsync(history[1].Hash), "checkout commit");
        Assert.True((await temp.Repository.GetStatusAsync()).IsDetached, "detached HEAD");
    }

    public static async Task StashesSaveListShowApplyPopDrop()
    {
        using TempRepository temp = await RepositoryWithCommitsAsync();
        temp.Write("file.txt", "changed\n");
        temp.Write("new.txt", "untracked\n");

        Assert.Success(await temp.Repository.StashAsync("Work in progress", true), "stash with untracked");
        Assert.True(!(await temp.Repository.GetStatusAsync()).HasChanges, "clean after stash");

        List<StashInfo> stashes = await temp.Repository.GetStashesAsync();
        Assert.Equal("stash@{0}", stashes.Single().Reference, "reference");
        Assert.True(stashes[0].Message.Contains("Work in progress"), "message");
        string diff = await temp.Repository.GetStashDiffAsync("stash@{0}");
        Assert.True(diff.Contains("+changed"), "stash diff");

        Assert.Success(await temp.Repository.StashApplyAsync("stash@{0}"), "apply keeps the stash");
        Assert.Equal(1, (await temp.Repository.GetStashesAsync()).Count, "still stashed");
        Assert.Success(await temp.Repository.StashAsync(null, true), "stash again");
        Assert.Success(await temp.Repository.StashDropAsync("stash@{1}"), "drop the older one");
        Assert.Success(await temp.Repository.StashPopAsync("stash@{0}"), "pop");
        Assert.Equal(0, (await temp.Repository.GetStashesAsync()).Count, "nothing stashed");
        Assert.True(File.Exists(Path.Combine(temp.Path, "new.txt")), "untracked file back");
    }

    private static async Task<TempRepository> ConflictingBranchesAsync()
    {
        TempRepository temp = await TempRepository.CreateAsync();
        temp.Write("file.txt", "base\n");
        await temp.CommitAllAsync("Base");
        Assert.Success(await temp.Repository.CreateBranchAsync("feature", true), "feature branch");
        temp.Write("file.txt", "feature\n");
        await temp.CommitAllAsync("Feature change");
        Assert.Success(await temp.Repository.SwitchAsync("main"), "main");
        temp.Write("file.txt", "main\n");
        await temp.CommitAllAsync("Main change");
        return temp;
    }

    public static async Task MergeConflictResolveWithTheirsAndContinue()
    {
        using TempRepository temp = await ConflictingBranchesAsync();
        Assert.True(!(await temp.Repository.MergeAsync("feature")).Success, "merge stops on the conflict");
        Assert.Equal(OperationState.Merge, await temp.Repository.GetOperationStateAsync(), "merge in progress");
        Assert.Equal(ChangeKind.Conflicted, (await temp.Repository.GetStatusAsync()).Unstaged.Single().Kind,
                     "conflicted file");

        Assert.Success(await temp.Repository.ResolveWithSideAsync(new[] { "file.txt" }, false), "take theirs");
        Assert.Equal("feature\n", File.ReadAllText(Path.Combine(temp.Path, "file.txt")), "incoming version");
        Assert.Success(await temp.Repository.ContinueAsync(OperationState.Merge), "finish the merge");
        Assert.Equal(OperationState.None, await temp.Repository.GetOperationStateAsync(), "done");
    }

    public static async Task MergeAbortAndCleanMerge()
    {
        using TempRepository temp = await ConflictingBranchesAsync();
        Assert.True(!(await temp.Repository.MergeAsync("feature")).Success, "conflict");
        Assert.Success(await temp.Repository.AbortAsync(OperationState.Merge), "abort");
        Assert.Equal("main\n", File.ReadAllText(Path.Combine(temp.Path, "file.txt")), "back to before");

        Assert.Success(await temp.Repository.CreateBranchAsync("clean", true), "clean branch");
        temp.Write("other.txt", "x\n");
        await temp.CommitAllAsync("Other");
        Assert.Success(await temp.Repository.SwitchAsync("main"), "main");
        Assert.Success(await temp.Repository.MergeAsync("clean"), "clean merge");
    }

    public static async Task RebaseConflictResolveAndContinue()
    {
        using TempRepository temp = await ConflictingBranchesAsync();
        Assert.Success(await temp.Repository.SwitchAsync("feature"), "feature");
        Assert.True(!(await temp.Repository.RebaseAsync("main")).Success, "rebase stops on the conflict");
        Assert.Equal(OperationState.Rebase, await temp.Repository.GetOperationStateAsync(), "rebase in progress");

        temp.Write("file.txt", "both\n");
        Assert.Success(await temp.Repository.MarkResolvedAsync(new[] { "file.txt" }), "mark resolved");
        Assert.Success(await temp.Repository.ContinueAsync(OperationState.Rebase), "continue without an editor");
        Assert.Equal(OperationState.None, await temp.Repository.GetOperationStateAsync(), "rebase done");
        Assert.Equal(3, (await temp.Repository.GetHistoryAsync()).Count, "feature commit replayed on main");
    }

    public static async Task CherryPickConflictAbort()
    {
        using TempRepository temp = await ConflictingBranchesAsync();
        string featureCommit = (await GitRunner.RunAsync(temp.Path, "rev-parse", "feature")).Output.Trim();
        Assert.True(!(await temp.Repository.CherryPickAsync(featureCommit)).Success, "conflict");
        Assert.Equal(OperationState.CherryPick, await temp.Repository.GetOperationStateAsync(), "cherry-pick state");
        Assert.Success(await temp.Repository.ResolveWithSideAsync(new[] { "file.txt" }, true), "take ours");
        Assert.Success(await temp.Repository.AbortAsync(OperationState.CherryPick), "abort");
        Assert.Equal(OperationState.None, await temp.Repository.GetOperationStateAsync(), "no operation");
    }

    public static async Task WhitespaceCheckAndNormalize()
    {
        using TempRepository temp = await TempRepository.CreateAsync();
        Assert.Success(await GitRunner.RunAsync(temp.Path, "config", "core.autocrlf", "false"), "autocrlf off");
        temp.Write("clean.txt", "fine\n");
        temp.Write("windows.txt", "a\r\nb\r\n");
        await temp.CommitAllAsync("Initial");

        temp.Write("clean.txt", "trailing   \n");
        Assert.Success(await temp.Repository.StageAllAsync(), "stage");
        List<string> problems = await temp.Repository.CheckStagedWhitespaceAsync();
        Assert.True(problems.Any(problem => problem.Contains("trailing whitespace")), "trailing space found");

        await LineEndingRules.WriteAsync(temp.Path, LineEndingPolicy.Lf, true);
        List<string> preview = await temp.Repository.PreviewNormalizeAsync();
        Assert.True(preview.Contains("windows.txt"), "CRLF file would be normalized");
        Assert.Success(await temp.Repository.NormalizeAsync(), "normalize");
        string staged = (await GitRunner.RunAsync(temp.Path, "show", ":windows.txt")).Output;
        Assert.Equal("a\nb\n", staged, "stored with LF");
    }

    public static void LineEndingRulesDetectAndWrite()
    {
        string theirs = "* text=auto eol=lf\n\n# Windows batch scripts need CRLF to run\n*.bat text eol=crlf\n" +
                        "*.cmd text eol=crlf\n\n*.cpp whitespace=trailing-space\n";
        Assert.Equal(LineEndingPolicy.LfWithWindowsScripts, LineEndingRules.Detect(theirs), "detect mixed");
        Assert.Equal(LineEndingPolicy.Custom, LineEndingRules.Detect(null), "no file");
        Assert.Equal(LineEndingPolicy.Auto, LineEndingRules.Detect("* text=auto\n"), "auto");

        string lf = LineEndingRules.ApplyToGitAttributes(theirs, LineEndingPolicy.Lf);
        Assert.Equal("# Line endings\n* text=auto eol=lf\n\n*.cpp whitespace=trailing-space\n", lf,
                     "rules replaced, other lines kept");
        Assert.Equal(LineEndingPolicy.Crlf,
                     LineEndingRules.Detect(LineEndingRules.ApplyToGitAttributes(lf, LineEndingPolicy.Crlf)),
                     "switch to CRLF");

        string config = "root = true\n\n[*]\ncharset = utf-8\nend_of_line = crlf\n\n" +
                        "[*.{bat,cmd}]\nend_of_line = crlf\n";
        string lfConfig = LineEndingRules.ApplyToEditorConfig(config, LineEndingPolicy.Lf);
        Assert.True(lfConfig.Contains("[*]\ncharset = utf-8\nend_of_line = lf"), "[*] set to LF");
        Assert.True(!lfConfig.Contains("end_of_line = crlf"), "script exception removed");

        string created = LineEndingRules.ApplyToEditorConfig(null, LineEndingPolicy.LfWithWindowsScripts);
        EditorConfig parsed = EditorConfig.Parse(created);
        Assert.Equal(LineEnding.Lf, parsed.EndOfLine("src/a.cs"), "new file LF");
        Assert.Equal(LineEnding.Crlf, parsed.EndOfLine("build.bat"), "new file scripts CRLF");
    }
}
