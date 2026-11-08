using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Urras.Core.Git;

namespace Urras.Core.Accounts;

// Bitbucket Cloud; the token is an access token or "username:app password"; it has no user search
public sealed class BitbucketClient : PlatformClient
{
    public const string BITBUCKET_API = "https://api.bitbucket.org/2.0";

    public BitbucketClient(HttpClient http, string? token, string api = BITBUCKET_API) : base(http, token, api)
    {
    }

    public override string PlatformName => "Bitbucket";
    public override string WebUrl => "https://bitbucket.org";

    protected override void Authorize(HttpRequestMessage request)
    {
        if (Token == null)
            return;
        request.Headers.Authorization = Token.Contains(':')
            ? new AuthenticationHeaderValue("Basic", Convert.ToBase64String(Encoding.UTF8.GetBytes(Token)))
            : new AuthenticationHeaderValue("Bearer", Token);
    }

    public override async Task<Account> GetAccountAsync()
    {
        using JsonDocument user = await GetAsync("/user");
        JsonElement root = user.RootElement;
        return new Account
        {
            Kind = AccountKind.Bitbucket,
            ApiUrl = Api,
            Username = Text(root, "username") ?? Text(root, "nickname") ?? string.Empty,
            Name = Text(root, "display_name"),
            AvatarUrl = Avatar(root)
        };
    }

    public override Task<PlatformUser?> GetUserAsync(string login)
    {
        return Task.FromResult<PlatformUser?>(null);
    }

    public override Task<List<PlatformUser>> SearchUsersAsync(string query, int count = SEARCH_RESULTS)
    {
        return Task.FromResult(new List<PlatformUser>());
    }

    public override async Task<List<PlatformRepository>> GetRepositoriesAsync(int maxPages = MAX_PAGES)
    {
        List<PlatformRepository> repositories = new List<PlatformRepository>();
        string? next = "/repositories?role=member&sort=-updated_on&pagelen=" + PAGE_SIZE;
        for (int page = 0; page < maxPages && next != null; ++page)
        {
            using JsonDocument result = await GetAsync(next);
            foreach (JsonElement item in result.RootElement.GetProperty("values").EnumerateArray())
            {
                string clone = item.GetProperty("links").GetProperty("clone").EnumerateArray()
                                   .Where(link => Text(link, "name") == "https")
                                   .Select(link => Text(link, "href") ?? string.Empty).FirstOrDefault() ?? "";
                repositories.Add(new PlatformRepository(Text(item, "full_name") ?? string.Empty, clone,
                                                        Text(item, "description"), Flag(item, "is_private"),
                                                        item.TryGetProperty("parent", out JsonElement parent) &&
                                                        parent.ValueKind == JsonValueKind.Object));
            }

            next = Text(result.RootElement, "next");
        }

        return repositories;
    }

    public override async Task<List<PlatformPullRequest>> GetPullRequestsAsync(string owner, string repository)
    {
        using JsonDocument result = await GetAsync("/repositories/" + owner + "/" + repository +
                                                   "/pullrequests?state=OPEN&pagelen=50");
        return result.RootElement.GetProperty("values").EnumerateArray().Select(item => new PlatformPullRequest(
            (int)Number(item, "id"), Text(item, "title") ?? string.Empty,
            item.TryGetProperty("author", out JsonElement author) ? Text(author, "display_name") ?? "?" : "?",
            Branch(item, "source"), Branch(item, "destination"),
            item.GetProperty("links").GetProperty("html").TryGetProperty("href", out JsonElement href)
                ? href.GetString() ?? string.Empty
                : string.Empty,
            Flag(item, "draft"))).ToList();
    }

    public override async Task<PlatformUser?> GetCommitAuthorAsync(string owner, string repository, string hash,
                                                                   string email)
    {
        using JsonDocument commit = await GetAsync("/repositories/" + owner + "/" + repository + "/commit/" + hash);
        if (!commit.RootElement.GetProperty("author").TryGetProperty("user", out JsonElement user))
            return null;
        return new PlatformUser(0, Text(user, "nickname") ?? string.Empty, Text(user, "display_name"), Avatar(user));
    }

    public override CoAuthor? CoAuthorFor(PlatformUser user)
    {
        return null;
    }

    public override string NewPullRequestUrl(string owner, string repository, string branch, string target)
    {
        return WebUrl + "/" + owner + "/" + repository + "/pull-requests/new?source=" + Uri.EscapeDataString(branch) +
               "&dest=" + Uri.EscapeDataString(target);
    }

    // Bitbucket doesn't publish refs for pull requests; the source branch is checked out instead
    public override string? PullRequestRef(int number)
    {
        return null;
    }

    private static string Branch(JsonElement pullRequest, string side)
    {
        return pullRequest.TryGetProperty(side, out JsonElement end) && end.TryGetProperty("branch", out JsonElement b)
            ? Text(b, "name") ?? string.Empty
            : string.Empty;
    }

    private static string? Avatar(JsonElement user)
    {
        return user.TryGetProperty("links", out JsonElement links) && links.TryGetProperty("avatar", out JsonElement a)
            ? Text(a, "href")
            : null;
    }
}
