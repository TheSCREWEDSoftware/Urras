using System.Net;
using System.Text;
using Urras.Core.Settings;

namespace Urras.Core.Tests;

public static class UpdateAndInstanceTests
{
    // Answers every request with the given latest release
    private sealed class FakeGitHub : HttpMessageHandler
    {
        private readonly string _tag;

        public FakeGitHub(string tag)
        {
            _tag = tag;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,
                                                               CancellationToken cancellationToken)
        {
            string body = "{\"tag_name\":\"" + _tag + "\",\"html_url\":\"https://github.com/x/Urras/releases/" + _tag +
                          "\"}";
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json")
            });
        }
    }

    public static void ReadsVersions()
    {
        Assert.Equal(new Version(0, 2, 0), UpdateCheck.Parse("v0.2.0"), "a tag");
        Assert.Equal(new Version(0, 1, 0), UpdateCheck.Parse("0.1.0+7a41306f2ab2"), "the app's own version");
        Assert.Equal(new Version(0, 2, 0), UpdateCheck.Parse("v0.2-beta.1"), "short, with a pre-release part");
        Assert.True(UpdateCheck.Parse("nightly") == null, "not a version");

        DateTimeOffset now = DateTimeOffset.Now;
        Assert.True(UpdateCheck.IsDue(null, now), "never checked");
        Assert.True(!UpdateCheck.IsDue(now.AddHours(-2), now), "checked today");
        Assert.True(UpdateCheck.IsDue(now - UpdateCheck.INTERVAL, now), "a day ago");
    }

    public static async Task OffersOnlyANewerRelease()
    {
        UpdateCheck.Release? newer = await UpdateCheck.NewerAsync(new HttpClient(new FakeGitHub("v0.2.0")),
                                                                  "0.1.0+abc");
        Assert.True(newer is { Tag: "v0.2.0" }, "0.2.0 is newer than 0.1.0");
        Assert.True(newer!.Url.EndsWith("/v0.2.0", StringComparison.Ordinal), "with its page");
        Assert.True(await UpdateCheck.NewerAsync(new HttpClient(new FakeGitHub("v0.1.0")), "0.1.0") == null,
                    "the same version");
        Assert.True(await UpdateCheck.NewerAsync(new HttpClient(new FakeGitHub("v0.0.9")), "0.1.0") == null,
                    "an older one");
    }

    public static async Task ASecondStartHandsOverToTheFirst()
    {
        string pipe = "Urras.Test." + Guid.NewGuid().ToString("N");
        Assert.True(!SingleInstance.HandOver(pipe), "nothing is listening yet");

        TaskCompletionSource activated = new TaskCompletionSource();
        using SingleInstance first = SingleInstance.Listen(() => activated.TrySetResult(), pipe);
        bool handed = false;
        for (int attempt = 0; attempt < 20 && !handed; attempt++)
        {
            handed = SingleInstance.HandOver(pipe);
            if (!handed)
                await Task.Delay(50);
        }

        Assert.True(handed, "the second start found the first");
        Assert.True(await Task.WhenAny(activated.Task, Task.Delay(2000)) == activated.Task, "the first came forward");
    }
}
