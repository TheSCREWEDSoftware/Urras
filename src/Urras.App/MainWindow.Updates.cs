using Urras.Core.Settings;

namespace Urras.App;

// Once a day Urras asks GitHub whether a newer version is out, and a title bar button offers it
public partial class MainWindow
{
    // A moment after opening, so the check never slows the start
    private static readonly TimeSpan UPDATE_DELAY = TimeSpan.FromSeconds(5);
    private static readonly HttpClient UpdateHttp = new HttpClient();
    private UpdateCheck.Release? _newRelease;

    private void InitializeUpdates()
    {
        UpdateButton.Click += (_, _) => OpenNewRelease();
        UpdatesItem.Click += async (_, _) => await CheckForUpdatesAsync(true);
        ShortcutsItem.Click += async (_, _) => await Dialogs.ShortcutsAsync(this);
        Opened += async (_, _) =>
        {
            await Task.Delay(UPDATE_DELAY);
            await CheckForUpdatesAsync(false);
        };
    }

    // asked: from the Help menu, so it always checks and always answers
    private async Task CheckForUpdatesAsync(bool asked)
    {
        DateTimeOffset now = DateTimeOffset.Now;
        if (!asked && (!_settings.CheckForUpdates || !UpdateCheck.IsDue(_settings.LastUpdateCheck, now)))
            return;

        UpdateCheck.Release? newer = await UpdateCheck.NewerAsync(UpdateHttp, AppVersion());
        _settings.LastUpdateCheck = now;
        _settings.Save();
        _newRelease = newer;
        UpdateButton.IsVisible = newer != null;
        if (newer != null)
        {
            UpdateButton.Content = "Urras " + newer.Version + " is out";
            Avalonia.Controls.ToolTip.SetTip(UpdateButton, "Open the release page to download it");
        }

        if (!asked)
            return;
        if (newer != null)
        {
            if (await Dialogs.ConfirmAsync(this, "Check for updates", "Urras " + newer.Version + " is out; you " +
                                                                      "have " + AppVersion() + ".", "Open its page"))
                OpenNewRelease();
        }
        else
        {
            await Dialogs.ShowTextAsync(this, "Check for updates", "No newer Urras was found (you have " +
                                                                   AppVersion() + "), or GitHub couldn't be " +
                                                                   "reached.");
        }
    }

    private void OpenNewRelease()
    {
        if (_newRelease != null && SystemShell.OpenUrl(_newRelease.Url) is { } problem)
            SetStatus(problem, true);
    }

    // A second start asked this Urras to come forward
    public void BringForward()
    {
        if (WindowState == Avalonia.Controls.WindowState.Minimized)
            WindowState = Avalonia.Controls.WindowState.Normal;
        Show();
        Activate();
    }
}
