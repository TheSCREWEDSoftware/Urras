using Avalonia;
using Avalonia.Controls;
using Urras.Core.Settings;

namespace Urras.App;

// What closing the window does: exit (asking first when chosen in Options) or hide to the notification area
public partial class MainWindow
{
    private TrayIcon? _trayIcon;
    private bool _exiting;

    private void InitializeExit()
    {
        Closing += OnWindowClosing;
        Closed += (_, _) => _trayIcon?.Dispose();
    }

    // File › Exit and the tray menu's Exit always exit, asking first when chosen
    private async Task ExitAsync()
    {
        if (_settings.ConfirmBeforeExit && !await ConfirmExitAsync())
            return;
        _exiting = true;
        Close();
    }

    private async void OnWindowClosing(object? sender, WindowClosingEventArgs e)
    {
        // Windows shutting down or signing out is never held up
        if (_exiting || e.CloseReason is WindowCloseReason.ApplicationShutdown or WindowCloseReason.OSShutdown)
            return;

        if (_settings.CloseButton == CloseAction.Tray && TrayWorks)
        {
            e.Cancel = true;
            HideToTray();
            return;
        }

        if (!_settings.ConfirmBeforeExit)
            return;
        e.Cancel = true;
        if (!await ConfirmExitAsync())
            return;
        _exiting = true;
        Close();
    }

    // Linux desktops without a notification area would leave a hidden window there was no way back to
    internal static bool TrayWorks { get; set; } = OperatingSystem.IsWindows() || OperatingSystem.IsMacOS();

    private async Task<bool> ConfirmExitAsync()
    {
        // A dialog needs a visible owner, so a window hidden in the tray comes back first
        BringForward();
        return await Dialogs.ConfirmAsync(this, "Exit Urras", "Exit Urras?", "Exit");
    }

    private void HideToTray()
    {
        _trayIcon ??= CreateTrayIcon();
        _trayIcon.IsVisible = true;
        Hide();
    }

    private TrayIcon CreateTrayIcon()
    {
        NativeMenuItem open = new NativeMenuItem("Open Urras");
        open.Click += (_, _) => BringForward();
        NativeMenuItem exit = new NativeMenuItem("Exit Urras");
        exit.Click += async (_, _) => await ExitAsync();

        TrayIcon icon = new TrayIcon
        {
            Icon = Icon,
            ToolTipText = "Urras",
            Menu = new NativeMenu { Items = { open, new NativeMenuItemSeparator(), exit } }
        };
        icon.Clicked += (_, _) => BringForward();
        if (Application.Current != null)
            TrayIcon.SetIcons(Application.Current, new TrayIcons { icon });
        return icon;
    }

    // Back from the tray, the icon goes away again
    private void HideTrayIcon()
    {
        if (_trayIcon != null)
            _trayIcon.IsVisible = false;
    }
}
