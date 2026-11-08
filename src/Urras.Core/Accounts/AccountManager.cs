using System.Text.Json;
using Urras.Core.Git;
using Urras.Core.Settings;

namespace Urras.Core.Accounts;

// Adds, removes and uses accounts: settings hold who is signed in, the token store holds the tokens
public sealed class AccountManager
{
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

    public async Task<Account> AddAsync(AccountKind kind, string token, string api)
    {
        if (_tokens.Unavailable is { } reason)
            throw new PlatformException(reason);

        Account account = await Client(kind, token.Trim(), api).GetAccountAsync();
        if (!await _tokens.SaveAsync(account.TokenKey, token.Trim()))
            throw new PlatformException("Couldn't save the token in the system's credential store.");

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
        string? token = await _tokens.LoadAsync(account.TokenKey);
        return token == null ? null : Client(account.Kind, token, account.ApiUrl);
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
        PlatformUser? user = client == null ? null : await client.GetUserAsync(login.TrimStart('@'));
        return user == null ? null : client!.CoAuthorFor(user);
    }

    public async Task<List<CoAuthor>> SearchCoAuthorsAsync(string query)
    {
        PlatformClient? client = await SearchClientAsync();
        if (client == null || query.TrimStart('@').Length < 2)
            return new List<CoAuthor>();

        return (await client.SearchUsersAsync(query.TrimStart('@'))).Select(client.CoAuthorFor)
                                                                     .OfType<CoAuthor>().ToList();
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
                PlatformUser? user = await client.GetCommitAuthorAsync(remote.Owner, remote.Name, commit.Hash,
                                                                      commit.Email);
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
