using Avalonia.Controls;
using Avalonia.Threading;
using Urras.Core.Accounts;

namespace Urras.App;

// Accounts on GitHub, GitLab, Gitea and Bitbucket, signed in with tokens
public partial class MainWindow
{
    private const int ACCOUNTS_TIMEOUT_SECONDS = 20;
    private static readonly HttpClient AccountsHttp = CreateAccountsHttp();
    private AccountManager? _accounts;

    // Git's questions: a signed-in account answers HTTPS logins by itself, anything else is asked in a dialog
    public async Task<string?> AnswerGitAsync(string prompt)
    {
        if (_accounts != null && await _accounts.AnswerGitAsync(prompt) is { } answer)
            return answer;
        return await Dialogs.AskPassAsync(this, prompt);
    }

    private static HttpClient CreateAccountsHttp()
    {
        return new HttpClient { Timeout = TimeSpan.FromSeconds(ACCOUNTS_TIMEOUT_SECONDS) };
    }

    private void InitializeAccounts()
    {
        string pictures = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                                       "Urras", "pictures.json");
        _accounts = new AccountManager(_settings, TokenStores.ForThisSystem(), AccountsHttp, pictures);
        _accounts.PicturesChanged += () => Dispatcher.UIThread.Post(AvatarView.RefreshAll);
        AvatarView.Accounts = _accounts;
        AccountsButton.Click += async (_, _) => await ManageAccountsAsync();
        NotificationsButton.Click += async (_, _) => await ShowNotificationsAsync();
    }

    private Task ManageAccountsAsync()
    {
        return OpenOptionsAsync(OptionsWindow.ACCOUNTS);
    }

    // Asks for the server where the platform can be self-hosted, opens the token page, then signs in
    private async Task AddAccountAsync(Window owner, AccountKind kind)
    {
        if (_accounts == null)
            return;

        string name = Account.PlatformName(kind);
        string? server = null;
        if (kind is not (AccountKind.GitHub or AccountKind.Bitbucket))
        {
            string suggestion = kind switch
            {
                AccountKind.GitLab => "https://gitlab.com",
                AccountKind.Gitea => "https://codeberg.org",
                _ => "https://"
            };
            server = await Dialogs.PromptAsync(owner, name, "Server address:", suggestion);
            if (server == null)
                return;
        }

        string? token = await SignInThroughBrowserAsync(owner, kind, name, server ?? string.Empty);
        if (token == DeviceSignInWindow.TOKEN_INSTEAD)
            token = await AskForTokenAsync(owner, kind, name, server);
        if (token == null)
            return;

        try
        {
            Account account = await _accounts.AddAsync(kind, token, Account.ApiFor(kind, server));
            SetStatus("Signed in to " + name + " as @" + account.Username + ".", false);
        }
        catch (PlatformException exception)
        {
            SetStatus("Couldn't add the account: " + exception.Message, true);
            await Dialogs.ShowTextAsync(owner, "Couldn't add the account", exception.Message);
        }
    }

    // The browser flow when the platform supports it and an app id is set; TOKEN_INSTEAD otherwise
    private async Task<string?> SignInThroughBrowserAsync(Window owner, AccountKind kind, string name, string server)
    {
        string? appId = kind is AccountKind.GitLab ? _settings.GitLabClientId : _settings.GitHubClientId;
        DeviceSignIn? flow = DeviceSignIn.For(kind, server, AccountsHttp, appId);
        if (flow == null)
            return DeviceSignInWindow.TOKEN_INSTEAD;

        try
        {
            DeviceCode code = await flow.StartAsync();
            return await new DeviceSignInWindow(name, flow, code).ShowDialog<string?>(owner);
        }
        catch (PlatformException exception)
        {
            SetStatus("Browser sign-in isn't available (" + exception.Message + "); paste a token instead.", true);
            return DeviceSignInWindow.TOKEN_INSTEAD;
        }
    }

    private async Task<string?> AskForTokenAsync(Window owner, AccountKind kind, string name, string? server)
    {
        await Report(SystemShell.OpenUrl(TokenPage(kind, server?.TrimEnd('/') ?? string.Empty)));
        string hint = kind == AccountKind.Bitbucket
            ? "Paste an access token, or username:app-password for an app password."
            : "Paste a personal access token from the page that opened.";
        return await Dialogs.PromptAsync(owner, "Sign in to " + name,
                                         hint + " It is kept in your system's credential store.");
    }

    // Where each platform creates tokens, with the right permissions ticked where the page allows it
    private static string TokenPage(AccountKind kind, string server)
    {
        return kind switch
        {
            AccountKind.GitHub => "https://github.com/settings/tokens/new?description=Urras&scopes=repo,read:user," +
                                  "notifications",
            AccountKind.GitHubEnterprise => server + "/settings/tokens/new?description=Urras&scopes=repo,read:user," +
                                            "notifications",
            AccountKind.GitLab => server + "/-/user_settings/personal_access_tokens?name=Urras&scopes=api,read_user",
            AccountKind.Gitea => server + "/user/settings/applications",
            AccountKind.Bitbucket => "https://bitbucket.org/account/settings/app-passwords/",
            _ => server + "/plugins/servlet/access-tokens/manage"
        };
    }
}
