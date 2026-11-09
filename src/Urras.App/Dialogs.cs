using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace Urras.App;

// Small modal dialogs built in code, since Avalonia has no built-in message box
public static class Dialogs
{
    private const double DIALOG_WIDTH = 460;

    // Explanatory text under a field is dimmed so the field itself stands out
    private const double HINT_OPACITY = 0.7;

    // Space above a heading inside a dialog
    private static readonly Thickness SectionGap = new Thickness(0, 8, 0, 0);

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

    // Ticks for several items, all ticked to start with unless chosen says otherwise; null when cancelled
    public static async Task<List<T>?> ChooseManyAsync<T>(Window owner, string title, string label,
                                                          IReadOnlyList<T> items, string action,
                                                          Func<T, bool>? chosen = null)
    {
        Window dialog = CreateWindow(title);
        List<(CheckBox Box, T Item)> boxes = items.Select(item => (new CheckBox
        {
            Content = item?.ToString()?.Replace("_", "__"),
            IsChecked = chosen?.Invoke(item) ?? true
        }, item)).ToList();
        TextBox filter = new TextBox { Watermark = "Filter" };
        StackPanel list = new StackPanel { Spacing = 2 };
        foreach ((CheckBox box, T _) in boxes)
            list.Children.Add(box);
        filter.TextChanged += (_, _) =>
        {
            string text = filter.Text ?? string.Empty;
            foreach ((CheckBox box, T item) in boxes)
                box.IsVisible = item?.ToString()?.Contains(text, StringComparison.OrdinalIgnoreCase) ?? true;
        };

        Button all = new Button { Content = "All" };
        Button none = new Button { Content = "None" };
        all.Click += (_, _) => boxes.Where(entry => entry.Box.IsVisible).ToList()
                                    .ForEach(entry => entry.Box.IsChecked = true);
        none.Click += (_, _) => boxes.Where(entry => entry.Box.IsVisible).ToList()
                                     .ForEach(entry => entry.Box.IsChecked = false);
        StackPanel selection = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
        selection.Children.Add(all);
        selection.Children.Add(none);

        Button ok = new Button { Content = action, IsDefault = true };
        Button cancel = new Button { Content = "Cancel", IsCancel = true };
        ok.Click += (_, _) => dialog.Close(boxes.Where(entry => entry.Box.IsChecked == true)
                                                .Select(entry => entry.Item).ToList());
        cancel.Click += (_, _) => dialog.Close(null);

        StackPanel input = new StackPanel { Spacing = 8 };
        if (items.Count > FILTER_THRESHOLD)
            input.Children.Add(filter);
        input.Children.Add(new ScrollViewer { Content = list, MaxHeight = LIST_HEIGHT * 1.5 });
        input.Children.Add(selection);
        dialog.Content = Layout(Paragraph(label), input, ok, cancel);
        return await dialog.ShowDialog<List<T>?>(owner);
    }

