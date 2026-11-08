using Urras.Core.Git;
using Urras.Core.Settings;

namespace Urras.Core.Tests;

public static class DiffViewTests
{
    public static void ParserKeepsCarriageReturnsAndMissingNewline()
    {
        string diff = "diff --git a/f.txt b/f.txt\n--- a/f.txt\n+++ b/f.txt\n@@ -1,2 +1,2 @@\n" +
                      " windows\r\n-old\r\n+new\n\\ No newline at end of file\n";
        List<DiffLine> lines = DiffParser.Parse(diff);

        DiffLine context = lines.Single(line => line.Kind == DiffLineKind.Context);
        Assert.Equal("windows", context.Text, "CR removed from the text");
        Assert.True(context.HasCarriageReturn, "CR remembered");
        Assert.True(lines.Single(line => line.Kind == DiffLineKind.Removed).HasCarriageReturn, "removed line CRLF");

        DiffLine added = lines.Single(line => line.Kind == DiffLineKind.Added);
        Assert.True(!added.HasCarriageReturn, "added line LF");
        Assert.True(added.MissingNewline, "last line without a line ending");
    }

    public static void IntralineMarksOnlyChangedWords()
    {
        (List<InlineSegment> oldSegments, List<InlineSegment> newSegments) =
            IntralineDiff.Compare("int count = 10;", "int total = 10;");

        Assert.Equal("count", oldSegments.Single(segment => segment.Changed).Text, "old changed word");
        Assert.Equal("total", newSegments.Single(segment => segment.Changed).Text, "new changed word");
        Assert.Equal("int total = 10;", string.Concat(newSegments.Select(segment => segment.Text)), "text kept whole");
    }

    public static void IntralineSkipsUnrelatedLines()
    {
        (List<InlineSegment> oldSegments, List<InlineSegment> newSegments) =
            IntralineDiff.Compare("completely different", "nothing alike here at all");
        Assert.True(oldSegments.All(segment => !segment.Changed) && newSegments.All(segment => !segment.Changed),
                    "no highlighting for unrelated lines");

        (List<InlineSegment> emptyOld, List<InlineSegment> emptyNew) =
            IntralineDiff.Compare(string.Empty, string.Empty);
        Assert.True(emptyOld.Count == 0 && emptyNew.Count == 0, "empty lines");
    }

    public static void IntralineHandlesVeryLongLines()
    {
        string common = string.Join(" ", Enumerable.Repeat("word", 300));
        (List<InlineSegment> oldSegments, List<InlineSegment> newSegments) =
            IntralineDiff.Compare(common + " end", common + " finish");
        Assert.Equal("end", oldSegments.Last(segment => segment.Changed).Text, "long line, old");
        Assert.Equal("finish", newSegments.Last(segment => segment.Changed).Text, "long line, new");
    }

    public static void ParsesNameStatus()
    {
        List<FileChange> changes = GitParsers.ParseNameStatus("M\0src/a.cs\0R087\0old name.txt\0new name.txt\0" +
                                                              "D\0gone.txt\0A\0added.png\0");
        Assert.Equal(4, changes.Count, "count");
        Assert.Equal("new name.txt", changes[1].Path, "rename target");
        Assert.Equal("old name.txt", changes[1].OriginalPath, "rename source");
        Assert.Equal(ChangeKind.Deleted, changes[2].Kind, "deleted");
        Assert.Equal(ChangeKind.Added, changes[3].Kind, "added");
    }

    public static async Task CommitFilesAndPerFileDiffs()
    {
        using TempRepository temp = await TempRepository.CreateAsync();
        temp.Write("a.txt", "one\n");
        temp.Write("b.txt", "same content for the rename\n");
        await temp.CommitAllAsync("First");

        temp.Write("a.txt", "two\n");
        File.Move(Path.Combine(temp.Path, "b.txt"), Path.Combine(temp.Path, "c.txt"));
        await temp.CommitAllAsync("Second");

        List<CommitInfo> history = await temp.Repository.GetHistoryAsync();
        List<FileChange> rootFiles = await temp.Repository.GetCommitFilesAsync(history[1].Hash);
        Assert.Equal(2, rootFiles.Count, "first commit compared with the empty tree");
        Assert.True(rootFiles.All(file => file.Kind == ChangeKind.Added), "all added in the first commit");

        List<FileChange> files = await temp.Repository.GetCommitFilesAsync(history[0].Hash);
        FileChange rename = files.Single(file => file.Kind == ChangeKind.Renamed);
        Assert.Equal("c.txt", rename.Path, "rename detected");

        string diff = await temp.Repository.GetCommitFileDiffAsync(history[0].Hash,
                                                                  files.Single(file => file.Path == "a.txt"));
        Assert.True(diff.Contains("-one") && diff.Contains("+two") && !diff.Contains("c.txt"), "one file only");

        string rootDiff = await temp.Repository.GetCommitFileDiffAsync(history[1].Hash, rootFiles[0]);
        Assert.True(rootDiff.Contains("+"), "first commit file diff");
    }

    public static async Task BlobsAndCombinedDiffs()
    {
        using TempRepository temp = await TempRepository.CreateAsync();
        byte[] image = { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0x00, 0xFF, 0x10 };
        File.WriteAllBytes(Path.Combine(temp.Path, "icon.png"), image);
        temp.Write("a.txt", "a\n");
        await temp.CommitAllAsync("Initial");

        byte[]? stored = await temp.Repository.GetBlobAsync("HEAD", "icon.png");
        Assert.True(stored != null && stored.SequenceEqual(image), "binary contents survive unchanged");
        Assert.Equal(null, await temp.Repository.GetBlobAsync("HEAD", "missing.png"), "missing file");
        Assert.True((await temp.Repository.GetWorkingFileAsync("icon.png"))!.SequenceEqual(image), "working copy");
        Assert.Equal(null, await temp.Repository.GetWorkingFileAsync("nothing.png"), "no working copy");
        Assert.True(ImageFiles.IsImage("textures/Crate.PNG") && !ImageFiles.IsImage("model.tga"), "image types");

        temp.Write("a.txt", "b\n");
        temp.Write("new.txt", "fresh\n");
        RepositoryStatus status = await temp.Repository.GetStatusAsync();
        string combined = await temp.Repository.GetCombinedDiffAsync(status.Unstaged, false);
        Assert.True(combined.Contains("+b") && combined.Contains("+fresh"), "every change in one diff");
    }

    public static void NewDiffSettingsDefaults()
    {
        AppSettings settings = new AppSettings();
        Assert.True(!settings.ShowWhitespace && !settings.WrapDiffLines, "whitespace and wrapping off");
        Assert.Equal(FileListMode.PerFile, settings.FileListMode, "per file by default");
    }
}
