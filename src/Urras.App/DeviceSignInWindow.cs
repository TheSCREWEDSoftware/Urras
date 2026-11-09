using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Urras.Core.Accounts;

namespace Urras.App;

// Shows the code to approve in the browser and waits; closes with the token, or with null to paste a token instead
public sealed class DeviceSignInWindow : Window
{
    private const double WINDOW_WIDTH = 460;
    private const double CODE_FONT_SIZE = 30;

    private readonly CancellationTokenSource _cancel = new CancellationTokenSource();
    private readonly TextBlock _status;

    public DeviceSignInWindow(string platform, DeviceSignIn flow, DeviceCode code)
    {
        Title = "Sign in to " + platform;
        Width = WINDOW_WIDTH;
        SizeToContent = SizeToContent.Height;
        CanResize = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        TextBlock intro = new TextBlock
        {
            Text = "Approve Urras in your browser: open the page, sign in if asked, and enter this code.",
            TextWrapping = TextWrapping.Wrap
        };
        TextBox codeBox = new TextBox
        {
            Text = code.UserCode,
            IsReadOnly = true,
            FontSize = CODE_FONT_SIZE,
            HorizontalContentAlignment = HorizontalAlignment.Center
        };
        _status = new TextBlock { Text = "Waiting for approval…", Opacity = 0.7 };

        Button open = new Button { Content = "Copy code and open the page", Classes = { "accent" } };
        Button token = new Button { Content = "Use a token instead" };
        Button cancel = new Button { Content = "Cancel", IsCancel = true };
        open.Click += async (_, _) =>
        {
            if (Clipboard != null)
                await Clipboard.SetTextAsync(code.UserCode);
            SystemShell.OpenUrl(code.VerificationUrl);
        };
        token.Click += (_, _) => Close(TOKEN_INSTEAD);
        cancel.Click += (_, _) => Close(null);
        Closed += (_, _) => _cancel.Cancel();

        StackPanel buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        buttons.Children.Add(open);
        buttons.Children.Add(token);
        buttons.Children.Add(cancel);
        StackPanel root = new StackPanel { Margin = new Thickness(16), Spacing = 12 };
        root.Children.Add(intro);
        root.Children.Add(codeBox);
        root.Children.Add(_status);
        root.Children.Add(buttons);
        Content = root;

        Opened += async (_, _) => await WaitAsync(flow, code);
    }

    // Returned when the user prefers pasting a token
    public const string TOKEN_INSTEAD = "\u0000token";

    // The token with its refresh token and expiry, once signed in
    public OAuthGrant? Grant { get; private set; }

    private async Task WaitAsync(DeviceSignIn flow, DeviceCode code)
    {
        try
        {
            Grant = await flow.WaitForGrantAsync(code, _cancel.Token);
            Close(Grant.AccessToken);
        }
        catch (PlatformException exception)
        {
            _status.Text = exception.Message;
            _status.Foreground = Brushes.IndianRed;
        }
        catch (TaskCanceledException)
        {
            // The window was closed
        }
    }
}
