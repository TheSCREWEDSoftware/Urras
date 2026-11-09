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

    // Push access and the fork's parent for a repository; null when the platform can't tell
    public virtual Task<PlatformAccess?> GetAccessAsync(string owner, string repository)
    {
        return Task.FromResult<PlatformAccess?>(null);
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

    // The web page for opening a pull request from a fork's branch into its parent; null when not supported
    public virtual string? NewForkPullRequestUrl(string owner, string repository, string forkOwner, string branch,
                                                 string target)
    {
        return null;
    }

    // The git ref a pull request's commits can be fetched from; null to use its source branch instead
    public virtual string? PullRequestRef(int number)
    {
        return "pull/" + number + "/head";
    }

    // Whether Urras can make repositories here, for publishing a repository that has no remote
    public virtual bool CanCreateRepositories => false;

    public virtual Task<List<PlatformOrganization>> GetOrganizationsAsync()
    {
        return Task.FromResult(new List<PlatformOrganization>());
    }

    // Makes an empty repository and returns it, with the address to push to
    public virtual Task<PlatformRepository> CreateRepositoryAsync(NewRemoteRepository repository)
    {
        throw new PlatformException("Urras can't make repositories on " + PlatformName + " yet.");
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
        string url = path.StartsWith("http", StringComparison.OrdinalIgnoreCase) ? path : Api + path;

        // Links taken from answers (such as the next page) never carry the token to another host
        if (!Uri.TryCreate(url, UriKind.Absolute, out Uri? target) ||
            (!string.Equals(target.Host, Host, StringComparison.OrdinalIgnoreCase) &&
             !string.Equals(target.Host, new Uri(WebUrl).Host, StringComparison.OrdinalIgnoreCase)))
            throw new PlatformException("Refused to send the token to " + url);

        using HttpRequestMessage request = new HttpRequestMessage(method, url);
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
                _ => PlatformName + " answered " + (int)response.StatusCode + ErrorDetail(text) + "."
            });
        }
    }

    // ": name already exists on this account" from the answer's message, so a refusal says why
    public static string ErrorDetail(string answer)
    {
        try
        {
            using JsonDocument document = JsonDocument.Parse(answer);
            JsonElement root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
                return string.Empty;

            // GitHub and Gitea put the reason in errors, GitLab in message, which can also be an object
            if (root.TryGetProperty("errors", out JsonElement errors) && errors.ValueKind == JsonValueKind.Array &&
                errors.EnumerateArray().Select(error => error.ValueKind == JsonValueKind.Object
                                                   ? Text(error, "message")
                                                   : error.ValueKind == JsonValueKind.String ? error.GetString()
                                                                                              : null)
                      .FirstOrDefault(message => !string.IsNullOrEmpty(message)) is { } first)
                return ": " + first;
            if (!root.TryGetProperty("message", out JsonElement message))
                return string.Empty;
            if (message.ValueKind == JsonValueKind.String)
                return ": " + message.GetString();
            if (message.ValueKind == JsonValueKind.Object)
                return ": " + string.Join("; ", message.EnumerateObject().Select(field =>
                                              field.Name + " " + string.Join(", ", field.Value.ValueKind ==
                                                  JsonValueKind.Array
                                                  ? field.Value.EnumerateArray().Select(item => item.ToString())
                                                  : new[] { field.Value.ToString() })));
            return string.Empty;
        }
        catch (JsonException)
        {
            return string.Empty;
        }
    }

    // Answers that don't have the expected shape become a readable error instead of a crash
    public static async Task<T> SafelyAsync<T>(Func<Task<T>> call)
    {
        try
        {
            return await call();
        }
        catch (Exception exception) when (exception is KeyNotFoundException or InvalidOperationException or
                                              JsonException or FormatException or IndexOutOfRangeException)
        {
            throw new PlatformException("The platform's answer wasn't what Urras expected: " + exception.Message);
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
