using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Urras.Core.Git;
using Urras.Core.Settings;

namespace Urras.App;

// The clone dialog: address, branch, where to put it and the folder's name, then a bar while Git works
public static partial class Dialogs
{
    private const double FULL_PROGRESS = 100;
    private const double CHOOSE_GAP = 6;
    private const double FIELD_SPACING = 4;

    // The folder a finished clone went into, or null when cancelled
    public static async Task<string?> CloneAsync(Window owner, string? startFolder, string initialAddress = "")
    {
        Window dialog = CreateWindow("Clone a repository");
        TextBox address = new TextBox { Text = initialAddress, Watermark = "https://github.com/owner/name" };
        TextBox branch = new TextBox { Watermark = "The repository's default branch" };
        TextBox parent = new TextBox
        {
            Text = startFolder ?? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)
        };
        TextBox name = new TextBox();
        TextBlock target = Hint(string.Empty);
        Button choose = new Button { Content = "Choose…" };
        Button open = new Button { Content = "Open" };
        ToolTip.SetTip(open, "Open this folder in the file explorer");
        ProgressBar bar = new ProgressBar { Minimum = 0, Maximum = FULL_PROGRESS, IsVisible = false };
        TextBlock detail = new TextBlock { TextWrapping = TextWrapping.Wrap, IsVisible = false };
        Button clone = new Button { Content = "Clone", IsDefault = true };
        Button cancel = new Button { Content = "Cancel", IsCancel = true };

        // Name and branch follow the address until the user types their own
        string autoName = string.Empty;
        string autoBranch = string.Empty;
        CloneSource source = CloneAddress.Parse(initialAddress);
        CancellationTokenSource? running = null;

        void AddressChanged()
        {
            source = CloneAddress.Parse(address.Text ?? string.Empty);
            string newName = source.Url.Length == 0 ? string.Empty : CloneAddress.FolderName(source.Url);
            if ((name.Text ?? string.Empty) == autoName)
                name.Text = newName;
            autoName = newName;
            string newBranch = source.BranchCandidates.Count == 0 ? string.Empty : source.BranchCandidates[^1];
            if ((branch.Text ?? string.Empty) == autoBranch)
                branch.Text = newBranch;
            autoBranch = newBranch;
            TargetChanged();
        }

        string TargetPath()
        {
            string folder = (parent.Text ?? string.Empty).Trim();
            string folderName = (name.Text ?? string.Empty).Trim();
            return folder.Length == 0 || folderName.Length == 0 ? string.Empty : Path.Combine(folder, folderName);
        }

        void TargetChanged()
        {
            string path = TargetPath();
            bool taken = path.Length > 0 && HasEntries(path);
            target.Text = path.Length == 0 ? "Choose a folder and a name."
                        : taken ? path + " already exists and isn't empty."
                        : "Clones into " + path;
            SetErrorColour(target, taken);
            open.IsEnabled = Directory.Exists((parent.Text ?? string.Empty).Trim());
            clone.IsEnabled = running == null && path.Length > 0 && !taken &&
                              !string.IsNullOrWhiteSpace(address.Text);
        }

        address.TextChanged += (_, _) => AddressChanged();
        branch.TextChanged += (_, _) => TargetChanged();
        parent.TextChanged += (_, _) => TargetChanged();
        name.TextChanged += (_, _) => TargetChanged();
        choose.Click += async (_, _) =>
        {
            IStorageFolder? start = Directory.Exists(parent.Text)
                ? await dialog.StorageProvider.TryGetFolderFromPathAsync(parent.Text!) : null;
            IReadOnlyList<IStorageFolder> picked = await dialog.StorageProvider.OpenFolderPickerAsync(
                new FolderPickerOpenOptions { Title = "Choose where to clone into", SuggestedStartLocation = start });
            if (picked.Count > 0 && picked[0].TryGetLocalPath() is { } chosen)
                parent.Text = chosen;
        };
        open.Click += (_, _) => SystemShell.OpenFolder((parent.Text ?? string.Empty).Trim());
        cancel.Click += (_, _) =>
        {
            if (running != null)
                running.Cancel();
            else
                dialog.Close(null);
        };
        dialog.Closing += (_, _) => running?.Cancel();
        dialog.Opened += (_, _) => address.Focus();

