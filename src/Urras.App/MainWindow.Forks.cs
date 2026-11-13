using Avalonia.Controls;
using Urras.Core.Accounts;
using Urras.Core.Git;
using Urras.Core.Settings;

namespace Urras.App;

// Forks: which repository pull requests and "View on…" go to, and what the signed-in user can push to
public partial class MainWindow
{
    // What the platform said: who is signed in, and push access to the fork (origin) and its parent
    private sealed record ForkAccess(string? Username, bool? CanPushOrigin, bool? CanPushParent, bool Checked,
                                     string? ParentBranch = null);

    private readonly Dictionary<string, ForkSettings> _forkSettings =
        new Dictionary<string, ForkSettings>(RepositoryLayout.PathComparer);
    private readonly Dictionary<string, ForkAccess> _forkAccess =
        new Dictionary<string, ForkAccess>(RepositoryLayout.PathComparer);
    private readonly HashSet<string> _forkAsked = new HashSet<string>(RepositoryLayout.PathComparer);

    private void InitializeForks()
    {
        ViewOnlineButton.Click += async (_, _) => await ViewOnlineAsync(_repository?.Path, false);
    }

    // Reads the saved choice, asks the platform about forks and access, and asks how a new fork is used
    private async Task UpdateForkSafelyAsync(string path, bool ask, bool force = false)
    {
        try
        {
            await UpdateForkAsync(path, ask, force);
        }
        catch (PlatformException exception)
        {
            SetStatus("Couldn't check the repository on its platform: " + exception.Message, true);
        }
    }

    // The platform is asked once per session per repository, unless forced (Repository settings, after signing in)
    private async Task UpdateForkAsync(string path, bool ask, bool force)
    {
        GitRepository repository = new GitRepository(path);
        ForkSettings fork = WithDefault(await repository.GetForkSettingsAsync());
        _forkSettings[path] = fork;
        UpdateViewOnlineButton();
        if (!force && _forkAccess.TryGetValue(path, out ForkAccess? known) && known.Username != null)
            return;

        RemoteIdentity? origin = RemoteIdentity.Read(path);
        if (origin == null)
            return;

        Account? account = AccountFor(origin.Host);
        PlatformClient? client = _accounts == null ? null : await _accounts.ClientForHostAsync(origin.Host);
        if (account == null || client == null)
        {
            _forkAccess[path] = new ForkAccess(null, null, null, false);
            return;
        }

        PlatformAccess? access = await AccessAsync(client, origin);
        if (access is { IsFork: true })
        {
            RemoteIdentity parent = new RemoteIdentity(access.ParentOwner!, access.ParentName!, origin.Host);
            if (fork.Parent?.Key != parent.Key)
            {
                await repository.SaveForkSettingsAsync(parent, fork.Target);
                fork = fork with { Parent = parent, Confirmed = true };
                _forkSettings[path] = fork;
            }
        }

        PlatformAccess? parentAccess = fork.Parent == null ? null : await AccessAsync(client, fork.Parent);
        _forkAccess[path] = new ForkAccess(account.Username, access?.CanPush, parentAccess?.CanPush, access != null,
                                           parentAccess?.DefaultBranch);
        if (_repository == null || !RepositoryLayout.PathComparer.Equals(_repository.Path, path))
            return;

        UpdateViewOnlineButton();
        if (!ask || access is not { IsFork: true } || fork.Target != ForkTarget.Unset || !_forkAsked.Add(path))
            return;

        Dialogs.ForkAnswer? answer = await Dialogs.ForkTargetAsync(this, ForkChoiceFor(path, origin, fork));
        if (answer == null)
            return;

        if (answer.ForAllForks)
        {
            // Every fork without a choice of its own follows this, so no other fork asks
            _settings.DefaultForkTarget = answer.Target;
            _settings.Save();
        }

        await SaveForkTargetAsync(path, answer.Target);
    }

    // A fork without a choice of its own follows the one made for all forks, if any
    private ForkSettings WithDefault(ForkSettings fork)
    {
        return fork.Target == ForkTarget.Unset && _settings.DefaultForkTarget != ForkTarget.Unset
            ? fork with { Target = _settings.DefaultForkTarget }
            : fork;
    }

    private static async Task<PlatformAccess?> AccessAsync(PlatformClient client, RemoteIdentity remote)
    {
        try
        {
            return await PlatformClient.SafelyAsync(() => client.GetAccessAsync(remote.Owner, remote.Name));
        }
        catch (PlatformException)
        {
            // Offline, private or deleted; the choice still works, only the access isn't shown
            return null;
        }
    }

