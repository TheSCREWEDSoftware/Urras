using Urras.Core.Git;
using Urras.Core.Settings;

namespace Urras.Core.Tests;

public static class DiscardBackupTests
{
    public static async Task KeepsACopyOfDiscardedFiles()
    {
        using TempRepository repository = await TempRepository.CreateAsync();
        repository.Write("kept.txt", "first");
        await repository.CommitAllAsync("First");
        repository.Write("kept.txt", "edited, then discarded");
        repository.Write("new.txt", "never committed");

        List<FileChange> changes = (await repository.Repository.GetStatusAsync()).Unstaged;
        Assert.Success(await repository.Repository.DiscardAsync(changes), "discard");
        string? folder = repository.Repository.LastDiscardBackup;
        try
        {
            Assert.True(folder != null, "a copy was made");
            Assert.Equal("edited, then discarded", File.ReadAllText(Path.Combine(folder!, "kept.txt")), "the edit");
            Assert.Equal("never committed", File.ReadAllText(Path.Combine(folder!, "new.txt")), "the new file");
            Assert.Equal("first", File.ReadAllText(Path.Combine(repository.Path, "kept.txt")), "still discarded");
        }
        finally
        {
            if (folder != null)
                TestFolders.Delete(folder);
        }
    }

    public static void RemovesCopiesAfterAWeek()
    {
        string root = Path.Combine(Path.GetTempPath(), "urras-discarded-" + Guid.NewGuid().ToString("N"));
        string repository = Path.Combine(Path.GetTempPath(), "urras-repo-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(repository);
        File.WriteAllText(Path.Combine(repository, "a.txt"), "a");
        try
        {
            string old = DiscardBackup.Save(repository, new[] { "a.txt" }, root)!;
            Assert.True(Directory.Exists(old), "saved");
            Assert.True(DiscardBackup.Save(repository, new[] { "missing.txt" }, root) == null, "nothing to copy");

            DateTime later = DateTime.Now + DiscardBackup.KEEP + TimeSpan.FromDays(1);
            DiscardBackup.Save(repository, new[] { "a.txt" }, root, later);
            Assert.True(!Directory.Exists(old), "a week later the old copy is gone");
        }
        finally
        {
            TestFolders.Delete(root);
            TestFolders.Delete(repository);
        }
    }
}
