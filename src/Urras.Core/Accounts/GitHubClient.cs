using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;

namespace Urras.Core.Accounts;

public sealed class PlatformException : Exception
{
    public PlatformException(string message) : base(message)
    {
    }
}

// The few GitHub REST calls Urras needs. Works with GitHub Enterprise Server too, given its API address.
public sealed class GitHubClient
{
    public const string GITHUB_API = "https://api.github.com";

    private readonly HttpClient _http;
    private readonly string _api;
    private readonly string? _token;

    public GitHubClient(HttpClient http, string? token, string api = GITHUB_API)
    {
        _http = http;
        _token = token;
        _api = api.TrimEnd('/');
    }

    // The signed-in user, which also checks that the token works
    public async Task<Account> GetAccountAsync()
    {
        using JsonDocument user = await GetAsync("/user");
        JsonElement root = user.RootElement;
        return new Account
        {
            Kind = _api == GITHUB_API ? AccountKind.GitHub : AccountKind.GitHubEnterprise,
            ApiUrl = _api,
            Id = root.GetProperty("id").GetInt64(),
            Username = root.GetProperty("login").GetString() ?? string.Empty,
            Name = Text(root, "name"),
            Email = Text(root, "email"),
            AvatarUrl = Text(root, "avatar_url")
        };
    }

    public async Task<PlatformUser?> GetUserAsync(string login)
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

    // Users whose login or name matches, best first
    public async Task<List<PlatformUser>> SearchUsersAsync(string query, int count = 8)
    {
        using JsonDocument result = await GetAsync("/search/users?per_page=" + count + "&q=" +
                                                   Uri.EscapeDataString(query + " in:login in:name"));
        return result.RootElement.GetProperty("items").EnumerateArray().Select(ReadUser).ToList();
    }

    // Every repository the signed-in user can see: their own, their organisations' and ones they collaborate on
    public async Task<List<PlatformRepository>> GetRepositoriesAsync(int maxPages = 10)
    {
        List<PlatformRepository> repositories = new List<PlatformRepository>();
        for (int page = 1; page <= maxPages; ++page)
        {
            using JsonDocument result = await GetAsync("/user/repos?per_page=100&sort=pushed&page=" + page);
            List<JsonElement> items = result.RootElement.EnumerateArray().ToList();
            foreach (JsonElement item in items)
            {
                repositories.Add(new PlatformRepository(
                    item.GetProperty("full_name").GetString() ?? string.Empty,
                    item.GetProperty("clone_url").GetString() ?? string.Empty,
                    Text(item, "description"),
                    item.TryGetProperty("private", out JsonElement isPrivate) && isPrivate.GetBoolean(),
                    item.TryGetProperty("fork", out JsonElement fork) && fork.GetBoolean()));
            }

            if (items.Count < 100)
                break;
        }

        return repositories;
    }

    private static PlatformUser ReadUser(JsonElement user)
    {
        return new PlatformUser(user.GetProperty("id").GetInt64(), user.GetProperty("login").GetString() ?? "",
                                Text(user, "name"), Text(user, "avatar_url"));
    }

    private static string? Text(JsonElement element, string name)
    {
        return element.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
    }

    private async Task<JsonDocument> GetAsync(string path)
    {
        using HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Get, _api + path);
        request.Headers.UserAgent.Add(new ProductInfoHeaderValue("Urras", "0.1"));
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        request.Headers.Add("X-GitHub-Api-Version", "2022-11-28");
        if (_token != null)
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _token);

        HttpResponseMessage response;
        try
        {
            response = await _http.SendAsync(request);
        }
        catch (HttpRequestException exception)
        {
            throw new PlatformException("Couldn't reach " + new Uri(_api).Host + ": " + exception.Message);
        }

        using (response)
        {
            string body = await response.Content.ReadAsStringAsync();
            if (response.IsSuccessStatusCode)
                return JsonDocument.Parse(body);

            throw new PlatformException(response.StatusCode switch
            {
                HttpStatusCode.Unauthorized => "The token was refused; it may have expired or been revoked.",
                HttpStatusCode.Forbidden => "GitHub refused the request (rate limit or missing permission).",
                HttpStatusCode.NotFound => "Not found.",
                _ => "GitHub answered " + (int)response.StatusCode + "."
            });
        }
    }

    // The picture for a GitHub user id, at a size in pixels; works without signing in
    public static string AvatarUrl(long id, int size = 64, string host = "avatars.githubusercontent.com")
    {
        return "https://" + host + "/u/" + id + "?s=" + size;
    }

    // "123+login@users.noreply.github.com" (or the older "login@users.noreply.github.com") gives the user
    public static (long? Id, string Login)? FromNoreplyEmail(string email)
    {
        const string DOMAIN = "@users.noreply.github.com";
        if (!email.EndsWith(DOMAIN, StringComparison.OrdinalIgnoreCase))
            return null;

        string local = email[..^DOMAIN.Length];
        int plus = local.IndexOf('+');
        if (plus > 0 && long.TryParse(local[..plus], out long id))
            return (id, local[(plus + 1)..]);
        return (null, local);
    }
}
