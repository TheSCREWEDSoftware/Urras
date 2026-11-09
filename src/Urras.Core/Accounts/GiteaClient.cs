using System.Net.Http.Headers;
using System.Text.Json;
using Urras.Core.Git;

namespace Urras.Core.Accounts;

// Gitea's REST API, which Forgejo and Codeberg share
public sealed class GiteaClient : PlatformClient
{
    // Gitea's largest page by default
    private const int GITEA_PAGE_SIZE = 50;

    public GiteaClient(HttpClient http, string? token, string api) : base(http, token, api)
    {
    }

    public override string PlatformName => "Gitea";
    public override string WebUrl => Api.Replace("/api/v1", string.Empty);

    protected override void Authorize(HttpRequestMessage request)
    {
        if (Token != null)
            request.Headers.Authorization = new AuthenticationHeaderValue("token", Token);
    }

    public override async Task<Account> GetAccountAsync()
    {
        using JsonDocument user = await GetAsync("/user");
        JsonElement root = user.RootElement;
        return new Account
        {
            Kind = AccountKind.Gitea,
            ApiUrl = Api,
            Id = Number(root, "id"),
            Username = Text(root, "login") ?? string.Empty,
            Name = Text(root, "full_name"),
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
        string path = "/users/search?limit=" + count + "&q=" + Uri.EscapeDataString(query);
        using JsonDocument result = await GetAsync(path);
        return result.RootElement.GetProperty("data").EnumerateArray().Select(ReadUser).ToList();
    }

    public override async Task<List<PlatformRepository>> GetRepositoriesAsync(int maxPages = MAX_PAGES)
    {
        List<PlatformRepository> repositories = new List<PlatformRepository>();
        for (int page = 1; page <= maxPages; ++page)
        {
            using JsonDocument result = await GetAsync("/user/repos?limit=" + GITEA_PAGE_SIZE + "&page=" + page);
            List<JsonElement> items = result.RootElement.EnumerateArray().ToList();
            repositories.AddRange(items.Select(item => new PlatformRepository(
                Text(item, "full_name") ?? string.Empty, Text(item, "clone_url") ?? string.Empty,
                Text(item, "description"), Flag(item, "private"), Flag(item, "fork"))));
            if (items.Count < GITEA_PAGE_SIZE)
                break;
        }

        return repositories;
    }

    // permissions.push is only there when signed in; parent only when the repository is a fork
    public override async Task<PlatformAccess?> GetAccessAsync(string owner, string repository)
    {
        using JsonDocument result = await GetAsync("/repos/" + Uri.EscapeDataString(owner) + "/" +
                                                   Uri.EscapeDataString(repository));
        JsonElement root = result.RootElement;
        bool push = root.TryGetProperty("permissions", out JsonElement permissions) && Flag(permissions, "push");
        (string? parentOwner, string? parentName) = root.TryGetProperty("parent", out JsonElement parent) &&
                                                    parent.ValueKind == JsonValueKind.Object
            ? PlatformAccess.Split(Text(parent, "full_name"))
            : (null, null);
        return new PlatformAccess(push, parentOwner, parentName, Text(root, "default_branch"));
    }

    public override async Task<List<PlatformPullRequest>> GetPullRequestsAsync(string owner, string repository)
    {
        using JsonDocument result = await GetAsync("/repos/" + owner + "/" + repository + "/pulls?state=open&limit=" +
                                                   GITEA_PAGE_SIZE);
        return result.RootElement.EnumerateArray().Select(item => new PlatformPullRequest(
            (int)Number(item, "number"), Text(item, "title") ?? string.Empty,
            item.TryGetProperty("user", out JsonElement user) ? Text(user, "login") ?? "?" : "?",
            item.TryGetProperty("head", out JsonElement head) ? Text(head, "ref") ?? string.Empty : string.Empty,
            item.TryGetProperty("base", out JsonElement target) ? Text(target, "ref") ?? string.Empty : string.Empty,
            Text(item, "html_url") ?? string.Empty, Flag(item, "draft"))).ToList();
    }

    public override async Task<PlatformUser?> GetCommitAuthorAsync(string owner, string repository, string hash,
                                                                   string email)
    {
        using JsonDocument commit = await GetAsync("/repos/" + owner + "/" + repository + "/git/commits/" + hash);
        return commit.RootElement.TryGetProperty("author", out JsonElement author) &&
               author.ValueKind == JsonValueKind.Object
            ? ReadUser(author)
            : null;
    }

    public override async Task<List<PlatformNotification>> GetNotificationsAsync()
    {
        using JsonDocument result = await GetAsync("/notifications?limit=" + GITEA_PAGE_SIZE);
        return result.RootElement.EnumerateArray().Select(item =>
        {
            JsonElement subject = item.GetProperty("subject");
            string repository = item.TryGetProperty("repository", out JsonElement repo)
                ? Text(repo, "full_name") ?? string.Empty
                : string.Empty;
            return new PlatformNotification(Number(item, "id").ToString(), repository,
                                            Text(subject, "title") ?? string.Empty, Text(subject, "type") ?? "",
                                            Text(subject, "state") ?? string.Empty, Text(subject, "html_url"));
        }).ToList();
    }

    public override async Task MarkNotificationReadAsync(PlatformNotification notification)
    {
        using JsonDocument _ = await SendAsync(HttpMethod.Patch, "/notifications/threads/" + notification.Id, null);
    }

    // Gitea's default private address: username@noreply.<host>
    public override CoAuthor CoAuthorFor(PlatformUser user)
    {
        return new CoAuthor(user.Name is { Length: > 0 } name ? name : user.Login,
                            user.Login + "@noreply." + new Uri(WebUrl).Host, user.Login);
    }

    public override string NewForkPullRequestUrl(string owner, string repository, string forkOwner, string branch,
                                                 string target)
    {
        return WebUrl + "/" + owner + "/" + repository + "/compare/" + Uri.EscapeDataString(target) + "..." +
               Uri.EscapeDataString(forkOwner) + ":" + Uri.EscapeDataString(branch);
    }

    public override string NewPullRequestUrl(string owner, string repository, string branch, string target)
    {
        return WebUrl + "/" + owner + "/" + repository + "/compare/" + Uri.EscapeDataString(target) + "..." +
               Uri.EscapeDataString(branch);
    }

    private static PlatformUser ReadUser(JsonElement user)
    {
        return new PlatformUser(Number(user, "id"), Text(user, "login") ?? string.Empty, Text(user, "full_name"),
                                Text(user, "avatar_url"));
    }
}
