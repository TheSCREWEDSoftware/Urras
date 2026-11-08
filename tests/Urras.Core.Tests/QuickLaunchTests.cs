using Urras.Core.Search;

namespace Urras.Core.Tests;

public static class QuickLaunchTests
{
    public static void MatchesLettersInOrder()
    {
        Assert.True(FuzzyMatch.Score("psh", "Push") != null, "subsequence");
        Assert.True(FuzzyMatch.Score("hsp", "Push") == null, "out of order");
        Assert.True(FuzzyMatch.Score("xyz", "Push") == null, "missing letters");
        Assert.Equal(0, FuzzyMatch.Score("  ", "anything"), "empty query");
        Assert.True(FuzzyMatch.Score("PUSH", "push") != null, "case ignored");
    }

    public static void RanksWordStartsAndShortTextsFirst()
    {
        string[] items = { "Stash changes", "Push", "Pull", "Push all tags", "Open in file explorer",
                           "Echo Flint Engine/Game_PhantomSonar" };
        Assert.Equal("Push", FuzzyMatch.Filter(items, "pu", item => item)[0], "Push and Pull tie; order kept");
        Assert.Equal("Push", FuzzyMatch.Filter(items, "push", item => item)[0], "shorter first");
        Assert.Equal("Echo Flint Engine/Game_PhantomSonar", FuzzyMatch.Filter(items, "ps", item => item)[0],
                     "word starts (Phantom Sonar) beat scattered letters");
        Assert.Equal("Open in file explorer", FuzzyMatch.Filter(items, "ofe", item => item)[0], "initials");
        Assert.Equal(items.Length, FuzzyMatch.Filter(items, "", item => item).Count, "everything for no query");
    }
}
