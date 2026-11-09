using Urras.Core.Git;

namespace Urras.Core.Tests;

public static class ModuleTests
{
    public static void ParsesSubmoduleAndWorktreeLists()
    {
        List<SubmoduleInfo> modules = GitParsers.ParseSubmodules(
            "-1111111 libs/raylib\n 2222222 libs/doctest (v2.5.3)\n+3333333 libs/my lib (heads/main)\nUabc x\n");
        Assert.Equal(4, modules.Count, "four submodules");
        Assert.Equal(SubmoduleState.NotInitialized, modules[0].State, "not checked out");
        Assert.Equal("v2.5.3", modules[1].Description, "description");
        Assert.Equal("libs/my lib", modules[2].Path, "path with a space");
        Assert.Equal(SubmoduleState.DifferentCommit, modules[2].State, "moved");
        Assert.Equal(SubmoduleState.Conflicted, modules[3].State, "conflicted");
        Assert.True(modules[0].ToString().Contains("not checked out"), "readable text");

        List<WorktreeInfo> worktrees = GitParsers.ParseWorktrees(
            "worktree /repo\nHEAD aaaaaaa1\nbranch refs/heads/main\n\n" +
            "worktree /repo-feature\nHEAD bbbbbbb2\nbranch refs/heads/feature/x\nlocked\n\n" +
            "worktree /gone\nHEAD ccccccc3\ndetached\nprunable gitdir file points to non-existent location\n");
        Assert.Equal(3, worktrees.Count, "three worktrees");
        Assert.True(worktrees[0].IsMain && !worktrees[1].IsMain, "first is the main one");
        Assert.Equal("feature/x", worktrees[1].Branch, "branch with a slash");
        Assert.True(worktrees[1].IsLocked && worktrees[2].IsPrunable, "locked and missing");
        Assert.True(worktrees[2].ToString().Contains("detached at ccccccc"), "detached text");
    }

    public static async Task AddsListsAndRemovesWorktrees()
    {
        using TempRepository temp = await TempRepository.CreateAsync();
        temp.Write("a.txt", "a\n");
        await temp.CommitAllAsync("Base");
        string folder = temp.Path + "-worktree";
        try
        {
            Assert.Success(await temp.Repository.AddWorktreeAsync(folder, "feature", true), "add");
            List<WorktreeInfo> worktrees = await temp.Repository.GetWorktreesAsync();
            Assert.Equal(2, worktrees.Count, "main and the new one");
            Assert.Equal("feature", worktrees[1].Branch, "on the new branch");
            Assert.True(File.Exists(Path.Combine(folder, "a.txt")), "files checked out");

            Assert.Success(await temp.Repository.RemoveWorktreeAsync(folder, false), "remove");
            Assert.Equal(1, (await temp.Repository.GetWorktreesAsync()).Count, "only main left");
            Assert.Success(await temp.Repository.PruneWorktreesAsync(), "prune");
        }
        finally
        {
            if (Directory.Exists(folder))
                TestFolders.Delete(folder);
        }
    }

    public static async Task AddsAndUpdatesSubmodules()
    {
        // Local file URLs are blocked for submodules by default
        Environment.SetEnvironmentVariable("GIT_ALLOW_PROTOCOL", "file");
        using TempRepository library = await TempRepository.CreateAsync();
        library.Write("lib.txt", "lib\n");
        await library.CommitAllAsync("Library");

        using TempRepository temp = await TempRepository.CreateAsync();
        temp.Write("a.txt", "a\n");
        await temp.CommitAllAsync("Base");
        Assert.Success(await temp.Repository.AddSubmoduleAsync(library.Path, "libs/lib"), "add submodule");
        await temp.CommitAllAsync("Add the library");

        List<SubmoduleInfo> modules = await temp.Repository.GetSubmodulesAsync();
        Assert.Equal(1, modules.Count, "one submodule");
        Assert.Equal(SubmoduleState.UpToDate, modules[0].State, "checked out");

        Assert.Success(await GitRunner.RunAsync(temp.Path, "submodule", "deinit", "-f", "libs/lib"), "deinit");
        Assert.Equal(SubmoduleState.NotInitialized, (await temp.Repository.GetSubmodulesAsync())[0].State,
                     "not checked out after deinit");
        Assert.Success(await temp.Repository.UpdateSubmodulesAsync(), "update");
        Assert.True(File.Exists(Path.Combine(temp.Path, "libs", "lib", "lib.txt")), "checked out again");
    }
}
