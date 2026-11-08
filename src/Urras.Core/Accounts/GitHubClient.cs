using System.Net.Http.Headers;
using System.Text.Json;
using Urras.Core.Git;

namespace Urras.Core.Accounts;

// GitHub's REST API; also GitHub Enterprise Server, given its API address
public sealed class GitHubClient : PlatformClient
{
    public const string GITHUB_API = "https://api.github.com";
    private const string NOREPLY_DOMAIN = "@users.noreply.github.com";

    public GitHubClient(HttpClient http, string? token, string api = GITHUB_API) : base(http, token, api)
    {
    }

    public override string PlatformName => "GitHub";

    protected override void Authorize(HttpRequestMessage request)
    {
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        request.Headers.Add("X-GitHub-Api-Version", "2022-11-28");
        if (Token != null)
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", Token);
    }

    // The signed-in user, which also checks that the token works
    public override async Task<Account> GetAccountAsync()
    {
        using JsonDocument user = await GetAsync("/user");
        JsonElement root = user.RootElement;
        return new Account
        {
            Kind = Api == GITHUB_API ? AccountKind.GitHub : AccountKind.GitHubEnterprise,
            ApiUrl = Api,
            Id = Number(root, "id"),
            Username = Text(root, "login") ?? string.Empty,
            Name = Text(root, "name"),
            Email = Text(root, "email"),
            AvatarUrl = Text(root, "avatar_url")
        };
    }

    public override async Task<PlatformUser?> GetUserAsync(string login)
    {
        try
        {
            using JsonDocument user = await GetAsync("/users/" + Uri.EscapeDataString(login));
            return ReadUser(user.RootElement);
        }
        catch (PlatformException exception) when (exception.Message.StartsWith("Not found"))
        {
            return null;
        }
    }

    public override async Task<List<PlatformUser>> SearchUsersAsync(string query, int count = SEARCH_RESULTS)
    {
        using JsonDocument result = await GetAsync("/search/users?per_page=" + count + "&q=" +
                                                   Uri.EscapeDataString(query + " in:login in:name"));
        return result.RootElement.GetProperty("items").EnumerateArray().Select(ReadUser).ToList();
    }

    // Every repository the signed-in user can see: their own, their organisations' and ones they collaborate on
    public override async Task<List<PlatformRepository>> GetRepositoriesAsync(int maxPages = MAX_PAGES)
    {
        List<PlatformRepository> repositories = new List<PlatformRepository>();
        for (int page = 1; page <= maxPages; ++page)
        {
            string path = "/user/repos?per_page=" + PAGE_SIZE + "&sort=pushed&page=" + page;
            using JsonDocument result = await GetAsync(path);
            List<JsonElement> items = result.RootElement.EnumerateArray().ToList();
            repositories.AddRange(items.Select(item => new PlatformRepository(
                Text(item, "full_name") ?? string.Empty, Text(item, "clone_url") ?? string.Empty,
                Text(item, "description"), Flag(item, "private"), Flag(item, "fork"))));
            if (items.Count < PAGE_SIZE)
                break;
        }

        return repositories;
    }

    public override async Task<List<PlatformPullRequest>> GetPullRequestsAsync(string owner, string repository)
    {
        string path = "/repos/" + owner + "/" + repository + "/pulls?state=open&per_page=" + PAGE_SIZE;
        using JsonDocument result = await GetAsync(path);
        return result.RootElement.EnumerateArray().Select(item => new PlatformPullRequest(
            (int)Number(item, "number"), Text(item, "title") ?? string.Empty,
            item.TryGetProperty("user", out JsonElement user) ? Text(user, "login") ?? "?" : "?",
            item.GetProperty("head").TryGetProperty("ref", out JsonElement head) ? head.GetString() ?? "" : "",
            item.GetProperty("base").TryGetProperty("ref", out JsonElement target) ? target.GetString() ?? "" : "",
            Text(item, "html_url") ?? string.Empty, Flag(item, "draft"))).ToList();
    }

    public override async Task<PlatformUser?> GetCommitAuthorAsync(string owner, string repository, string hash,
                                                                   string email)
    {
        try
        {
            using JsonDocument commit = await GetAsync("/repos/" + owner + "/" + repository + "/commits/" + hash);
            return commit.RootElement.TryGetProperty("author", out JsonElement author) &&
                   author.ValueKind == JsonValueKind.Object
                ? ReadUser(author)
                : null;
        }
        catch (PlatformException exception) when (exception.Message.StartsWith("Not found"))
        {
            return null;
        }
    }

    public override async Task<List<PlatformNotification>> GetNotificationsAsync()
    {
        using JsonDocument result = await GetAsync("/notifications?per_page=" + PAGE_SIZE);
        return result.RootElement.EnumerateArray().Select(item =>
        {
            JsonElement subject = item.GetProperty("subject");
            string repository = item.TryGetProperty("repository", out JsonElement repo)
                ? Text(repo, "full_name") ?? string.Empty
                : string.Empty;
            return new PlatformNotification(Text(item, "id") ?? string.Empty, repository,
                                            Text(subject, "title") ?? string.Empty, Text(subject, "type") ?? "",
                                            Text(item, "reason") ?? string.Empty, WebPageFor(Text(subject, "url")));
        }).ToList();
    }

    public override async Task MarkNotificationReadAsync(PlatformNotification notification)
    {
        using JsonDocument _ = await SendAsync(HttpMethod.Patch, "/notifications/threads/" + notification.Id, null);
    }

    public override CoAuthor CoAuthorFor(PlatformUser user)
    {
        return CoAuthor.FromGitHub(user.Id, user.Login, user.Name);
    }

    public override string NewPullRequestUrl(string owner, string repository, string branch, string target)
    {
        return WebUrl + "/" + owner + "/" + repository + "/compare/" + Uri.EscapeDataString(target) + "..." +
               Uri.EscapeDataString(branch) + "?expand=1";
    }

    public override string WebUrl => Api == GITHUB_API ? "https://github.com" : Api.Replace("/api/v3", string.Empty);

    // API addresses of pull requests and issues, turned into their web pages
    public static string? WebPageFor(string? apiUrl)
    {
        if (apiUrl == null)
            return null;
        return apiUrl.Replace("://api.github.com/repos/", "://github.com/").Replace("/api/v3/repos/", "/")
                     .Replace("/pulls/", "/pull/");
    }

    private static PlatformUser ReadUser(JsonElement user)
    {
        return new PlatformUser(Number(user, "id"), Text(user, "login") ?? string.Empty, Text(user, "name"),
                                Text(user, "avatar_url"));
    }

    // The picture for a GitHub user id, at a size in pixels; works without signing in
    public static string AvatarUrl(long id, int size = AVATAR_SIZE, string host = "avatars.githubusercontent.com")
    {
        return "https://" + host + "/u/" + id + "?s=" + size;
    }

    // "123+login@users.noreply.github.com" (or the older "login@users.noreply.github.com") gives the user
    public static (long? Id, string Login)? FromNoreplyEmail(string email)
    {
        if (!email.EndsWith(NOREPLY_DOMAIN, StringComparison.OrdinalIgnoreCase))
            return null;

        string local = email[..^NOREPLY_DOMAIN.Length];
        int plus = local.IndexOf('+');
        if (plus > 0 && long.TryParse(local[..plus], out long id))
            return (id, local[(plus + 1)..]);
        return (null, local);
    }
}
