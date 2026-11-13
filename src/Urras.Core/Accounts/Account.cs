using System.Text.Json.Serialization;

namespace Urras.Core.Accounts;

public enum AccountKind
{
    GitHub,
    GitHubEnterprise,
    GitLab,
    Gitea,
    Bitbucket,
    BitbucketServer
}

// A signed-in account; its token lives in the system's credential store under TokenKey
public sealed class Account
{
    [JsonConverter(typeof(JsonStringEnumConverter<AccountKind>))]
    public AccountKind Kind { get; set; }

    // API address: GitHub's own, or a GitHub Enterprise server's address followed by /api/v3
    public string ApiUrl { get; set; } = string.Empty;
    public long Id { get; set; }
    public string Username { get; set; } = string.Empty;
    public string? Name { get; set; }
    public string? Email { get; set; }
    public string? AvatarUrl { get; set; }

    // When the token stops working, for tokens that expire (GitLab's); a refresh token renews it before then
    public DateTimeOffset? TokenExpiresAt { get; set; }

    [JsonIgnore]
    public string TokenKey => "Urras:" + Kind + ":" + new Uri(ApiUrl).Host + ":" + Username;

    [JsonIgnore]
    public string Host => new Uri(ApiUrl).Host;

    // The website's host, which remotes use: github.com rather than api.github.com
    [JsonIgnore]
    public string WebHost => Host switch
    {
        "api.github.com" => "github.com",
        "api.bitbucket.org" => "bitbucket.org",
        _ => Host
    };

    // The account's page on its website, such as gitlab.com/TheSCREWEDSoftware
    [JsonIgnore]
    public string ProfileUrl
    {
        get
        {
            string user = Uri.EscapeDataString(Username);
            return Kind switch
            {
                AccountKind.GitHub => "https://github.com/" + user,
                AccountKind.Bitbucket => "https://bitbucket.org/" + user,
                AccountKind.BitbucketServer => SiteBefore(BITBUCKET_SERVER_API) + "/users/" + user,
                AccountKind.GitHubEnterprise => SiteBefore(ENTERPRISE_API) + "/" + user,
                AccountKind.GitLab => SiteBefore(GITLAB_API) + "/" + user,
                _ => SiteBefore(GITEA_API) + "/" + user
            };
        }
    }

    private const string ENTERPRISE_API = "/api/v3";
    private const string GITLAB_API = "/api/v4";
    private const string GITEA_API = "/api/v1";
    private const string BITBUCKET_SERVER_API = "/rest/api/1.0";

    // The website address the API sits under, keeping any path a self-hosted server lives at
    private string SiteBefore(string apiPath)
    {
        string api = ApiUrl.TrimEnd('/');
        return api.EndsWith(apiPath, StringComparison.OrdinalIgnoreCase) ? api[..^apiPath.Length]
                                                                         : "https://" + WebHost;
    }

    public static string PlatformName(AccountKind kind)
    {
        return kind switch
        {
            AccountKind.GitHubEnterprise => "GitHub Enterprise",
            AccountKind.BitbucketServer => "Bitbucket Server",
            _ => kind.ToString()
        };
    }

    // The API address for a platform; server is the website address for self-hosted ones
    public static string ApiFor(AccountKind kind, string? server)
    {
        string site = (server ?? string.Empty).Trim().TrimEnd('/');
        return kind switch
        {
            AccountKind.GitHub => GitHubClient.GITHUB_API,
            AccountKind.GitHubEnterprise => site + ENTERPRISE_API,
            AccountKind.GitLab => (site.Length == 0 ? "https://gitlab.com" : site) + GITLAB_API,
            AccountKind.Gitea => (site.Length == 0 ? "https://codeberg.org" : site) + GITEA_API,
            AccountKind.Bitbucket => BitbucketClient.BITBUCKET_API,
            _ => site + BITBUCKET_SERVER_API
        };
    }

    public override string ToString()
    {
        string platform = Kind switch
        {
            AccountKind.GitHub => "GitHub",
            AccountKind.Bitbucket => "Bitbucket",
            _ => PlatformName(Kind) + " (" + WebHost + ")"
        };
        return "@" + Username + (string.IsNullOrEmpty(Name) ? string.Empty : "  ·  " + Name) + "  ·  " + platform;
    }
}

// A user found on a platform, for co-authors and pictures
public sealed record PlatformUser(long Id, string Login, string? Name, string? AvatarUrl, string? Email = null);

// Whether the signed-in user can push to a repository, and the repository it was forked from, if any
public sealed record PlatformAccess(bool CanPush, string? ParentOwner, string? ParentName,
                                    string? DefaultBranch = null)
{
    public bool IsFork => ParentOwner != null && ParentName != null;

    // "owner/name" from a full name such as "azerothcore/wiki" or "group/sub/project"
    public static (string? Owner, string? Name) Split(string? fullName)
    {
        int slash = fullName?.LastIndexOf('/') ?? -1;
        return slash > 0 && slash < fullName!.Length - 1 ? (fullName[..slash], fullName[(slash + 1)..]) : (null, null);
    }
}

// An organisation (a group on GitLab) the signed-in user can make repositories in
public sealed record PlatformOrganization(long Id, string Login)
{
    public override string ToString()
    {
        return Login;
    }
}

// What to make when publishing: no organisation puts it under the signed-in user
public sealed record NewRemoteRepository(string Name, string Description, bool IsPrivate,
                                         PlatformOrganization? Organization);

// A repository on a platform, for cloning
public sealed record PlatformRepository(string FullName, string CloneUrl, string? Description, bool IsPrivate,
                                        bool IsFork)
{
    public string Name => FullName[(FullName.IndexOf('/') + 1)..];

    public override string ToString()
    {
        return FullName + (IsPrivate ? "  ·  private" : string.Empty) + (IsFork ? "  ·  fork" : string.Empty) +
               (string.IsNullOrEmpty(Description) ? string.Empty : "  ·  " + Description);
    }
}
