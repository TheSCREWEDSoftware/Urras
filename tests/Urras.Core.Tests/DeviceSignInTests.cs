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
        Assert.True(DeviceSignIn.For(AccountKind.GitHub, string.Empty, http, null) == null ||
                    DeviceSignIn.GITHUB_CLIENT_ID.Length > 0, "no app id: a token is pasted instead");
        Assert.True(DeviceSignIn.For(AccountKind.Gitea, "https://codeberg.org", http, "x") == null,
                    "Gitea has no device flow");
        Assert.True(DeviceSignIn.For(AccountKind.GitLab, "https://gitlab.example.com", http, "x") != null,
                    "a self-managed GitLab with your own app id");
    }
}
