using Urras.Core.Git;

namespace Urras.Core.Tests;

public static class DiffStatsTests
{
    public static void CountsAddedAndRemovedLines()
    {
        const string DIFF = "diff --git a/x b/x\n--- a/x\n+++ b/x\n@@ -1,3 +1,3 @@\n a\n-b\n+c\n+d\n" +
                            "diff --git a/y b/y\n--- a/y\n+++ b/y\n@@ -1 +1 @@\n-old\n";
        (int added, int removed) = DiffStats.Count(DIFF);
        Assert.Equal(2, added, "added lines, not the +++ headers");
        Assert.Equal(2, removed, "removed lines, not the --- headers");
        Assert.Equal((0, 0), DiffStats.Count(null), "no diff");
    }

    public static void SharesFiveSquaresLikeGitHub()
    {
        Assert.Equal((2, 3), DiffStats.Squares(406, 553), "GitHub's +406 -553");
        Assert.Equal((5, 0), DiffStats.Squares(10, 0), "only added");
        Assert.Equal((1, 1), DiffStats.Squares(1, 1), "two lines fill two squares");
        Assert.Equal((4, 1), DiffStats.Squares(1000, 1), "a single removed line still shows");
        Assert.Equal((0, 0), DiffStats.Squares(0, 0), "nothing changed");
    }
}
