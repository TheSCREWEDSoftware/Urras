using Avalonia;
using Avalonia.Controls;

namespace Urras.App;

// On Windows Urras draws its own title bar, with the File menu and title in it, like VS Code
public partial class MainWindow
{
    private const double TITLE_BAR_HEIGHT = 40;
    // Room on the right for Windows' minimise, maximise and close buttons
    private const double CAPTION_BUTTONS_WIDTH = 140;

    private void InitializeTitleBar()
    {
        // Elsewhere the system title bar stays and this row is just the menu
        if (!OperatingSystem.IsWindows())
            return;

        ExtendClientAreaToDecorationsHint = true;
        // Avalonia's own minimise, maximise and close buttons follow the theme; Windows' turn white on hover
        ExtendClientAreaChromeHints = Avalonia.Platform.ExtendClientAreaChromeHints.NoChrome;
        ExtendClientAreaTitleBarHeightHint = TITLE_BAR_HEIGHT;
        TitleBar.Height = TITLE_BAR_HEIGHT;
        TitleBar.Margin = new Thickness(0, 0, CAPTION_BUTTONS_WIDTH, 0);
        TitleIcon.IsVisible = true;
        WindowTitle.IsVisible = true;

        // Maximised, Windows puts the window's edges off-screen; the content moves in to stay visible
        PropertyChanged += (_, change) =>
        {
            if (change.Property == OffScreenMarginProperty || change.Property == WindowStateProperty)
                RootPanel.Margin = WindowState == WindowState.Maximized ? OffScreenMargin : default;
        };
    }

    // "cnc-exoduscommunity.github.io - Urras" in the title bar and the taskbar
    private void ShowWindowTitle(string? repository)
    {
        string title = string.IsNullOrEmpty(repository) ? "Urras" : repository + " - Urras";
        Title = title;
        WindowTitle.Text = title;
    }
}
