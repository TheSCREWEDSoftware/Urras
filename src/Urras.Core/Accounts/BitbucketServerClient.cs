using System.Net.Http.Headers;
using System.Text.Json;
using Urras.Core.Git;

namespace Urras.Core.Accounts;

// Bitbucket Server and Data Center's REST API; owners are project keys and repositories are slugs
public sealed class BitbucketServerClient : PlatformClient
{
    public BitbucketServerClient(HttpClient http, string? token, string api) : base(http, token, api)
    {
    }

    public override string PlatformName => "Bitbucket Server";
    public override string WebUrl => Api.Replace("/rest/api/1.0", string.Empty);

    protected override void Authorize(HttpRequestMessage request)
    {
        if (Token != null)
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", Token);
    }

    // Bitbucket Server has no "current user" call; whoami answers with the user's slug as plain text
    public override async Task<Account> GetAccountAsync()
    {
        string slug = await WhoAmIAsync();
        using JsonDocument user = await GetAsync("/users/" + Uri.EscapeDataString(slug));
        PlatformUser read = ReadUser(user.RootElement);
        return new Account
        {
            Kind = AccountKind.BitbucketServer,
            ApiUrl = Api,
            Id = read.Id,
            Username = read.Login,
            Name = read.Name,
            Email = read.Email,
            AvatarUrl = read.AvatarUrl
        };
    }

    private async Task<string> WhoAmIAsync()
    {
        string slug = (await SendTextAsync(HttpMethod.Get, WebUrl + "/plugins/servlet/applinks/whoami", null)).Trim();
        if (slug.Length == 0)
            throw new PlatformException("The token was refused; it may have expired or been revoked.");
        return slug;
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
        using JsonDocument result = await GetAsync("/users?limit=" + count + "&filter=" + Uri.EscapeDataString(query));
        return result.RootElement.GetProperty("values").EnumerateArray().Select(ReadUser).ToList();
    }

    public override async Task<List<PlatformRepository>> GetRepositoriesAsync(int maxPages = MAX_PAGES)
    {
        List<PlatformRepository> repositories = new List<PlatformRepository>();
        int start = 0;
        for (int page = 0; page < maxPages; ++page)
        {
            using JsonDocument result = await GetAsync("/repos?limit=" + PAGE_SIZE + "&start=" + start);
            JsonElement root = result.RootElement;
            foreach (JsonElement item in root.GetProperty("values").EnumerateArray())
            {
                string key = Text(item.GetProperty("project"), "key") ?? string.Empty;
                string clone = item.GetProperty("links").GetProperty("clone").EnumerateArray()
                                   .Where(link => Text(link, "name") == "http")
                                   .Select(link => Text(link, "href") ?? string.Empty).FirstOrDefault() ?? "";
                repositories.Add(new PlatformRepository(key + "/" + (Text(item, "slug") ?? string.Empty), clone,
                                                        Text(item, "description"), !Flag(item, "public"),
                                                        item.TryGetProperty("origin", out JsonElement origin) &&
                                                        origin.ValueKind == JsonValueKind.Object));
            }

            if (Flag(root, "isLastPage"))
                break;
            start = (int)Number(root, "nextPageStart");
        }

        return repositories;
    }

    public override async Task<List<PlatformPullRequest>> GetPullRequestsAsync(string owner, string repository)
    {
        using JsonDocument result = await GetAsync("/projects/" + owner + "/repos/" + repository +
                                                   "/pull-requests?state=OPEN&limit=" + PAGE_SIZE);
        return result.RootElement.GetProperty("values").EnumerateArray().Select(item => new PlatformPullRequest(
            (int)Number(item, "id"), Text(item, "title") ?? string.Empty,
            item.GetProperty("author").TryGetProperty("user", out JsonElement user)
                ? Text(user, "displayName") ?? "?"
                : "?",
            Text(item.GetProperty("fromRef"), "displayId") ?? string.Empty,
            Text(item.GetProperty("toRef"), "displayId") ?? string.Empty,
            item.GetProperty("links").GetProperty("self").EnumerateArray()
                .Select(link => Text(link, "href") ?? string.Empty).FirstOrDefault() ?? string.Empty,
            Flag(item, "draft"))).ToList();
    }

    public override async Task<PlatformUser?> GetCommitAuthorAsync(string owner, string repository, string hash,
                                                                   string email)
    {
        using JsonDocument commit = await GetAsync("/projects/" + owner + "/repos/" + repository + "/commits/" + hash);
        JsonElement author = commit.RootElement.GetProperty("author");
        string? slug = Text(author, "slug");
        return slug == null ? null : new PlatformUser(Number(author, "id"), slug, Text(author, "displayName"),
                                                      AvatarFor(slug), Text(author, "emailAddress"));
    }

    // Bitbucket Server has no private addresses, so the user's own email is used when the server shows it
    public override CoAuthor? CoAuthorFor(PlatformUser user)
    {
        return user.Email == null ? null : new CoAuthor(user.Name ?? user.Login, user.Email, user.Login);
    }

    public override string NewPullRequestUrl(string owner, string repository, string branch, string target)
    {
        return WebUrl + "/projects/" + owner + "/repos/" + repository + "/pull-requests?create&sourceBranch=" +
               Uri.EscapeDataString("refs/heads/" + branch) + "&targetBranch=" +
               Uri.EscapeDataString("refs/heads/" + target);
    }

    public override string PullRequestRef(int number)
    {
        return "refs/pull-requests/" + number + "/from";
    }

    private string AvatarFor(string slug)
    {
        return WebUrl + "/users/" + Uri.EscapeDataString(slug) + "/avatar.png?s=" + AVATAR_SIZE;
    }

    private PlatformUser ReadUser(JsonElement user)
    {
        string slug = Text(user, "slug") ?? Text(user, "name") ?? string.Empty;
        return new PlatformUser(Number(user, "id"), slug, Text(user, "displayName"), AvatarFor(slug),
                                Text(user, "emailAddress"));
    }
}
