using Urras.Core.Git;

namespace Urras.Core.Tests;

public static class ManageTests
{
    public static async Task UndoesTheLastCommitKeepingItsChanges()
    {
        using TempRepository repository = await TempRepository.CreateAsync();
        repository.Write("a.txt", "a");
        await repository.CommitAllAsync("first");
        string? first = await repository.Repository.GetHeadAsync();
        repository.Write("b.txt", "b");
        await repository.CommitAllAsync("second");

        Assert.Success(await repository.Repository.UndoLastCommitAsync(), "undo");
        Assert.Equal(first, await repository.Repository.GetHeadAsync(), "back on the first commit");
        RepositoryStatus status = await repository.Repository.GetStatusAsync();
        Assert.Equal("b.txt", status.Staged.Single().Path, "its change is staged again");

        Assert.Success(await repository.Repository.UndoLastCommitAsync(), "undo the very first commit");
        Assert.Equal(null, await repository.Repository.GetHeadAsync(), "no commits left");
        Assert.Equal(2, (await repository.Repository.GetStatusAsync()).Staged.Count, "both files staged");
    }

    public static async Task ResetsAsFarAsAsked()
    {
        using TempRepository repository = await TempRepository.CreateAsync();
        repository.Write("a.txt", "a");
        await repository.CommitAllAsync("first");
        string first = (await repository.Repository.GetHeadAsync())!;
        repository.Write("a.txt", "changed");
        await repository.CommitAllAsync("second");
        string second = (await repository.Repository.GetHeadAsync())!;

        Assert.Success(await repository.Repository.ResetAsync(first, ResetMode.Soft), "soft");
        Assert.Equal(1, (await repository.Repository.GetStatusAsync()).Staged.Count, "kept staged");
        Assert.Success(await repository.Repository.ResetAsync(second, ResetMode.Hard), "back");
        Assert.Success(await repository.Repository.ResetAsync(first, ResetMode.Mixed), "mixed");
        RepositoryStatus mixed = await repository.Repository.GetStatusAsync();
        Assert.True(mixed.Staged.Count == 0 && mixed.Unstaged.Count == 1, "kept unstaged");
        Assert.Success(await repository.Repository.ResetAsync(first, ResetMode.Hard), "hard");
        Assert.True(!(await repository.Repository.GetStatusAsync()).HasChanges, "thrown away");
        Assert.Equal("a", File.ReadAllText(Path.Combine(repository.Path, "a.txt")), "the file as it was");
    }

    public static async Task RenamesABranchHereAndOnTheRemote()
    {
        using TempRepository online = await TempRepository.CreateAsync();
        Assert.Success(await GitRunner.RunAsync(online.Path, "config", "receive.denyCurrentBranch", "ignore"), "push");
        using TempRepository local = await TempRepository.CreateAsync();
        local.Write("a.txt", "a");
        await local.CommitAllAsync("first");
        Assert.Success(await local.Repository.AddRemoteAsync("origin", online.Path), "remote");
        Assert.Success(await GitRunner.RunAsync(local.Path, "switch", "-q", "-c", "feature"), "branch");
        Assert.Success(await GitRunner.RunAsync(local.Path, "push", "-q", "-u", "origin", "feature"), "published");

        GitResult refused = await local.Repository.RenameBranchAsync("feature", "bad..name", true);
        Assert.True(!refused.Success, "a name Git doesn't allow");
        Assert.Success(await local.Repository.RenameBranchAsync("feature", "port/tc434", true), "rename");
        Assert.Equal("origin/port/tc434", (await local.Repository.GetStatusAsync()).Upstream, "tracks the new one");
        string branches = (await GitRunner.RunAsync(online.Path, "branch", "--list")).Output;
        Assert.True(branches.Contains("port/tc434") && !branches.Contains("feature"), "renamed on the remote");

        Assert.Success(await local.Repository.RenameBranchAsync("port/tc434", "local-only", false), "here only");
        Assert.True((await GitRunner.RunAsync(online.Path, "branch", "--list")).Output.Contains("port/tc434"),
                    "the remote's branch is left");
    }

    public static async Task AddsToGitIgnoreOnce()
    {
        using TempRepository repository = await TempRepository.CreateAsync();
        string file = Path.Combine(repository.Path, ".gitignore");
        File.WriteAllText(file, "bin/\r\nobj/");
        Assert.Success(await repository.Repository.AddToGitIgnoreAsync("*.log"), "added");
        Assert.Success(await repository.Repository.AddToGitIgnoreAsync("*.log"), "again");
        Assert.Equal("bin/\r\nobj/\r\n*.log\r\n", File.ReadAllText(file), "once, on its own line, in CRLF");

        (string single, string? kind, string? folder) = GitRepository.IgnorePatterns("logs\\today\\run.log");
        Assert.Equal("/logs/today/run.log", single, "the file");
        Assert.Equal("*.log", kind, "every file of its kind");
        Assert.Equal("/logs/today/", folder, "its folder");
        Assert.Equal((null, null), (GitRepository.IgnorePatterns("Makefile").Extension,
                                    GitRepository.IgnorePatterns("Makefile").Folder), "no kind or folder");
    }

    public static async Task ManagesRemotes()
    {
        using TempRepository repository = await TempRepository.CreateAsync();
        Assert.Success(await repository.Repository.AddRemoteAsync("origin", "https://example.org/a.git"), "add");
        Assert.True(!(await repository.Repository.AddRemoteAsync("--upload-pack=x", "u")).Success, "an option");
        Assert.Success(await repository.Repository.RenameRemoteAsync("origin", "upstream"), "rename");
        Assert.Success(await repository.Repository.SetRemoteUrlAsync("upstream", "https://example.org/b.git"), "url");
        RemoteInfo remote = (await repository.Repository.GetRemoteListAsync()).Single();
        Assert.Equal(new RemoteInfo("upstream", "https://example.org/b.git"), remote, "listed");
        Assert.Success(await repository.Repository.RemoveRemoteAsync("upstream"), "remove");
        Assert.Equal(0, (await repository.Repository.GetRemoteListAsync()).Count, "gone");
    }
}
