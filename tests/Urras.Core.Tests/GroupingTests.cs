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
}
