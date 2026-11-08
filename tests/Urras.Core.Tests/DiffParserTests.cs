using Urras.Core.Git;

namespace Urras.Core.Tests;

public static class DiffParserTests
{
    private const string SAMPLE_DIFF =
        "diff --git a/src/file.txt b/src/file.txt\n" +
        "index 1111111..2222222 100644\n" +
        "--- a/src/file.txt\n" +
        "+++ b/src/file.txt\n" +
        "@@ -10,4 +10,5 @@ void Function()\n" +
        " keep one\n" +
        "-old line\n" +
        "+new line\n" +
        "+extra line\n" +
        " keep two\n" +
        "-removed alone\n" +
        "\\ No newline at end of file\n";

    public static void ParsesLineNumbersAndKinds()
    {
        List<DiffLine> lines = DiffParser.Parse(SAMPLE_DIFF);

        Assert.Equal(DiffLineKind.FileHeader, lines[0].Kind, "file header");
        Assert.Equal("src/file.txt", lines[0].Text, "file name");
        Assert.Equal(DiffLineKind.HunkHeader, lines[1].Kind, "index and --- +++ lines are hidden");

        Assert.Equal(DiffLineKind.Context, lines[2].Kind, "context");
        Assert.Equal("keep one", lines[2].Text, "marker removed");
        Assert.Equal(10, lines[2].OldNumber, "context old number");
        Assert.Equal(10, lines[2].NewNumber, "context new number");

        Assert.Equal(DiffLineKind.Removed, lines[3].Kind, "removed");
        Assert.Equal(11, lines[3].OldNumber, "removed old number");
        Assert.Equal(null, lines[3].NewNumber, "removed has no new number");

        Assert.Equal(DiffLineKind.Added, lines[4].Kind, "added");
        Assert.Equal(11, lines[4].NewNumber, "added new number");
        Assert.Equal(12, lines[5].NewNumber, "second added new number");

        Assert.Equal(12, lines[6].OldNumber, "context after change, old");
        Assert.Equal(13, lines[6].NewNumber, "context after change, new");
        Assert.Equal(DiffLineKind.Meta, lines[8].Kind, "no newline marker");
        Assert.Equal(9, lines.Count, "line count, no trailing empty line");
    }

    public static void SplitPairsRemovedWithAdded()
    {
        List<SplitDiffRow> rows = DiffParser.ToSplit(DiffParser.Parse(SAMPLE_DIFF));

        Assert.True(rows[0].Full != null && rows[1].Full != null, "headers are full-width rows");
        Assert.True(rows[2].Left == rows[2].Right && rows[2].Left!.Kind == DiffLineKind.Context, "context both sides");
        Assert.Equal("old line", rows[3].Left?.Text, "pair left");
        Assert.Equal("new line", rows[3].Right?.Text, "pair right");
        Assert.True(rows[4].Left == null && rows[4].Right?.Text == "extra line", "extra added line right only");
        Assert.True(rows[6].Left?.Text == "removed alone" && rows[6].Right == null, "lone removal left only");
        Assert.True(rows[7].Full?.Kind == DiffLineKind.Meta, "no newline marker full width");
    }

    public static void ShowOutputKeepsCommitHeaderAsText()
    {
        string show = "commit abc\nAuthor: Ana <ana@example.com>\n\n    Subject\n\n file.txt | 1 +\n\n" +
                      "diff --git a/file.txt b/file.txt\nnew file mode 100644\n--- /dev/null\n+++ b/file.txt\n" +
                      "@@ -0,0 +1 @@\n+hello\n";
        List<DiffLine> lines = DiffParser.Parse(show);

        Assert.Equal(DiffLineKind.Text, lines[0].Kind, "commit line is text");
        Assert.True(lines.Any(line => line.Kind == DiffLineKind.Meta && line.Text == "new file mode 100644"),
                    "file mode shown");
        DiffLine added = lines.Last();
        Assert.Equal(DiffLineKind.Added, added.Kind, "added line");
        Assert.Equal(1, added.NewNumber, "first line of new file");
    }

    public static void RenamesUseTheNewName()
    {
        List<DiffLine> lines = DiffParser.Parse("diff --git a/old name.txt b/new name.txt\n" +
                                                "similarity index 100%\nrename from old name.txt\n" +
                                                "rename to new name.txt\n");
        Assert.Equal("new name.txt", lines[0].Text, "file name");
        Assert.Equal(3, lines.Count(line => line.Kind == DiffLineKind.Meta), "rename details");
    }

    public static async Task ParsesRealGitOutput()
    {
        using TempRepository temp = await TempRepository.CreateAsync();
        temp.Write("file.txt", "a\nb\nc\nd\ne\nf\ng\nh\n");
        await temp.CommitAllAsync("Initial");
        temp.Write("file.txt", "a\nb\nC\nd\ne\nf\ng\nh\ni\n");

        RepositoryStatus status = await temp.Repository.GetStatusAsync();
        string diff = await temp.Repository.GetDiffAsync(status.Unstaged[0], false);
        List<DiffLine> lines = DiffParser.Parse(diff);

        DiffLine removed = lines.Single(line => line.Kind == DiffLineKind.Removed);
        Assert.Equal("c", removed.Text, "removed text");
        Assert.Equal(3, removed.OldNumber, "removed is line 3");
        DiffLine appended = lines.Single(line => line.Kind == DiffLineKind.Added && line.Text == "i");
        Assert.Equal(9, appended.NewNumber, "appended is line 9");

        List<CommitInfo> history = await temp.Repository.GetHistoryAsync();
        List<DiffLine> showLines = DiffParser.Parse(await temp.Repository.GetCommitDiffAsync(history[0].Hash));
        Assert.True(showLines.Count(line => line.Kind == DiffLineKind.Added) == 8, "eight added lines in commit");
    }
}
