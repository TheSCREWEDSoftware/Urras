using Urras.Core.Accounts;

namespace Urras.App;

// Accounts: sign in to GitHub with a token, for co-authors by @username and profile pictures
public partial class MainWindow
{
    private const string TOKEN_PAGE = "https://github.com/settings/tokens/new?description=Urras&scopes=repo,read:user";

    private static readonly HttpClient AccountsHttp = CreateAccountsHttp();
    private AccountManager? _accounts;

    private static HttpClient CreateAccountsHttp()
    {
        return new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
    }

    private void InitializeAccounts()
    {
        _accounts = new AccountManager(_settings, TokenStores.ForThisSystem(), AccountsHttp);
        AvatarView.Accounts = _accounts;
        AccountsButton.Click += async (_, _) => await ManageAccountsAsync();
    }

    private async Task ManageAccountsAsync()
    {
        if (_accounts == null)
            return;

        List<Account> accounts = _accounts.Accounts.ToList();
        string label = accounts.Count == 0
            ? "No accounts yet. Add your GitHub account to find co-authors by @username and show profile pictures."
            : "Signed in:";
        (Account? account, string? action) = await Dialogs.ListActionsAsync(
            this, "Accounts", label, accounts, "Add GitHub account…", "Add GitHub Enterprise account…", "Remove");

        switch (action)
        {
            case "Add GitHub account…":
                await AddGitHubAccountAsync(GitHubClient.GITHUB_API);
                break;
            case "Add GitHub Enterprise account…":
                string? host = await Dialogs.PromptAsync(this, "GitHub Enterprise", "Server address:",
                                                         "https://github.example.com");
                if (host != null)
                    await AddGitHubAccountAsync(host.TrimEnd('/') + "/api/v3");
                break;
            case "Remove" when account != null:
                if (await Dialogs.ConfirmAsync(this, "Remove account",
                                               "Sign out of " + account + "? Its token is deleted from this computer.",
                                               "Remove"))
                    await _accounts.RemoveAsync(account);
                break;
            default:
                return;
        }

        await ManageAccountsAsync();
    }

    private async Task AddGitHubAccountAsync(string api)
    {
        if (_accounts == null)
            return;

        // The token page opens with the right permissions ticked; the user copies the token back here
        if (api == GitHubClient.GITHUB_API)
            await Report(SystemShell.OpenFile(TOKEN_PAGE));

        string? token = await Dialogs.PromptAsync(this, "Add GitHub account",
                                                  "Paste a personal access token (the page that opened creates one " +
                                                      "with the repo and read:user permissions). It is kept in " +
                                                      "your system's credential store.");
        if (token == null)
            return;

        try
        {
            Account account = await _accounts.AddGitHubAsync(token, api);
            SetStatus("Signed in as @" + account.Username + ".", false);
        }
        catch (PlatformException exception)
        {
            SetStatus("Couldn't add the account: " + exception.Message, true);
        }
    }
}