    private async Task SaveForkTargetAsync(string path, ForkTarget target)
    {
        ForkSettings fork = _forkSettings.TryGetValue(path, out ForkSettings? known)
            ? known
            : await new GitRepository(path).GetForkSettingsAsync();
        await new GitRepository(path).SaveForkSettingsAsync(fork.Parent, target);
        fork = fork with { Target = target };
        _forkSettings[path] = fork;
        UpdateViewOnlineButton();

        RemoteIdentity? chosen = fork.ContributionTarget(RemoteIdentity.Read(path));
        if (chosen != null)
            SetStatus("Pull requests and \"View on…\" now go to " + FullName(chosen) + ".", false);
    }

    private Dialogs.ForkChoice ForkChoiceFor(string path, RemoteIdentity origin, ForkSettings fork)
    {
        return new Dialogs.ForkChoice(fork.Parent == null ? null : FullName(fork.Parent), FullName(origin),
                                      fork.Target, AccessText(path, origin, fork), fork.Confirmed);
    }

    // Who is signed in and what they can push to, in plain words
    private string AccessText(string path, RemoteIdentity origin, ForkSettings fork)
    {
        string platform = PlatformNameFor(origin.Host);
        _forkAccess.TryGetValue(path, out ForkAccess? access);
        if (access?.Username == null)
            return "You aren't signed in to " + platform + " (" + origin.Host + "), so Urras can't tell whether " +
                   "this is a fork or what you can push to. Sign in under Accounts.";

        List<string> lines = new List<string> { "Signed in to " + platform + " as @" + access.Username + "." };
        if (!access.Checked)
        {
            lines.Add("Urras couldn't reach " + FullName(origin) + " on " + platform + " to check access.");
            return string.Join("\n", lines);
        }

        lines.Add(PushText(access.CanPushOrigin, origin));
        if (fork.Parent != null)
            lines.Add(PushText(access.CanPushParent, fork.Parent) +
                      (access.CanPushParent == false ? " Your changes reach it through pull requests." : string.Empty));
        else
            lines.Add("This repository isn't a fork.");
        return string.Join("\n", lines);
    }

    private static string PushText(bool? canPush, RemoteIdentity remote)
    {
        return canPush switch
        {
            true => "You can push to " + FullName(remote) + ".",
            false => "You can't push to " + FullName(remote) + ".",
            _ => "Urras couldn't check whether you can push to " + FullName(remote) + "."
        };
    }

    private static string FullName(RemoteIdentity remote)
    {
        return remote.Owner + "/" + remote.Name;
    }

    private Account? AccountFor(string host)
    {
        return _accounts?.Accounts.FirstOrDefault(account => string.Equals(account.WebHost, host,
                                                                           StringComparison.OrdinalIgnoreCase));
    }

    // GitHub, GitLab and so on, from a signed-in account or the well-known hosts; otherwise the host itself
    private string PlatformNameFor(string host)
    {
        if (AccountFor(host) is { } account)
            return Account.PlatformName(account.Kind);

        return host.ToLowerInvariant() switch
        {
            "github.com" => "GitHub",
            "gitlab.com" => "GitLab",
            "bitbucket.org" => "Bitbucket",
            "codeberg.org" => "Codeberg",
            _ => host
        };
    }

    // The repository the open one contributes to, and the other side of a fork
    private (RemoteIdentity? Target, RemoteIdentity? Other) OnlineTargets(string path)
    {
        Dictionary<string, RemoteIdentity> remotes = RemoteIdentity.ReadAll(path);
        remotes.TryGetValue(RemoteIdentity.ORIGIN, out RemoteIdentity? origin);
        ForkSettings fork = _forkSettings.TryGetValue(path, out ForkSettings? known)
            ? known
            : new ForkSettings(ForkSettings.ParentFrom(null, origin, remotes), ForkTarget.Unset, false);
        RemoteIdentity? target = fork.ContributionTarget(origin);
        RemoteIdentity? other = fork.Parent == null ? null : target?.Key == origin?.Key ? fork.Parent : origin;
        return (target, other);
    }

