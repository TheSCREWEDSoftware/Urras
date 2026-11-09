using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Urras.Core.Accounts;
using Urras.Core.Git;
using Urras.Core.Settings;

namespace Urras.App;

// The branch button and its picker: switch, filter, see pull requests, start a branch or merge one in
public partial class MainWindow
{
    private readonly BranchPickerView _branchPicker = new BranchPickerView();
    private readonly Flyout _branchFlyout = new Flyout { Placement = PlacementMode.BottomEdgeAlignedLeft };
    private (PlatformClient Client, RemoteIdentity Remote)? _pullSource;

    private void InitializeBranchPicker()
    {
        _branchFlyout.Content = _branchPicker;
        BranchButton.Click += async (_, _) => await OpenBranchPickerAsync();

        _branchPicker.BranchChosen += branch => AfterClosing(() => SwitchToBranchAsync(branch));
        _branchPicker.NewBranchRequested += () => AfterClosing(CreateBranchAsync);
        _branchPicker.MergeRequested += () => AfterClosing(MergeBranchAsync);
        _branchPicker.PullRequestsRequested += () => _ = LoadPickerPullRequestsAsync();
        _branchPicker.PullRequestChosen += pull => AfterClosing(async () =>
        {
            if (_pullSource is { } source)
                await CheckOutPullRequestAsync(source.Client, source.Remote, pull);
        });
    }

    private void AfterClosing(Func<Task> action)
    {
        _branchFlyout.Hide();
        _ = action();
    }

    // Reads when each branch last changed and which were used lately, then opens the picker
    private async Task OpenBranchPickerAsync()
    {
        if (_repository == null)
            return;

        GitRepository repository = _repository;
        Task<string> defaultBranch = repository.GetDefaultBranchAsync();
        Task<List<string>> recent = repository.GetRecentBranchesAsync();
        Task<Dictionary<string, DateTimeOffset>> dates = repository.GetBranchDatesAsync();
        await Task.WhenAll(defaultBranch, recent, dates);
        if (!ReferenceEquals(repository, _repository))
            return;

        _branchPicker.Show(_branches, defaultBranch.Result, recent.Result, dates.Result);
        _branchFlyout.ShowAt(BranchButton);
    }

    // Pull requests of the repository changes go to (a fork's parent, unless chosen otherwise)
    private async Task LoadPickerPullRequestsAsync()
    {
        _pullSource = null;
        (PlatformClient? client, RemoteIdentity? remote) = await PlatformForRepositoryAsync();
        if (remote == null)
        {
            _branchPicker.ShowPullRequests("Pull requests", Array.Empty<PlatformPullRequest>(),
                                           "This repository has no remote on a hosting platform.");
            return;
        }

        string heading = "Pull requests in " + remote.Owner + "/" + remote.Name;
        if (client == null)
        {
            string signIn = "Sign in to " + PlatformNameFor(remote.Host) + " under Accounts to see them.";
            _branchPicker.ShowPullRequests(heading, Array.Empty<PlatformPullRequest>(), signIn);
            return;
        }

        try
        {
            List<PlatformPullRequest> pulls =
                await PlatformClient.SafelyAsync(() => client.GetPullRequestsAsync(remote.Owner, remote.Name));
            _pullSource = (client, remote);
            _branchPicker.ShowPullRequests(heading, pulls, null);
        }
        catch (PlatformException exception)
        {
            _branchPicker.ShowPullRequests(heading, Array.Empty<PlatformPullRequest>(),
                                           "Couldn't list them: " + exception.Message);
        }
    }
}
