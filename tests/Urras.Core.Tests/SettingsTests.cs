using Urras.Core.Settings;

namespace Urras.Core.Tests;

public static class SettingsTests
{
    public static void SaveLoadRoundTripAndDuplicates()
    {
        string folder = "urras-settings-" + Guid.NewGuid().ToString("N");
        string path = Path.Combine(Path.GetTempPath(), folder, "settings.json");
        try
        {
            AppSettings settings = new AppSettings();
            string repo = Path.Combine(Path.GetTempPath(), "SomeRepo");
            Assert.True(settings.AddRepository(repo), "first add");
            Assert.True(!settings.AddRepository(repo + Path.DirectorySeparatorChar), "duplicate with trailing slash");
            settings.LastRepository = repo;
            settings.Save(path);

            AppSettings loaded = AppSettings.Load(path);
            Assert.Equal(1, loaded.Repositories.Count, "one repository");
            Assert.Equal(repo, loaded.LastRepository, "last repository");

            loaded.RemoveRepository(repo);
            Assert.Equal(0, loaded.Repositories.Count, "removed");
            Assert.Equal(null, loaded.LastRepository, "last cleared");
        }
        finally
        {
            Directory.Delete(Path.GetDirectoryName(path)!, true);
        }
    }

    public static void BrokenFileLoadsDefaults()
    {
        string path = Path.Combine(Path.GetTempPath(), "urras-broken-" + Guid.NewGuid().ToString("N") + ".json");
        File.WriteAllText(path, "{ not json");
        try
        {
            Assert.Equal(0, AppSettings.Load(path).Repositories.Count, "defaults");
        }
        finally
        {
            File.Delete(path);
        }
    }
}
