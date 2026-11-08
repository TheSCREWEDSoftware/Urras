using Urras.Core.Git;

namespace Urras.Core.Tests;

public static class StatisticsTests
{
    public static void CountsPeopleTimesAndFiles()
    {
        const string log =
            "\u001eAna\u001fana@example.com\u001f2026-01-05T09:30:00+00:00\n\n3\t1\tsrc/a.cs\n-\t-\tlogo.png\n" +
            "\u001eBob\u001fbob@example.com\u001f2026-03-07T22:10:00+00:00\n\n" +
            "2\t0\tsrc/{old => new}/b.cs\n1\t1\tsrc/a.cs\n" +
            "\u001eana\u001fANA@example.com\u001f2026-03-08T09:00:00+00:00\n\n0\t4\told.txt => new.txt\n";
        RepositoryStatistics stats = RepositoryStatistics.Parse(log);

        Assert.Equal(3, stats.Commits, "commits");
        Assert.Equal(6, stats.Added, "lines added");
        Assert.Equal(6, stats.Removed, "lines removed");
        Assert.Equal(2, stats.Contributors.Count, "Ana counted once despite the case");
        Assert.Equal(new ContributorStats("Ana", "ana@example.com", 2, 3, 5), stats.Contributors[0], "Ana first");
        Assert.Equal("src/a.cs", stats.Files[0].Path, "most changed file");
        Assert.True(stats.Files.Any(file => file.Path == "src/new/b.cs"), "rename in braces");
        Assert.True(stats.Files.Any(file => file.Path == "new.txt"), "plain rename");
        Assert.Equal(1, stats.ByWeekday[0], "a Monday");
        Assert.Equal(1, stats.ByWeekday[5], "a Saturday");
        Assert.Equal(2, stats.ByHour[9], "two at nine");
        Assert.Equal("2026-01:1|2026-02:0|2026-03:2",
                     string.Join("|", stats.ByMonth.Select(month => month.Month + ":" + month.Commits)),
                     "empty months included");
        Assert.Equal(0, RepositoryStatistics.Parse("").Commits, "empty history");
    }

    public static async Task ReadsARealRepository()
    {
        using TempRepository temp = await TempRepository.CreateAsync();
        Assert.Equal(0, (await temp.Repository.GetStatisticsAsync()).Commits, "no commits yet");
        temp.Write("a.txt", "1\n2\n");
        await temp.CommitAllAsync("First");
        temp.Write("a.txt", "1\n3\n");
        await temp.CommitAllAsync("Second");

        RepositoryStatistics stats = await temp.Repository.GetStatisticsAsync();
        Assert.Equal(2, stats.Commits, "two commits");
        Assert.Equal(3, stats.Added, "2 + 1 lines added");
        Assert.Equal(1, stats.Removed, "one removed");
        Assert.Equal(2, (await temp.Repository.GetStatisticsAsync("1 year ago")).Commits, "recent ones");
    }
}
