using Urras.Core.Git;

namespace Urras.App;

// Git LFS from the Branch menu: files, tracked patterns, downloading and locks
public partial class MainWindow
{
    private async Task ManageLfsAsync()
    {
        if (_repository == null)
            return;

        if (!await GitRepository.IsLfsInstalledAsync())
        {
            await Dialogs.ShowTextAsync(this, "Git LFS",
                                        "Git LFS is not installed. Install it from https://git-lfs.com (Git for " +
                                            "Windows usually includes it), then try again.");
            return;
        }

        List<string> patterns = await _repository.GetLfsPatternsAsync();
        List<LfsFile> files = await _repository.GetLfsFilesAsync();
        string label = patterns.Count == 0
            ? "No file types are stored with LFS yet. Track a pattern such as *.psd to start."
            : "Tracked: " + string.Join(", ", patterns) + ". Files stored with LFS:";
        (LfsFile? file, string? action) = await Dialogs.ListActionsAsync(
            this, "Git LFS", label, files, "Download files", "Track a pattern…", "Stop tracking…", "Lock",
            "Locks…", "Set up LFS here");

        switch (action)
        {
            case "Download files":
                await RunGitAsync("Downloading LFS files…", repository => repository.LfsPullAsync(),
                                  "LFS files downloaded.");
                break;
            case "Track a pattern…":
                string? pattern = await Dialogs.PromptAsync(this, "Track with LFS",
                                                            "Files to store with LFS (for example *.psd or art/**):");
                if (pattern != null)
                    await RunGitAsync("Tracking " + pattern + "…", repository => repository.LfsTrackAsync(pattern),
                                      "Tracking " + pattern + "; commit .gitattributes to share it.");
                break;
            case "Stop tracking…" when patterns.Count > 0:
                string? untrack = await Dialogs.ChooseAsync(this, "Stop tracking", "Pattern:", patterns);
                if (untrack != null)
                    await RunGitAsync("Untracking " + untrack + "…", repository => repository.LfsUntrackAsync(untrack),
                                      "No longer tracking " + untrack + ".");
                break;
            case "Lock" when file != null:
                await RunGitAsync("Locking " + file.Path + "…", repository => repository.LfsLockAsync(file.Path),
                                  "Locked " + file.Path + ".");
                break;
            case "Locks…":
                await ManageLfsLocksAsync();
                break;
            case "Set up LFS here":
                await RunGitAsync("Setting up LFS…", repository => repository.SetUpLfsAsync(),
                                  "LFS is set up for this repository.");
                break;
            default:
                break;
        }
    }

    private async Task ManageLfsLocksAsync()
    {
        if (_repository == null)
            return;

        (List<LfsLock> locks, string? error) = await _repository.GetLfsLocksAsync();
        string label = error != null ? "Couldn't ask the server for locks: " + FirstLine(error)
                       : locks.Count == 0 ? "No files are locked." : "Locked files:";
        (LfsLock? fileLock, string? action) = await Dialogs.ListActionsAsync(this, "LFS locks", label, locks,
                                                                             "Unlock", "Force unlock…");
        if (fileLock == null || action == null)
            return;

        bool force = action == "Force unlock…";
        if (force && !await Dialogs.ConfirmAsync(this, "Force unlock",
                                                 "Remove " + fileLock.Owner + "'s lock on " + fileLock.Path +
                                                     "? They may lose work if they are still editing it.",
                                                 "Force unlock"))
            return;

        await RunGitAsync("Unlocking " + fileLock.Path + "…",
                          repository => repository.LfsUnlockAsync(fileLock.Path, force),
                          "Unlocked " + fileLock.Path + ".");
    }
}
