using Urras.Core.Settings;

namespace Urras.Core.Tests;

public static class ThemePresetTests
{
    public static void ReadyMadeThemesHaveEveryColour()
    {
        Assert.True(ThemePresets.Names.Count >= 12, "the WoW colours, Anarres, GitHub, GitLab, SourceGit and Fork");
        foreach (string name in ThemePresets.Names)
        {
            Theme theme = ThemePresets.Find(name)!;
            Assert.True(theme.IsBuiltIn, name + " isn't a file");
            foreach (ThemeColour colour in ThemeColours.All)
                Assert.True(ThemeColours.IsColour(theme.Colour(colour.Key)), name + " has " + colour.Key);
        }

        Assert.Equal("#0D1117", ThemePresets.Find("github dark")!.Colour(ThemeColours.SURFACE), "any case");
        Assert.Equal(ThemeBase.Light, ThemePresets.Find(ThemePresets.GITHUB_LIGHT)!.Base, "a light one");
        Assert.True(ThemePresets.Find("Nope") == null, "unknown");
    }

    public static void ChangingACopyLeavesThePresetAlone()
    {
        Theme copy = ThemePresets.Find(ThemePresets.FORK_DARK)!.Copy("Fork (mine)");
        copy.Colours[ThemeColours.ACCENT] = "#FF0000";
        Assert.True(!copy.IsBuiltIn, "a copy is a theme file");
        Assert.Equal("#3D8AF7", ThemePresets.Find(ThemePresets.FORK_DARK)!.Colour(ThemeColours.ACCENT), "unchanged");
    }
}
