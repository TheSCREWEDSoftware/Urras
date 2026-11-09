using Urras.Core.Settings;

namespace Urras.Core.Tests;

public static class GroupingTests
{
    public static void ReadsOwnerAndNameFromUrls()
    {
        Assert.Equal(new RemoteIdentity("Aceddian", "azerothcore-wotlk", "github.com"),
                     RemoteIdentity.FromUrl("https://github.com/Aceddian/azerothcore-wotlk.git"), "https");
        Assert.Equal(new RemoteIdentity("TheSCREWEDSoftware", "Urras", "github.com"),
                     RemoteIdentity.FromUrl("git@github.com:TheSCREWEDSoftware/Urras.git"), "scp-like ssh");
        Assert.Equal(new RemoteIdentity("sub", "repo", "gitlab.example.com"),
                     RemoteIdentity.FromUrl("ssh://git@gitlab.example.com:2222/group/sub/repo/"), "ssh with port");
        Assert.Equal("Engine", RemoteIdentity.FromUrl(@"F:\Repos\Engine")!.Name, "local path remote");
        Assert.True(RemoteIdentity.FromUrl("https://host") == null, "no path");
    }

    private static readonly Dictionary<string, RemoteIdentity?> Remotes = new()
    {
        [Path.GetFullPath("/r/a/azerothcore-wotlk")] = new RemoteIdentity("Aceddian", "azerothcore-wotlk",
                                                                          "github.com"),
        [Path.GetFullPath("/r/b/azerothcore-wotlk")] = new RemoteIdentity("TheSCREWEDSoftware", "azerothcore-wotlk",
                                                                          "github.com"),
        [Path.GetFullPath("/r/c/azerothcore-wotlk")] = new RemoteIdentity("TheSCREWEDSoftware", "AzerothCore-wotlk",
                                                                          "github.com"),
        [Path.GetFullPath("/r/mop")] = new RemoteIdentity("Ryan-s-Projects", "azerothcore-mop", "github.com"),
        [Path.GetFullPath("/r/scratch")] = null
    };

    private static string Describe(IEnumerable<RepositoryTreeNode> nodes)
    {
        return string.Join("; ", nodes.Select(node => node.IsGroup
                                                  ? node.Name + "[" + string.Join(", ", node.Children
                                                        .Select(child => child.Name)) + "]"
                                                  : node.Name));
    }

    public static void GroupsByNameWithEachCopyInside()
    {
        List<RepositoryTreeNode> tree = RepositoryLayout.BuildByName(Remotes.Keys, path => Remotes[path]);
        Assert.Equal("azerothcore-mop; azerothcore-wotlk[Aceddian, TheSCREWEDSoftware (b), " +
                     "TheSCREWEDSoftware (c)]; scratch", Describe(tree),
                     "copies under the name, same owner told apart by folder, single copies on their own");
        Assert.Equal("name:azerothcore-wotlk", tree[1].Folder, "a key for remembering collapsed groups");
    }

    // Same name, different projects: only copies sharing a remote or a first commit are grouped
    public static void GroupsOnlyRelatedCopiesByName()
    {
        string screwed = Path.GetFullPath("/w/screwed/wiki");
        string vmangos = Path.GetFullPath("/w/vmangos/wiki");
        string azeroth = Path.GetFullPath("/w/azerothcore/wiki");
        Dictionary<string, RemoteIdentity?> remotes = new()
        {
            [screwed] = new RemoteIdentity("TheSCREWEDSoftware", "wiki", "github.com"),
            [vmangos] = new RemoteIdentity("vmangos", "wiki", "github.com"),
            [azeroth] = new RemoteIdentity("azerothcore", "wiki", "github.com")
        };
        Dictionary<string, string[]> kin = new()
        {
            [screwed] = new[] { "root:aaa" },
            [vmangos] = new[] { "root:bbb" },
            [azeroth] = new[] { "root:aaa" }
        };

        List<RepositoryTreeNode> tree = RepositoryLayout.BuildByName(remotes.Keys, path => remotes[path],
                                                                     path => kin[path]);
        Assert.Equal("wiki (azerothcore, TheSCREWEDSoftware)[azerothcore, TheSCREWEDSoftware]; wiki (vmangos)",
                     Describe(tree),
                     "the fork with its parent; the unrelated wiki on its own, told apart by owner");

        kin[azeroth] = Array.Empty<string>();
        tree = RepositoryLayout.BuildByName(remotes.Keys, path => remotes[path], path => kin[path]);
        Assert.Equal("wiki (azerothcore); wiki (TheSCREWEDSoftware); wiki (vmangos)", Describe(tree),
                     "nothing shared, nothing grouped");
    }

