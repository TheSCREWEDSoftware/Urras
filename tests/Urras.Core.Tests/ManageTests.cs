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
        Assert.Success(await repository.Repository.AddRemoteAsync("disk", "F:/Echo Flint Engine/Urras"), "a path");
        Assert.Equal("F:/Echo Flint Engine/Urras", (await repository.Repository.GetRemoteListAsync())
                         .Single(listed => listed.Name == "disk").Url, "an address with spaces is kept whole");
        Assert.Success(await repository.Repository.RemoveRemoteAsync("disk"), "remove the path");
        Assert.Success(await repository.Repository.RemoveRemoteAsync("upstream"), "remove");
        Assert.Equal(0, (await repository.Repository.GetRemoteListAsync()).Count, "gone");
    }

    public static async Task StashesOnlyTheChosenFiles()
    {
        using TempRepository repository = await TempRepository.CreateAsync();
        repository.Write("a.txt", "a");
        repository.Write("b.txt", "b");
        await repository.CommitAllAsync("first");
        repository.Write("a.txt", "a changed");
        repository.Write("b.txt", "b changed");
        repository.Write("new.txt", "new");

        Assert.Success(await repository.Repository.StashFilesAsync(new[] { "a.txt", "new.txt" }, "part"), "stash");
        RepositoryStatus status = await repository.Repository.GetStatusAsync();
        Assert.Equal("b.txt", string.Join(",", status.Unstaged.Select(change => change.Path)), "the rest stays");
        Assert.Equal("part", (await repository.Repository.GetStashesAsync())[0].Message.Split(':').Last().Trim(),
                     "named");
    }

    public static async Task FindsBranchesToCleanUp()
    {
        using TempRepository online = await TempRepository.CreateAsync();
        Assert.Success(await GitRunner.RunAsync(online.Path, "config", "receive.denyCurrentBranch", "ignore"), "push");
        using TempRepository local = await TempRepository.CreateAsync();
        local.Write("a.txt", "a");
        await local.CommitAllAsync("first");
        Assert.Success(await local.Repository.AddRemoteAsync("origin", online.Path), "remote");
        Assert.Success(await GitRunner.RunAsync(local.Path, "push", "-q", "-u", "origin", "main"), "main");
        Assert.Success(await GitRunner.RunAsync(local.Path, "branch", "done"), "merged branch");
        Assert.Success(await GitRunner.RunAsync(local.Path, "switch", "-q", "-c", "gone"), "gone branch");
        local.Write("b.txt", "b");
        await local.CommitAllAsync("work");
        Assert.Success(await GitRunner.RunAsync(local.Path, "push", "-q", "-u", "origin", "gone"), "published");
        Assert.Success(await GitRunner.RunAsync(online.Path, "branch", "-D", "gone"), "deleted on the remote");
        Assert.Success(await GitRunner.RunAsync(local.Path, "fetch", "-q", "--prune"), "fetch");
        Assert.Success(await GitRunner.RunAsync(local.Path, "switch", "-q", "-c", "working"), "current");
        local.Write("c.txt", "c");
        await local.CommitAllAsync("current work");

        List<BranchCleanup> found = await local.Repository.GetCleanupCandidatesAsync();
        Assert.Equal("done  (merged),gone  (gone from the remote)", string.Join(",", found.OrderBy(b => b.Name)),
                     "merged and gone, not main or the current branch");
    }

    public static async Task SavesAndAppliesPatchesAndExportsZips()
    {
        using TempRepository source = await TempRepository.CreateAsync();
        source.Write("a.txt", "a\n");
        await source.CommitAllAsync("first");
        using TempRepository other = await TempRepository.CreateAsync();
        Assert.Success(await GitRunner.RunAsync(other.Path, "pull", "-q", source.Path, "main"), "same start");
        source.Write("a.txt", "a\nmore\n");
        await source.CommitAllAsync("add more");
        string folder = Path.Combine(Path.GetTempPath(), "urras-patch-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            string commitPatch = Path.Combine(folder, "commit.patch");
            Assert.Success(await source.Repository.SaveCommitPatchAsync((await source.Repository.GetHeadAsync())!,
                                                                        commitPatch), "save commit");
            Assert.True(GitRepository.IsMailPatch(commitPatch), "a format-patch file");
            Assert.Success(await other.Repository.ApplyPatchAsync(commitPatch, true), "apply as a commit");
            Assert.Equal("add more", (await other.Repository.GetHistoryAsync(1))[0].Subject, "commit made");

            source.Write("a.txt", "a\nmore\nagain\n");
            string changesPatch = Path.Combine(folder, "changes.patch");
            Assert.Success(await source.Repository.SaveChangesPatchAsync(new[] { "a.txt" }, false, changesPatch),
                           "save changes");
            Assert.True(!GitRepository.IsMailPatch(changesPatch), "a plain diff");
            Assert.Success(await other.Repository.ApplyPatchAsync(changesPatch, false), "apply as changes");
            Assert.Equal("a\nmore\nagain\n", File.ReadAllText(Path.Combine(other.Path, "a.txt")), "changed");

            string zip = Path.Combine(folder, "files.zip");
            Assert.Success(await source.Repository.ExportZipAsync("HEAD", zip), "export");
            using System.IO.Compression.ZipArchive archive = System.IO.Compression.ZipFile.OpenRead(zip);
            Assert.Equal("a.txt", string.Join(",", archive.Entries.Select(entry => entry.FullName)), "the files");
        }
        finally
        {
            TestFolders.Delete(folder);
        }
    }

    public static async Task CountsCommitsNotOnAnyRemote()
    {
        using TempRepository online = await TempRepository.CreateAsync();
        Assert.Success(await GitRunner.RunAsync(online.Path, "config", "receive.denyCurrentBranch", "ignore"), "push");
        using TempRepository local = await TempRepository.CreateAsync();
        local.Write("a.txt", "a");
        await local.CommitAllAsync("first");
        Assert.Success(await local.Repository.AddRemoteAsync("origin", online.Path), "remote");
        Assert.Success(await GitRunner.RunAsync(local.Path, "push", "-q", "-u", "origin", "main"), "main");
        Assert.Success(await GitRunner.RunAsync(local.Path, "switch", "-q", "-c", "port/tc434"), "new branch");
        for (int index = 0; index < 3; index++)
        {
            local.Write("n" + index + ".txt", "n");
            await local.CommitAllAsync("commit " + index);
        }

        RepositoryStatus status = await local.Repository.GetStatusAsync();
        Assert.Equal(null, status.Upstream, "not published");
        Assert.Equal(3, status.Unpublished, "the three new commits, not main's");
    }
}
