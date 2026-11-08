using Urras.Core.Git;
using Urras.Core.Settings;

namespace Urras.Core.Tests;

public static class LayoutTests
{
    // Folder layouts are built from paths alone, so the tests can use folders at the top of the drive
    private static string Root => Path.GetPathRoot(Path.GetTempPath())!;

    private static string P(params string[] parts)
    {
        return Path.Combine(new[] { Root }.Concat(parts).ToArray());
    }

    private static List<string> SampleRepositories()
    {
        return new List<string>
        {
            P("Echo Flint Engine", "0_Engine", "Echo Flint Engine"),
            P("Echo Flint Engine", "0_Engine", "Documentation"),
            P("Echo Flint Engine", "A_Phantom Sonar", "Game_Phantom Sonar"),
            P("Echo Flint Engine", "A_Phantom Sonar", "Assets"),
            P("Echo Flint Engine", "Z_Urras"),
            P("Other", "Solo")
        };
    }

    public static void BuildByFolderMirrorsTheFolders()
    {
        List<RepositoryTreeNode> roots = RepositoryLayout.BuildByFolder(SampleRepositories());

        Assert.Equal(2, roots.Count, "two top-level groups");
        RepositoryTreeNode engine = roots.Single(root => root.Name == "Echo Flint Engine");
        Assert.True(engine.IsGroup, "top level is a group");
        Assert.Equal("0_Engine,A_Phantom Sonar,Z_Urras", string.Join(",", engine.Children.Select(child => child.Name)),
                     "folders and repositories sorted by name");
        Assert.True(engine.Children[2].RepositoryPath != null, "Z_Urras is a repository");
        Assert.Equal("Documentation,Echo Flint Engine",
                     string.Join(",", engine.Children[0].Children.Select(child => child.Name)), "engine folder");

        RepositoryTreeNode other = roots.Single(root => root.Name != "Echo Flint Engine");
        Assert.Equal("Other", other.Name, "a single repository is grouped by its folder");
        Assert.Equal(6, roots.SelectMany(RepositoryLayout.AllRepositories).Count(), "every repository once");

        List<RepositoryTreeNode> deep = RepositoryLayout.BuildByFolder(new[]
        {
            P("SteamLibrary", "steamapps", "common", "Command & Conquer Renegade", "CnC_Renegade-main")
        });
        Assert.Equal("Command & Conquer Renegade", deep.Single().Name, "single-folder chains are skipped");
    }

    public static void ManualLayoutFromTreeKeepsStructureAndFolders()
    {
        List<RepositoryTreeNode> roots = RepositoryLayout.BuildByFolder(SampleRepositories());
        LayoutGroup layout = RepositoryLayout.FromTree(roots);

        Assert.Equal(6, RepositoryLayout.AllRepositories(layout).Count(), "all repositories kept");
        LayoutItem? engineFolder = RepositoryLayout.FindGroupFromFolder(layout, P("Echo Flint Engine", "0_Engine"));
        Assert.True(engineFolder?.Group?.Name == "0_Engine", "group remembers its folder");

        List<RepositoryTreeNode> back = RepositoryLayout.ToTree(layout);
        Assert.Equal(string.Join(",", roots.Select(root => root.Name)),
                     string.Join(",", back.Select(node => node.Name)), "same top level after the round trip");
    }

    public static void MoveReorderAndRemoveGroups()
    {
        LayoutGroup layout = RepositoryLayout.FromTree(RepositoryLayout.BuildByFolder(SampleRepositories()));
        LayoutItem urras = RepositoryLayout.FindRepository(layout, P("Echo Flint Engine", "Z_Urras"))!;
        LayoutItem engine = RepositoryLayout.FindGroupFromFolder(layout, P("Echo Flint Engine", "0_Engine"))!;

        Assert.True(RepositoryLayout.Move(layout, urras, -1), "move up");
        Assert.True(!RepositoryLayout.Move(layout, engine, -1), "first item can't move up");

        LayoutItem tools = RepositoryLayout.AddGroup(layout, "Tools");
        Assert.True(RepositoryLayout.MoveInto(layout, urras, tools.Group), "move into a group");
        Assert.True(tools.Group!.Items.Single() == urras, "repository is in the new group");
        Assert.True(!RepositoryLayout.MoveInto(layout, tools, tools.Group), "a group can't contain itself");

        LayoutItem parentOfEngine = RepositoryLayout.FindGroupFromFolder(layout, P("Echo Flint Engine"))!;
        Assert.True(!RepositoryLayout.MoveInto(layout, parentOfEngine, engine.Group), "no group inside its own child");

        Assert.True(RepositoryLayout.RemoveGroup(layout, tools), "remove group");
        Assert.True(RepositoryLayout.FindParent(layout, urras) == layout, "its repositories stay, at the same level");
    }

