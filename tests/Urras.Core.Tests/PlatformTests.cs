using System.Net;
using System.Text;
using Urras.Core.Accounts;
using Urras.Core.Settings;

namespace Urras.Core.Tests;

public static class PlatformTests
{
    // Answers by path (without the query) with canned JSON, and remembers each request's method and address
    private sealed class FakeApi : HttpMessageHandler
    {
        private readonly Dictionary<string, string> _answers;

        public FakeApi(Dictionary<string, string> answers)
        {
            _answers = answers;
        }

        public List<string> Requests { get; } = new List<string>();

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,
                                                               CancellationToken cancellationToken)
        {
            Requests.Add(request.Method + " " + request.RequestUri!.PathAndQuery);
            string path = request.RequestUri.AbsolutePath;
            bool found = _answers.TryGetValue(path, out string? body);
            return Task.FromResult(new HttpResponseMessage(found ? HttpStatusCode.OK : HttpStatusCode.NotFound)
            {
                Content = new StringContent(body ?? "{}", Encoding.UTF8, "application/json")
            });
        }
    }

    private static (T Client, FakeApi Fake) Create<T>(Func<HttpClient, T> create, Dictionary<string, string> answers)
    {
        FakeApi fake = new FakeApi(answers);
        return (create(new HttpClient(fake)), fake);
    }

    public static async Task GitHubPullRequestsCommitsAndNotifications()
    {
        (GitHubClient client, FakeApi fake) = Create(http => new GitHubClient(http, "t"), new Dictionary<string, string>
        {
            ["/repos/o/r/pulls"] = "[{\"number\":5,\"title\":\"Fix\",\"user\":{\"login\":\"ana\"}," +
                                   "\"head\":{\"ref\":\"fix\"},\"base\":{\"ref\":\"main\"}," +
                                   "\"html_url\":\"https://github.com/o/r/pull/5\",\"draft\":true}]",
            ["/repos/o/r/commits/abc"] = "{\"author\":{\"id\":9,\"login\":\"bob\",\"avatar_url\":\"https://a/9\"}}",
            ["/notifications"] = "[{\"id\":\"77\",\"reason\":\"review_requested\",\"repository\":{\"full_name\":" +
                                 "\"o/r\"},\"subject\":{\"title\":\"Fix\",\"type\":\"PullRequest\",\"url\":" +
                                 "\"https://api.github.com/repos/o/r/pulls/5\"}}]",
            ["/notifications/threads/77"] = ""
        });

        PlatformPullRequest pull = (await client.GetPullRequestsAsync("o", "r"))[0];
        Assert.Equal(new PlatformPullRequest(5, "Fix", "ana", "fix", "main", "https://github.com/o/r/pull/5", true),
                     pull, "pull request");
        Assert.True(pull.ToString().Contains("(draft)"), "readable");
        Assert.Equal("https://a/9", (await client.GetCommitAuthorAsync("o", "r", "abc", "x@y"))!.AvatarUrl, "author");
        Assert.True(await client.GetCommitAuthorAsync("o", "r", "missing", "x@y") == null, "unknown commit");

        PlatformNotification notification = (await client.GetNotificationsAsync())[0];
        Assert.Equal("https://github.com/o/r/pull/5", notification.Url, "API address turned into the web page");
        await client.MarkNotificationReadAsync(notification);
        Assert.True(fake.Requests.Contains("PATCH /notifications/threads/77"), "marked as read");
        Assert.Equal("https://github.com/o/r/compare/main...fix?expand=1",
                     client.NewPullRequestUrl("o", "r", "fix", "main"), "new pull request page");
    }

    public static async Task GitLabMergeRequestsAndAvatars()
    {
        string api = Account.ApiFor(AccountKind.GitLab, null);
        Assert.Equal("https://gitlab.com/api/v4", api, "gitlab.com by default");
        (GitLabClient client, FakeApi fake) = Create(http => new GitLabClient(http, "t", api),
                                                     new Dictionary<string, string>
        {
            ["/api/v4/user"] = "{\"id\":3,\"username\":\"me\",\"name\":\"Me\",\"commit_email\":\"me@x\"}",
            ["/api/v4/users"] = "[{\"id\":4,\"username\":\"ana\",\"name\":\"Ana\"}]",
            ["/api/v4/projects/o%2Fr/merge_requests"] = "[{\"iid\":2,\"title\":\"MR\",\"author\":{\"username\":" +
                                                        "\"ana\"},\"source_branch\":\"f\",\"target_branch\":\"main\"," +
                                                        "\"web_url\":\"https://gitlab.com/o/r/-/merge_requests/2\"}]",
            ["/api/v4/avatar"] = "{\"avatar_url\":\"https://gl/a.png\"}",
            ["/api/v4/projects"] = "[{\"path_with_namespace\":\"o/r\",\"http_url_to_repo\":\"https://gitlab.com/o/r.git\"," +
                                   "\"visibility\":\"private\",\"forked_from_project\":{\"id\":1}}]"
        });

        Account account = await client.GetAccountAsync();
        Assert.True(account.Username == "me" && account.Email == "me@x", "account");
        Assert.Equal("gitlab.com", account.WebHost, "web host");
        Urras.Core.Git.CoAuthor person = client.CoAuthorFor((await client.GetUserAsync("ana"))!);
        Assert.Equal("4-ana@users.noreply.gitlab.com", person.Email, "GitLab private address");
        Assert.Equal("f", (await client.GetPullRequestsAsync("o", "r"))[0].SourceBranch, "merge request");
        Assert.Equal("https://gl/a.png", (await client.GetCommitAuthorAsync("o", "r", "h", "a@b"))!.AvatarUrl,
                     "picture by email");
        PlatformRepository repository = (await client.GetRepositoriesAsync())[0];
        Assert.True(repository.IsPrivate && repository.IsFork, "private fork");
        Assert.Equal("merge-requests/2/head", client.PullRequestRef(2), "ref for checking out");
        Assert.True(fake.Requests.Any(request => request.Contains("email=a%40b")), "email sent escaped");
    }

    public static async Task GiteaAndBitbucket()
    {
        (GiteaClient gitea, _) = Create(http => new GiteaClient(http, "t", "https://codeberg.org/api/v1"),
                                        new Dictionary<string, string>
        {
            ["/api/v1/users/search"] = "{\"data\":[{\"id\":5,\"login\":\"kim\",\"full_name\":\"\"}]}"
        });
        List<PlatformUser> found = await gitea.SearchUsersAsync("ki");
        Assert.Equal("kim@noreply.codeberg.org", gitea.CoAuthorFor(found[0]).Email, "Gitea private address");

        (BitbucketClient bitbucket, _) = Create(http => new BitbucketClient(http, "user:app-password"),
                                                new Dictionary<string, string>
        {
            ["/2.0/repositories"] = "{\"values\":[{\"full_name\":\"team/app\",\"is_private\":true,\"links\":" +
                                    "{\"clone\":[{\"name\":\"ssh\",\"href\":\"git@x\"},{\"name\":\"https\"," +
                                    "\"href\":\"https://bitbucket.org/team/app.git\"}]}}]}"
        });
        PlatformRepository repository = (await bitbucket.GetRepositoriesAsync())[0];
        Assert.Equal("https://bitbucket.org/team/app.git", repository.CloneUrl, "HTTPS clone address");
        Assert.True(bitbucket.CoAuthorFor(new PlatformUser(0, "x", null, null)) == null, "no private addresses");
        Assert.True(bitbucket.PullRequestRef(1) == null, "pull requests use their branch");
        Assert.Equal("https://bitbucket.example.com/rest/api/1.0",
                     Account.ApiFor(AccountKind.BitbucketServer, "https://bitbucket.example.com/"), "server API");
    }

    // A next-page link pointing elsewhere is refused instead of being sent the token
    public static async Task TokenStaysOnThePlatform()
    {
        (BitbucketClient bitbucket, FakeApi fake) = Create(http => new BitbucketClient(http, "user:app-password"),
                                                           new Dictionary<string, string>
        {
            ["/2.0/repositories"] = "{\"values\":[],\"next\":\"https://attacker.example/steal\"}"
        });
        bool refused = false;
        try
        {
            await bitbucket.GetRepositoriesAsync();
        }
        catch (PlatformException)
        {
            refused = true;
        }

        Assert.True(refused, "foreign next page refused");
        Assert.Equal(1, fake.Requests.Count, "nothing sent to the other host");
    }

    public static async Task LearnsPicturesForCommitEmails()
    {
        using TempRepository temp = await TempRepository.CreateAsync();
        Assert.Success(await Urras.Core.Git.GitRunner.RunAsync(temp.Path, "remote", "add", "origin",
                                                               "https://github.com/o/r.git"), "origin");
        temp.Write("a.txt", "a\n");
        await temp.CommitAllAsync("First");
        List<Urras.Core.Git.CommitInfo> history = await temp.Repository.GetHistoryAsync();

        string file = Path.Combine(Path.GetTempPath(), "urras-pictures-" + Guid.NewGuid().ToString("N") + ".json");
        AppSettings settings = new AppSettings();
        settings.Accounts.Add(new Account { Kind = AccountKind.GitHub, ApiUrl = GitHubClient.GITHUB_API,
                                            Username = "me" });
        MemoryTokenStore tokens = new MemoryTokenStore();
        await tokens.SaveAsync(settings.Accounts[0].TokenKey, "t");
        FakeApi fake = new FakeApi(new Dictionary<string, string>
        {
            ["/repos/o/r/commits/" + history[0].Hash] = "{\"author\":{\"id\":1,\"login\":\"me\",\"avatar_url\":" +
                                                        "\"https://a/1\"}}"
        });
        try
        {
            AccountManager manager = new AccountManager(settings, tokens, new HttpClient(fake), file);
            bool changed = false;
            manager.PicturesChanged += () => changed = true;
            Assert.Equal(1, await manager.LearnPicturesAsync(temp.Path, history), "one picture found");
            Assert.True(changed, "views told");
            Assert.Equal("https://a/1", manager.AvatarUrlFor(history[0].Email), "picture for the email");
            Assert.Equal(0, await manager.LearnPicturesAsync(temp.Path, history), "not asked again");

            AccountManager reloaded = new AccountManager(settings, tokens, new HttpClient(fake), file);
            Assert.Equal("https://a/1", reloaded.AvatarUrlFor(history[0].Email), "kept between runs");
        }
        finally
        {
            File.Delete(file);
        }
    }

    public static async Task ChecksOutPullRequestsAndFindsTheMainBranch()
    {
        using TempRepository origin = await TempRepository.CreateAsync();
        origin.Write("a.txt", "a\n");
        await origin.CommitAllAsync("Base");
        Assert.Success(await Urras.Core.Git.GitRunner.RunAsync(origin.Path, "checkout", "-q", "-b", "feature"),
                       "branch");
        origin.Write("b.txt", "b\n");
        await origin.CommitAllAsync("Feature");
        // Platforms publish pull requests under refs like these
        Assert.Success(await Urras.Core.Git.GitRunner.RunAsync(origin.Path, "update-ref", "refs/pull/5/head",
                                                               "HEAD"), "pull request ref");
        Assert.Success(await Urras.Core.Git.GitRunner.RunAsync(origin.Path, "checkout", "-q", "main"), "main");

        string clonePath = origin.Path + "-clone";
        try
        {
            Assert.Success(await Urras.Core.Git.GitRepository.CloneAsync(origin.Path, clonePath), "clone");
            Urras.Core.Git.GitRepository clone = new Urras.Core.Git.GitRepository(clonePath);
            Assert.Equal("main", await clone.GetDefaultBranchAsync(), "origin's main branch");
            Assert.Success(await clone.CheckOutPullRequestAsync("pull/5/head", "pr/5"), "check out");
            Assert.True(File.Exists(Path.Combine(clonePath, "b.txt")), "the pull request's files");
            Assert.Equal("pr/5", (await clone.GetStatusAsync()).Branch, "on its branch");
        }
        finally
        {
            if (Directory.Exists(clonePath))
                TestFolders.Delete(clonePath);
        }
    }

    public static async Task ReadsCiChecksForACommit()
    {
        (GitHubClient github, _) = Create(http => new GitHubClient(http, "t"), new Dictionary<string, string>
        {
            ["/repos/ryan/game/commits/abc/check-runs"] =
                "{\"check_runs\":[{\"status\":\"completed\",\"conclusion\":\"success\"}," +
                "{\"status\":\"completed\",\"conclusion\":\"failure\"}," +
                "{\"status\":\"in_progress\",\"conclusion\":null}," +
                "{\"status\":\"completed\",\"conclusion\":\"skipped\"}]}",
            ["/repos/ryan/game/commits/abc/status"] = "{\"statuses\":[{\"state\":\"success\"}]}"
        });
        CommitChecks checks = (await github.GetChecksAsync("ryan", "game", "abc"))!;
        Assert.Equal((2, 1, 1, 1), (checks.Passed, checks.Failed, checks.Running, checks.Skipped),
                     "check runs and statuses together");
        Assert.Equal(CheckState.Failed, checks.State, "a failure decides");
        Assert.Equal("1 of 5 checks failed", checks.Summary, "summary");
        Assert.Equal("https://github.com/ryan/game/commit/abc", checks.WebUrl, "where to read more");

        (GitLabClient gitlab, _) = Create(http => new GitLabClient(http, "t", "https://gitlab.com/api/v4"),
                                          new Dictionary<string, string>
        {
            ["/api/v4/projects/ryan%2Fgame/repository/commits/abc/statuses"] =
                "[{\"status\":\"success\"},{\"status\":\"failed\",\"allow_failure\":true}," +
                "{\"status\":\"manual\"}]"
        });
        CommitChecks pipeline = (await gitlab.GetChecksAsync("ryan", "game", "abc"))!;
        Assert.Equal(CheckState.Passed, pipeline.State, "a job allowed to fail doesn't fail it");
        Assert.Equal("2 checks passed, 1 skipped", pipeline.Summary, "summary");
        Assert.Equal("https://gitlab.com/ryan/game/-/commit/abc/pipelines", pipeline.WebUrl, "pipelines page");

        Assert.True(CommitChecks.Count(Array.Empty<CheckState>(), "x") == null, "no CI, nothing shown");
        Assert.Equal("3 of 4 checks running",
                     CommitChecks.Count(new[] { CheckState.Running, CheckState.Running, CheckState.Running,
                                                CheckState.Passed }, "x")!.Summary, "still going");
    }
}