        clone.Click += async (_, _) =>
        {
            string path = TargetPath();
            bool existed = Directory.Exists(path);
            running = new CancellationTokenSource();
            foreach (Control field in new Control[] { address, branch, parent, name, choose, clone })
                field.IsEnabled = false;
            bar.IsVisible = detail.IsVisible = true;
            bar.IsIndeterminate = true;
            SetErrorColour(detail, false);
            detail.Text = "Connecting to " + source.Url + "…";

            GitResult result;
            try
            {
                // A guessed branch from a page such as tree/feature/x/src is checked against the remote's branches
                string? chosenBranch = (branch.Text ?? string.Empty).Trim();
                if (chosenBranch == autoBranch && source.BranchCandidates.Count > 1)
                    chosenBranch = await GitRepository.ChooseBranchAsync(source.Url, source.BranchCandidates,
                                                                         running.Token);
                result = await GitRepository.CloneAsync(source.Url, path,
                                                        string.IsNullOrEmpty(chosenBranch) ? null : chosenBranch,
                                                        line => ShowCloneProgress(line, bar, detail), running.Token);
            }
            catch (OperationCanceledException)
            {
                result = new GitResult(-1, string.Empty, "Cancelled.");
            }

            bool cancelled = running.IsCancellationRequested;
            running = null;
            if (result.Success && !cancelled)
            {
                dialog.Close(path);
                return;
            }

            // A clone stopped halfway leaves files that can't be used; an empty folder chosen for it is kept
            RemoveFolder(path, existed);
            if (!dialog.IsVisible)
                return;

            foreach (Control field in new Control[] { address, branch, parent, name, choose })
                field.IsEnabled = true;
            bar.IsVisible = false;
            detail.Text = cancelled ? "Cancelled." : result.Message.Trim();
            SetErrorColour(detail, !cancelled);
            TargetChanged();
        };

        Grid folderRow = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto,Auto") };
        choose.Margin = new Thickness(CHOOSE_GAP, 0, 0, 0);
        open.Margin = new Thickness(CHOOSE_GAP, 0, 0, 0);
        Grid.SetColumn(choose, 1);
        Grid.SetColumn(open, 2);
        folderRow.Children.Add(parent);
        folderRow.Children.Add(choose);
        folderRow.Children.Add(open);

        StackPanel fields = new StackPanel { Spacing = BODY_SPACING };
        fields.Children.Add(Field("Address, a page of the repository, or owner/name for GitHub",
                                  address));
        fields.Children.Add(Field("Branch", branch));
        fields.Children.Add(Field("Clone into", folderRow));
        fields.Children.Add(Field("Folder name", name));
        fields.Children.Add(target);
        fields.Children.Add(bar);
        fields.Children.Add(detail);

        AddressChanged();
        dialog.Content = Layout(fields, null, clone, cancel);
        return await dialog.ShowDialog<string?>(owner);
    }

    // A label above its box
    private static Control Field(string label, Control input)
    {
        StackPanel field = new StackPanel { Spacing = FIELD_SPACING };
        field.Children.Add(new TextBlock { Text = label, TextWrapping = TextWrapping.Wrap });
        field.Children.Add(input);
        return field;
    }

    // Red for a problem, otherwise the theme's own text colour (a null brush would hide the text)
    private static void SetErrorColour(TextBlock text, bool error)
    {
        if (error)
            text.Foreground = ThemeBrushes.Get(ThemeColours.ERROR);
        else
            text.ClearValue(TextBlock.ForegroundProperty);
    }

    // Called off the interface thread with each of Git's progress lines
    private static void ShowCloneProgress(string line, ProgressBar bar, TextBlock detail)
    {
        if (GitProgress.Parse(line) is not { } progress)
            return;

        double? overall = CloneProgress.Overall(progress);
        Dispatcher.UIThread.Post(() =>
        {
            detail.Text = line.Trim();
            if (overall is not { } value)
                return;
            bar.IsIndeterminate = false;
            bar.Value = Math.Max(bar.Value, value);
        });
    }

    // A folder that can't be read counts as taken, so a clone never goes where it can't look first
    private static bool HasEntries(string path)
    {
        try
        {
            return Directory.Exists(path) && Directory.EnumerateFileSystemEntries(path).Any();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return true;
        }
    }

    // Git marks its object files read-only, which stops Directory.Delete on Windows; keepFolder empties it instead
    private static void RemoveFolder(string path, bool keepFolder)
    {
        try
        {
            if (!Directory.Exists(path))
                return;
            foreach (string file in Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories))
                File.SetAttributes(file, FileAttributes.Normal);
            if (!keepFolder)
            {
                Directory.Delete(path, true);
                return;
            }

            foreach (string folder in Directory.EnumerateDirectories(path))
                Directory.Delete(folder, true);
            foreach (string file in Directory.EnumerateFiles(path))
                File.Delete(file);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // Left for the user; the error under the bar already says the clone stopped
        }
    }
}
