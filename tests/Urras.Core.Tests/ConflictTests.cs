using Urras.Core.Git;

namespace Urras.Core.Tests;

public static class ConflictTests
{
    private const string TEXT =
        "top\n<<<<<<< HEAD\nours 1\nours 2\n=======\ntheirs\n>>>>>>> feature\nmiddle\n" +
        "<<<<<<< HEAD\nA\n||||||| base\nB\n=======\nC\n>>>>>>> feature\nend\n";

    public static void SplitsIntoBlocksAndRenders()
    {
        ConflictFile file = ConflictFile.Parse(TEXT)!;
        Assert.Equal(5, file.Blocks.Count, "top, conflict, middle, conflict, end");
        Assert.Equal(2, file.Conflicts.Count(), "two conflicts");
        Assert.Equal("HEAD", file.OursLabel, "ours label");
        Assert.Equal("feature", file.TheirsLabel, "theirs label");
        Assert.Equal("B", file.Blocks[3].Base![0], "diff3 base kept");
        Assert.True(!file.IsResolved, "not resolved yet");
        Assert.Equal(TEXT, file.Render(), "unresolved renders the markers back");

        file.Blocks[1].Choice = ConflictChoice.TheirsThenOurs;
        file.Blocks[3].Choice = ConflictChoice.Ours;
        Assert.True(file.IsResolved, "resolved");
        Assert.Equal("top\ntheirs\nours 1\nours 2\nmiddle\nA\nend\n", file.Render(), "chosen sides");

        file.ChooseAll(ConflictChoice.OursThenTheirs);
        Assert.Equal("top\nours 1\nours 2\ntheirs\nmiddle\nA\nC\nend\n", file.Render(), "both, ours first");
    }

    public static void HandlesCrlfMissingNewlinesAndBrokenMarkers()
    {
        ConflictFile crlf = ConflictFile.Parse("a\r\n<<<<<<< HEAD\r\nx\r\n=======\r\ny\r\n>>>>>>> b\r\nz")!;
        crlf.ChooseAll(ConflictChoice.Theirs);
        Assert.Equal("a\r\ny\r\nz", crlf.Render(), "CRLF kept, no final newline added");
        Assert.True(ConflictFile.Parse("no conflicts\n") == null, "plain file");
        Assert.True(ConflictFile.Parse("<<<<<<< HEAD\nunfinished\n") == null, "unfinished markers are text");
        Assert.True(ConflictFile.Parse("<<<<<<<< not a marker\n=======\n>>>>>>> x\n") == null, "eight angles");
    }

    public static async Task ResolvesARealMergeConflict()
    {
        using TempRepository temp = await TempRepository.CreateAsync();
        temp.Write("f.txt", "one\ntwo\n");
        await temp.CommitAllAsync("Base");
        Assert.Success(await GitRunner.RunAsync(temp.Path, "checkout", "-q", "-b", "feature"), "branch");
        temp.Write("f.txt", "one\nfeature\n");
        await temp.CommitAllAsync("Feature");
        Assert.Success(await GitRunner.RunAsync(temp.Path, "checkout", "-q", "main"), "main");
        temp.Write("f.txt", "one\nmain\n");
        await temp.CommitAllAsync("Main");
        Assert.True(!(await temp.Repository.MergeAsync("feature")).Success, "merge conflicts");

        ConflictFile file = (await temp.Repository.GetConflictAsync("f.txt"))!;
        Assert.Equal("main", file.Blocks[1].Ours![0], "our side");
        Assert.Equal("feature", file.Blocks[1].Theirs![0], "their side");
        file.ChooseAll(ConflictChoice.OursThenTheirs);
        Assert.Success(await temp.Repository.SaveResolvedAsync("f.txt", file), "save and stage");
        Assert.Equal("one\nmain\nfeature\n", File.ReadAllText(Path.Combine(temp.Path, "f.txt")), "written");
        RepositoryStatus status = await temp.Repository.GetStatusAsync();
        Assert.True(status.Unstaged.All(change => change.Kind != ChangeKind.Conflicted), "no longer conflicted");
        Assert.True(await temp.Repository.GetConflictAsync("missing.txt") == null, "missing file");
    }

    public static async Task ResolvesByHand()
    {
        ConflictFile crlf = ConflictFile.Parse("a\r\n<<<<<<< HEAD\r\nx\r\n=======\r\ny\r\n>>>>>>> b\r\nz\r\n")!;
        Assert.True(crlf.UsesCrlf, "CRLF file");
        crlf.Conflicts.First().SetCustom("x and y\nboth", crlf.UsesCrlf);
        Assert.True(crlf.IsResolved, "hand-written counts as resolved");
        Assert.Equal("a\r\nx and y\r\nboth\r\nz\r\n", crlf.Render(), "typed lines follow the file's endings");
        Assert.True(ConflictFile.HasMarkers("ok\n<<<<<<< HEAD\n"), "markers left");
        Assert.True(!ConflictFile.HasMarkers("ok\n"), "clean");

        using TempRepository temp = await TempRepository.CreateAsync();
        temp.Write("f.txt", "x\n");
        await temp.CommitAllAsync("Base");
        Assert.True(!(await temp.Repository.SaveResolvedTextAsync("f.txt", "<<<<<<< HEAD\n")).Success,
                    "refused with markers");
        Assert.Success(await temp.Repository.SaveResolvedTextAsync("f.txt", "written\n"), "saved");
        Assert.Equal("written\n", File.ReadAllText(Path.Combine(temp.Path, "f.txt")), "file written");
    }
}
