using System.Text.Json;
using Urras.Core.Git;
using Urras.Core.Settings;

namespace Urras.Core.Accounts;

// Adds, removes and uses accounts: settings hold who is signed in, the token store holds the tokens
public sealed class AccountManager
{
    private const string GITLAB_TOKEN_LOGIN = "oauth2";
    private const string REFRESH_SUFFIX = ":refresh";
    private const string GITLAB_API_SUFFIX = "/api/v4";

    // Tokens are renewed a little before they expire, so one never runs out in the middle of a push
    private static readonly TimeSpan RenewEarly = TimeSpan.FromMinutes(5);

    // One renewal at a time: GitLab replaces the refresh token each time, so two at once would clash
    private readonly SemaphoreSlim _renewing = new SemaphoreSlim(1, 1);
    private const string BITBUCKET_TOKEN_LOGIN = "x-token-auth";
    // Commits asked about per refresh when looking up pictures, to stay well inside rate limits
    public const int PICTURE_LOOKUPS = 20;

    private readonly AppSettings _settings;
    private readonly ITokenStore _tokens;
    private readonly HttpClient _http;
    private readonly string? _picturesFile;
    private readonly Dictionary<string, string> _pictures;

    // picturesFile keeps the pictures found for commit emails between runs; null keeps them in memory only
    public AccountManager(AppSettings settings, ITokenStore tokens, HttpClient http, string? picturesFile = null)
    {
        _settings = settings;
        _tokens = tokens;
        _http = http;
        _picturesFile = picturesFile;
        _pictures = LoadPictures(picturesFile);
    }

    public IReadOnlyList<Account> Accounts => _settings.Accounts;

    // Raised when pictures were found for more emails, so views can show them
    public event Action? PicturesChanged;

    // Checks the token with the platform, then keeps it; throws PlatformException with a readable message
    public Task<Account> AddGitHubAsync(string token, string api = GitHubClient.GITHUB_API)
    {
        return AddAsync(api == GitHubClient.GITHUB_API ? AccountKind.GitHub : AccountKind.GitHubEnterprise, token,
                        api);
    }

    // refreshToken and expiresAt come with tokens that expire, from the browser sign-in
    public async Task<Account> AddAsync(AccountKind kind, string token, string api, string? refreshToken = null,
                                        DateTimeOffset? expiresAt = null)
    {
        if (_tokens.Unavailable is { } reason)
            throw new PlatformException(reason);

        PlatformClient client = Client(kind, token.Trim(), api);
        Account account = await PlatformClient.SafelyAsync(client.GetAccountAsync);
        if (!await _tokens.SaveAsync(account.TokenKey, token.Trim()))
            throw new PlatformException("Couldn't save the token in the system's credential store.");
        if (refreshToken != null && await _tokens.SaveAsync(account.TokenKey + REFRESH_SUFFIX, refreshToken))
            account.TokenExpiresAt = expiresAt;
        else
            await _tokens.DeleteAsync(account.TokenKey + REFRESH_SUFFIX);

        _settings.Accounts.RemoveAll(existing => existing.TokenKey == account.TokenKey);
        _settings.Accounts.Add(account);
        _settings.Save();
        return account;
    }

    // Other secrets, such as the AI service's key, kept in the same credential store
    public Task<bool> SaveSecretAsync(string key, string value)
    {
        return _tokens.SaveAsync(key, value);
    }

    public Task<string?> LoadSecretAsync(string key)
    {
        return _tokens.LoadAsync(key);
    }

    public Task DeleteSecretAsync(string key)
    {
        return _tokens.DeleteAsync(key);
    }

    public async Task RemoveAsync(Account account)
    {
        await _tokens.DeleteAsync(account.TokenKey);
        await _tokens.DeleteAsync(account.TokenKey + REFRESH_SUFFIX);
        _settings.Accounts.RemoveAll(existing => existing.TokenKey == account.TokenKey);
        _settings.Save();
    }

    public PlatformClient Client(AccountKind kind, string? token, string api)
    {
        return kind switch
        {
            AccountKind.GitLab => new GitLabClient(_http, token, api),
            AccountKind.Gitea => new GiteaClient(_http, token, api),
            AccountKind.Bitbucket => new BitbucketClient(_http, token, api),
            AccountKind.BitbucketServer => new BitbucketServerClient(_http, token, api),
            _ => new GitHubClient(_http, token, api)
        };
    }

    public async Task<PlatformClient?> ClientAsync(Account account)
    {
        string? token = await TokenAsync(account);
        return token == null ? null : Client(account.Kind, token, account.ApiUrl);
    }

