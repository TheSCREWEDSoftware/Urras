using Urras.Core.Git;

namespace Urras.Core.Tests;

public static class RepositoryTests
{
    public static async Task FindRootWorksFromSubfolder()
    {
        using TempRepository temp = await TempRepository.CreateAsync();
        temp.Write("sub/dir/file.txt", "x");

        string? root = await GitRepository.FindRootAsync(Path.Combine(temp.Path, "sub", "dir"));
        Assert.Equal(Path.GetFullPath(temp.Path), root, "root");

        string? notRepo = await GitRepository.FindRootAsync(Path.GetTempPath());
        Assert.True(notRepo == null || notRepo != temp.Path, "temp folder is not this repository");
    }

    public static async Task UnbornRepositoryStageUnstageAndFirstCommit()
    {
        using TempRepository temp = await TempRepository.CreateAsync();
        temp.Write("a file.txt", "hello\n");

        RepositoryStatus status = await temp.Repository.GetStatusAsync();
        Assert.True(status.IsUnborn, "unborn");
        Assert.Equal(1, status.Unstaged.Count, "untracked count");
        Assert.Equal(0, (await temp.Repository.GetHistoryAsync()).Count, "empty history");

        Assert.Success(await temp.Repository.StageAsync(new[] { "a file.txt" }), "stage");
        status = await temp.Repository.GetStatusAsync();
        Assert.Equal(1, status.Staged.Count, "staged after stage");

        Assert.Success(await temp.Repository.UnstageAsync(new[] { "a file.txt" }), "unstage before first commit");
        status = await temp.Repository.GetStatusAsync();
        Assert.Equal(0, status.Staged.Count, "nothing staged");

        await temp.CommitAllAsync("First commit");
        status = await temp.Repository.GetStatusAsync();
        Assert.True(!status.IsUnborn, "born");
        Assert.True(!status.HasChanges, "clean");
        Assert.Equal("main", status.Branch, "branch");
    }

    public static async Task DiffsForStagedUnstagedAndUntracked()
    {
        using TempRepository temp = await TempRepository.CreateAsync();
        temp.Write("file.txt", "one\n");
        await temp.CommitAllAsync("Initial");

        temp.Write("file.txt", "one\ntwo\n");
        temp.Write("new.txt", "brand new\n");
        RepositoryStatus status = await temp.Repository.GetStatusAsync();

        FileChange modified = status.Unstaged.First(change => change.Path == "file.txt");
        string diff = await temp.Repository.GetDiffAsync(modified, false);
        Assert.True(diff.Contains("+two"), "unstaged diff shows added line");

        FileChange untracked = status.Unstaged.First(change => change.Kind == ChangeKind.Untracked);
        string untrackedDiff = await temp.Repository.GetDiffAsync(untracked, false);
        Assert.True(untrackedDiff.Contains("+brand new"), "untracked diff shows content");

        Assert.Success(await temp.Repository.StageAsync(new[] { "file.txt" }), "stage");
        status = await temp.Repository.GetStatusAsync();
        string stagedDiff = await temp.Repository.GetDiffAsync(status.Staged[0], true);
        Assert.True(stagedDiff.Contains("+two"), "staged diff shows added line");
    }

    public static async Task UnstageAndDiscardAfterCommits()
    {
        using TempRepository temp = await TempRepository.CreateAsync();
        temp.Write("file.txt", "original\n");
        await temp.CommitAllAsync("Initial");

        temp.Write("file.txt", "changed\n");
        temp.Write("junk.txt", "junk\n");
        Assert.Success(await temp.Repository.StageAllAsync(), "stage all");
        Assert.Success(await temp.Repository.UnstageAllAsync(), "unstage all");

        RepositoryStatus status = await temp.Repository.GetStatusAsync();
        Assert.Equal(0, status.Staged.Count, "nothing staged");
        Assert.Equal(2, status.Unstaged.Count, "two unstaged");

        Assert.Success(await temp.Repository.DiscardAsync(status.Unstaged), "discard");
        status = await temp.Repository.GetStatusAsync();
        Assert.True(!status.HasChanges, "clean after discard");
        Assert.Equal("original\n", File.ReadAllText(Path.Combine(temp.Path, "file.txt")), "content restored");
        Assert.True(!File.Exists(Path.Combine(temp.Path, "junk.txt")), "untracked deleted");
    }

    public static async Task DeletionsAndRenamesStage()
    {
        using TempRepository temp = await TempRepository.CreateAsync();
        temp.Write("gone.txt", "bye\n");
        temp.Write("old.txt", "same content for rename detection\n");
        await temp.CommitAllAsync("Initial");

        File.Delete(Path.Combine(temp.Path, "gone.txt"));
        File.Move(Path.Combine(temp.Path, "old.txt"), Path.Combine(temp.Path, "renamed file.txt"));
        Assert.Success(await temp.Repository.StageAllAsync(), "stage all");

        RepositoryStatus status = await temp.Repository.GetStatusAsync();
        Assert.True(status.Staged.Any(change => change.Kind == ChangeKind.Deleted && change.Path == "gone.txt"),
                    "deletion staged");
        FileChange rename = status.Staged.First(change => change.Kind == ChangeKind.Renamed);
        Assert.Equal("old.txt", rename.OriginalPath, "rename original");
        Assert.Equal("renamed file.txt", rename.Path, "rename target");

        string diff = await temp.Repository.GetDiffAsync(rename, true);
        Assert.True(diff.Contains("rename from old.txt"), "rename diff");
    }

