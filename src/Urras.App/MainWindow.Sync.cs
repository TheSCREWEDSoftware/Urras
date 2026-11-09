using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;
using Urras.Core.Git;

namespace Urras.App;

// The sync button, like GitHub Desktop's: publish, pull, push or fetch, with the other actions in the menu beside it
public partial class MainWindow
{
    private const int SYNC_DETAIL_MINUTES = 1;
    private const int SPIN_FRAME_MS = 16;
    private const double SPIN_DEGREES_PER_FRAME = 6;
    private const double FULL_TURN = 360;

    // 16 by 16 outlines: an arrow up, an arrow down, and two arrows going round for fetch
    private static readonly Geometry UpArrow = Geometry.Parse("M8,13.5 L8,2.5 M3.5,7 L8,2.5 L12.5,7");
    private static readonly Geometry DownArrow = Geometry.Parse("M8,2.5 L8,13.5 M3.5,9 L8,13.5 L12.5,9");
    private static readonly Geometry FetchArrows = Geometry.Parse(
        "M13.5,8 A5.5,5.5 0 0 1 3.7,11.4 M2.5,8 A5.5,5.5 0 0 1 12.3,4.6 " +
        "M12.5,1.8 L12.5,4.8 L9.5,4.8 M3.5,14.2 L3.5,11.2 L6.5,11.2");

    // Keeps "Last fetched 5 minutes ago" current between refreshes
    private readonly DispatcherTimer _syncDetailTimer = new DispatcherTimer
    {
        Interval = TimeSpan.FromMinutes(SYNC_DETAIL_MINUTES)
    };

    // Turns the icon while Git works
    private readonly DispatcherTimer _spinTimer = new DispatcherTimer
    {
        Interval = TimeSpan.FromMilliseconds(SPIN_FRAME_MS)
    };

    private readonly RotateTransform _spin = new RotateTransform();
    private bool _syncing;
    private SyncState? _sync;
    private DateTimeOffset? _lastFetched;

    private void InitializeSync()
    {
        SyncButton.Click += async (_, _) => await RunSyncAsync(_sync?.Kind ?? SyncKind.Fetch);
        SyncMenuButton.Click += (_, _) => ShowSyncMenu();
        _syncDetailTimer.Tick += (_, _) => ShowSync(_status, _lastFetched);
        _syncDetailTimer.Start();
        SyncIcon.RenderTransform = _spin;
        _spinTimer.Tick += (_, _) => _spin.Angle = (_spin.Angle + SPIN_DEGREES_PER_FRAME) % FULL_TURN;
        ShowSync(null, null);
    }

    private async Task RunSyncAsync(SyncKind kind)
    {
        if (_repository == null || _busy)
            return;

        StartSyncAnimation(kind);
        try
        {
            await RunSyncStepAsync(kind);
        }
        finally
        {
            StopSyncAnimation();
        }
    }

    // "Pushing to origin…" with the icon turning and the bar running, until Git is done
    private void StartSyncAnimation(SyncKind kind)
    {
        string remote = SyncState.Remote(_status?.Upstream) ?? "origin";
        _syncing = true;
        SyncTitle.Text = kind switch
        {
            SyncKind.Pull => "Pulling " + remote + "…",
            SyncKind.Push => "Pushing to " + remote + "…",
            SyncKind.Publish => "Publishing to " + remote + "…",
            _ => "Fetching " + remote + "…"
        };
        SyncDetail.Text = "Working, this can take a moment";
        // Like GitHub Desktop, every action shows the arrows going round while it runs
        SyncIcon.Data = FetchArrows;
        SyncProgress.IsIndeterminate = true;
        SyncProgress.IsVisible = true;
        _spinTimer.Start();
    }

    private void StopSyncAnimation()
    {
        _syncing = false;
        _spinTimer.Stop();
        _spin.Angle = 0;
        SyncProgress.IsVisible = false;
        ShowSync(_status, _lastFetched);
    }

