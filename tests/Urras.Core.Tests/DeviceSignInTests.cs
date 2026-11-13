using System.Net;
using System.Text;
using Urras.Core.Accounts;

namespace Urras.Core.Tests;

public static class DeviceSignInTests
{
    // Answers the device code request, then "pending" once, then the token
    private sealed class FakeOAuth : HttpMessageHandler
    {
        private int _polls;

        public List<string> Forms { get; } = new List<string>();

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,
                                                                     CancellationToken cancellationToken)
        {
            Forms.Add(await request.Content!.ReadAsStringAsync(cancellationToken));
            string body = request.RequestUri!.AbsolutePath.EndsWith("/device/code")
                ? "{\"device_code\":\"dc\",\"user_code\":\"ABCD-1234\",\"verification_uri\":" +
                  "\"https://github.com/login/device\",\"interval\":0,\"expires_in\":60}"
                : ++_polls == 1 ? "{\"error\":\"authorization_pending\"}" : "{\"access_token\":\"gho_token\"}";
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json")
            };
        }
    }

    public static async Task SignsInThroughTheBrowser()
    {
        FakeOAuth fake = new FakeOAuth();
        DeviceSignIn flow = DeviceSignIn.For(AccountKind.GitHub, string.Empty, new HttpClient(fake), "my-app-id")!;
        DeviceCode code = await flow.StartAsync();
        Assert.Equal("ABCD-1234", code.UserCode, "code to type");
        Assert.Equal("https://github.com/login/device", code.VerificationUrl, "where to type it");
        Assert.Equal("gho_token", await flow.WaitForTokenAsync(code, CancellationToken.None), "token after approval");
        Assert.True(fake.Forms[0].Contains("client_id=my-app-id") && fake.Forms[0].Contains("scope=repo"),
                    "app id and permissions sent");
        Assert.True(fake.Forms[^1].Contains("grant_type=urn"), "device grant");
    }

    public static void NeedsARegisteredApp()
    {
        HttpClient http = new HttpClient();
        Assert.True(DeviceSignIn.For(AccountKind.GitHub, string.Empty, http, null) != null,
                    "github.com signs in through the browser with Urras's own app");
        Assert.True(DeviceSignIn.For(AccountKind.GitHubEnterprise, "https://github.example.com", http, null) == null,
                    "an Enterprise server needs its own app id: a token is pasted instead");
        Assert.True(DeviceSignIn.For(AccountKind.Gitea, "https://codeberg.org", http, "x") == null,
                    "Gitea has no device flow");
        Assert.True(DeviceSignIn.For(AccountKind.GitLab, "https://gitlab.example.com", http, "x") != null,
                    "a self-managed GitLab with your own app id");
    }

    // GitLab: renews tokens at /oauth/token and says who is signed in at /api/v4/user
    private sealed class FakeGitLab : HttpMessageHandler
    {
        public int Renewals { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,
                                                                     CancellationToken cancellationToken)
        {
            string path = request.RequestUri!.AbsolutePath;
            string body = "{}";
            if (path == "/oauth/token")
            {
                string form = await request.Content!.ReadAsStringAsync(cancellationToken);
                Renewals++;
                body = form.Contains("refresh_token=old-refresh") && form.Contains("client_id=lab-app")
                    ? "{\"access_token\":\"new-token\",\"refresh_token\":\"new-refresh\",\"expires_in\":7200}"
                    : "{\"error\":\"invalid_grant\"}";
            }
            else if (path == "/api/v4/user")
            {
                body = "{\"id\":5,\"username\":\"ryan\",\"name\":\"Ryan\"}";
            }

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json")
            };
        }
    }

    public static async Task RenewsGitLabTokensBeforeTheyExpire()
    {
        string settingsFile = Path.Combine(Path.GetTempPath(), "urras-renew-" + Guid.NewGuid() + ".json");
        Urras.Core.Settings.AppSettings settings = Urras.Core.Settings.AppSettings.Load(settingsFile);
        settings.GitLabClientId = "lab-app";
        MemoryTokenStore tokens = new MemoryTokenStore();
        FakeGitLab fake = new FakeGitLab();
        AccountManager manager = new AccountManager(settings, tokens, new HttpClient(fake));
        try
        {
            Account account = await manager.AddAsync(AccountKind.GitLab, "old-token", "https://gitlab.com/api/v4",
                                                     "old-refresh", DateTimeOffset.UtcNow.AddHours(2));
            Assert.Equal("old-token", await manager.TokenAsync(account), "still good: not renewed");
            Assert.Equal(0, fake.Renewals, "nothing asked");

            account.TokenExpiresAt = DateTimeOffset.UtcNow.AddMinutes(1);
            Assert.Equal("new-token", await manager.TokenAsync(account), "about to expire: renewed");
            Assert.True(account.TokenExpiresAt > DateTimeOffset.UtcNow.AddHours(1), "new expiry kept");
            Assert.Equal("new-token", await manager.AnswerGitAsync("Password for 'https://oauth2@gitlab.com': "),
                         "Git gets the renewed token");

            // The kept refresh token is the new one; a refused renewal leaves the token as it was
            account.TokenExpiresAt = DateTimeOffset.UtcNow;
            Assert.Equal("new-token", await manager.TokenAsync(account), "refused: the old token is kept");
            Assert.Equal(2, fake.Renewals, "asked once more, with the new refresh token");
        }
        finally
        {
            File.Delete(settingsFile);
        }
    }

    // Answers every request with the same body and status
    private sealed class FakeAnswer(HttpStatusCode status, string body, string type) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,
                                                               CancellationToken cancellationToken)
        {
            return Task.FromResult(new HttpResponseMessage(status)
            {
                Content = new StringContent(body, Encoding.UTF8, type)
            });
        }
    }

    public static async Task SaysWhatToFixWhenGitLabRefuses()
    {
        string confidential = await StartErrorAsync(HttpStatusCode.Unauthorized,
                                                    "{\"error\":\"invalid_client\",\"error_description\":\"Client " +
                                                    "authentication failed\"}", "application/json");
        Assert.True(confidential.Contains("must not be Confidential"), "an application marked Confidential");
        string scopes = await StartErrorAsync(HttpStatusCode.BadRequest, "{\"error\":\"invalid_scope\"}",
                                              "application/json");
        Assert.True(scopes.Contains("api and read_user"), "missing scopes");
        string grant = await StartErrorAsync(HttpStatusCode.BadRequest, "{\"error\":\"unauthorized_client\"}",
                                             "application/json");
        Assert.True(grant.Contains("Device authorization grant"), "the device grant box left unticked");
        string old = await StartErrorAsync(HttpStatusCode.NotFound, "<html>Not found</html>", "text/html");
        Assert.True(old.Contains("doesn't offer browser sign-in") && old.Contains("404"), "a GitLab too old");
    }

    private static async Task<string> StartErrorAsync(HttpStatusCode status, string body, string type)
    {
        DeviceSignIn flow = DeviceSignIn.For(AccountKind.GitLab, "https://gitlab.com",
                                             new HttpClient(new FakeAnswer(status, body, type)), "lab-app")!;
        try
        {
            await flow.StartAsync();
            return "no error";
        }
        catch (PlatformException exception)
        {
            return exception.Message;
        }
    }
}
