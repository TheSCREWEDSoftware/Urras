using System.Net;
using System.Text;
using Urras.Core.Accounts;
using Urras.Core.Git;
using Urras.Core.Settings;

namespace Urras.Core.Tests;

public static class ForkTests
{
    private sealed class FakeApi : HttpMessageHandler
    {
        private readonly Dictionary<string, string> _answers;

        public FakeApi(Dictionary<string, string> answers)
        {
            _answers = answers;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,
                                                               CancellationToken cancellationToken)
        {
            bool found = _answers.TryGetValue(request.RequestUri!.AbsolutePath, out string? body);
            return Task.FromResult(new HttpResponseMessage(found ? HttpStatusCode.OK : HttpStatusCode.NotFound)
            {
                Content = new StringContent(body ?? "{}", Encoding.UTF8, "application/json")
            });
        }
    }

    public static async Task GitHubTellsForksAndPushAccess()
    {
        GitHubClient client = new GitHubClient(new HttpClient(new FakeApi(new Dictionary<string, string>
        {
            ["/repos/TheSCREWEDSoftware/wiki"] = "{\"fork\":true,\"permissions\":{\"push\":true}," +
                                                 "\"parent\":{\"full_name\":\"azerothcore/wiki\"}}",
            ["/repos/azerothcore/wiki"] = "{\"fork\":false,\"permissions\":{\"push\":false}}"
        })), "t");

        PlatformAccess fork = (await client.GetAccessAsync("TheSCREWEDSoftware", "wiki"))!;
        Assert.Equal(new PlatformAccess(true, "azerothcore", "wiki"), fork, "fork with push access");
        Assert.True(fork.IsFork, "is a fork");
        PlatformAccess parent = (await client.GetAccessAsync("azerothcore", "wiki"))!;
        Assert.True(!parent.IsFork && !parent.CanPush, "parent, read only");
        Assert.Equal("https://github.com/azerothcore/wiki/compare/master...TheSCREWEDSoftware:fix%2Ftypo",
                     client.NewForkPullRequestUrl("azerothcore", "wiki", "TheSCREWEDSoftware", "fix/typo", "master"),
                     "pull request from the fork into the parent");
        Assert.Equal(("group/sub", "project"), PlatformAccess.Split("group/sub/project"), "nested owners");
        Assert.Equal(((string?)null, (string?)null), PlatformAccess.Split("plain"), "not a full name");
    }

    public static void ChoosesWhereContributionsGo()
    {
        RemoteIdentity fork = new RemoteIdentity("TheSCREWEDSoftware", "wiki", "github.com");
        RemoteIdentity parent = new RemoteIdentity("azerothcore", "wiki", "github.com");
        Assert.Equal(parent, new ForkSettings(parent, ForkTarget.Unset).ContributionTarget(fork), "parent by default");
        Assert.Equal(parent, new ForkSettings(parent, ForkTarget.Parent).ContributionTarget(fork), "parent");
        Assert.Equal(fork, new ForkSettings(parent, ForkTarget.Own).ContributionTarget(fork), "own purposes");
        Assert.Equal(fork, new ForkSettings(null, ForkTarget.Unset).ContributionTarget(fork), "not a fork");

        Dictionary<string, RemoteIdentity> remotes = new Dictionary<string, RemoteIdentity>
        {
            [RemoteIdentity.ORIGIN] = fork,
            [ForkSettings.UPSTREAM] = parent
        };
        Assert.Equal(parent, ForkSettings.ParentFrom(" azerothcore/wiki\n", fork, remotes), "saved parent");
        Assert.Equal(parent, ForkSettings.ParentFrom(null, fork, remotes), "upstream remote");
        Assert.True(ForkSettings.ParentFrom(null, fork, new Dictionary<string, RemoteIdentity>()) == null, "none");
        Assert.Equal(ForkTarget.Own, ForkSettings.ParseTarget("own\n"), "parse");
        Assert.Equal("https://github.com/azerothcore/wiki", parent.WebUrl, "web page");
        Assert.True(RemoteIdentity.FromUrl(@"F:\Repos\Engine")!.WebUrl == null, "no page for local paths");
    }

