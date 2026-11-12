using Urras.Core.Settings;

namespace Urras.Core.Tests;

public static class ThemeTests
{
    public static void BuiltInThemesHaveEveryColour()
    {
        Theme dark = Theme.BuiltIn(ThemeBase.Dark);
        Theme light = Theme.BuiltIn(ThemeBase.Light);
        foreach (ThemeColour colour in ThemeColours.All)
        {
            Assert.True(ThemeColours.IsColour(colour.Dark) && ThemeColours.IsColour(colour.Light),
                        colour.Key + " defaults are colours");
            Assert.Equal(colour.Dark, dark.Colour(colour.Key), colour.Key + " in Dark");
            Assert.Equal(colour.Light, light.Colour(colour.Key), colour.Key + " in Light");
        }

        Assert.Equal(ThemeColours.All.Count, ThemeColours.All.Select(colour => colour.Key).Distinct().Count(),
                     "keys are unique");
        Assert.True(dark.IsBuiltIn && !dark.Copy("Mine").IsBuiltIn, "copies are custom");
    }

    public static void ChecksAndChangesColours()
    {
        Assert.True(ThemeColours.IsColour("#abc") && ThemeColours.IsColour("#A1B2C3") &&
                    ThemeColours.IsColour("#80A1B2C3"), "short, plain and with alpha");
        Assert.True(!ThemeColours.IsColour("red") && !ThemeColours.IsColour("#12345") &&
                    !ThemeColours.IsColour("#GGGGGG"), "not colours");
        Assert.Equal("#33B083F0", ThemeColours.WithAlpha("#B083F0", 0x33), "faint");
        Assert.Equal("#33AABBCC", ThemeColours.WithAlpha("#ABC", 0x33), "short form widened");
        Assert.Equal("#33112233", ThemeColours.WithAlpha("#FF112233", 0x33), "alpha replaced");
    }

    public static void ThemeFilesGetNewColoursOnLoad()
    {
        string folder = Path.Combine(Path.GetTempPath(), "urras-themes-" + Guid.NewGuid().ToString("N"));
        try
        {
            ThemeStore store = new ThemeStore(folder);
            Directory.CreateDirectory(folder);
            // An older build's file: one colour of its own, one broken, the rest unknown to it
            File.WriteAllText(Path.Combine(folder, "Sunset.json"),
                              "{\"Name\":\"Sunset\",\"Base\":\"Light\",\"Colours\":{\"Text\":\"#112233\"," +
                              "\"Error\":\"oops\"}}");

            Theme theme = store.LoadAll().Single();
            Assert.Equal("#112233", theme.Colour(ThemeColours.TEXT), "its own colour is kept");
            Assert.Equal("#CF222E", theme.Colour(ThemeColours.ERROR), "a broken one takes Light's default");
            Theme? again = Theme.FromJson(File.ReadAllText(store.FileFor("Sunset")));
            Assert.Equal(ThemeColours.All.Count, again!.Colours.Count, "the file now has every key");

            store.Save(theme.Copy("Night: blue"));
            Assert.True(store.Find("night: blue") != null, "found by name, whatever the case");
            Assert.True(File.Exists(Path.Combine(folder, "Night- blue.json")), "unsafe characters replaced");
            File.WriteAllText(Path.Combine(folder, "Broken.json"), "{ not json");
            Assert.Equal(2, store.LoadAll().Count, "a broken file is left out");
        }
        finally
        {
            TestFolders.Delete(folder);
        }
    }

    public static void CopiesRememberTheirOriginalAndText()
    {
        Theme preset = ThemePresets.Find(ThemePresets.Names[0])!;
        Theme copy = preset.Copy("Mine");
        Assert.Equal(preset.Name, copy.CopiedFrom, "a ready-made theme's copy remembers it");
        Assert.Equal(preset.Name, copy.Copy("Mine 2").CopiedFrom, "a copy of a copy keeps the original");
        Assert.Equal(preset.Colour(ThemeColours.SURFACE), copy.Original()!.Colour(ThemeColours.SURFACE),
                     "the original is found again");
        Assert.Equal(Theme.DARK, Theme.BuiltIn(ThemeBase.Dark).Copy("D").CopiedFrom, "built-in colours too");
        Assert.True(new Theme { Name = "Own" }.Original() == null, "a theme of your own has none");

        Assert.True(!copy.HasOwnText, "fonts follow Options until a theme sets its own");
        copy.FontFamily = "Inter";
        copy.FontSize = 16;
        Theme? loaded = Theme.FromJson(copy.ToJson());
        Assert.Equal("Inter", loaded!.FontFamily, "font saved");
        Assert.Equal(16.0, loaded.FontSize, "size saved");
        Assert.True(!Theme.BuiltIn(ThemeBase.Dark).ToJson().Contains("FontSize"), "unset text isn't written");
    }
}
