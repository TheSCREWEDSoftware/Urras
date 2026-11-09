using Urras.Core.Accounts;
using Urras.Core.Git;
using Urras.Core.Settings;

namespace Urras.App;

// Platform features for the open repository: pull requests, pictures, and notifications across accounts
public partial class MainWindow
{
    private const string PULL_REQUEST_BRANCH_PREFIX = "pr/";

    private async Task LearnPicturesAsync(List<CommitInfo> history)
    {
        if (_accounts == null || _repository == null || _accounts.Accounts.Count == 0)
            return;

        try
        {
            await _accounts.LearnPicturesAsync(_repository.Path, history);
        }
        catch (PlatformException)
        {
            // Pictures are a nicety; offline or rate-limited, initials stay
        }
    }

    // The account and owner/name pull requests go to (a fork's parent, unless chosen otherwise), or a message
    private async Task<(PlatformClient? Client, RemoteIdentity? Remote)> PlatformForRepositoryAsync()
    {
        RemoteIdentity? remote = _repository == null ? null : OnlineTargets(_repository.Path).Target;
        if (remote == null)
        {
            SetStatus("This repository has no origin remote on a hosting platform.", true);
            return (null, null);
        }

        PlatformClient? client = _accounts == null ? null : await _accounts.ClientForHostAsync(remote.Host);
        if (client == null)
            SetStatus("Sign in to " + remote.Host + " in Accounts to see its pull requests.", true);
        return (client, remote);
    }

    private async Task ShowPullRequestsAsync()
    {
        (PlatformClient? client, RemoteIdentity? remote) = await PlatformForRepositoryAsync();
        if (client == null || remote == null)
            return;

        List<PlatformPullRequest> pulls;
        try
        {
            pulls = await PlatformClient.SafelyAsync(() => client.GetPullRequestsAsync(remote.Owner, remote.Name));
        }
        catch (PlatformException exception)
        {
            SetStatus("Couldn't list pull requests: " + exception.Message, true);
            return;
        }

        string label = pulls.Count == 0 ? "No open pull requests on " + remote.Owner + "/" + remote.Name + "."
                                        : "Open pull requests on " + remote.Owner + "/" + remote.Name + ":";
        (PlatformPullRequest? pull, string? action) = await Dialogs.ListActionsAsync(
            this, "Pull requests", label, pulls, "Check out", "Open in browser", "Copy link", "Create pull request…");

        switch (action)
        {
            case "Check out" when pull != null:
                await CheckOutPullRequestAsync(client, remote, pull);
                break;
            case "Open in browser" when pull != null:
                await Report(SystemShell.OpenUrl(pull.Url));
                break;
            case "Copy link" when pull != null:
                await CopyAsync(pull.Url);
                break;
            case "Create pull request…":
                await CreatePullRequestAsync(client, remote);
                break;
            default:
                break;
        }
    }

    private async Task CheckOutPullRequestAsync(PlatformClient client, RemoteIdentity remote, PlatformPullRequest pull)
    {
        string? reference = client.PullRequestRef(pull.Number);
        // A fork's parent isn't a remote here, so its pull requests are fetched from its address
        RemoteIdentity? origin = _repository == null ? null : RemoteIdentity.Read(_repository.Path);
        string source = origin?.Key == remote.Key || remote.WebUrl == null ? RemoteIdentity.ORIGIN
                                                                           : remote.WebUrl + ".git";
        if (reference == null)
        {
            await RunGitAsync("Checking out " + pull.SourceBranch + "…",
                              repository => repository.SwitchToRemoteAsync("origin/" + pull.SourceBranch),
                              "Checked out " + pull.SourceBranch + ".");
            return;
        }

        string branch = PULL_REQUEST_BRANCH_PREFIX + pull.Number;
        await RunGitAsync("Checking out pull request #" + pull.Number + "…",
                          repository => repository.CheckOutPullRequestAsync(reference, branch, source),
                          "Checked out pull request #" + pull.Number + " as " + branch + ".");
    }

    // The platform's own page creates it, from the current branch into the main one
    private async Task CreatePullRequestAsync(PlatformClient client, RemoteIdentity remote)
    {
        if (_repository == null || _status?.Branch is not { } branch)
        {
            SetStatus("Switch to the branch you want to propose first.", true);
            return;
        }

        string target = await _repository.GetDefaultBranchAsync();
        RemoteIdentity? origin = RemoteIdentity.Read(_repository.Path);
        // Into the parent's own main branch, which may not be named like the fork's
        string parentBranch = _forkAccess.TryGetValue(_repository.Path, out ForkAccess? access) &&
                              access.ParentBranch is { } known ? known : target;
        string? fromFork = origin == null || origin.Key == remote.Key
            ? null
            : client.NewForkPullRequestUrl(remote.Owner, remote.Name, origin.Owner, branch, parentBranch);
        await Report(SystemShell.OpenUrl(fromFork ?? client.NewPullRequestUrl(origin?.Owner ?? remote.Owner,
                                                                              origin?.Name ?? remote.Name, branch,
                                                                              target)));
    }

    private async Task ShowNotificationsAsync()
    {
        if (_accounts == null || _accounts.Accounts.Count == 0)
        {
            SetStatus("Sign in to an account (Accounts…) to see notifications.", true);
            return;
        }

        List<(PlatformClient Client, PlatformNotification Notification)> all = await LoadNotificationsAsync();
        List<PlatformNotification> notifications = all.Select(entry => entry.Notification).ToList();
        (PlatformNotification? chosen, string? action) = await Dialogs.ListActionsAsync(
            this, "Notifications", notifications.Count == 0 ? "Nothing new." : "Unread notifications:",
            notifications, "Open in browser", "Mark as read");
        if (chosen == null)
            return;

        PlatformClient client = all.First(entry => entry.Notification == chosen).Client;
        if (action == "Open in browser" && chosen.Url != null)
            await Report(SystemShell.OpenUrl(chosen.Url));

        try
        {
            await PlatformClient.SafelyAsync(async () =>
            {
                await client.MarkNotificationReadAsync(chosen);
                return true;
            });
        }
        catch (PlatformException exception)
        {
            SetStatus("Couldn't mark it as read: " + exception.Message, true);
        }

        await UpdateNotificationCountAsync();
    }

    private async Task<List<(PlatformClient, PlatformNotification)>> LoadNotificationsAsync()
    {
        List<(PlatformClient, PlatformNotification)> all = new List<(PlatformClient, PlatformNotification)>();
        foreach (Account account in _accounts?.Accounts.ToList() ?? new List<Account>())
        {
            try
            {
                if (await _accounts!.ClientAsync(account) is { } client)
                    all.AddRange((await PlatformClient.SafelyAsync(client.GetNotificationsAsync))
                                 .Select(item => (client, item)));
            }
            catch (PlatformException)
            {
                // One account being offline doesn't hide the others
            }
        }

        return all;
    }

    // Runs with the background fetch; the button shows how many are unread
    private async Task UpdateNotificationCountAsync()
    {
        NotificationsButton.IsVisible = _accounts != null && _accounts.Accounts.Count > 0;
        if (!NotificationsButton.IsVisible)
            return;

        int count = (await LoadNotificationsAsync()).Count;
        NotificationsButton.Content = count == 0 ? "Notifications" : "Notifications (" + count + ")";
    }
}
