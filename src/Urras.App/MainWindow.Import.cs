using Avalonia.Controls;
using Urras.Core.Accounts;
using Urras.Core.Git;
using Urras.Core.Settings;

namespace Urras.App;

// Bringing many repositories in at once: from a folder, from GitHub Desktop, or cloned from a GitHub account
public partial class MainWindow
{
    private void ShowImportMenu()
    {
        ContextMenu menu = new ContextMenu();
        AddMenuItem(menu, "All repositories in a folder…", ImportFromFolderAsync);
        AddMenuItem(menu, "Repositories from GitHub Desktop", ImportFromGitHubDesktopAsync);
        AddMenuItem(menu, "Clone from your accounts…", CloneFromAccountAsync);
        menu.Open(ImportButton);
    }

    private async Task ImportFromFolderAsync()
    {
        string? folder = await PickFolderAsync("Choose a folder to search for repositories");
        if (folder == null)
            return;

        SetStatus("Looking for repositories in " + folder + "…", false);
        List<string> found = await Task.Run(() => RepositoryImport.FindUnder(folder));
        await AddFoundAsync(found, "No repositories found in " + folder + " (searched 4 folders deep).");
    }

    private async Task ImportFromGitHubDesktopAsync()
    {
        List<string> found = await Task.Run(() => RepositoryImport.FromGitHubDesktop());
        await AddFoundAsync(found, "Couldn't find GitHub Desktop's repositories. If they are in one folder (often " +
                                   "Documents\\GitHub), use All repositories in a folder instead.");
    }

    // Lets the user untick some, then adds the rest; ones already in the list are left unticked
    private async Task AddFoundAsync(List<string> found, string noneMessage)
    {
        if (found.Count == 0)
        {
            SetStatus(noneMessage, true);
            return;
        }

        bool Known(string path) => _settings.Repositories.Contains(path, RepositoryLayout.PathComparer);
        List<string>? chosen = await Dialogs.ChooseManyAsync(
            this, "Add repositories", $"Found {found.Count} repositories. Ones already in Urras are unticked.",
            found, "Add", path => !Known(path));
        if (chosen == null)
            return;

        int added = chosen.Count(path => _settings.AddRepository(path));
        _settings.Save();
        BuildRepositoryTree();
        SetStatus(added == 1 ? "Added 1 repository." : $"Added {added} repositories.", false);
    }

    private async Task CloneFromAccountAsync()
    {
        List<Account> accounts = _accounts?.Accounts.ToList() ?? new List<Account>();
        Account? account = accounts.FirstOrDefault();
        if (accounts.Count > 1)
        {
            string? which = await Dialogs.ChooseAsync(this, "Clone", "Clone from which account?",
                                                      accounts.Select(item => item.ToString()));
            account = accounts.FirstOrDefault(item => item.ToString() == which);
        }

        PlatformClient? client = account == null ? null : await _accounts!.ClientAsync(account);
        if (client == null)
        {
            SetStatus("Add an account first (Accounts…), then clone from it.", true);
            return;
        }

        List<PlatformRepository> repositories;
        try
        {
            SetStatus("Asking " + client.PlatformName + " for your repositories…", false);
            repositories = await client.GetRepositoriesAsync();
        }
        catch (PlatformException exception)
        {
            SetStatus("Couldn't list your repositories: " + exception.Message, true);
            return;
        }

        // Ones already cloned somewhere in the list start unticked
        HashSet<string> cloned = _settings.Repositories.Select(RemoteIdentity.Read)
                                          .Where(remote => remote != null)
                                          .Select(remote => (remote!.Owner + "/" + remote.Name).ToLowerInvariant())
                                          .ToHashSet();
        List<PlatformRepository>? chosen = await Dialogs.ChooseManyAsync(
            this, "Clone from " + client.PlatformName,
            $"{repositories.Count} repositories on your account. Ones you already have are unticked.",
            repositories, "Choose folder…",
            repository => !cloned.Contains(repository.FullName.ToLowerInvariant()));
        if (chosen == null || chosen.Count == 0)
            return;

        string? parent = await PickFolderAsync("Choose where to clone them");
        if (parent == null)
            return;

        // Each goes into parent/Owner/Name, so forks of the same project don't collide
        List<string> failed = new List<string>();
        int done = 0;
        foreach (PlatformRepository repository in chosen)
        {
            string target = Path.Combine(parent, repository.FullName.Split('/')[0], repository.Name);
            SetBusy(true, $"Cloning {repository.FullName} ({++done} of {chosen.Count})…");
            if (Directory.Exists(target) && Directory.EnumerateFileSystemEntries(target).Any())
            {
                if (RepositoryImport.IsRepository(target))
                    _settings.AddRepository(target);
                else
                    failed.Add(repository.FullName + " (folder not empty)");
                continue;
            }

            GitResult result = await GitRepository.CloneAsync(repository.CloneUrl, target);
            if (result.Success)
                _settings.AddRepository(target);
            else
                failed.Add(repository.FullName);
        }

        SetBusy(false, null);
        _settings.Save();
        BuildRepositoryTree();
        SetStatus(failed.Count == 0 ? $"Cloned {chosen.Count} repositories into {parent}."
                                    : "Couldn't clone " + string.Join(", ", failed) + ".", failed.Count > 0);
    }
}