    public static async Task KeepsTheChoiceInTheRepository()
    {
        using TempRepository temp = await TempRepository.CreateAsync();
        GitRepository repository = new GitRepository(temp.Path);
        Assert.Success(await GitRunner.RunAsync(temp.Path, "remote", "add", "origin",
                                                "git@github.com:TheSCREWEDSoftware/wiki.git"), "origin");
        Assert.Success(await GitRunner.RunAsync(temp.Path, "remote", "add", "upstream",
                                                "https://github.com/vmangos/wiki.git"), "upstream");
        Assert.Equal(2, RemoteIdentity.ReadAll(temp.Path).Count, "both remotes read");

        ForkSettings fresh = await repository.GetForkSettingsAsync();
        Assert.Equal("vmangos", fresh.Parent?.Owner, "upstream until the platform says otherwise");
        Assert.Equal(ForkTarget.Unset, fresh.Target, "not chosen");

        await repository.SaveForkSettingsAsync(new RemoteIdentity("azerothcore", "wiki", "github.com"), ForkTarget.Own);
        ForkSettings saved = await repository.GetForkSettingsAsync();
        Assert.Equal(new ForkSettings(new RemoteIdentity("azerothcore", "wiki", "github.com"), ForkTarget.Own), saved,
                     "saved in the repository's configuration");

        await repository.SaveForkSettingsAsync(null, ForkTarget.Unset);
        Assert.Equal("vmangos", (await repository.GetForkSettingsAsync()).Parent?.Owner, "cleared");
    }

    public static async Task FindsTheFirstCommits()
    {
        using TempRepository temp = await TempRepository.CreateAsync();
        GitRepository repository = new GitRepository(temp.Path);
        Assert.Equal(0, (await repository.GetRootCommitsAsync()).Count, "no commits yet");
        File.WriteAllText(Path.Combine(temp.Path, "a.txt"), "a");
        Assert.Success(await GitRunner.RunAsync(temp.Path, "add", "a.txt"), "add");
        Assert.Success(await GitRunner.RunAsync(temp.Path, "commit", "-m", "First"), "commit");
        Assert.Equal(1, (await repository.GetRootCommitsAsync()).Count, "one first commit");
    }
}

public static class BranchChangesTests
{
    public static async Task KeepsChangesWithTheirBranch()
    {
        using TempRepository temp = await TempRepository.CreateAsync();
        GitRepository repository = new GitRepository(temp.Path);
        File.WriteAllText(Path.Combine(temp.Path, "a.txt"), "a");
        Assert.Success(await GitRunner.RunAsync(temp.Path, "add", "a.txt"), "add");
        Assert.Success(await GitRunner.RunAsync(temp.Path, "commit", "-m", "First"), "commit");
        Assert.Success(await GitRunner.RunAsync(temp.Path, "branch", "other"), "branch");
        string main = (await GitRunner.RunAsync(temp.Path, "branch", "--show-current")).Output.Trim();

        File.WriteAllText(Path.Combine(temp.Path, "a.txt"), "changed");
        File.WriteAllText(Path.Combine(temp.Path, "new.txt"), "new");
        Assert.Success(await GitRunner.RunAsync(temp.Path, "add", "a.txt"), "stage");
        Assert.Success(await repository.KeepChangesWithBranchAsync(main), "keep");
        Assert.Equal(string.Empty, (await GitRunner.RunAsync(temp.Path, "status", "--porcelain")).Output.Trim(),
                     "clean once kept, untracked files too");

        Assert.Success(await repository.SwitchAsync("other"), "switch away");
        Assert.True(await repository.RestoreKeptChangesAsync("other") == null, "nothing kept with other");
        Assert.Success(await repository.SwitchAsync(main), "switch back");
        Assert.Success((await repository.RestoreKeptChangesAsync(main))!, "restore");
        Assert.Equal("M  a.txt|?? new.txt",
                     string.Join("|", (await GitRunner.RunAsync(temp.Path, "status", "--porcelain")).Output
                                      .Split('\n', StringSplitOptions.RemoveEmptyEntries)),
                     "back as they were, staging included");
        Assert.Equal(0, (await repository.GetStashesAsync()).Count, "the kept stash is used up");
    }

    public static void WritesTheCrashLog()
    {
        string path = Path.Combine(Path.GetTempPath(), "urras-crash-" + Guid.NewGuid().ToString("N"), "crash.log");
        try
        {
            Assert.Equal(path, Urras.Core.Settings.CrashLog.Write(new InvalidOperationException("Boom"), "Test", path),
                         "written");
            Assert.True(File.ReadAllText(path).Contains("Boom"), "has the error");
        }
        finally
        {
            Directory.Delete(Path.GetDirectoryName(path)!, true);
        }
    }
}
