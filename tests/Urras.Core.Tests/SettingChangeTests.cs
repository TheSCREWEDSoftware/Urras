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

    public static void MovesARepositoryThatWasFoundElsewhere()
    {
        string root = Path.Combine(Path.GetTempPath(), "urras-move");
        string from = Path.Combine(root, "old", "Game");
        string to = Path.Combine(root, "new", "Game");
        AppSettings settings = new AppSettings { LastRepository = from };
        settings.AddRepository(from);
        settings.AddRepository(Path.Combine(root, "Other"));
        settings.PinnedRepositories.Add(from);
        settings.CloneAddresses[from] = "https://github.com/a/game";
        settings.ManualLayout = new LayoutGroup();
        settings.ManualLayout.Items.Add(LayoutItem.ForRepository(from));

        settings.MoveRepository(from, to);
        Assert.True(settings.Repositories.Contains(to) && !settings.Repositories.Contains(from), "list");
        Assert.Equal(to, settings.LastRepository, "last opened");
        Assert.Equal(to, settings.PinnedRepositories[0], "pinned");
        Assert.Equal(to, settings.ManualLayout.Items[0].Repository, "manual layout");
        Assert.Equal(0, settings.CloneAddresses.Count, "the new folder learns its own address");
    }

    public static async Task RemembersTheOriginAddress()
    {
        using TempRepository repository = await TempRepository.CreateAsync();
        Assert.Equal(null, Urras.Core.Settings.RemoteIdentity.OriginUrl(repository.Path), "no origin");
        Assert.Success(await Urras.Core.Git.GitRunner.RunAsync(repository.Path, "remote", "add", "origin",
                                                               "git@github.com:a/game.git"), "origin");
        Assert.Equal("git@github.com:a/game.git", Urras.Core.Settings.RemoteIdentity.OriginUrl(repository.Path),
                     "as written");
    }
}
