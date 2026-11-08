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

    [JsonIgnore]
    public string TokenKey => "Urras:" + Kind + ":" + new Uri(ApiUrl).Host + ":" + Username;

    [JsonIgnore]
    public string Host => new Uri(ApiUrl).Host;

    public override string ToString()
    {
        string platform = Kind switch
        {
            AccountKind.GitHub => "GitHub",
            AccountKind.GitHubEnterprise => "GitHub Enterprise (" + Host + ")",
            _ => Kind + " (" + Host + ")"
        };
        return "@" + Username + (string.IsNullOrEmpty(Name) ? string.Empty : "  ·  " + Name) + "  ·  " + platform;
    }
}

// A user found on a platform, for co-authors and pictures
public sealed record PlatformUser(long Id, string Login, string? Name, string? AvatarUrl);

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
