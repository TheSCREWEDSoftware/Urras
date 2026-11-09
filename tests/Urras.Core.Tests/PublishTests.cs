using System.Net;
using System.Text;
using Urras.Core.Accounts;
using Urras.Core.Git;
using Urras.Core.Settings;

namespace Urras.Core.Tests;

public static class PublishTests
{
    // Answers "METHOD /path" with canned JSON and remembers each request
    private sealed class FakeApi : HttpMessageHandler
    {
        private readonly Dictionary<string, string> _answers;

        public FakeApi(Dictionary<string, string> answers)
        {
            _answers = answers;
        }

        public List<string> Requests { get; } = new List<string>();
        public List<string> Bodies { get; } = new List<string>();

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,
                                                                     CancellationToken cancellationToken)
        {
            string key = request.Method + " " + request.RequestUri!.AbsolutePath;
            Requests.Add(key);
            Bodies.Add(request.Content == null ? string.Empty
                                               : await request.Content.ReadAsStringAsync(cancellationToken));
            bool found = _answers.TryGetValue(key, out string? body);
            return new HttpResponseMessage(found ? HttpStatusCode.OK : HttpStatusCode.NotFound)
            {
                Content = new StringContent(body ?? "{}", Encoding.UTF8, "application/json")
            };
        }
    }

    public static void OffersToPublishARepositoryWithoutARemote()
    {
        RepositoryStatus status = new RepositoryStatus { Branch = "master" };
        DateTimeOffset now = DateTimeOffset.Now;
        SyncState state = SyncState.For(status, null, now, false);
        Assert.Equal(SyncKind.PublishRepository, state.Kind, "no remote: publish the repository");
        Assert.Equal("Publish repository", state.Title, "like GitHub Desktop");
        Assert.Equal(SyncKind.Publish, SyncState.For(status, null, now).Kind, "a remote but no upstream: the branch");
    }

    public static void SuggestsANameTheSiteAccepts()
    {
        Assert.Equal("B_Forlorn-Hope---Complete", RepositoryPublisher.SuggestName("B_Forlorn Hope - Complete"),
                     "spaces become hyphens, like GitHub");
        Assert.Equal("Echo.Flint_Engine-2", RepositoryPublisher.SuggestName(" Echo.Flint_Engine-2 "), "kept");
        Assert.Equal("caf-", RepositoryPublisher.SuggestName("café"), "letters outside ASCII aren't taken");
    }

    public static async Task MakesRepositoriesOnGitHubForTheUserOrAnOrganisation()
    {
        FakeApi fake = new FakeApi(new Dictionary<string, string>
        {
            ["GET /user/orgs"] = "[{\"id\":2,\"login\":\"Phantom-Sonar\"},{\"id\":1,\"login\":\"agents\"}]",
            ["POST /user/repos"] = "{\"full_name\":\"ryan/game\",\"clone_url\":\"https://github.com/ryan/game.git\"," +
                                   "\"private\":true}",
            ["POST /orgs/Phantom-Sonar/repos"] = "{\"full_name\":\"Phantom-Sonar/game\",\"clone_url\":" +
                                                 "\"https://github.com/Phantom-Sonar/game.git\",\"private\":false}"
        });
        GitHubClient client = new GitHubClient(new HttpClient(fake), "t");
        Assert.True(client.CanCreateRepositories, "GitHub can make repositories");

        List<PlatformOrganization> organizations = await client.GetOrganizationsAsync();
        Assert.Equal("agents,Phantom-Sonar", string.Join(",", organizations), "sorted by name");

        PlatformRepository mine = await client.CreateRepositoryAsync(new NewRemoteRepository("game", "", true, null));
        Assert.Equal("https://github.com/ryan/game.git", mine.CloneUrl, "where to push");
        Assert.True(mine.IsPrivate, "private");
        Assert.True(fake.Bodies[^1].Contains("\"private\":true"), "asked for a private repository");

        PlatformRepository theirs = await client.CreateRepositoryAsync(
            new NewRemoteRepository("game", "A game", false, organizations[1]));
        Assert.Equal("Phantom-Sonar/game", theirs.FullName, "made in the organisation");
        Assert.True(fake.Bodies[^1].Contains("A game"), "with the description");
    }

    public static async Task MakesProjectsOnGitLabAndGitea()
    {
        FakeApi lab = new FakeApi(new Dictionary<string, string>
        {
            ["GET /api/v4/groups"] = "[{\"id\":7,\"full_path\":\"team/games\"}]",
            ["POST /api/v4/projects"] = "{\"path_with_namespace\":\"team/games/game\",\"http_url_to_repo\":" +
                                        "\"https://gitlab.com/team/games/game.git\",\"visibility\":\"private\"}"
        });
        GitLabClient gitLab = new GitLabClient(new HttpClient(lab), "t", "https://gitlab.com/api/v4");
        PlatformOrganization group = (await gitLab.GetOrganizationsAsync())[0];
        PlatformRepository project = await gitLab.CreateRepositoryAsync(new NewRemoteRepository("game", "", true,
                                                                                                group));
        Assert.Equal("https://gitlab.com/team/games/game.git", project.CloneUrl, "GitLab's address");
        Assert.True(lab.Bodies[^1].Contains("\"namespace_id\":7"), "in the group");
        Assert.True(lab.Bodies[^1].Contains("\"visibility\":\"private\""), "private");

        FakeApi tea = new FakeApi(new Dictionary<string, string>
        {
            ["POST /api/v1/user/repos"] = "{\"full_name\":\"ryan/game\",\"clone_url\":" +
                                          "\"https://codeberg.org/ryan/game.git\"}"
        });
        GiteaClient gitea = new GiteaClient(new HttpClient(tea), "t", "https://codeberg.org/api/v1");
        Assert.Equal("https://codeberg.org/ryan/game.git",
                     (await gitea.CreateRepositoryAsync(new NewRemoteRepository("game", "", false, null))).CloneUrl,
                     "Gitea's address");

        BitbucketClient bitbucket = new BitbucketClient(new HttpClient(tea), "t");
        Assert.True(!bitbucket.CanCreateRepositories, "Bitbucket isn't offered");
    }

    public static void SaysWhyASiteRefused()
    {
        Assert.Equal(": name already exists on this account",
                     PlatformClient.ErrorDetail("{\"message\":\"Repository creation failed.\",\"errors\":[" +
                                                "{\"resource\":\"Repository\",\"message\":" +
                                                "\"name already exists on this account\"}]}"),
                     "GitHub's reason");
        Assert.Equal(": name has already been taken",
                     PlatformClient.ErrorDetail("{\"message\":{\"name\":[\"has already been taken\"]}}"), "GitLab's");
        Assert.Equal(string.Empty, PlatformClient.ErrorDetail("<html>"), "not JSON");
    }

    public static async Task ConnectsAndPushesTheFirstTime()
    {
        using TempRepository online = await TempRepository.CreateAsync();
        Assert.Success(await GitRunner.RunAsync(online.Path, "config", "receive.denyCurrentBranch", "ignore"), "bare");
        using TempRepository local = await TempRepository.CreateAsync();
        PlatformRepository target = new PlatformRepository("ryan/game", online.Path, null, true, false);

        // Nothing committed yet: only connected
        Assert.Success(await RepositoryPublisher.ConnectAsync(local.Repository, target), "connect an empty one");
        Assert.Equal("origin", string.Join(",", await local.Repository.GetRemotesAsync()), "origin added");
        Assert.True((await local.Repository.GetStatusAsync()).Upstream == null, "nothing pushed yet");

        Assert.Success(await GitRunner.RunAsync(local.Path, "remote", "remove", "origin"), "start again");
        local.Write("a.txt", "a");
        await local.CommitAllAsync("First");
        Assert.Success(await RepositoryPublisher.ConnectAsync(local.Repository, target), "publish");
        Assert.Equal("origin/main", (await local.Repository.GetStatusAsync()).Upstream, "pushed and tracking");
    }
}
