using Urras.Core.Accounts;
using Urras.Core.Git;

namespace Urras.App;

// Publish repository: make it on GitHub (or GitLab, Gitea), add it as origin and push, like GitHub Desktop
public partial class MainWindow
{
    private async Task PublishRepositoryAsync()
    {
        if (_repository == null || _accounts == null || _busy)
            return;

        List<(Account Account, PlatformClient Client)> choices = new List<(Account Account, PlatformClient Client)>();
        foreach (Account account in _accounts.Accounts)
        {
            try
            {
                if (await _accounts.ClientAsync(account) is { CanCreateRepositories: true } client)
                    choices.Add((account, client));
            }
            catch (PlatformException)
            {
                // An account whose token can't be renewed is left out; Options shows it needs signing in again
            }
        }

        if (choices.Count == 0)
        {
            SetStatus("Sign in to GitHub, GitLab or Gitea to publish this repository.", true);
            await OpenOptionsAsync(OptionsWindow.ACCOUNTS);
            return;
        }

        PublishRepositoryWindow window = new PublishRepositoryWindow(choices, _repository.Name, SignInAgainAsync);
        if (await window.ShowDialog<PlatformRepository?>(this) is not { } online)
            return;

        // A brand-new repository has nothing to push yet; it's connected and the first commit can be published
        bool hasCommits = await _repository.HasCommitsAsync();
        StartSyncAnimation(SyncKind.Publish);
        try
        {
            await RunGitAsync("Publishing " + online.FullName + "…",
                              repository => RepositoryPublisher.ConnectAsync(repository, online, ReportSyncProgress),
                              hasCommits
                                  ? "Published " + online.FullName + "."
                                  : "Made " + online.FullName + "; commit something, then publish the branch.");
        }
        finally
        {
            StopSyncAnimation();
        }
    }
}