    private async Task RunSyncStepAsync(SyncKind kind)
    {
        switch (kind)
        {
            case SyncKind.Pull:
            {
                await RunGitAsync("Pulling…", repository => repository.PullAsync(ReportSyncProgress), "Pulled.");
                break;
            }
            case SyncKind.Push:
            case SyncKind.Publish:
            {
                await RunGitAsync(kind == SyncKind.Publish ? "Publishing…" : "Pushing…",
                                  repository => repository.PushAsync(ReportSyncProgress),
                                  kind == SyncKind.Publish ? "Published." : "Pushed.");
                break;
            }
            default:
            {
                await RunGitAsync("Fetching…", repository => repository.FetchAsync(false, ReportSyncProgress),
                                  "Fetched.");
                break;
            }
        }
    }

    // Called by Git's output reader, off the interface thread: "Writing objects 45%" and the bar filling up
    private void ReportSyncProgress(string line)
    {
        if (GitProgress.Parse(line) is not { } progress)
            return;

        Dispatcher.UIThread.Post(() =>
        {
            if (!_syncing)
                return;
            SyncDetail.Text = progress.ToString();
            SyncProgress.IsIndeterminate = false;
            SyncProgress.Value = progress.Percent;
        });
    }

    // The actions the main button isn't showing right now
    private void ShowSyncMenu()
    {
        string remote = SyncState.Remote(_status?.Upstream) ?? "origin";
        bool publish = _status is { IsDetached: false, Upstream: null };
        ContextMenu menu = new ContextMenu();
        SyncKind shown = _sync?.Kind ?? SyncKind.Fetch;
        if (shown != SyncKind.Fetch)
            AddMenuItem(menu, "Fetch " + remote, () => RunSyncAsync(SyncKind.Fetch));
        if (shown != SyncKind.Pull && !publish)
            AddMenuItem(menu, "Pull " + remote, () => RunSyncAsync(SyncKind.Pull));
        if (shown != SyncKind.Push && shown != SyncKind.Publish)
            AddMenuItem(menu, publish ? "Publish branch" : "Push " + remote,
                        () => RunSyncAsync(publish ? SyncKind.Publish : SyncKind.Push));
        menu.Open(SyncMenuButton);
    }

    private void ShowSync(RepositoryStatus? status, DateTimeOffset? lastFetched)
    {
        _lastFetched = lastFetched;
        if (_syncing)
            return;
        if (status == null)
        {
            _sync = null;
            SyncTitle.Text = "Fetch origin";
            SyncDetail.Text = "No repository";
            SyncIcon.Data = FetchArrows;
            SyncBadge.IsVisible = false;
            ToolTip.SetTip(SyncButton, null);
            BranchCaption.Text = "Current branch";
            BranchCounts.Text = string.Empty;
            return;
        }

        _sync = SyncState.For(status, lastFetched, DateTimeOffset.Now);
        BranchCaption.Text = SyncState.BranchCaption(lastFetched, DateTimeOffset.Now);
        BranchCounts.Text = SyncState.BranchCounts(status);
        SyncTitle.Text = _sync.Title;
        SyncDetail.Text = _sync.Detail;
        SyncIcon.Data = _sync.Kind switch
        {
            SyncKind.Pull => DownArrow,
            SyncKind.Push or SyncKind.Publish => UpArrow,
            _ => FetchArrows
        };
        SyncBadgeText.Text = _sync.Badge;
        SyncBadge.IsVisible = _sync.Badge.Length > 0;
        ToolTip.SetTip(SyncButton, _sync.Kind switch
        {
            SyncKind.Pull => "Bring in the commits on the remote",
            SyncKind.Push => "Send your commits to the remote",
            SyncKind.Publish => "Publish this branch so others can see it",
            _ => "Fetch the latest changes from the remote"
        });
    }
}
