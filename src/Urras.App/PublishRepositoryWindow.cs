using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Urras.Core.Accounts;
using Urras.Core.Git;

namespace Urras.App;

// Make the repository online, like GitHub Desktop's Publish repository: name, description, private, organisation
public sealed class PublishRepositoryWindow : Window
{
    private const double WINDOW_WIDTH = 460;
    private const string NONE = "None";

    private static IBrush MutedBrush => ThemeBrushes.MutedText;

    private readonly List<(Account Account, PlatformClient Client)> _choices;
    private readonly Func<Window, Account, Task<PlatformClient?>> _signInAgain;
    private readonly ComboBox _account = new ComboBox { HorizontalAlignment = HorizontalAlignment.Stretch };
    private readonly TextBox _name = new TextBox();
    private readonly TextBlock _renamed = new TextBlock { Foreground = MutedBrush, TextWrapping = TextWrapping.Wrap };
    private readonly TextBox _description = new TextBox();
    private readonly CheckBox _private = new CheckBox { Content = "Keep this code private", IsChecked = true };
    private readonly ComboBox _organization = new ComboBox
    {
        ItemsSource = new object[] { NONE },
        SelectedIndex = 0,
        HorizontalAlignment = HorizontalAlignment.Stretch
    };
    private readonly TextBlock _problem = new TextBlock { Foreground = Brushes.IndianRed,
                                                          TextWrapping = TextWrapping.Wrap, IsVisible = false };
    private readonly Button _publish = new Button { Content = "Publish repository", IsDefault = true,
                                                    Classes = { "accent" } };
    private readonly Button _signIn = new Button { Content = "Sign in again", IsVisible = false };
    private int _organizationsLoad;

    // signInAgain replaces an account whose token the site refused; null when the sign-in was cancelled
    public PublishRepositoryWindow(List<(Account Account, PlatformClient Client)> choices, string folderName,
                                   Func<Window, Account, Task<PlatformClient?>> signInAgain)
    {
        _choices = choices;
        _signInAgain = signInAgain;
        Title = "Publish repository";
        Width = WINDOW_WIDTH;
        SizeToContent = SizeToContent.Height;
        CanResize = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = ThemeBrushes.Get(Urras.Core.Settings.ThemeColours.SURFACE);
        _name.Text = folderName;
        _account.ItemsSource = choices.Select(choice => choice.Account).ToList();
        _account.SelectedIndex = 0;
        // With a single account there's nothing to choose
        _account.IsVisible = choices.Count > 1;

        Button cancel = new Button { Content = "Cancel", IsCancel = true };
        cancel.Click += (_, _) => Close(null);
        _publish.Click += async (_, _) => await PublishAsync();
        _signIn.Click += async (_, _) => await SignInAgainAsync();
        StackPanel buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Spacing = 8,
            Margin = new Thickness(0, 8, 0, 0)
        };
        buttons.Children.Add(_signIn);
        buttons.Children.Add(_publish);
        buttons.Children.Add(cancel);

        StackPanel root = new StackPanel { Margin = new Thickness(20), Spacing = 6 };
        TextBlock accountLabel = Label("Account");
        accountLabel.IsVisible = _account.IsVisible;
        foreach (Control control in new Control[]
                 {
                     accountLabel, _account, Label("Name"), _name, _renamed, Label("Description"), _description,
                     _private, Label("Organization"), _organization, _problem, buttons
                 })
            root.Children.Add(control);
        Content = root;

        _name.TextChanged += (_, _) => Update();
        _account.SelectionChanged += async (_, _) => await LoadOrganizationsAsync();
        Update();
        Opened += async (_, _) =>
        {
            _name.Focus();
            _name.SelectAll();
            await LoadOrganizationsAsync();
        };
    }

    private PlatformClient Client => _choices[Math.Max(0, _account.SelectedIndex)].Client;
    private Account SelectedAccount => _choices[Math.Max(0, _account.SelectedIndex)].Account;
    private string FinalName => RepositoryPublisher.SuggestName(_name.Text ?? string.Empty);

    // "Will be published as B_Forlorn-Hope---Complete" when the site won't take the name as typed
    private void Update()
    {
        string typed = _name.Text?.Trim() ?? string.Empty;
        _publish.IsEnabled = typed.Length > 0;
        _renamed.Text = typed.Length > 0 && FinalName != typed ? "Will be published as " + FinalName : string.Empty;
        _renamed.IsVisible = _renamed.Text.Length > 0;
    }

    private async Task LoadOrganizationsAsync()
    {
        int load = ++_organizationsLoad;
        _organization.ItemsSource = new object[] { NONE };
        _organization.SelectedIndex = 0;
        try
        {
            List<PlatformOrganization> organizations = await Client.GetOrganizationsAsync();
            // Another account was picked while this one loaded
            if (load != _organizationsLoad)
                return;
            _organization.ItemsSource = new object[] { NONE }.Concat(organizations).ToList();
            _organization.SelectedIndex = 0;
        }
        catch (PlatformException exception) when (exception.TokenRefused)
        {
            if (load == _organizationsLoad)
                ShowRefused();
        }
        catch (PlatformException exception)
        {
            ShowProblem("Couldn't list your organizations (" + exception.Message + "). You can still publish to " +
                        "your own account.");
        }
    }

    private async Task PublishAsync()
    {
        _problem.IsVisible = false;
        _publish.IsEnabled = false;
        try
        {
            NewRemoteRepository wanted = new NewRemoteRepository(FinalName, _description.Text?.Trim() ?? string.Empty,
                                                                 _private.IsChecked == true,
                                                                 _organization.SelectedItem as PlatformOrganization);
            Close(await Client.CreateRepositoryAsync(wanted));
        }
        catch (PlatformException exception) when (exception.TokenRefused)
        {
            ShowRefused();
            _publish.IsEnabled = true;
        }
        catch (PlatformException exception)
        {
            ShowProblem("Couldn't publish: " + exception.Message);
            _publish.IsEnabled = true;
        }
    }

    // Git still pushes with its own sign-in, but Urras's saved one was turned down
    private void ShowRefused()
    {
        Account account = SelectedAccount;
        ShowProblem(Account.PlatformName(account.Kind) + " refused Urras's sign-in for @" + account.Username +
                    "; it may have been revoked. Sign in again to publish.");
        _signIn.IsVisible = true;
    }

    private async Task SignInAgainAsync()
    {
        int index = Math.Max(0, _account.SelectedIndex);
        if (await _signInAgain(this, _choices[index].Account) is not { } client)
            return;

        _choices[index] = (_choices[index].Account, client);
        _signIn.IsVisible = false;
        _problem.IsVisible = false;
        await LoadOrganizationsAsync();
    }

    private void ShowProblem(string text)
    {
        _problem.Text = text;
        _problem.IsVisible = true;
    }

    private static TextBlock Label(string text)
    {
        return new TextBlock { Text = text, Foreground = MutedBrush, Margin = new Thickness(0, 4, 0, 0) };
    }
}