    public static async Task CommitMessageAmendAndHistory()
    {
        using TempRepository temp = await TempRepository.CreateAsync();
        temp.Write("file.txt", "1\n");
        await temp.CommitAllAsync("First");
        temp.Write("file.txt", "2\n");
        Assert.Success(await temp.Repository.StageAllAsync(), "stage");
        Assert.Success(await temp.Repository.CommitAsync("Second: with \"quotes\"\n\nBody line", false), "commit");

        List<CommitInfo> history = await temp.Repository.GetHistoryAsync();
        Assert.Equal(2, history.Count, "two commits");
        Assert.Equal("Second: with \"quotes\"", history[0].Subject, "subject");
        Assert.Equal("Second: with \"quotes\"\n\nBody line", await temp.Repository.GetLastCommitMessageAsync(),
                     "full message");

        Assert.Success(await temp.Repository.CommitAsync("Second, reworded", true), "amend");
        history = await temp.Repository.GetHistoryAsync();
        Assert.Equal(2, history.Count, "still two commits");
        Assert.Equal("Second, reworded", history[0].Subject, "amended subject");

        string show = await temp.Repository.GetCommitDiffAsync(history[0].Hash);
        Assert.True(show.Contains("Second, reworded") && show.Contains("+2"), "commit diff");
    }

    public static async Task BranchesCreateSwitchDelete()
    {
        using TempRepository temp = await TempRepository.CreateAsync();
        temp.Write("file.txt", "x\n");
        await temp.CommitAllAsync("Initial");

        Assert.Success(await temp.Repository.CreateBranchAsync("feature/test", true), "create and switch");
        RepositoryStatus status = await temp.Repository.GetStatusAsync();
        Assert.Equal("feature/test", status.Branch, "switched");

        List<BranchInfo> branches = await temp.Repository.GetBranchesAsync();
        Assert.Equal(2, branches.Count, "two branches");
        Assert.True(branches.Single(branch => branch.Name == "feature/test").IsCurrent, "feature is current");

        Assert.Success(await temp.Repository.SwitchAsync("main"), "switch back");
        Assert.Success(await temp.Repository.DeleteBranchAsync("feature/test", false), "delete");
        Assert.Equal(1, (await temp.Repository.GetBranchesAsync()).Count, "one branch left");
    }

    public static async Task PushPullFetchWithLocalRemote()
    {
        using TempRepository remote = await TempRepository.CreateAsync();
        Assert.Success(await GitRunner.RunAsync(remote.Path, "config", "receive.denyCurrentBranch", "updateInstead"),
                       "allow push to checked out branch");
        remote.Write("readme.txt", "remote\n");
        await remote.CommitAllAsync("Remote initial");

        using TempRepository local = await TempRepository.CreateAsync();
        Assert.Success(await GitRunner.RunAsync(local.Path, "remote", "add", "origin", remote.Path), "add remote");
        Assert.Success(await local.Repository.FetchAsync(), "fetch");

        List<BranchInfo> branches = await local.Repository.GetBranchesAsync();
        Assert.True(branches.Any(branch => branch.IsRemote && branch.Name == "origin/main"), "remote branch fetched");

        Assert.Success(await GitRunner.RunAsync(local.Path, "config", "user.name", "Local"), "name");
        Assert.Success(await local.Repository.SwitchToRemoteAsync("origin/main"), "track remote branch");
        RepositoryStatus status = await local.Repository.GetStatusAsync();
        Assert.Equal("origin/main", status.Upstream, "upstream set");

        local.Write("local.txt", "from local\n");
        await local.CommitAllAsync("Local change");
        status = await local.Repository.GetStatusAsync();
        Assert.Equal(1, status.Ahead, "ahead by one");

        Assert.Success(await local.Repository.PushAsync(), "push");
        Assert.True(File.Exists(Path.Combine(remote.Path, "local.txt")), "remote received the file");

        remote.Write("remote2.txt", "more\n");
        await remote.CommitAllAsync("Remote second");
        Assert.Success(await local.Repository.PullAsync(), "pull");
        Assert.True(File.Exists(Path.Combine(local.Path, "remote2.txt")), "local received the file");

        Assert.Success(await local.Repository.CreateBranchAsync("new-branch", true), "new branch");
        local.Write("branch.txt", "b\n");
        await local.CommitAllAsync("Branch work");
        Assert.Success(await local.Repository.PushAsync(), "push sets upstream");
        status = await local.Repository.GetStatusAsync();
        Assert.Equal("origin/new-branch", status.Upstream, "upstream after first push");
    }

    public static async Task PushWithoutRemoteFailsClearly()
    {
        using TempRepository temp = await TempRepository.CreateAsync();
        temp.Write("file.txt", "x\n");
        await temp.CommitAllAsync("Initial");

        GitResult result = await temp.Repository.PushAsync();
        Assert.True(!result.Success && result.Message.Contains("no remote"), "clear error");
    }
}
