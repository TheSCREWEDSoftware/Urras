using Urras.Core.Git;

namespace Urras.App;

// Submodules and worktrees, from the Branch menu
public partial class MainWindow
{
    private async Task ManageSubmodulesAsync()
    {
        if (_repository == null)
            return;

        List<SubmoduleInfo> modules = await _repository.GetSubmodulesAsync();
        string label = modules.Count == 0 ? "This repository has no submodules." : "Submodules:";
        (SubmoduleInfo? module, string? action) = await Dialogs.ListActionsAsync(
            this, "Submodules", label, modules, "Open in Urras", "Open folder", "Update all", "Add submodule…");
        string root = _repository.Path;

        switch (action)
        {
            case "Open in Urras" when module != null:
                await OpenSubmoduleAsync(Path.Combine(root, module.Path), module);
                break;
            case "Open folder" when module != null:
                await Report(SystemShell.OpenFolder(Path.Combine(root, module.Path)));
                break;
            case "Update all":
                await RunGitAsync("Updating submodules…", repository => repository.UpdateSubmodulesAsync(),
                                  "Submodules checked out at their recorded commits.");
                break;
            case "Add submodule…":
                await AddSubmoduleAsync();
                break;
            default:
                break;
        }
    }

    private async Task OpenSubmoduleAsync(string folder, SubmoduleInfo module)
    {
        if (module.State == SubmoduleState.NotInitialized)
        {
            SetStatus(module.Path + " is not checked out yet; use Update all first.", true);
            return;
        }

        await AddAndOpenRepositoryAsync(folder);
    }

    private async Task AddSubmoduleAsync()
    {
        string? url = await Dialogs.PromptAsync(this, "Add submodule", "Repository URL:");
        if (url == null)
            return;

        string name = url.TrimEnd('/');
        name = name[(name.LastIndexOfAny(new[] { '/', '\\', ':' }) + 1)..];
        if (name.EndsWith(".git", StringComparison.OrdinalIgnoreCase))
            name = name[..^".git".Length];

        string? path = await Dialogs.PromptAsync(this, "Add submodule",
                                                 "Folder inside this repository (for example libs/" + name + "):",
                                                 "libs/" + name);
        if (path == null)
            return;

        await RunGitAsync("Adding submodule " + path + "…", repository => repository.AddSubmoduleAsync(url, path),
                          "Added " + path + "; commit it to record the submodule.");
    }

    private async Task ManageWorktreesAsync()
    {
        if (_repository == null)
            return;

        List<WorktreeInfo> worktrees = await _repository.GetWorktreesAsync();
        (WorktreeInfo? worktree, string? action) = await Dialogs.ListActionsAsync(
            this, "Worktrees", "Each worktree is another folder with its own branch checked out, sharing this " +
                                   "repository's history:",
            worktrees, "Open in Urras", "New worktree…", "Remove…", "Forget missing ones");

        switch (action)
        {
            case "Open in Urras" when worktree != null && !worktree.IsPrunable:
                await AddAndOpenRepositoryAsync(worktree.Path);
                break;
            case "New worktree…":
                await AddWorktreeAsync();
                break;
            case "Remove…" when worktree != null:
                await RemoveWorktreeAsync(worktree);
                break;
            case "Forget missing ones":
                await RunGitAsync("Pruning worktrees…", repository => repository.PruneWorktreesAsync(),
                                  "Forgot worktrees whose folders are gone.");
                break;
            default:
                break;
        }
    }

    private async Task AddWorktreeAsync()
    {
        if (_repository == null)
            return;

        List<string> branches = _branches.Where(branch => !branch.IsRemote && !branch.IsCurrent)
                                         .Select(branch => branch.Name).ToList();
        const string newBranch = "A new branch from the current commit…";
        string? choice = await Dialogs.ChooseAsync(this, "New worktree", "Branch to check out in the new folder:",
                                                   branches.Prepend(newBranch));
        if (choice == null)
            return;

        bool create = choice == newBranch;
        string? branch = create ? await Dialogs.PromptAsync(this, "New worktree", "New branch name:") : choice;
        if (branch == null)
            return;

        string? parent = await PickFolderAsync("Choose where to put the worktree");
        if (parent == null)
            return;

        string folder = Path.Combine(parent, Path.GetFileName(_repository.Path) + "-" + branch.Replace('/', '-'));
        if (await RunGitAsync("Creating worktree…", repository => repository.AddWorktreeAsync(folder, branch, create),
                              "Created " + folder + "."))
            await AddAndOpenRepositoryAsync(folder);
    }

    private async Task RemoveWorktreeAsync(WorktreeInfo worktree)
    {
        if (worktree.IsMain)
        {
            SetStatus("The main worktree can't be removed.", true);
            return;
        }

        if (!await Dialogs.ConfirmAsync(this, "Remove worktree",
                                        "Remove " + worktree.Path + "? Its folder is deleted; the branch is kept. " +
                                            "Uncommitted changes there are lost.", "Remove"))
            return;

        await RunGitAsync("Removing worktree…", repository => repository.RemoveWorktreeAsync(worktree.Path, true),
                          "Removed " + worktree.Path + ".");
    }
}
