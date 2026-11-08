using Urras.Core.Git;

namespace Urras.Core.Tests;

public static class HistoryTests
{
    private static async Task<TempRepository> SampleHistoryAsync()
    {
        TempRepository temp = await TempRepository.CreateAsync();
        temp.Write("docs/readme.md", "hello\n");
        await temp.CommitAllAsync("Add readme");
        temp.Write("src/main.cpp", "int main() { return 0; }\n");
        await temp.CommitAllAsync("Add main with SECRET_TOKEN");
        File.Move(Path.Combine(temp.Path, "docs", "readme.md"), Path.Combine(temp.Path, "docs", "guide.md"));
        await temp.CommitAllAsync("Rename readme to guide");
        temp.Write("src/main.cpp", "int main() { return 1; }\n");
        await temp.CommitAllAsync("Fix exit code");
        return temp;
    }

    public static async Task FiltersByMessageAuthorPathAndContent()
    {
        using TempRepository temp = await SampleHistoryAsync();

        List<CommitInfo> message = await temp.Repository.GetHistoryAsync(
            filter: new HistoryFilter(HistorySearch.Message, "readme"));
        Assert.Equal(2, message.Count, "two messages mention readme, case ignored");

        Assert.Equal(4, (await temp.Repository.GetHistoryAsync(
                            filter: new HistoryFilter(HistorySearch.Author, "urras test"))).Count, "author");
        Assert.Equal(0, (await temp.Repository.GetHistoryAsync(
                            filter: new HistoryFilter(HistorySearch.Author, "nobody"))).Count, "no such author");

        List<CommitInfo> followed = await temp.Repository.GetHistoryAsync(
            filter: new HistoryFilter(HistorySearch.Path, "docs/guide.md"));
        Assert.Equal("Add readme", followed.Last().Subject, "a single file is followed through its rename");

        List<CommitInfo> folder = await temp.Repository.GetHistoryAsync(
            filter: new HistoryFilter(HistorySearch.Path, "src"));
        Assert.Equal(2, folder.Count, "folder history");

        List<CommitInfo> content = await temp.Repository.GetHistoryAsync(
            filter: new HistoryFilter(HistorySearch.Content, "return 1"));
        Assert.Equal("Fix exit code", content.Single().Subject, "commit that added the text");

        Assert.True(new HistoryFilter(HistorySearch.Path, "src").Description.Contains("History of src"),
                    "description");
    }

    public static async Task BlameShowsWhoChangedEachLine()
    {
        using TempRepository temp = await TempRepository.CreateAsync();
        temp.Write("file.txt", "first\nsecond\n");
        await temp.CommitAllAsync("Initial lines");
        temp.Write("file.txt", "first\nchanged\nthird\n");
        await temp.CommitAllAsync("Change the second line");

        List<BlameLine> blame = await temp.Repository.GetBlameAsync("file.txt");
        Assert.Equal(3, blame.Count, "three lines");
        Assert.Equal("Initial lines", blame[0].Summary, "first line from the first commit");
        Assert.Equal("Change the second line", blame[1].Summary, "second line from the second commit");
        Assert.Equal(2, blame[1].Number, "line number");
        Assert.Equal("changed", blame[1].Text, "line text");
        Assert.Equal("Urras Test", blame[2].Author, "author");
        Assert.True(!blame[0].IsShaded && blame[1].IsShaded && blame[2].IsShaded, "blocks alternate");

        List<CommitInfo> history = await temp.Repository.GetHistoryAsync();
        Assert.Equal(2, (await temp.Repository.GetBlameAsync("file.txt", history[1].Hash)).Count,
                     "blame at an older commit");

        temp.Write("file.txt", "first\nchanged\nthird\nnot committed\n");
        Assert.True((await temp.Repository.GetBlameAsync("file.txt")).Last().IsUncommitted, "uncommitted line");
    }

    public static async Task FilesAtACommit()
    {
        using TempRepository temp = await SampleHistoryAsync();
        List<CommitInfo> history = await temp.Repository.GetHistoryAsync();

        List<string> first = await temp.Repository.GetTreeFilesAsync(history[3].Hash);
        Assert.Equal("docs/readme.md", first.Single(), "one file in the first commit");
        List<string> latest = await temp.Repository.GetTreeFilesAsync("HEAD");
        Assert.True(latest.Contains("docs/guide.md") && latest.Contains("src/main.cpp"), "files now");

        Assert.Equal("int main() { return 0; }\n", await temp.Repository.GetFileTextAsync(history[1].Hash,
                                                                                          "src/main.cpp"),
                     "file text at an older commit");
        Assert.Equal(null, await temp.Repository.GetFileTextAsync(history[3].Hash, "src/main.cpp"),
                     "file did not exist yet");
    }

    public static async Task CompareTwoRevisions()
    {
        using TempRepository temp = await SampleHistoryAsync();
        List<CommitInfo> history = await temp.Repository.GetHistoryAsync();
        string oldest = history[3].Hash;
        string newest = history[0].Hash;

        List<FileChange> files = await temp.Repository.GetCompareFilesAsync(oldest, newest);
        Assert.True(files.Any(file => file.Path == "src/main.cpp" && file.Kind == ChangeKind.Added), "added file");
        Assert.True(files.Any(file => file.Kind == ChangeKind.Renamed && file.Path == "docs/guide.md"), "rename");

        string all = await temp.Repository.GetCompareDiffAsync(oldest, newest);
        string one = await temp.Repository.GetCompareDiffAsync(oldest, newest,
                                                               files.Single(file => file.Path == "src/main.cpp"));
        Assert.True(all.Contains("guide.md") && !one.Contains("guide.md") && one.Contains("return 1"),
                    "whole and per-file comparison");
    }

    public static async Task ReflogRestoresLostCommits()
    {
        using TempRepository temp = await TempRepository.CreateAsync();
        temp.Write("file.txt", "1\n");
        await temp.CommitAllAsync("Keep");
        temp.Write("file.txt", "2\n");
        await temp.CommitAllAsync("Lost later");
        string lost = (await temp.Repository.GetHistoryAsync())[0].Hash;
        Assert.Success(await GitRunner.RunAsync(temp.Path, "reset", "--hard", "HEAD~1"), "lose the commit");

        List<ReflogEntry> reflog = await temp.Repository.GetReflogAsync();
        ReflogEntry entry = reflog.First(item => item.Hash == lost);
        Assert.True(entry.Action.Contains("Lost later") && entry.Selector.StartsWith("HEAD@{"), "reflog entry");

        Assert.Success(await temp.Repository.CreateBranchAtAsync("rescued", entry.Hash), "branch at the entry");
        Assert.True((await temp.Repository.GetBranchesAsync()).Any(branch => branch.Name == "rescued"), "rescued");
    }
}
