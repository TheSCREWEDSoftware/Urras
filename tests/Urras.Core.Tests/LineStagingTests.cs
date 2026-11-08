using Urras.Core.Git;

namespace Urras.Core.Tests;

public static class LineStagingTests
{
    private static async Task<string> Show(TempRepository temp, string revision)
    {
        GitResult result = await GitRunner.RunAsync(temp.Path, "show", revision);
        Assert.Success(result, "show " + revision);
        return result.Output;
    }

    private static HashSet<int> LinesMatching(string diff, Func<string, bool> predicate)
    {
        string[] lines = diff.Split('\n');
        return Enumerable.Range(0, lines.Length).Where(i => predicate(lines[i])).ToHashSet();
    }

    private static async Task<TempRepository> FiveLinesAsync(string newline = "\n")
    {
        TempRepository temp = await TempRepository.CreateAsync();
        temp.Write("f.txt", string.Join(newline, "one", "two", "three", "four", "five") + newline);
        await temp.CommitAllAsync("Base");
        return temp;
    }

    public static async Task StagesOnlySelectedLines()
    {
        using TempRepository temp = await FiveLinesAsync();
        temp.Write("f.txt", "ONE\ntwo\nthree\nfour\nFIVE\nsix\n");
        FileChange change = new FileChange("f.txt", ChangeKind.Modified);
        string diff = await temp.Repository.GetDiffAsync(change, false);

        // Stage "one" -> "ONE" only
        HashSet<int> first = LinesMatching(diff, line => line is "-one" or "+ONE");
        Assert.Success(await temp.Repository.StageLinesAsync(diff, first), "stage lines");
        Assert.Equal("ONE\ntwo\nthree\nfour\nfive\n", await Show(temp, ":f.txt"), "index has only the first change");

        // Then unstage it again from the staged diff
        string staged = await temp.Repository.GetDiffAsync(change, true);
        Assert.Success(await temp.Repository.UnstageLinesAsync(staged, PatchBuilder.AllChanges(staged)), "unstage");
        Assert.Equal("one\ntwo\nthree\nfour\nfive\n", await Show(temp, ":f.txt"), "index back to HEAD");

        // An added line on its own, with the removed line kept
        HashSet<int> six = LinesMatching(diff, line => line == "+six");
        Assert.Success(await temp.Repository.StageLinesAsync(diff, six), "stage the new last line");
        Assert.Equal("one\ntwo\nthree\nfour\nfive\nsix\n", await Show(temp, ":f.txt"), "only six added");
        Assert.Equal("ONE\ntwo\nthree\nfour\nFIVE\nsix\n", File.ReadAllText(Path.Combine(temp.Path, "f.txt")),
                     "working tree untouched");
    }

    public static async Task DiscardsSelectedLinesAndHunks()
    {
        using TempRepository temp = await FiveLinesAsync();
        temp.Write("f.txt", "ONE\ntwo\nthree\nfour\nFIVE\n");
        string diff = await temp.Repository.GetDiffAsync(new FileChange("f.txt", ChangeKind.Modified), false);

        HashSet<int> five = LinesMatching(diff, line => line is "-five" or "+FIVE");
        Assert.Success(await temp.Repository.DiscardLinesAsync(diff, five), "discard lines");
        Assert.Equal("ONE\ntwo\nthree\nfour\nfive\n", File.ReadAllText(Path.Combine(temp.Path, "f.txt")),
                     "only the last change reverted");

        int plusOne = Array.IndexOf(diff.Split('\n'), "+ONE");
        HashSet<int> hunk = PatchBuilder.HunkOf(diff, plusOne);
        Assert.Equal(4, hunk.Count, "both changes are one hunk in a five-line file");
        Assert.Equal(0, PatchBuilder.HunkOf(diff, 0).Count, "a file header line is in no hunk");
        Assert.True(PatchBuilder.Build(diff, new HashSet<int>(), false) == null, "nothing selected, no patch");
        Assert.True(!(await temp.Repository.StageLinesAsync(diff, new HashSet<int>())).Success, "and an error");
    }

    public static async Task KeepsCrlfAndPartlyStagesNewFiles()
    {
        using TempRepository temp = await FiveLinesAsync("\r\n");
        Assert.Success(await GitRunner.RunAsync(temp.Path, "config", "core.autocrlf", "false"), "no conversion");
        temp.Write("f.txt", "one\r\nTWO\r\nthree\r\nfour\r\nFIVE\r\n");
        string diff = await temp.Repository.GetDiffAsync(new FileChange("f.txt", ChangeKind.Modified), false);
        HashSet<int> two = LinesMatching(diff, line => line is "-two\r" or "+TWO\r");
        Assert.Success(await temp.Repository.StageLinesAsync(diff, two), "stage a CRLF line");
        Assert.Equal("one\r\nTWO\r\nthree\r\nfour\r\nfive\r\n", await Show(temp, ":f.txt"), "CRLF kept");

        temp.Write("new.txt", "a\nb\nc");
        FileChange untracked = new FileChange("new.txt", ChangeKind.Untracked);
        string newDiff = await temp.Repository.GetDiffAsync(untracked, false);
        HashSet<int> firstTwo = LinesMatching(newDiff, line => line is "+a" or "+b");
        Assert.Success(await temp.Repository.StageLinesAsync(newDiff, firstTwo), "stage part of a new file");
        Assert.Equal("a\nb\n", await Show(temp, ":new.txt"), "new file staged with two lines");

        // Unstaging part of a staged new file leaves the file staged with the rest
        FileChange added = new FileChange("new.txt", ChangeKind.Added);
        string staged = await temp.Repository.GetDiffAsync(added, true);
        Assert.Success(await temp.Repository.UnstageLinesAsync(staged, LinesMatching(staged, l => l == "+b")),
                       "unstage one line of a new file");
        Assert.Equal("a\n", await Show(temp, ":new.txt"), "still staged, without b");

        // The last line without a newline can be staged too
        string rest = await temp.Repository.GetDiffAsync(added, false);
        Assert.Success(await temp.Repository.StageLinesAsync(rest, PatchBuilder.AllChanges(rest)), "stage the rest");
        Assert.Equal("a\nb\nc", await Show(temp, ":new.txt"), "whole file, no final newline");
    }

    public static async Task PartlyStagesADeletion()
    {
        using TempRepository temp = await FiveLinesAsync();
        File.Delete(Path.Combine(temp.Path, "f.txt"));
        string diff = await temp.Repository.GetDiffAsync(new FileChange("f.txt", ChangeKind.Deleted), false);
        Assert.Success(await temp.Repository.StageLinesAsync(diff, LinesMatching(diff, line => line == "-one")),
                       "stage one removed line of a deleted file");
        Assert.Equal("two\nthree\nfour\nfive\n", await Show(temp, ":f.txt"), "file kept in the index, minus one");
    }

    public static void RecordsRawLineNumbers()
    {
        const string diff = "diff --git a/f b/f\nindex 1..2 100644\n--- a/f\n+++ b/f\n@@ -1,2 +1,2 @@\n-a\n+b\n c\n";
        List<DiffLine> lines = DiffParser.Parse(diff);
        Assert.Equal(0, lines[0].RawIndex, "file header");
        Assert.Equal(4, lines[1].RawIndex, "hunk header after the skipped index, --- and +++ lines");
        Assert.Equal(5, lines[2].RawIndex, "removed line");
        Assert.True(lines[2].IsChange && !lines[4].IsChange, "changes and context");
    }
}
