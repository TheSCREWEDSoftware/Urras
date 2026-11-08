using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace Urras.Core.Accounts;

public sealed class PlatformException : Exception
{
    public PlatformException(string message) : base(message)
    {
    }
}

public sealed record PlatformPullRequest(int Number, string Title, string Author, string SourceBranch,
                                         string TargetBranch, string Url, bool IsDraft)
{
    public override string ToString()
    {
        return "#" + Number + "  " + Title + (IsDraft ? "  (draft)" : string.Empty) + "  ·  " + Author + "  ·  " +
               SourceBranch + " into " + TargetBranch;
    }
}

public sealed record PlatformNotification(string Id, string Repository, string Title, string Kind, string Reason,
                                          string? Url)
{
    public override string ToString()
    {
        return Repository + "  ·  " + Kind + ": " + Title + "  ·  " + Reason.Replace('_', ' ');
    }
}

// What Urras asks of a hosting platform. Each platform answers in its own JSON; the subclasses translate.
public abstract class PlatformClient
{
    public const int PAGE_SIZE = 100;
    public const int MAX_PAGES = 10;
    public const int SEARCH_RESULTS = 8;
    public const int AVATAR_SIZE = 64;

    private readonly HttpClient _http;

    protected PlatformClient(HttpClient http, string? token, string api)
    {
        _http = http;
        Token = token;
        Api = api.TrimEnd('/');
    }

    public string Api { get; }
    protected string? Token { get; }
    public abstract string PlatformName { get; }
    public string Host => new Uri(Api).Host;

    public abstract Task<Account> GetAccountAsync();
    public abstract Task<PlatformUser?> GetUserAsync(string login);
    public abstract Task<List<PlatformUser>> SearchUsersAsync(string query, int count = SEARCH_RESULTS);
    public abstract Task<List<PlatformRepository>> GetRepositoriesAsync(int maxPages = MAX_PAGES);

    public virtual Task<List<PlatformPullRequest>> GetPullRequestsAsync(string owner, string repository)
    {
        return Task.FromResult(new List<PlatformPullRequest>());
    }

    // Who a commit belongs to on the platform, for their picture; null when the platform can't tell
    public virtual Task<PlatformUser?> GetCommitAuthorAsync(string owner, string repository, string hash,
                                                            string email)
    {
        return Task.FromResult<PlatformUser?>(null);
    }

    public virtual Task<List<PlatformNotification>> GetNotificationsAsync()
    {
        return Task.FromResult(new List<PlatformNotification>());
    }

    public virtual Task MarkNotificationReadAsync(PlatformNotification notification)
    {
        return Task.CompletedTask;
    }

    // A co-author line for a user, using the platform's private address where it has one
    public abstract Urras.Core.Git.CoAuthor? CoAuthorFor(PlatformUser user);

    // The web page for opening a pull request from branch into target
    public abstract string NewPullRequestUrl(string owner, string repository, string branch, string target);

    // The git ref a pull request's commits can be fetched from; null to use its source branch instead
    public virtual string? PullRequestRef(int number)
    {
        return "pull/" + number + "/head";
    }

    // The website, from the API address
    public virtual string WebUrl => Api;

    protected abstract void Authorize(HttpRequestMessage request);

    protected Task<JsonDocument> GetAsync(string path)
    {
        return SendAsync(HttpMethod.Get, path, null);
    }

    protected async Task<JsonDocument> SendAsync(HttpMethod method, string path, object? body)
    {
        return JsonDocument.Parse(await SendTextAsync(method, path, body) is { Length: > 0 } text ? text : "{}");
    }

    // The answer as text, for the few calls that don't answer in JSON
    protected async Task<string> SendTextAsync(HttpMethod method, string path, object? body)
    {
        using HttpRequestMessage request = new HttpRequestMessage(method, path.StartsWith("http") ? path : Api + path);
        request.Headers.UserAgent.Add(new ProductInfoHeaderValue("Urras", "0.1"));
        Authorize(request);
        if (body != null)
            request.Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");

        HttpResponseMessage response;
        try
        {
            response = await _http.SendAsync(request);
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException)
        {
            throw new PlatformException("Couldn't reach " + Host + ": " + exception.Message);
        }

        using (response)
        {
            string text = await response.Content.ReadAsStringAsync();
            if (response.IsSuccessStatusCode)
                return text;

            throw new PlatformException(response.StatusCode switch
            {
                HttpStatusCode.Unauthorized => "The token was refused; it may have expired or been revoked.",
                HttpStatusCode.Forbidden => PlatformName + " refused the request (rate limit or missing permission).",
                HttpStatusCode.NotFound => "Not found.",
                _ => PlatformName + " answered " + (int)response.StatusCode + "."
            });
        }
    }

    protected static string? Text(JsonElement element, string name)
    {
        return element.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
    }

    protected static long Number(JsonElement element, string name)
    {
        return element.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.Number
            ? value.GetInt64()
            : 0;
    }

    protected static bool Flag(JsonElement element, string name)
    {
        return element.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.True;
    }
}
