using Urras.Core.Settings;

namespace Urras.Core.Tests;

public static class SettingChangeTests
{
    public static void ListsOnlyWhatChanged()
    {
        string theme = "Dark";
        string name = string.Empty;
        string tab = "4";
        SettingTracker tracker = new SettingTracker();
        tracker.Track("Appearance", "Theme", () => theme);
        tracker.Track("Git", "Name", () => name);
        tracker.Track("Appearance", "Tab size", () => tab);
        Assert.Equal(0, tracker.Changes().Count, "nothing changed yet");

        theme = "Light";
        name = "Ryan";
        tab = "8";
        tab = "4";
        List<SettingChange> changes = tracker.Changes();
        Assert.Equal(2, changes.Count, "changed back means unchanged");
        Assert.Equal("Changed Theme from Dark to Light (Appearance)", changes[0].ToString(), "readable");
        Assert.Equal("Changed Name from (empty) to Ryan (Git)", changes[1].ToString(), "empty values named");

        // Loaded later: what it loads is the starting point, not a change
        tracker.Track("Git", "Name", () => name);
        Assert.Equal(1, tracker.Changes().Count, "tracked again from its loaded value");
    }
}
