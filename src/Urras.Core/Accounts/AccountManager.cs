using Urras.Core.Git;
using Urras.Core.Settings;

namespace Urras.Core.Accounts;

// Adds, removes and uses accounts: settings hold who is signed in, the token store holds the tokens
public sealed class AccountManager
{
    private readonly AppSettings _settings;
    private readonly ITokenStore _tokens;
    private readonly HttpClient _http;

    public AccountManager(AppSettings settings, ITokenStore tokens, HttpClient http)
    {
        _settings = settings;
        _tokens = tokens;
        _http = http;
    }

    public IReadOnlyList<Account> Accounts => _settings.Accounts;

    // Checks the token with the platform, then keeps it; throws PlatformException with a readable message
    public async Task<Account> AddGitHubAsync(string token, string api = GitHubClient.GITHUB_API)
    {
        if (_tokens.Unavailable is { } reason)
            throw new PlatformException(reason);

        Account account = await new GitHubClient(_http, token.Trim(), api).GetAccountAsync();
        if (!await _tokens.SaveAsync(account.TokenKey, token.Trim()))
            throw new PlatformException("Couldn't save the token in the system's credential store.");

        _settings.Accounts.RemoveAll(existing => existing.TokenKey == account.TokenKey);
        _settings.Accounts.Add(account);
        _settings.Save();
        return account;
    }

    public async Task RemoveAsync(Account account)
    {
        await _tokens.DeleteAsync(account.TokenKey);
        _settings.Accounts.RemoveAll(existing => existing.TokenKey == account.TokenKey);
        _settings.Save();
    }

    public async Task<GitHubClient?> GitHubAsync()
    {
        Account? account = _settings.Accounts.FirstOrDefault(item => item.Kind is AccountKind.GitHub
                                                                          or AccountKind.GitHubEnterprise);
        if (account == null)
            return null;

        string? token = await _tokens.LoadAsync(account.TokenKey);
        return token == null ? null : new GitHubClient(_http, token, account.ApiUrl);
    }

    // "@login" becomes a co-author with GitHub's private address for that user
    public async Task<CoAuthor?> FindCoAuthorAsync(string login)
    {
        GitHubClient? client = await GitHubAsync();
        PlatformUser? user = client == null ? null : await client.GetUserAsync(login.TrimStart('@'));
        return user == null ? null : CoAuthor.FromGitHub(user.Id, user.Login, user.Name);
    }

    public async Task<List<CoAuthor>> SearchCoAuthorsAsync(string query)
    {
        GitHubClient? client = await GitHubAsync();
        if (client == null || query.TrimStart('@').Length < 2)
            return new List<CoAuthor>();

        return (await client.SearchUsersAsync(query.TrimStart('@')))
               .Select(user => CoAuthor.FromGitHub(user.Id, user.Login, user.Name)).ToList();
    }

    // Where to get the picture for someone who committed with this email, if it can be known without asking
    public string? AvatarUrlFor(string email)
    {
        Account? own = _settings.Accounts.FirstOrDefault(account =>
            string.Equals(account.Email, email, StringComparison.OrdinalIgnoreCase));
        if (own?.AvatarUrl != null)
            return own.AvatarUrl;

        (long? Id, string Login)? noreply = GitHubClient.FromNoreplyEmail(email);
        if (noreply == null)
            return null;
        return noreply.Value.Id is { } githubId
            ? GitHubClient.AvatarUrl(githubId)
            : "https://github.com/" + Uri.EscapeDataString(noreply.Value.Login) + ".png?size=64";
    }
}
