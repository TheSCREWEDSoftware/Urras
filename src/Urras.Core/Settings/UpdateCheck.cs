using System.Net.Http.Headers;
using System.Text.Json;

namespace Urras.Core.Settings;

// Asks GitHub for Urras's latest release, so a newer version can be offered; pre-releases aren't "latest"
public static class UpdateCheck
{
    public const string LATEST_RELEASE = "https://api.github.com/repos/TheSCREWEDSoftware/Urras/releases/latest";
    public static readonly TimeSpan INTERVAL = TimeSpan.FromDays(1);
    private static readonly TimeSpan TIMEOUT = TimeSpan.FromSeconds(10);

    public sealed record Release(Version Version, string Tag, string Url);

    // 0.2.0 from "v0.2.0", "0.2.0-beta.1" or "0.1.0+7a41306"; null when there's no version in it
    public static Version? Parse(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return null;

        string version = text.Trim().TrimStart('v', 'V');
        int end = version.IndexOfAny(new[] { '+', '-', ' ' });
        if (end >= 0)
            version = version[..end];
        return Version.TryParse(version, out Version? parsed) ? Normalised(parsed) : null;
    }

    // Whether to ask today: never asked, or asked more than a day ago
    public static bool IsDue(DateTimeOffset? lastChecked, DateTimeOffset now)
    {
        return lastChecked == null || now - lastChecked.Value >= INTERVAL;
    }

    // The latest release when it's newer than this Urras; null when it isn't, or GitHub couldn't be asked
    public static async Task<Release?> NewerAsync(HttpClient http, string currentVersion)
    {
        if (Parse(currentVersion) is not { } current)
            return null;

        try
        {
            using HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Get, LATEST_RELEASE);
            request.Headers.UserAgent.Add(new ProductInfoHeaderValue("Urras", current.ToString()));
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
            using CancellationTokenSource timeout = new CancellationTokenSource(TIMEOUT);
            using HttpResponseMessage response = await http.SendAsync(request, timeout.Token);
            if (!response.IsSuccessStatusCode)
                return null;

            using JsonDocument document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(timeout.Token));
            JsonElement root = document.RootElement;
            string? tag = root.TryGetProperty("tag_name", out JsonElement tagName) ? tagName.GetString() : null;
            string? url = root.TryGetProperty("html_url", out JsonElement page) ? page.GetString() : null;
            return Parse(tag) is { } latest && latest > current && tag != null && url != null
                ? new Release(latest, tag, url)
                : null;
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException or
                                              JsonException or InvalidOperationException)
        {
            return null;
        }
    }

    // 0.2 and 0.2.0 compare equal
    private static Version Normalised(Version version)
    {
        return new Version(version.Major, version.Minor, Math.Max(0, version.Build));
    }
}
