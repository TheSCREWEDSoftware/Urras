using System.Net.Http.Headers;
using System.Text.Json;
using Urras.Core.Git;

namespace Urras.Core.Accounts;

// GitLab's REST API (gitlab.com or a self-managed server); pull requests are merge requests, notifications to-dos
public sealed class GitLabClient : PlatformClient
{
    public GitLabClient(HttpClient http, string? token, string api) : base(http, token, api)
    {
    }

    public override string PlatformName => "GitLab";
    public override string WebUrl => Api.Replace("/api/v4", string.Empty);

    protected override void Authorize(HttpRequestMessage request)
    {
        if (Token != null)
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", Token);
    }

    public override async Task<Account> GetAccountAsync()
    {
        using JsonDocument user = await GetAsync("/user");
        JsonElement root = user.RootElement;
        return new Account
        {
            Kind = AccountKind.GitLab,
            ApiUrl = Api,
            Id = Number(root, "id"),
            Username = Text(root, "username") ?? string.Empty,
            Name = Text(root, "name"),
            Email = Text(root, "commit_email") ?? Text(root, "public_email") ?? Text(root, "email"),
            AvatarUrl = Text(root, "avatar_url")
        };
    }

    public override async Task<PlatformUser?> GetUserAsync(string login)
    {
        using JsonDocument users = await GetAsync("/users?username=" + Uri.EscapeDataString(login));
        return users.RootElement.EnumerateArray().Select(ReadUser).FirstOrDefault();
    }

    public override async Task<List<PlatformUser>> SearchUsersAsync(string query, int count = SEARCH_RESULTS)
    {
        string path = "/users?per_page=" + count + "&search=" + Uri.EscapeDataString(query);
        using JsonDocument users = await GetAsync(path);
        return users.RootElement.EnumerateArray().Select(ReadUser).ToList();
    }

    public override async Task<List<PlatformRepository>> GetRepositoriesAsync(int maxPages = MAX_PAGES)
    {
        List<PlatformRepository> repositories = new List<PlatformRepository>();
        for (int page = 1; page <= maxPages; ++page)
        {
            using JsonDocument result = await GetAsync("/projects?membership=true&order_by=last_activity_at&per_page=" +
                                                       PAGE_SIZE + "&page=" + page);
            List<JsonElement> items = result.RootElement.EnumerateArray().ToList();
            repositories.AddRange(items.Select(item => new PlatformRepository(
                Text(item, "path_with_namespace") ?? string.Empty, Text(item, "http_url_to_repo") ?? string.Empty,
                Text(item, "description"), Text(item, "visibility") != "public",
                item.TryGetProperty("forked_from_project", out JsonElement parent) &&
                parent.ValueKind == JsonValueKind.Object)));
            if (items.Count < PAGE_SIZE)
                break;
        }

        return repositories;
    }

    public override async Task<List<PlatformPullRequest>> GetPullRequestsAsync(string owner, string repository)
    {
        string path = Project(owner, repository) + "/merge_requests?state=opened&per_page=" + PAGE_SIZE;
        using JsonDocument result = await GetAsync(path);
        return result.RootElement.EnumerateArray().Select(item => new PlatformPullRequest(
            (int)Number(item, "iid"), Text(item, "title") ?? string.Empty,
            item.TryGetProperty("author", out JsonElement author) ? Text(author, "username") ?? "?" : "?",
            Text(item, "source_branch") ?? string.Empty, Text(item, "target_branch") ?? string.Empty,
            Text(item, "web_url") ?? string.Empty, Flag(item, "draft"))).ToList();
    }

    // GitLab gives the picture for an email directly
    public override async Task<PlatformUser?> GetCommitAuthorAsync(string owner, string repository, string hash,
                                                                   string email)
    {
        using JsonDocument avatar = await GetAsync("/avatar?size=" + AVATAR_SIZE + "&email=" +
                                                   Uri.EscapeDataString(email));
        string? url = Text(avatar.RootElement, "avatar_url");
        return url == null ? null : new PlatformUser(0, string.Empty, null, url);
    }

    public override async Task<List<PlatformNotification>> GetNotificationsAsync()
    {
        using JsonDocument result = await GetAsync("/todos?state=pending&per_page=" + PAGE_SIZE);
        return result.RootElement.EnumerateArray().Select(item => new PlatformNotification(
            Number(item, "id").ToString(),
            item.TryGetProperty("project", out JsonElement project) ? Text(project, "path_with_namespace") ?? "" : "",
            item.TryGetProperty("target", out JsonElement target) ? Text(target, "title") ?? string.Empty : "",
            Text(item, "target_type") ?? string.Empty, Text(item, "action_name") ?? string.Empty,
            Text(item, "target_url"))).ToList();
    }

    public override async Task MarkNotificationReadAsync(PlatformNotification notification)
    {
        using JsonDocument _ = await SendAsync(HttpMethod.Post, "/todos/" + notification.Id + "/mark_as_done", null);
    }

    // GitLab's private commit address: id-username@users.noreply.<host>
    public override CoAuthor CoAuthorFor(PlatformUser user)
    {
        return new CoAuthor(user.Name ?? user.Login, user.Id + "-" + user.Login + "@users.noreply." +
                                                     new Uri(WebUrl).Host, user.Login);
    }

    public override string NewPullRequestUrl(string owner, string repository, string branch, string target)
    {
        return WebUrl + "/" + owner + "/" + repository + "/-/merge_requests/new?merge_request%5Bsource_branch%5D=" +
               Uri.EscapeDataString(branch) + "&merge_request%5Btarget_branch%5D=" + Uri.EscapeDataString(target);
    }

    public override string PullRequestRef(int number)
    {
        return "merge-requests/" + number + "/head";
    }

    private static string Project(string owner, string repository)
    {
        return "/projects/" + Uri.EscapeDataString(owner + "/" + repository);
    }

    private static PlatformUser ReadUser(JsonElement user)
    {
        return new PlatformUser(Number(user, "id"), Text(user, "username") ?? string.Empty, Text(user, "name"),
                                Text(user, "avatar_url"));
    }
}
