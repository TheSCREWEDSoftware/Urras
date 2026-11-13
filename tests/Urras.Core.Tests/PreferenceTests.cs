using Urras.Core.Git;
using Urras.Core.Settings;

namespace Urras.Core.Tests;

public static class PreferenceTests
{
    public static void FormatsDatesAsChosen()
    {
        DateTimeOffset date = new DateTimeOffset(2026, 11, 8, 14, 30, 0, TimeZoneInfo.Local.GetUtcOffset(
                                                     new DateTime(2026, 11, 8)));
        try
        {
            Assert.Equal("2026-11-08 14:30", DisplayFormats.DateAndTime(date), "default");
            DisplayFormats.DateFormat = "dd/MM/yyyy";
            DisplayFormats.TimeFormat = "h:mm tt";
            Assert.Equal("08/11/2026 2:30 PM", DisplayFormats.DateAndTime(date), "chosen formats");
            Assert.Equal("08/11/2026", DisplayFormats.Date(date), "date only");
            Assert.Equal("08/11/2026  (dd/MM/yyyy)", DisplayFormats.Example("dd/MM/yyyy"), "example for the list");
            Assert.Equal("DD/MM/YYYY H:MM AM", DisplayFormats.Heading(), "column title");
            Assert.True(DisplayFormats.Full(date, date.AddHours(3))
                                      .StartsWith("Sunday, 08/11/2026 2:30:00 PM (UTC", StringComparison.Ordinal),
                        "tooltip in the chosen format, with seconds");
            Assert.True(DisplayFormats.Full(date, date.AddHours(3))
                                      .EndsWith("), 3 hours ago", StringComparison.Ordinal), "and how long ago");
        }
        finally
        {
            DisplayFormats.DateFormat = DisplayFormats.DEFAULT_DATE;
            DisplayFormats.TimeFormat = DisplayFormats.DEFAULT_TIME;
        }
    }

    public static void BuildsProgramArguments()
    {
        ProgramChoice editor = new ProgramChoice
        {
            Name = ProgramChoice.CUSTOM, Path = "subl", Arguments = "-n \"%TARGET_PATH%\""
        };
        Assert.Equal("-n|/r/My Game", string.Join("|", editor.ArgumentsFor("/r/My Game")), "target filled in");
        ProgramChoice plain = new ProgramChoice { Arguments = "--new-window" };
        Assert.Equal("--new-window|/r", string.Join("|", plain.ArgumentsFor("/r")), "target added at the end");
        Assert.True(editor.IsCustom && !plain.IsCustom, "custom");
    }

    public static async Task ReadsAndWritesGlobalGitSettings()
    {
        // A throwaway global config, so the real one is never touched
        string file = Path.Combine(Path.GetTempPath(), "urras-gitconfig-" + Guid.NewGuid().ToString("N"));
        string? previous = Environment.GetEnvironmentVariable("GIT_CONFIG_GLOBAL");
        Environment.SetEnvironmentVariable("GIT_CONFIG_GLOBAL", file);
        try
        {
            Assert.True(await GlobalConfig.GetAsync(GlobalConfig.DEFAULT_BRANCH) == null, "not set");
            Assert.Success(await GlobalConfig.SetAsync(GlobalConfig.DEFAULT_BRANCH, " main "), "set");
            Assert.Equal("main", await GlobalConfig.GetAsync(GlobalConfig.DEFAULT_BRANCH), "trimmed and read back");
            Assert.Success(await GlobalConfig.SetAsync(GlobalConfig.DEFAULT_BRANCH, ""), "unset");
            Assert.Success(await GlobalConfig.SetAsync(GlobalConfig.DEFAULT_BRANCH, null), "unset again is fine");
            Assert.True(await GlobalConfig.GetAsync(GlobalConfig.DEFAULT_BRANCH) == null, "gone");
        }
        finally
        {
            Environment.SetEnvironmentVariable("GIT_CONFIG_GLOBAL", previous);
            File.Delete(file);
        }
    }

    public static void KeepsPreferencesInTheSettingsFile()
    {
        AppSettings settings = new AppSettings();
        settings.Prompts.DiscardingChanges = false;
        settings.Prompts.SwitchingWithChanges = BranchSwitchChanges.Stash;
        settings.Theme = ThemeChoice.Light;
        string json = System.Text.Json.JsonSerializer.Serialize(settings);
        AppSettings loaded = System.Text.Json.JsonSerializer.Deserialize<AppSettings>(json)!;
        Assert.True(!loaded.Prompts.DiscardingChanges && loaded.Prompts.RemovingRepositories, "prompts");
        Assert.True(json.Contains("\"Stash\"") && json.Contains("\"Light\""), "saved by name");
        Assert.Equal(4, loaded.TabSize, "default tab size");
    }
}
