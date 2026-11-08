using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace Urras.App;

// Small modal dialogs built in code, since Avalonia has no built-in message box
public static class Dialogs
{
    private const double DIALOG_WIDTH = 460;

    public static async Task<bool> ConfirmAsync(Window owner, string title, string message, string confirmText)
    {
        Window dialog = CreateWindow(title);
        Button confirm = new Button { Content = confirmText, IsDefault = true };
        Button cancel = new Button { Content = "Cancel", IsCancel = true };
        confirm.Click += (_, _) => dialog.Close(true);
        cancel.Click += (_, _) => dialog.Close(false);

        dialog.Content = Layout(new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap }, null, confirm,
                                cancel);
        return await dialog.ShowDialog<bool>(owner);
    }

    public static async Task<string?> PromptAsync(Window owner, string title, string label, string initialText = "")
    {
        Window dialog = CreateWindow(title);
        TextBox input = new TextBox { Text = initialText };
        Button ok = new Button { Content = "OK", IsDefault = true };
        Button cancel = new Button { Content = "Cancel", IsCancel = true };
        ok.Click += (_, _) => dialog.Close(input.Text?.Trim());
        cancel.Click += (_, _) => dialog.Close(null);
        dialog.Opened += (_, _) => input.Focus();

        dialog.Content = Layout(new TextBlock { Text = label, TextWrapping = TextWrapping.Wrap }, input, ok, cancel);
        string? result = await dialog.ShowDialog<string?>(owner);
        return string.IsNullOrWhiteSpace(result) ? null : result;
    }

    public static async Task<string?> ChooseAsync(Window owner, string title, string label, IEnumerable<string> options)
    {
        Window dialog = CreateWindow(title);
        ComboBox choices = new ComboBox
        {
            ItemsSource = options.ToList(),
            SelectedIndex = 0,
            HorizontalAlignment = HorizontalAlignment.Stretch
        };
        Button ok = new Button { Content = "OK", IsDefault = true };
        Button cancel = new Button { Content = "Cancel", IsCancel = true };
        ok.Click += (_, _) => dialog.Close(choices.SelectedItem as string);
        cancel.Click += (_, _) => dialog.Close(null);

        dialog.Content = Layout(new TextBlock { Text = label, TextWrapping = TextWrapping.Wrap }, choices, ok, cancel);
        return await dialog.ShowDialog<string?>(owner);
    }

    private static Window CreateWindow(string title)
    {
        return new Window
        {
            Title = title,
            Width = DIALOG_WIDTH,
            SizeToContent = SizeToContent.Height,
            CanResize = false,
            ShowInTaskbar = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner
        };
    }

    private static Control Layout(Control message, Control? input, Button primary, Button secondary)
    {
        StackPanel buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Spacing = 8
        };
        buttons.Children.Add(primary);
        buttons.Children.Add(secondary);

        StackPanel root = new StackPanel { Margin = new Thickness(16), Spacing = 12 };
        root.Children.Add(message);
        if (input != null)
            root.Children.Add(input);
        root.Children.Add(buttons);
        return root;
    }
}