    // The account's token, renewed first when it expires soon and a refresh token is kept
    public async Task<string?> TokenAsync(Account account)
    {
        if (!ExpiresSoon(account))
            return await _tokens.LoadAsync(account.TokenKey);

        await _renewing.WaitAsync();
        try
        {
            // Another call may have renewed it while this one waited
            if (ExpiresSoon(account) && await _tokens.LoadAsync(account.TokenKey + REFRESH_SUFFIX) is { } refresh &&
                RenewalFlow(account) is { } flow)
            {
                OAuthGrant grant = await flow.RefreshAsync(refresh);
                await _tokens.SaveAsync(account.TokenKey, grant.AccessToken);
                if (grant.RefreshToken != null)
                    await _tokens.SaveAsync(account.TokenKey + REFRESH_SUFFIX, grant.RefreshToken);
                account.TokenExpiresAt = grant.ExpiresAt;
                _settings.Save();
            }
        }
        catch (PlatformException)
        {
            // Kept as it is; the platform will refuse it, and signing in again fixes that
        }
        finally
        {
            _renewing.Release();
        }

        return await _tokens.LoadAsync(account.TokenKey);
    }

    private static bool ExpiresSoon(Account account)
    {
        return account.TokenExpiresAt is { } expires && DateTimeOffset.UtcNow >= expires - RenewEarly;
    }

    // The same app that signed in renews the token: GitLab's site is its API address without /api/v4
    private DeviceSignIn? RenewalFlow(Account account)
    {
        string site = account.ApiUrl.EndsWith(GITLAB_API_SUFFIX, StringComparison.OrdinalIgnoreCase)
            ? account.ApiUrl[..^GITLAB_API_SUFFIX.Length]
            : "https://" + account.WebHost;
        string appId = account.Kind == AccountKind.GitLab ? _settings.GitLabClientId : _settings.GitHubClientId;
        return DeviceSignIn.For(account.Kind, site, _http, appId);
    }

    // Answers Git's HTTPS login from a signed-in account (username, then token as password); null if none fits
    public async Task<string?> AnswerGitAsync(string prompt)
    {
        bool username = prompt.StartsWith("Username for '", StringComparison.Ordinal);
        bool password = prompt.StartsWith("Password for '", StringComparison.Ordinal);
        int open = prompt.IndexOf('\'');
        int close = prompt.LastIndexOf('\'');
        if ((!username && !password) || close <= open ||
            !Uri.TryCreate(prompt[(open + 1)..close], UriKind.Absolute, out Uri? address))
            return null;

        // A password prompt names the user before the host; it must be the login given for that account
        string named = Uri.UnescapeDataString(address.UserInfo);
        foreach (Account account in _settings.Accounts.Where(item => string.Equals(item.WebHost, address.Host,
                                                                                   StringComparison.OrdinalIgnoreCase)))
        {
            (string login, string? secret) = GitCredentials(account, await TokenAsync(account));
            if (named.Length == 0 || string.Equals(named, login, StringComparison.OrdinalIgnoreCase))
                return username ? login : secret;
        }

        return null;
    }

    // Login and password for Git: a Bitbucket app password is kept as username:app-password, other tokens alone
    private static (string Login, string? Secret) GitCredentials(Account account, string? token)
    {
        int colon = token?.IndexOf(':') ?? -1;
        if (account.Kind is AccountKind.Bitbucket or AccountKind.BitbucketServer && colon > 0)
            return (token![..colon], token[(colon + 1)..]);
        return (GitLogin(account), token);
    }

    // The username Git sends with a token: GitLab and Bitbucket want fixed names for OAuth tokens
    public static string GitLogin(Account account)
    {
        return account.Kind switch
        {
            AccountKind.GitLab => GITLAB_TOKEN_LOGIN,
            AccountKind.Bitbucket => BITBUCKET_TOKEN_LOGIN,
            _ => account.Username
        };
    }

    public async Task<GitHubClient?> GitHubAsync()
    {
        Account? account = _settings.Accounts.FirstOrDefault(item => item.Kind is AccountKind.GitHub
                                                                          or AccountKind.GitHubEnterprise);
        return account == null ? null : await ClientAsync(account) as GitHubClient;
    }

    // The signed-in account for a remote's host, such as github.com or a company GitLab
    public async Task<PlatformClient?> ClientForHostAsync(string host)
    {
        Account? account = _settings.Accounts.FirstOrDefault(item =>
            string.Equals(item.WebHost, host, StringComparison.OrdinalIgnoreCase));
        return account == null ? null : await ClientAsync(account);
    }