    public static void GroupsByOwnerLikeGitHubDesktop()
    {
        List<RepositoryTreeNode> tree = RepositoryLayout.BuildByOwner(Remotes.Keys, path => Remotes[path]);
        Assert.Equal("Aceddian[azerothcore-wotlk]; Other[scratch]; Ryan-s-Projects[azerothcore-mop]; " +
                     "TheSCREWEDSoftware[azerothcore-wotlk (b), AzerothCore-wotlk (c)]", Describe(tree), "by owner");
    }

    public static async Task ReadsTheOriginOfARealRepository()
    {
        using TempRepository temp = await TempRepository.CreateAsync();
        Assert.True(RemoteIdentity.Read(temp.Path) == null, "no origin yet");
        Assert.Success(await Urras.Core.Git.GitRunner.RunAsync(temp.Path, "remote", "add", "origin",
                                                               "https://github.com/Owner/Thing.git"), "add origin");
        Assert.Equal(new RemoteIdentity("Owner", "Thing", "github.com"), RemoteIdentity.Read(temp.Path), "read");
        Assert.True(RemoteIdentity.Read(Path.Combine(temp.Path, "missing")) == null, "not a repository");
    }

    public static void KeepsPinnedFoldersAsTheyAre()
    {
        string engine = Path.GetFullPath("/f/Echo Flint Engine");
        List<string> repositories = new()
        {
            Path.Combine(engine, "0_Engine", "Echo Flint Engine"),
            Path.Combine(engine, "A_Phantom Sonar", "Game_Phantom Sonar"),
            Path.Combine(engine, "Z_Urras"),
            Path.GetFullPath("/f/Echo Flint Engine Notes/notes"),
            Path.GetFullPath("/r/other")
        };
        List<string> rest = new();
        List<RepositoryTreeNode> tree = RepositoryLayout.BuildWithPins(
            repositories, new[] { engine, Path.Combine(engine, "0_Engine"), Path.GetFullPath("/empty") },
            others =>
            {
                rest.AddRange(others);
                return others.Select(path => new RepositoryTreeNode(Path.GetFileName(path), path, null, null))
                             .ToList();
            });

        Assert.Equal("Echo Flint Engine", tree[0].Name, "pinned folder first, named after itself");
        Assert.True(tree[0].IsPinned && !tree[0].Children[0].IsPinned, "only the pinned group is marked");
        Assert.Equal(engine, tree[0].Folder, "the pinned folder itself, not skipped down");
        Assert.Equal("0_Engine|A_Phantom Sonar|Z_Urras", string.Join("|", tree[0].Children.Select(c => c.Name)),
                     "its layout on disk; the inner pin is covered by the outer one");
        Assert.Equal("notes|other", string.Join("|", rest.Select(Path.GetFileName)),
                     "a folder with a similar name is not inside; the rest is grouped normally");
        Assert.Equal(3, tree.Count, "the empty pin adds nothing");
    }

    public static void PutsPinnedRepositoriesFirst()
    {
        string engine = Path.GetFullPath("/f/Engine");
        string game = Path.GetFullPath("/g/Game");
        string inside = Path.Combine(engine, "Z_Urras");
        List<RepositoryTreeNode> tree = RepositoryLayout.BuildWithPins(
            new[] { inside, game, Path.GetFullPath("/g/Other") }, new[] { engine },
            rest => rest.Select(path => new RepositoryTreeNode(Path.GetFileName(path), path, null, null)).ToList(),
            new[] { game, inside, Path.GetFullPath("/not/in/the/list") });

        Assert.Equal("Game|Engine|Other", string.Join("|", tree.Select(node => node.Name)),
                     "pinned repository, pinned folder, then the rest");
        Assert.True(tree[0].IsPinned && !tree[0].IsGroup, "pinned repository marked");
        Assert.Equal("Z_Urras", tree[1].Children[0].Name, "a pinned repository inside a pinned folder stays there");
    }
}
