using Urras.Core.Git;
using Urras.Core.Settings;

namespace Urras.App;

// "Can't find this repository": locate it, clone it again into the same folder, or take it off the list
public partial class MainWindow
{
    private string? _missingPath;

    private void InitializeMissingPanel()
    {
        LocateButton.Click += async (_, _) => await LocateMissingAsync();
        CloneAgainButton.Click += async (_, _) => await CloneMissingAgainAsync();
        RemoveMissingButton.Click += async (_, _) => await RemoveMissingAsync();
    }

    // Shows the panel in place of the tabs when the folder is gone; true when it did
    private bool ShowIfMissing(string? path)
    {
        bool missing = path != null && !Directory.Exists(path);
        _missingPath = missing ? path : null;
        MissingPanel.IsVisible = missing;
        MainTabs.IsVisible = !missing;
        if (!missing)
            return false;

        string name = Path.GetFileName(path!.TrimEnd('/', '\\'));
        string? address = CloneAddressOf(path);
        MissingTitle.Text = "Can't find \"" + name + "\"";
        MissingText.Text = "It was last seen at " + path + ". It may have been moved, renamed or deleted. " +
                           (address != null ? "Locate it where it is now, or clone it again from " + address + "."
                                            : "Locate it where it is now, or remove it from the list.");
        CloneAgainButton.IsVisible = address != null;
        ShowEmptyState();
        RepositoryTitle.Text = name + " (missing)";
        ShowWindowTitle(name);
        return true;
    }

    private string? CloneAddressOf(string path)
    {
        string key = RepositoryLayout.Normalize(path);
        return _settings.CloneAddresses.TryGetValue(key, out string? address) ? address : null;
    }

    private async Task LocateMissingAsync()
    {
        if (_missingPath is not { } missing || await PickFolderAsync("Where is " + Path.GetFileName(missing) + " now?")
                is not { } folder)
            return;

        string? root = await GitRepository.FindRootAsync(folder);
        if (root == null)
        {
            SetStatus(folder + " is not a Git repository.", true);
            return;
        }

        _settings.MoveRepository(missing, root);
        _settings.Save();
        BuildRepositoryTree();
        await OpenRepositoryAsync(root);
    }

    private async Task CloneMissingAgainAsync()
    {
        if (_missingPath is not { } missing || CloneAddressOf(missing) is not { } address)
            return;

        string busy = "Cloning " + address + "…";
        SetBusy(true, busy);
        GitResult result = await GitRepository.CloneAsync(address, missing, null, CloneStatus(busy));
        SetBusy(false, null);
        if (ReportResult(result, "Cloned into " + missing + "."))
            await OpenRepositoryAsync(missing);
    }

    private async Task RemoveMissingAsync()
    {
        if (_missingPath is not { } missing)
            return;

        _settings.RemoveRepository(missing);
        _settings.Save();
        BuildRepositoryTree();
        await OpenRepositoryAsync(null);
    }
}