    // The first account that can search users, for @username co-authors
    private async Task<PlatformClient?> SearchClientAsync()
    {
        foreach (Account account in _settings.Accounts.Where(item => item.Kind is not (AccountKind.Bitbucket or
                                                                         AccountKind.BitbucketServer)))
        {
            if (await ClientAsync(account) is { } client)
                return client;
        }

        return null;
    }

    // "@login" becomes a co-author with the platform's private address for that user
    public async Task<CoAuthor?> FindCoAuthorAsync(string login)
    {
        PlatformClient? client = await SearchClientAsync();
        PlatformUser? user = client == null
            ? null
            : await PlatformClient.SafelyAsync(() => client.GetUserAsync(login.TrimStart('@')));
        return user == null ? null : client!.CoAuthorFor(user);
    }

    public async Task<List<CoAuthor>> SearchCoAuthorsAsync(string query)
    {
        PlatformClient? client = await SearchClientAsync();
        if (client == null || query.TrimStart('@').Length < 2)
            return new List<CoAuthor>();

        string login = query.TrimStart('@');
        List<PlatformUser> users = await PlatformClient.SafelyAsync(() => client.SearchUsersAsync(login));
        return users.Select(client.CoAuthorFor).OfType<CoAuthor>().ToList();
    }

    // Where to get the picture for someone who committed with this email, if it is known without asking
    public string? AvatarUrlFor(string email)
    {
        if (_pictures.TryGetValue(email.ToLowerInvariant(), out string? learned))
            return learned.Length > 0 ? learned : null;

        Account? own = _settings.Accounts.FirstOrDefault(account =>
            string.Equals(account.Email, email, StringComparison.OrdinalIgnoreCase));
        if (own?.AvatarUrl != null)
            return own.AvatarUrl;

        (long? Id, string Login)? noreply = GitHubClient.FromNoreplyEmail(email);
        if (noreply == null)
            return null;
        return noreply.Value.Id is { } githubId
            ? GitHubClient.AvatarUrl(githubId)
            : "https://github.com/" + Uri.EscapeDataString(noreply.Value.Login) + ".png?size=" +
              PlatformClient.AVATAR_SIZE;
    }

    // Asks the repository's platform who wrote commits whose emails have no picture yet; returns how many were found
    public async Task<int> LearnPicturesAsync(string repositoryPath, IEnumerable<CommitInfo> commits)
    {
        RemoteIdentity? remote = RemoteIdentity.Read(repositoryPath);
        PlatformClient? client = remote == null ? null : await ClientForHostAsync(remote.Host);
        if (remote == null || client == null)
            return 0;

        List<CommitInfo> unknown = commits.Where(commit => commit.Stash == null && commit.Email.Length > 0 &&
                                                           AvatarUrlFor(commit.Email) == null &&
                                                           !_pictures.ContainsKey(commit.Email.ToLowerInvariant()))
                                          .DistinctBy(commit => commit.Email.ToLowerInvariant())
                                          .Take(PICTURE_LOOKUPS).ToList();
        int found = 0;
        foreach (CommitInfo commit in unknown)
        {
            try
            {
                PlatformUser? user = await PlatformClient.SafelyAsync(() => client.GetCommitAuthorAsync(
                                                                          remote.Owner, remote.Name, commit.Hash,
                                                                          commit.Email));
                // An empty entry remembers that nobody was found, so the commit isn't asked about again
                _pictures[commit.Email.ToLowerInvariant()] = user?.AvatarUrl ?? string.Empty;
                if (user?.AvatarUrl != null)
                    ++found;
            }
            catch (PlatformException)
            {
                break;
            }
        }

        if (unknown.Count > 0)
            SavePictures();
        if (found > 0)
            PicturesChanged?.Invoke();
        return found;
    }

    private static Dictionary<string, string> LoadPictures(string? file)
    {
        try
        {
            if (file != null && File.Exists(file))
                return JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(file)) ??
                       new Dictionary<string, string>();
        }
        catch (Exception exception) when (exception is IOException or JsonException or UnauthorizedAccessException)
        {
            // A damaged file is replaced the next time pictures are found
        }

        return new Dictionary<string, string>();
    }

    private void SavePictures()
    {
        if (_picturesFile == null)
            return;

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_picturesFile)!);
            File.WriteAllText(_picturesFile, JsonSerializer.Serialize(_pictures));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // Pictures are found again next time
        }
    }
}
