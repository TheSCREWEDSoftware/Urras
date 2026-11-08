using Urras.Core.Git;

namespace Urras.Core.Tests;

public static class LfsTests
{
    public static void ParsesLfsOutput()
    {
        List<string> patterns = GitParsers.ParseLfsPatterns(
            "Listing tracked patterns\n    *.png (.gitattributes)\n    art/*.psd (art/.gitattributes)\n" +
            "Listing excluded patterns\n    *.txt (.gitattributes)\n");
        Assert.Equal("*.png|art/*.psd", string.Join("|", patterns), "tracked patterns only");

        List<LfsFile> files = GitParsers.ParseLfsFiles("abc123 * art/a b.png\ndef456 - big.bin\n");
        Assert.Equal(2, files.Count, "two files");
        Assert.True(files[0].Downloaded && !files[1].Downloaded, "downloaded and pointer");
        Assert.Equal("art/a b.png", files[0].Path, "path with a space");
        Assert.True(files[1].ToString().Contains("not downloaded"), "readable");

        List<LfsLock> locks = GitParsers.ParseLfsLocks(
            "[{\"id\":\"7\",\"path\":\"art/a.psd\",\"owner\":{\"name\":\"Ana\"}," +
            "\"locked_at\":\"2026-11-08T10:00:00Z\"}]");
        Assert.Equal(new LfsLock("7", "art/a.psd", "Ana", new DateTimeOffset(2026, 11, 8, 10, 0, 0, TimeSpan.Zero)),
                     locks[0], "lock");
        Assert.Equal(0, GitParsers.ParseLfsLocks("not json").Count, "garbage");
        Assert.Equal(0, GitParsers.ParseLfsLocks("").Count, "empty");
    }

    public static async Task TracksAndListsLfsFiles()
    {
        if (!await GitRepository.IsLfsInstalledAsync())
            return;

        using TempRepository temp = await TempRepository.CreateAsync();
        Assert.Success(await temp.Repository.SetUpLfsAsync(), "set up");
        Assert.Success(await temp.Repository.LfsTrackAsync("*.bin"), "track");
        Assert.Equal("*.bin", string.Join("|", await temp.Repository.GetLfsPatternsAsync()), "pattern listed");
        temp.Write("big.bin", "pretend this is large\n");
        await temp.CommitAllAsync("Add a large file");

        List<LfsFile> files = await temp.Repository.GetLfsFilesAsync();
        Assert.Equal(1, files.Count, "one LFS file");
        Assert.True(files[0].Downloaded && files[0].Path == "big.bin", "downloaded");
        Assert.Success(await temp.Repository.LfsUntrackAsync("*.bin"), "untrack");
        Assert.Equal(0, (await temp.Repository.GetLfsPatternsAsync()).Count, "no patterns");
        (List<LfsLock> locks, string? _) = await temp.Repository.GetLfsLocksAsync();
        Assert.Equal(0, locks.Count, "no server, so no locks");
    }
}