    public static void ReconcileAddsNewAndDropsRemoved()
    {
        List<string> repositories = SampleRepositories();
        LayoutGroup layout = RepositoryLayout.FromTree(RepositoryLayout.BuildByFolder(repositories));

        repositories.Remove(P("Other", "Solo"));
        repositories.Add(P("New", "Fresh"));
        RepositoryLayout.Reconcile(layout, repositories);

        List<string> all = RepositoryLayout.AllRepositories(layout).ToList();
        Assert.True(!all.Contains(P("Other", "Solo")), "removed repository dropped");
        Assert.True(layout.Items.Last().Repository == P("New", "Fresh"), "new repository added at the end");
        Assert.Equal(6, all.Count, "count");
    }

    public static void SettingsRoundTripTheLayout()
    {
        string path = Path.Combine(Path.GetTempPath(), "urras-layout-" + Guid.NewGuid().ToString("N") + ".json");
        try
        {
            AppSettings settings = new AppSettings { GroupByFolder = false };
            foreach (string repository in SampleRepositories())
                settings.AddRepository(repository);
            settings.ManualLayout = RepositoryLayout.FromTree(RepositoryLayout.BuildByFolder(settings.Repositories));
            settings.CollapsedFolders.Add(P("Other"));
            settings.Save(path);

            AppSettings loaded = AppSettings.Load(path);
            Assert.True(!loaded.GroupByFolder, "mode saved");
            Assert.Equal(6, RepositoryLayout.AllRepositories(loaded.ManualLayout!).Count(), "layout saved");
            Assert.Equal(P("Other"), loaded.CollapsedFolders.Single(), "collapsed folders saved");

            loaded.RemoveRepository(P("Other", "Solo"));
            Assert.Equal(5, RepositoryLayout.AllRepositories(loaded.ManualLayout!).Count(), "removed from layout too");
        }
        finally
        {
            File.Delete(path);
        }
    }

    public static async Task SummaryReportsEachState()
    {
        RepositorySummary missing = await RepositorySummary.LoadAsync(P("does", "not", "exist"));
        Assert.Equal(RepositoryState.Missing, missing.State, "missing folder");

        using TempRepository local = await TempRepository.CreateAsync();
        local.Write("file.txt", "x\n");
        await local.CommitAllAsync("Initial");
        local.Write("new.txt", "y\n");
        RepositorySummary localSummary = await RepositorySummary.LoadAsync(local.Path);
        Assert.Equal(RepositoryState.LocalOnly, localSummary.State, "no remote");
        Assert.Equal("main", localSummary.Branch, "branch");
        Assert.Equal(1, localSummary.Changes, "one change");

        string subfolder = Path.Combine(local.Path, "sub");
        Directory.CreateDirectory(subfolder);
        Assert.Equal(RepositoryState.Missing, (await RepositorySummary.LoadAsync(subfolder)).State,
                     "a plain folder inside a repository is not a repository");

        using TempRepository remote = await TempRepository.CreateAsync();
        Assert.Success(await GitRunner.RunAsync(local.Path, "remote", "add", "origin", remote.Path), "add remote");
        Assert.Equal(RepositoryState.NotPublished, (await RepositorySummary.LoadAsync(local.Path)).State,
                     "remote but no upstream");

        Assert.Success(await GitRunner.RunAsync(remote.Path, "config", "receive.denyCurrentBranch", "updateInstead"),
                       "allow push to checked out branch");
        Assert.Success(await GitRunner.RunAsync(local.Path, "push", "-u", "origin", "main"), "publish");
        await local.CommitAllAsync("Second");
        RepositorySummary published = await RepositorySummary.LoadAsync(local.Path);
        Assert.Equal(RepositoryState.Published, published.State, "published");
        Assert.Equal("origin", published.UpstreamRemote, "upstream remote");
        Assert.Equal(1, published.Ahead, "one commit to push");
    }
}
