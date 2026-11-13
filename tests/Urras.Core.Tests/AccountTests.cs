using System.Net;
using System.Text;
using Urras.Core.Accounts;
using Urras.Core.Settings;

namespace Urras.Core.Tests;

public static class AccountTests
{
    // Answers GitHub API paths with canned JSON and remembers what was asked
    private sealed class FakeGitHub : HttpMessageHandler
    {
        public List<HttpRequestMessage> Requests { get; } = new List<HttpRequestMessage>();

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,
                                                               CancellationToken cancellationToken)
        {
            Requests.Add(request);
            string path = request.RequestUri!.PathAndQuery;
            bool authorized = request.Headers.Authorization?.Parameter == "good-token";
            (HttpStatusCode status, string body) = path switch
            {
                "/user" when !authorized => (HttpStatusCode.Unauthorized, "{}"),
                "/user" => (HttpStatusCode.OK, "{\"id\":42,\"login\":\"FlyingArowana\",\"name\":\"Ryan\"," +
                                               "\"email\":null,\"avatar_url\":\"https://avatars/42\"}"),
                "/users/Nyeriah" => (HttpStatusCode.OK, "{\"id\":123,\"login\":\"Nyeriah\",\"name\":null}"),
                _ when path.StartsWith("/search/users") =>
                    (HttpStatusCode.OK, "{\"items\":[{\"id\":7,\"login\":\"nyx\"},{\"id\":8,\"login\":\"nye\"}]}"),
                _ => (HttpStatusCode.NotFound, "{}")
            };
            return Task.FromResult(new HttpResponseMessage(status)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json")
            });
        }
    }

    private static (AccountManager Manager, AppSettings Settings, MemoryTokenStore Tokens, FakeGitHub Fake) Create()
    {
        string settingsFile = Path.Combine(Path.GetTempPath(), "urras-accounts-" + Guid.NewGuid() + ".json");
        AppSettings settings = AppSettings.Load(settingsFile);
        MemoryTokenStore tokens = new MemoryTokenStore();
        FakeGitHub fake = new FakeGitHub();
        return (new AccountManager(settings, tokens, new HttpClient(fake)), settings, tokens, fake);
    }

    public static async Task AddsAGitHubAccountWithAToken()
    {
        (AccountManager manager, AppSettings settings, MemoryTokenStore tokens, FakeGitHub fake) = Create();
        Account account = await manager.AddGitHubAsync(" good-token ");

        Assert.Equal("FlyingArowana", account.Username, "login");
        Assert.Equal(AccountKind.GitHub, account.Kind, "github.com");
        Assert.Equal("good-token", await tokens.LoadAsync(account.TokenKey), "token kept in the store, trimmed");
        Assert.Equal(1, settings.Accounts.Count, "account kept in the settings");
        Assert.True(!System.Text.Json.JsonSerializer.Serialize(settings).Contains("good-token"),
                    "the settings never hold the token");
        Assert.True(fake.Requests[0].Headers.UserAgent.ToString().Contains("Urras"), "GitHub needs a user agent");
        Assert.True(account.ToString().StartsWith("@FlyingArowana"), "readable");

        await manager.AddGitHubAsync("good-token");
        Assert.Equal(1, settings.Accounts.Count, "adding again replaces it");
        await manager.RemoveAsync(account);
        Assert.True(settings.Accounts.Count == 0 && await tokens.LoadAsync(account.TokenKey) == null, "removed");
    }

    public static async Task AnswersGitLoginsFromTheAccount()
    {
        (AccountManager manager, AppSettings settings, MemoryTokenStore tokens, FakeGitHub _) = Create();
        await manager.AddGitHubAsync("good-token");

        Assert.Equal("FlyingArowana", await manager.AnswerGitAsync("Username for 'https://github.com': "), "login");
        Assert.Equal("good-token", await manager.AnswerGitAsync(
            "Password for 'https://FlyingArowana@github.com': "), "token as the password");
        Assert.Equal(null, await manager.AnswerGitAsync("Password for 'https://someone@github.com': "),
                     "another user is asked in a dialog");
        Assert.Equal(null, await manager.AnswerGitAsync("Username for 'https://gitlab.com': "), "no account there");
        Assert.Equal(null, await manager.AnswerGitAsync("Enter passphrase for key '/home/me/.ssh/id_ed25519': "),
                     "SSH questions are not logins");

        // Bitbucket app passwords are kept as username:app-password and given to Git as the two halves
        Account bitbucket = new Account { Kind = AccountKind.Bitbucket, ApiUrl = BitbucketClient.BITBUCKET_API,
                                          Username = "ryan" };
        settings.Accounts.Add(bitbucket);
        await tokens.SaveAsync(bitbucket.TokenKey, "ryan-b:app-secret");
        Assert.Equal("ryan-b", await manager.AnswerGitAsync("Username for 'https://bitbucket.org': "), "its user");
        Assert.Equal("app-secret", await manager.AnswerGitAsync("Password for 'https://ryan-b@bitbucket.org': "),
                     "its app password");
    }

    public static async Task RefusesBadTokens()
    {
        (AccountManager manager, AppSettings settings, MemoryTokenStore _, FakeGitHub _) = Create();
        try
        {
            await manager.AddGitHubAsync("bad-token");
            Assert.True(false, "a refused token throws");
        }
        catch (PlatformException exception)
        {
            Assert.True(exception.Message.Contains("refused"), "readable message: " + exception.Message);
        }

        Assert.Equal(0, settings.Accounts.Count, "nothing kept");
    }

    public static async Task FindsCoAuthorsOnGitHub()
    {
        (AccountManager manager, _, _, _) = Create();
        Assert.True(await manager.FindCoAuthorAsync("@Nyeriah") == null, "nothing without an account");
        await manager.AddGitHubAsync("good-token");

        Urras.Core.Git.CoAuthor? person = await manager.FindCoAuthorAsync("@Nyeriah");
        Assert.Equal("123+Nyeriah@users.noreply.github.com", person!.Email, "private address");
        Assert.Equal("@Nyeriah", person.Label, "shown by username");
        Assert.True(await manager.FindCoAuthorAsync("@nobody") == null, "unknown user");
        Assert.Equal("nyx|nye", string.Join("|", (await manager.SearchCoAuthorsAsync("@ny")).Select(p => p.Username)),
                     "search");
        Assert.Equal(0, (await manager.SearchCoAuthorsAsync("@n")).Count, "too short to search");
    }

    public static void WorksOutPictureAddresses()
    {
        (AccountManager manager, AppSettings settings, _, _) = Create();
        settings.Accounts.Add(new Account { ApiUrl = GitHubClient.GITHUB_API, Username = "me",
                                            Email = "me@example.com", AvatarUrl = "https://avatars/me" });
        Assert.Equal("https://avatars/me", manager.AvatarUrlFor("ME@example.com"), "own account");
        Assert.Equal("https://avatars.githubusercontent.com/u/123?s=64",
                     manager.AvatarUrlFor("123+Nyeriah@users.noreply.github.com"), "noreply with id");
        Assert.Equal("https://github.com/old.png?size=64", manager.AvatarUrlFor("old@users.noreply.github.com"),
                     "older noreply");
        Assert.True(manager.AvatarUrlFor("someone@example.com") == null, "unknown without asking");
        Assert.Equal("https://avatars.githubusercontent.com/u/41898282?s=64",
                     manager.AvatarUrlFor("github-actions[bot]@users.noreply.github.com"), "GitHub Actions' picture");
        Assert.Equal("https://github.com/claude.png?size=64", manager.AvatarUrlFor("noreply@anthropic.com"),
                     "an address GitHub links to an account");
        Assert.Equal("https://github.com/apps/github-actions", GitHubClient.ProfileUrl("github-actions[bot]"),
                     "a bot's page");
        Assert.Equal("https://github.com/Nyeriah", GitHubClient.ProfileUrl("Nyeriah"), "a person's page");
    }

    public static void LinksEachAccountToItsProfile()
    {
        Account github = new Account { Kind = AccountKind.GitHub, ApiUrl = GitHubClient.GITHUB_API, Username = "me" };
        Assert.Equal("https://github.com/me", github.ProfileUrl, "GitHub");
        Account gitlab = new Account { Kind = AccountKind.GitLab, ApiUrl = Account.ApiFor(AccountKind.GitLab, null),
                                       Username = "TheSCREWEDSoftware" };
        Assert.Equal("https://gitlab.com/TheSCREWEDSoftware", gitlab.ProfileUrl, "gitlab.com");
        Account own = new Account { Kind = AccountKind.GitLab, Username = "ryan",
                                    ApiUrl = Account.ApiFor(AccountKind.GitLab, "https://example.org/gitlab") };
        Assert.Equal("https://example.org/gitlab/ryan", own.ProfileUrl, "a server under a path");
        Account server = new Account { Kind = AccountKind.BitbucketServer, Username = "ryan",
                                       ApiUrl = Account.ApiFor(AccountKind.BitbucketServer, "https://git.example.org") };
        Assert.Equal("https://git.example.org/users/ryan", server.ProfileUrl, "Bitbucket Server");
    }
}