    // A large text editor; null when cancelled
    public static async Task<string?> EditTextAsync(Window owner, string title, string text)
    {
        Window dialog = new Window
        {
            Title = title,
            Width = EDITOR_WIDTH,
            Height = EDITOR_HEIGHT,
            WindowStartupLocation = WindowStartupLocation.CenterOwner
        };
        TextBox editor = new TextBox
        {
            Text = text,
            AcceptsReturn = true,
            TextWrapping = TextWrapping.NoWrap,
            FontFamily = new FontFamily("Cascadia Mono,Consolas,DejaVu Sans Mono,Liberation Mono,monospace")
        };
        Button save = new Button { Content = "Save", IsDefault = false };
        Button cancel = new Button { Content = "Cancel", IsCancel = true };
        save.Click += (_, _) => dialog.Close(editor.Text ?? string.Empty);
        cancel.Click += (_, _) => dialog.Close(null);

        StackPanel buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 8, 0, 0)
        };
        buttons.Children.Add(save);
        buttons.Children.Add(cancel);
        DockPanel root = new DockPanel { Margin = new Thickness(12) };
        DockPanel.SetDock(buttons, Dock.Bottom);
        root.Children.Add(buttons);
        root.Children.Add(editor);
        dialog.Content = root;
        return await dialog.ShowDialog<string?>(owner);
    }

    private const double EDITOR_WIDTH = 900;
    private const double EDITOR_HEIGHT = 640;

    // A commit message: summary on the first line, then a blank line and the description
    public static async Task<string?> MessageAsync(Window owner, string title, string initialText)
    {
        Window dialog = CreateWindow(title);
        TextBox input = new TextBox
        {
            Text = initialText,
            AcceptsReturn = true,
            TextWrapping = TextWrapping.Wrap,
            Height = MESSAGE_HEIGHT
        };
        Button ok = new Button { Content = "OK" };
        Button cancel = new Button { Content = "Cancel", IsCancel = true };
        ok.Click += (_, _) => dialog.Close(input.Text?.Trim());
        cancel.Click += (_, _) => dialog.Close(null);
        dialog.Opened += (_, _) => input.Focus();

        TextBlock label = new TextBlock
        {
            Text = "Summary on the first line, then a blank line and the description.",
            TextWrapping = TextWrapping.Wrap
        };
        dialog.Content = Layout(label, input, ok, cancel);
        string? result = await dialog.ShowDialog<string?>(owner);
        return string.IsNullOrWhiteSpace(result) ? null : result;
    }

    private const double MESSAGE_HEIGHT = 160;
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
            "or (at your option) any later version. It comes with ABSOLUTELY NO WARRANTY; see the licence for " +
            "details."));
        text.Children.Add(Links.ToPage("Source code: " + Links.REPOSITORY, Links.REPOSITORY));
        text.Children.Add(Links.ToPage("Licence (GNU AGPL 3.0)", Links.Document("LICENSE")));
        text.Children.Add(Paragraph("Built with Avalonia (MIT) and Tmds.DBus.Protocol (MIT) on .NET."));

        Button close = new Button { Content = "Close", IsDefault = true, IsCancel = true };
        Button unused = new Button { IsVisible = false };
        close.Click += (_, _) => dialog.Close(null);
        dialog.Content = Layout(text, null, close, unused);
        await dialog.ShowDialog<object?>(owner);
    }

    // A list with a button per action; returns the selected item and the action clicked, or nulls when closed
    public static async Task<(T? Item, string? Action)> ListActionsAsync<T>(Window owner, string title, string label,
                                                                           IReadOnlyList<T> items,
                                                                           params string[] actions)
        where T : class
    {
        Window dialog = CreateWindow(title);
        ListBox list = new ListBox
        {
            ItemsSource = items,
            Height = LIST_HEIGHT,
            SelectedIndex = items.Count > 0 ? 0 : -1
        };
        WrapPanel buttons = new WrapPanel();
        foreach (string action in actions)
        {
            Button button = new Button { Content = action, Margin = new Thickness(0, 0, 6, 6) };
            button.Click += (_, _) => dialog.Close((list.SelectedItem as T, (string?)action));
            buttons.Children.Add(button);
        }

        Button close = new Button { Content = "Close", IsCancel = true };
        Button unused = new Button { IsVisible = false };
        close.Click += (_, _) => dialog.Close(((T?)null, (string?)null));

        StackPanel input = new StackPanel { Spacing = 8 };
        input.Children.Add(list);
        input.Children.Add(buttons);
        dialog.Content = Layout(Paragraph(label), input, close, unused);
        return await dialog.ShowDialog<(T?, string?)>(owner);
    }

    // Asks for a stash message and whether untracked files are included; null when cancelled
    public static async Task<(string Message, bool IncludeUntracked)?> StashAsync(Window owner)
    {
        Window dialog = CreateWindow("Stash changes");
        TextBox message = new TextBox { Watermark = "Message (optional)" };
        CheckBox untracked = new CheckBox { Content = "Include untracked files", IsChecked = true };
        Button stash = new Button { Content = "Stash", IsDefault = true };
        Button cancel = new Button { Content = "Cancel", IsCancel = true };
        stash.Click += (_, _) => dialog.Close(((string, bool)?)(message.Text?.Trim() ?? string.Empty,
                                                               untracked.IsChecked == true));
        cancel.Click += (_, _) => dialog.Close(null);
        dialog.Opened += (_, _) => message.Focus();

        StackPanel input = new StackPanel { Spacing = 8 };
        input.Children.Add(message);
        input.Children.Add(untracked);
        dialog.Content = Layout(Paragraph("Put your uncommitted changes aside, to bring them back later."), input,
                                stash, cancel);
        return await dialog.ShowDialog<(string, bool)?>(owner);
    }

    public sealed record RepositorySettingsResult(Urras.Core.Git.LineEndingPolicy Policy, bool EditorConfig,
                                                  bool Normalize, Urras.Core.Git.ForkTarget? Fork);

    // A fork and its parent as owner/name, the current choice, and what the user can push to
    public sealed record ForkChoice(string? Parent, string Fork, Urras.Core.Git.ForkTarget Target, string Access,
                                    bool ParentByDefault = true);

    // A question from Git or SSH: a username, a password or passphrase (hidden), or yes or no for a new host key
    public static async Task<string?> AskPassAsync(Window owner, string prompt)
    {
        Window dialog = CreateWindow(prompt.Contains("passphrase", StringComparison.OrdinalIgnoreCase)
                                         ? "SSH key passphrase"
                                         : "Git is asking");
        TextBlock question = Paragraph(prompt.Trim());
        if (prompt.Contains("(yes/no", StringComparison.OrdinalIgnoreCase))
        {
            Button yes = new Button { Content = "Yes, connect", IsDefault = true };
            Button no = new Button { Content = "No", IsCancel = true };
            yes.Click += (_, _) => dialog.Close("yes");
            no.Click += (_, _) => dialog.Close("no");
            dialog.Content = Layout(question, null, yes, no);
            return await dialog.ShowDialog<string?>(owner);
        }

        bool secret = !prompt.TrimStart().StartsWith("Username", StringComparison.OrdinalIgnoreCase);
        TextBox answer = new TextBox { PasswordChar = secret ? '•' : default };
        Button ok = new Button { Content = "OK", IsDefault = true };
        Button cancel = new Button { Content = "Cancel", IsCancel = true };
        ok.Click += (_, _) => dialog.Close(answer.Text ?? string.Empty);
        cancel.Click += (_, _) => dialog.Close(null);
        StackPanel input = new StackPanel { Spacing = 6 };
        input.Children.Add(answer);
        input.Children.Add(Hint("Urras passes this to Git once and doesn't keep it. A credential manager or SSH " +
                                "agent can remember it instead."));
        dialog.Content = Layout(question, input, ok, cancel);
        dialog.Opened += (_, _) => answer.Focus();
        return await dialog.ShowDialog<string?>(owner);
    }

    // Asks how a fork will be used, like GitHub Desktop does; null when cancelled
    public static async Task<Urras.Core.Git.ForkTarget?> ForkTargetAsync(Window owner, ForkChoice choice)
    {
        Window dialog = CreateWindow("How are you planning to use this fork?");
        (Control radios, Func<Urras.Core.Git.ForkTarget> selected) = ForkRadios(choice);
        Button save = new Button { Content = "Continue", IsDefault = true };
        Button cancel = new Button { Content = "Cancel", IsCancel = true };
        save.Click += (_, _) => dialog.Close(selected());
        cancel.Click += (_, _) => dialog.Close(null);

        StackPanel input = new StackPanel { Spacing = 10 };
        input.Children.Add(radios);
        input.Children.Add(Hint("Pull requests, \"View on…\" and checking out pull requests use the repository you " +
                                "choose. You can change it later in Repository settings."));
        input.Children.Add(Hint(choice.Access));
        dialog.Content = Layout(Paragraph("This repository is a fork of " + choice.Parent + ". How do you plan to " +
                                          "use it?"), input, save, cancel);
        return await dialog.ShowDialog<Urras.Core.Git.ForkTarget?>(owner);
    }

    private static (Control Radios, Func<Urras.Core.Git.ForkTarget> Selected) ForkRadios(ForkChoice choice)
    {
        RadioButton parent = new RadioButton
        {
            GroupName = "fork",
            Content = "To contribute to the parent project (" + choice.Parent + ")",
            IsChecked = UsesParent(choice)
        };
        RadioButton own = new RadioButton
        {
            GroupName = "fork",
            Content = "For my own purposes (" + choice.Fork + ")",
            IsChecked = !UsesParent(choice)
        };
        StackPanel radios = new StackPanel { Spacing = 4 };
        radios.Children.Add(parent);
        radios.Children.Add(own);
        return (radios, () => own.IsChecked == true ? Urras.Core.Git.ForkTarget.Own : Urras.Core.Git.ForkTarget.Parent);
    }

    // What is in effect now: the parent when chosen, or by default once the platform confirmed the fork
    private static bool UsesParent(ForkChoice choice)
    {
        return choice.Target == Urras.Core.Git.ForkTarget.Parent ||
               (choice.Target == Urras.Core.Git.ForkTarget.Unset && choice.ParentByDefault);
    }

    private static TextBlock Hint(string text)
    {
        return new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap, Opacity = HINT_OPACITY };
    }

    public static async Task<RepositorySettingsResult?> RepositorySettingsAsync(Window owner, string name,
                                                                                Urras.Core.Git.LineEndingPolicy current,
                                                                                ForkChoice fork)
    {
        Urras.Core.Git.LineEndingPolicy[] policies =
        {
            Urras.Core.Git.LineEndingPolicy.Custom, Urras.Core.Git.LineEndingPolicy.LfWithWindowsScripts,
            Urras.Core.Git.LineEndingPolicy.Lf, Urras.Core.Git.LineEndingPolicy.Crlf,
            Urras.Core.Git.LineEndingPolicy.Auto
        };
        string[] labels =
        {
            "Keep the current rules", "LF, with CRLF for Windows scripts (.bat, .cmd)", "LF everywhere",
            "CRLF everywhere", "Automatic (stored as LF, checked out with the system's endings)"
        };

        Window dialog = CreateWindow("Repository settings: " + name);
        ComboBox policy = new ComboBox
        {
            ItemsSource = labels,
            SelectedIndex = Math.Max(0, Array.IndexOf(policies, current)),
            HorizontalAlignment = HorizontalAlignment.Stretch
        };
        CheckBox editorConfig = new CheckBox { Content = "Also set end_of_line in .editorconfig", IsChecked = true };
        CheckBox normalize = new CheckBox
        {
            Content = "Normalize files already in the repository (shows the list first)",
            IsChecked = false
        };
        Button save = new Button { Content = "Save", IsDefault = true };
        Button cancel = new Button { Content = "Cancel", IsCancel = true };
        Func<Urras.Core.Git.ForkTarget>? forkTarget = null;
        Control? forkRadios = null;
        if (fork.Parent != null)
            (forkRadios, forkTarget) = ForkRadios(fork);
        // Only a changed choice is saved, so saving line endings doesn't also decide the fork question
        Urras.Core.Git.ForkTarget? shown = forkTarget?.Invoke();
        save.Click += (_, _) =>
        {
            Urras.Core.Git.ForkTarget? chosen = forkTarget?.Invoke();
            dialog.Close(new RepositorySettingsResult(policies[Math.Max(0, policy.SelectedIndex)],
                                                      editorConfig.IsChecked == true, normalize.IsChecked == true,
                                                      chosen == shown ? null : chosen));
        };
        cancel.Click += (_, _) => dialog.Close(null);

        StackPanel input = new StackPanel { Spacing = 10 };
        input.Children.Add(policy);
        input.Children.Add(editorConfig);
        input.Children.Add(normalize);
        input.Children.Add(new TextBlock { Text = "Fork", FontWeight = FontWeight.SemiBold, Margin = SectionGap });
        if (forkRadios != null)
            input.Children.Add(forkRadios);
        input.Children.Add(Hint(fork.Access));
        TextBlock label = Paragraph("Line endings for this repository. Urras writes the rule to .gitattributes, and " +
                                    "optionally .editorconfig; commit those files to share the rule.");
        dialog.Content = Layout(label, input, save, cancel);
        return await dialog.ShowDialog<RepositorySettingsResult?>(owner);
    }

    // Adds or edits a custom command; null when cancelled
    public static async Task<Urras.Core.Commands.CustomCommand?> CustomCommandAsync(
        Window owner, Urras.Core.Commands.CustomCommand? existing)
    {
        Window dialog = CreateWindow(existing == null ? "Add a command" : "Edit command");
        TextBox name = new TextBox { Text = existing?.Name, Watermark = "Name in the menu, such as Build" };
        TextBox program = new TextBox { Text = existing?.Program, Watermark = "Program: git, code, cmd, dotnet…" };
        TextBox arguments = new TextBox
        {
            Text = existing?.Arguments,
            Watermark = "Arguments, such as build \"${repo}/Urras.slnx\""
        };
        TextBlock noShell = new TextBlock
        {
            Text = "Arguments go straight to the program, not through a shell, so pipes and && don't apply.",
            TextWrapping = TextWrapping.Wrap,
            Opacity = HINT_OPACITY
        };
        ComboBox scope = new ComboBox
        {
            ItemsSource = new[] { "Commands menu (repository)", "File menu", "Commit menu" },
            SelectedIndex = (int)(existing?.Scope ?? Urras.Core.Commands.CommandScope.Repository),
            HorizontalAlignment = HorizontalAlignment.Stretch
        };
        CheckBox confirm = new CheckBox { Content = "Ask before running", IsChecked = existing?.Confirm ?? false };
        TextBlock problem = new TextBlock { Foreground = Brushes.IndianRed, TextWrapping = TextWrapping.Wrap };

        Button save = new Button { Content = "Save", IsDefault = true };
        Button cancel = new Button { Content = "Cancel", IsCancel = true };
        save.Click += (_, _) =>
        {
            if (string.IsNullOrWhiteSpace(name.Text) || string.IsNullOrWhiteSpace(program.Text))
            {
                problem.Text = "A command needs a name and a program.";
                return;
            }

            dialog.Close(new Urras.Core.Commands.CustomCommand
            {
                Name = name.Text.Trim(),
                Program = program.Text.Trim(),
                Arguments = arguments.Text?.Trim() ?? string.Empty,
                Scope = (Urras.Core.Commands.CommandScope)Math.Max(0, scope.SelectedIndex),
                Confirm = confirm.IsChecked == true
            });
        };
        cancel.Click += (_, _) => dialog.Close(null);

        StackPanel input = new StackPanel { Spacing = 8 };
        foreach (Control control in new Control[] { name, program, arguments, noShell, scope, confirm, problem })
            input.Children.Add(control);
        TextBlock label = Paragraph("Placeholders: ${repo} (repository folder), ${branch}, ${file} (path in the " +
                                    "repository), ${filePath} (full path) and ${sha} (commit). Quote arguments " +
                                    "with spaces. The command runs in the repository folder.");
        dialog.Content = Layout(label, input, save, cancel);
        return await dialog.ShowDialog<Urras.Core.Commands.CustomCommand?>(owner);
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
