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
            AccountKind.GitHubEnterprise => site + "/api/v3",
            AccountKind.GitLab => (site.Length == 0 ? "https://gitlab.com" : site) + "/api/v4",
            AccountKind.Gitea => (site.Length == 0 ? "https://codeberg.org" : site) + "/api/v1",
            AccountKind.Bitbucket => BitbucketClient.BITBUCKET_API,
            _ => site + "/rest/api/1.0"
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
