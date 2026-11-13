using Urras.Core.Settings;

namespace Urras.Core.Tests;

public static class RepositoryTabsTests
{
    public static void OpensSwitchesAndClosesTabs()
    {
        RepositoryTabs tabs = new RepositoryTabs(Array.Empty<string>(), 0, "/r/urras");
        Assert.Equal("/r/urras", tabs.ActivePath, "the repository last open becomes the first tab");

        tabs.Show("/r/anarres");
        Assert.Equal("/r/anarres", string.Join(",", tabs.Paths), "the current tab shows another repository");
        tabs.ShowInNewTab("/r/urras");
        tabs.ShowInNewTab("/r/lumen");
        Assert.Equal("/r/anarres,/r/urras,/r/lumen", string.Join(",", tabs.Paths), "new tabs open beside");
        Assert.Equal(2, tabs.Active, "the new tab shows");

        tabs.Show("/r/anarres");
        Assert.Equal(0, tabs.Active, "a repository already open switches to its tab");
        Assert.Equal(3, tabs.Paths.Count, "instead of opening it twice");

        tabs.Step(-1);
        Assert.Equal(2, tabs.Active, "Ctrl+Shift+Tab goes round to the end");
        tabs.Step(1);
        Assert.Equal(0, tabs.Active, "Ctrl+Tab goes round to the start");

        Assert.True(tabs.Close(0), "close the showing tab");
        Assert.Equal("/r/urras", tabs.ActivePath, "the one to the right takes over");
        tabs.Step(1);
        Assert.True(tabs.Close(1), "close the last tab");
        Assert.Equal("/r/urras", tabs.ActivePath, "the one to the left takes over");
        Assert.True(!tabs.Close(0), "the only tab stays");

        tabs.ShowInNewTab("/r/lumen");
        tabs.Forget("/r/urras");
        Assert.Equal("/r/lumen", string.Join(",", tabs.Paths), "a removed repository leaves its tab");
        tabs.Forget("/r/lumen");
        Assert.Equal(null, tabs.ActivePath, "no tabs once every repository is gone");
    }

    public static void FollowsAMovedRepository()
    {
        RepositoryTabs tabs = new RepositoryTabs(new[] { "/r/a", "/r/b", "/r/c" }, 1, null);
        tabs.Move("/r/b", "/s/b");
        Assert.Equal("/r/a,/s/b,/r/c", string.Join(",", tabs.Paths), "a moved repository keeps its tab");
        Assert.Equal("/s/b", tabs.ActivePath, "and still shows");
        tabs.Move("/r/c", "/s/b");
        Assert.Equal("/r/a,/s/b", string.Join(",", tabs.Paths), "moving onto an open one leaves one tab");
    }

    public static void RestoresTheSavedTabs()
    {
        RepositoryTabs tabs = new RepositoryTabs(new[] { "/r/a", "/r/b", "/r/a" }, 1, "/r/b");
        Assert.Equal("/r/a,/r/b", string.Join(",", tabs.Paths), "saved tabs, each once");
        Assert.Equal(1, tabs.Active, "the tab that was showing");
        RepositoryTabs lost = new RepositoryTabs(new[] { "/r/a" }, 5, "/r/c");
        Assert.Equal("/r/c", lost.ActivePath, "the repository last open shows even if its tab was lost");
    }
}
