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

    private const int FILTER_THRESHOLD = 8;
    private const double LIST_HEIGHT = 260;

    // A drop-down for short lists; a filter box and a list for long ones
    public static async Task<string?> ChooseAsync(Window owner, string title, string label, IEnumerable<string> options)
    {
        List<string> all = options.ToList();
        Window dialog = CreateWindow(title);
        Button ok = new Button { Content = "OK", IsDefault = true };
        Button cancel = new Button { Content = "Cancel", IsCancel = true };
        cancel.Click += (_, _) => dialog.Close(null);

        if (all.Count <= FILTER_THRESHOLD)
        {
            ComboBox choices = new ComboBox
            {
                ItemsSource = all,
                SelectedIndex = 0,
                HorizontalAlignment = HorizontalAlignment.Stretch
            };
            ok.Click += (_, _) => dialog.Close(choices.SelectedItem as string);
            dialog.Content = Layout(new TextBlock { Text = label, TextWrapping = TextWrapping.Wrap }, choices, ok,
                                    cancel);
            return await dialog.ShowDialog<string?>(owner);
        }

        TextBox filter = new TextBox { Watermark = "Filter" };
        ListBox list = new ListBox { ItemsSource = all, Height = LIST_HEIGHT, SelectedIndex = 0 };
        filter.TextChanged += (_, _) =>
        {
            string text = filter.Text ?? string.Empty;
            List<string> matches = all.Where(option => option.Contains(text, StringComparison.OrdinalIgnoreCase))
                .ToList();
            list.ItemsSource = matches;
            list.SelectedIndex = matches.Count > 0 ? 0 : -1;
        };
        ok.Click += (_, _) => dialog.Close(list.SelectedItem as string);
        list.DoubleTapped += (_, _) => dialog.Close(list.SelectedItem as string);
        dialog.Opened += (_, _) => filter.Focus();

        StackPanel input = new StackPanel { Spacing = 8 };
        input.Children.Add(filter);
        input.Children.Add(list);
        dialog.Content = Layout(new TextBlock { Text = label, TextWrapping = TextWrapping.Wrap }, input, ok, cancel);
        return await dialog.ShowDialog<string?>(owner);
    }

    // Read-only text, such as a commit message template
    public static async Task ShowTextAsync(Window owner, string title, string text)
    {
        Window dialog = CreateWindow(title);
        TextBox content = new TextBox
        {
            Text = text,
            IsReadOnly = true,
            AcceptsReturn = true,
            TextWrapping = TextWrapping.Wrap,
            MaxHeight = LIST_HEIGHT
        };
        Button close = new Button { Content = "Close", IsDefault = true, IsCancel = true };
        Button unused = new Button { IsVisible = false };
        close.Click += (_, _) => dialog.Close(null);

        dialog.Content = Layout(new TextBlock(), content, close, unused);
        await dialog.ShowDialog<object?>(owner);
    }

    public static async Task AboutAsync(Window owner, string version)
    {
        Window dialog = CreateWindow("About Urras");
        StackPanel text = new StackPanel { Spacing = 8 };
        text.Children.Add(new TextBlock { Text = "Urras", FontSize = 22, FontWeight = FontWeight.SemiBold });
        text.Children.Add(new TextBlock { Text = "Version " + version, Opacity = 0.7 });
        text.Children.Add(Paragraph("A Git client for Windows and Linux."));
        text.Children.Add(Paragraph("Copyright (C) 2026 TheSCREWEDSoftware"));
        text.Children.Add(Paragraph(
            "Urras is free software: you can redistribute it and/or modify it under the terms of the GNU Affero " +
            "General Public License as published by the Free Software Foundation, either version 3 of the License, " +
            "or (at your option) any later version. It comes with ABSOLUTELY NO WARRANTY; see the LICENSE file for " +
            "details. The source code is available from the Urras repository."));
        text.Children.Add(Paragraph("Built with Avalonia (MIT) and Tmds.DBus.Protocol (MIT) on .NET."));

        Button close = new Button { Content = "Close", IsDefault = true, IsCancel = true };
        Button unused = new Button { IsVisible = false };
        close.Click += (_, _) => dialog.Close(null);
        dialog.Content = Layout(text, null, close, unused);
        await dialog.ShowDialog<object?>(owner);
    }

    // Returns the chosen background fetch interval in minutes (0 for off), or null when cancelled
    public static async Task<int?> SettingsAsync(Window owner, int autoFetchMinutes)
    {
        int[] intervals = { 0, 5, 10, 15, 30, 60 };
        Window dialog = CreateWindow("Settings");
        ComboBox fetch = new ComboBox
        {
            ItemsSource = intervals.Select(minutes => minutes == 0 ? "Off" : "Every " + minutes + " minutes")
                .ToList(),
            SelectedIndex = Math.Max(0, Array.IndexOf(intervals, autoFetchMinutes)),
            HorizontalAlignment = HorizontalAlignment.Stretch
        };
        Button save = new Button { Content = "Save", IsDefault = true };
        Button cancel = new Button { Content = "Cancel", IsCancel = true };
        save.Click += (_, _) => dialog.Close(intervals[Math.Max(0, fetch.SelectedIndex)]);
        cancel.Click += (_, _) => dialog.Close(null);

        TextBlock label = Paragraph("Fetch every repository in the background. Fetching never changes your files; " +
                                    "it only updates the commits to pull shown next to each repository.");
        dialog.Content = Layout(label, fetch, save, cancel);
        return await dialog.ShowDialog<int?>(owner);
    }

    private static TextBlock Paragraph(string text)
    {
        return new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap };
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
