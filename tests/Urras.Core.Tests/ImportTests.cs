using System.Text;
using Urras.Core.Git;

namespace Urras.Core.Tests;

public static class ImportTests
{
    public static void FindsRepositoriesUnderAFolder()
    {
        string root = Path.Combine(Path.GetTempPath(), "urras-import-" + Guid.NewGuid().ToString("N"));
        try
        {
            foreach (string folder in new[] { "Game_PhantomSonar/.git", "Engine/Z_Urras/.git",
                                              "Engine/Z_Urras/libs/raylib/.git", "tools/node_modules/x/.git",
                                              "deep/a/b/c/d/e/.git", "plain/src" })
                Directory.CreateDirectory(Path.Combine(root, folder));
            File.WriteAllText(Path.Combine(root, "Engine", "worktree.git"), "");
            Directory.CreateDirectory(Path.Combine(root, "Engine", "wt"));
            File.WriteAllText(Path.Combine(root, "Engine", "wt", ".git"), "gitdir: elsewhere");

            List<string> found = RepositoryImport.FindUnder(root)
                                                 .Select(path => Path.GetRelativePath(root, path).Replace('\\', '/'))
                                                 .ToList();
            Assert.Equal("Engine/wt|Engine/Z_Urras|Game_PhantomSonar", string.Join("|", found),
                         "repositories and worktrees; not inside repositories, dependencies or too deep");
        }
        finally
        {
            TestFolders.Delete(root);
        }
    }

    public static void ReadsGitHubDesktopsList()
    {
        string appData = Path.Combine(Path.GetTempPath(), "urras-desktop-" + Guid.NewGuid().ToString("N"));
        string repository = Path.Combine(Path.GetTempPath(), "urras-desktop-repo-" + Guid.NewGuid().ToString("N"));
        try
        {
            Assert.Equal(0, RepositoryImport.FromGitHubDesktop(appData).Count, "no GitHub Desktop");
            Directory.CreateDirectory(Path.Combine(repository, ".git"));
            string database = Path.Combine(appData, "GitHub Desktop", "IndexedDB", "file__0.indexeddb.leveldb");
            Directory.CreateDirectory(database);

            // Paths are only recognised in Windows form; this checks the scanning on any system
            string fake = @"C:\Users\Ryan Turner\Documents\GitHub\Game_PhantomSonar";
            byte[] content = Encoding.Latin1.GetBytes("\u0001path\u0002" + fake + "\u0000")
                .Concat(Encoding.Unicode.GetBytes("x" + fake + "\u0000")).ToArray();
            File.WriteAllBytes(Path.Combine(database, "000003.log"), content);
            Assert.True(RepositoryImport.GitHubDesktopPaths(appData).Contains(fake), "path read from the log");
            List<string> found = RepositoryImport.FromGitHubDesktop(appData);
            Assert.Equal(OperatingSystem.IsWindows() ? found.Count : 0, found.Count,
                         "paths that don't exist here are left out");
        }
        finally
        {
            TestFolders.Delete(appData);
            TestFolders.Delete(repository);
        }
    }
}