    private void UpdateViewOnlineButton()
    {
        (RemoteIdentity? target, RemoteIdentity? other) = _repository == null
            ? (null, null)
            : OnlineTargets(_repository.Path);
        ViewOnlineButton.IsVisible = target?.WebUrl != null;
        if (target == null || _repository == null)
            return;

        string platform = PlatformNameFor(target.Host);
        if (other == null)
        {
            ViewOnlineButton.Content = "View on " + platform;
            ToolTip.SetTip(ViewOnlineButton, "Opens " + FullName(target) + " in the browser.");
            return;
        }

        // A fork names which side it opens, and the other one, so it's clear where the button goes
        RemoteIdentity.ReadAll(_repository.Path).TryGetValue(RemoteIdentity.ORIGIN, out RemoteIdentity? origin);
        bool fork = target.Key == origin?.Key;
        ViewOnlineButton.Content = (fork ? "View fork on " : "View original on ") + platform;
        ToolTip.SetTip(ViewOnlineButton, fork
            ? "Opens your fork, " + FullName(target) + ". The original is " + FullName(other) + "; Repository " +
              "settings can make this button open it instead."
            : "Opens the original, " + FullName(target) + ". Your fork is " + FullName(other) + "; Repository " +
              "settings can make this button open it instead.");
    }

    private async Task ViewOnlineAsync(string? path, bool other)
    {
        if (path == null)
            return;

        (RemoteIdentity? target, RemoteIdentity? alternative) = OnlineTargets(path);
        RemoteIdentity? page = other ? alternative : target;
        if (page?.WebUrl is not { } url)
        {
            SetStatus("This repository has no remote on a website.", true);
            return;
        }

        await Report(SystemShell.OpenUrl(url));
    }

    // "View on GitHub" for a file at a branch or commit, on the repository's origin
    private void AddViewFileOnlineItem(ContextMenu menu, string path, string revision)
    {
        RemoteIdentity? origin = _repository == null ? null : RemoteIdentity.Read(_repository.Path);
        if (origin?.WebUrl is not { } site)
            return;

        string platform = PlatformNameFor(origin.Host);
        string url = site + FilePagePath(platform, revision) + Uri.EscapeDataString(revision) + "/" +
                     string.Join("/", path.Replace('\\', '/').Split('/').Select(Uri.EscapeDataString));
        AddMenuItem(menu, "View on " + platform, () => Report(SystemShell.OpenUrl(url)));
    }

    // A commit's page on the repository's website, or null without one
    private string? CommitUrl(string hash)
    {
        RemoteIdentity? origin = _repository == null ? null : RemoteIdentity.Read(_repository.Path);
        if (origin?.WebUrl is not { } site)
            return null;

        string path = PlatformNameFor(origin.Host) switch
        {
            "GitLab" => "/-/commit/",
            "Bitbucket" or "Bitbucket Server" => "/commits/",
            _ => "/commit/"
        };
        return site + path + hash;
    }

    // The author's profile when a GitHub private address says who they are, otherwise the commit (which links them)
    private string? AuthorUrl(CommitInfo commit)
    {
        return PersonUrl(commit.People[0], commit);
    }

    // Their GitHub profile when the address gives it away, otherwise the commit
    private string? PersonUrl(CommitPerson person, CommitInfo commit)
    {
        if (GitHubClient.FromNoreplyEmail(person.Email) is { } user)
            return GitHubClient.ProfileUrl(user.Login);
        if (GitHubClient.KnownLogin(person.Email) is { } known)
            return GitHubClient.ProfileUrl(known);
        return CommitUrl(commit.Hash);
    }

    // Where each platform shows a file at a revision; Gitea tells commits and branches apart in the address
    private static string FilePagePath(string platform, string revision)
    {
        bool commit = revision.Length >= Hashes.SHORT_LENGTH && revision.All(Uri.IsHexDigit);
        return platform switch
        {
            "GitLab" => "/-/blob/",
            "Bitbucket" or "Bitbucket Server" => "/src/",
            "Gitea" or "Codeberg" => commit ? "/src/commit/" : "/src/branch/",
            _ => "/blob/"
        };
    }

    // "View owner/name on GitHub", and the other side of a fork when there is one
    private void AddViewOnlineItems(ContextMenu menu, string path)
    {
        (RemoteIdentity? target, RemoteIdentity? other) = OnlineTargets(path);
        if (target?.WebUrl == null)
            return;

        string platform = PlatformNameFor(target.Host);
        AddMenuItem(menu, "View " + FullName(target) + " on " + platform, () => ViewOnlineAsync(path, false));
        if (other?.WebUrl != null)
            AddMenuItem(menu, "View " + FullName(other) + " on " + platform, () => ViewOnlineAsync(path, true));
    }
}
