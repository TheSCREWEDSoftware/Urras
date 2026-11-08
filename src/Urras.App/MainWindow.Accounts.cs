using Avalonia.Controls;
using Urras.Core.Accounts;

namespace Urras.App;

// Accounts: sign in to GitHub with a token, for co-authors by @username and profile pictures
public partial class MainWindow
{
    private const string TOKEN_PAGE = "https://github.com/settings/tokens/new?description=Urras&scopes=repo,read:user";

    private const int ACCOUNTS_TIMEOUT_SECONDS = 20;
    private static readonly HttpClient AccountsHttp = CreateAccountsHttp();
    private AccountManager? _accounts;

    private static HttpClient CreateAccountsHttp()
    {
        return new HttpClient { Timeout = TimeSpan.FromSeconds(ACCOUNTS_TIMEOUT_SECONDS) };
    }

    private void InitializeAccounts()
    {
        _accounts = new AccountManager(_settings, TokenStores.ForThisSystem(), AccountsHttp);
        AvatarView.Accounts = _accounts;
        AccountsButton.Click += async (_, _) => await ManageAccountsAsync();
    }

    private Task ManageAccountsAsync()
    {
        return OpenOptionsAsync(OptionsWindow.ACCOUNTS);
    }

    // An empty api asks for a GitHub Enterprise server's address first
    private async Task AddGitHubAccountAsync(Window owner, string api)
    {
        if (api.Length == 0)
        {
            string? host = await Dialogs.PromptAsync(owner, "GitHub Enterprise", "Server address:",
                                                     "https://github.example.com");
            if (host == null)
                return;
            api = host.TrimEnd('/') + "/api/v3";
        }

        await SignInWithTokenAsync(owner, api);
    }

    private async Task SignInWithTokenAsync(Window owner, string api)
    {
        if (_accounts == null)
            return;

        // The token page opens with the right permissions ticked; the user copies the token back here
        if (api == GitHubClient.GITHUB_API)
            await Report(SystemShell.OpenFile(TOKEN_PAGE));

        string? token = await Dialogs.PromptAsync(owner, "Add GitHub account",
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
            await Dialogs.ShowTextAsync(owner, "Couldn't add the account", exception.Message);
        }
    }
}
