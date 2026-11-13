using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Urras.Core.Git;
using Urras.Core.Settings;

namespace Urras.App;

// Remotes: where the repository fetches from and pushes to, such as origin and a fork's parent
public static partial class Dialogs
{
    private const double REMOTE_ROW_SPACING = 6;
    private const double REMOTE_LIST_HEIGHT = 320;
    // Wider than other dialogs, for addresses
    private const double REMOTES_WIDTH = 720;

    public static async Task RemotesAsync(Window owner, GitRepository repository)
    {
        Window dialog = CreateWindow("Remotes");
        StackPanel list = new StackPanel { Spacing = REMOTE_ROW_SPACING };
        TextBlock problem = new TextBlock { TextWrapping = TextWrapping.Wrap, IsVisible = false };
        Button add = new Button { Content = "Add remote…" };
        Button close = new Button { Content = "Close", IsCancel = true, IsDefault = true };
        close.Click += (_, _) => dialog.Close();

        // Runs a change, shows any error under the list, and lists the remotes again
        async Task Change(Func<Task<GitResult>> action)
        {
            GitResult result = await action();
            problem.IsVisible = !result.Success;
            problem.Text = result.Message.Trim();
            problem.Foreground = ThemeBrushes.Get(ThemeColours.ERROR);
            await Fill();
        }

        async Task Fill()
        {
            list.Children.Clear();
            List<RemoteInfo> remotes = await repository.GetRemoteListAsync();
            if (remotes.Count == 0)
                list.Children.Add(Hint("No remotes: this repository is only on this computer."));

            foreach (RemoteInfo remote in remotes)
            {
                Button rename = new Button { Content = "Rename…" };
                Button address = new Button { Content = "Change address…" };
                Button remove = new Button { Content = "Remove" };
                rename.Click += async (_, _) =>
                {
                    if (await PromptAsync(dialog, "Rename remote", "New name for " + remote.Name + ":",
                                          remote.Name) is { } name && name != remote.Name)
                        await Change(() => repository.RenameRemoteAsync(remote.Name, name));
                };
                address.Click += async (_, _) =>
                {
                    if (await PromptAsync(dialog, "Change address", "Where " + remote.Name + " points:",
                                          remote.Url) is { } url && url != remote.Url)
                        await Change(() => repository.SetRemoteUrlAsync(remote.Name, url));
                };
                remove.Click += async (_, _) =>
                {
                    if (await ConfirmAsync(dialog, "Remove remote",
                                           "Remove " + remote.Name + "? Only this repository forgets it; nothing " +
                                           "on " + remote.Url + " changes.", "Remove"))
                        await Change(() => repository.RemoveRemoteAsync(remote.Name));
                };

                StackPanel buttons = new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Spacing = REMOTE_ROW_SPACING,
                    VerticalAlignment = VerticalAlignment.Center
                };
                buttons.Children.Add(rename);
                buttons.Children.Add(address);
                buttons.Children.Add(remove);
                StackPanel names = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
                names.Children.Add(new TextBlock { Text = remote.Name, FontWeight = FontWeight.SemiBold });
                names.Children.Add(new TextBlock
                {
                    Text = remote.Url,
                    Opacity = HINT_OPACITY,
                    TextTrimming = TextTrimming.CharacterEllipsis
                });
                ToolTip.SetTip(names, remote.Url);
                DockPanel row = new DockPanel();
                DockPanel.SetDock(buttons, Dock.Right);
                row.Children.Add(buttons);
                row.Children.Add(names);
                list.Children.Add(row);
            }
        }

        add.Click += async (_, _) =>
        {
            if (await PromptAsync(dialog, "Add remote", "Name, such as origin or upstream:") is not { } name ||
                await PromptAsync(dialog, "Add remote", "Address of " + name + ":") is not { } url)
                return;
            await Change(() => repository.AddRemoteAsync(name, url));
        };

        await Fill();
        StackPanel body = new StackPanel { Spacing = BODY_SPACING };
        body.Children.Add(new ScrollViewer { Content = list, MaxHeight = REMOTE_LIST_HEIGHT });
        body.Children.Add(problem);
        body.Children.Add(add);
        dialog.Width = REMOTES_WIDTH;
        dialog.Content = Layout(Paragraph("Where this repository fetches from and pushes to."), body, close,
                                new Button { IsVisible = false });
        await dialog.ShowDialog(owner);
    }
}
