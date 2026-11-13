using Avalonia;
using Avalonia.Controls;
using Urras.Core.Settings;

namespace Urras.App;

// On Windows Urras draws its own title bar, with the File menu and title in it, like VS Code
public partial class MainWindow
{
    private const double TITLE_BAR_HEIGHT = 40;
    // The maximise button's square, and two overlapping squares once maximised
    private const string MAXIMISE_GLYPH = "M0.5,0.5 H9.5 V9.5 H0.5 Z";
    private const string RESTORE_GLYPH = "M2.5,0.5 H9.5 V7.5 M0.5,2.5 H7.5 V9.5 H0.5 Z";

    private void InitializeTitleBar()
    {
        // Elsewhere the system title bar stays and this row is just the menu
        if (!OperatingSystem.IsWindows())
            return;

        ExtendClientAreaToDecorationsHint = true;
        // Windows' own buttons turn white on hover in dark themes, so Urras draws themed ones instead
        ExtendClientAreaChromeHints = Avalonia.Platform.ExtendClientAreaChromeHints.NoChrome;
        ExtendClientAreaTitleBarHeightHint = TITLE_BAR_HEIGHT;
        TitleBar.Height = TITLE_BAR_HEIGHT;
        TitleIcon.IsVisible = true;
        WindowTitle.IsVisible = true;
        ShowCaptionButtons();

        // Maximised, Windows puts the window's edges off-screen; the content moves in to stay visible
        PropertyChanged += (_, change) =>
        {
            if (change.Property == OffScreenMarginProperty || change.Property == WindowStateProperty)
                RootPanel.Margin = WindowState == WindowState.Maximized ? OffScreenMargin : default;
        };
    }

    private void ShowCaptionButtons()
    {
        CaptionButtons.IsVisible = true;
        MinimiseButton.Click += (_, _) => WindowState = WindowState.Minimized;
        MaximiseButton.Click += (_, _) => ToggleMaximised();
        CloseButton.Click += (_, _) => Close();
        PropertyChanged += (_, change) =>
        {
            if (change.Property != WindowStateProperty)
                return;
            bool maximised = WindowState == WindowState.Maximized;
            MaximiseGlyph.Data = Avalonia.Media.Geometry.Parse(maximised ? RESTORE_GLYPH : MAXIMISE_GLYPH);
            ToolTip.SetTip(MaximiseButton, maximised ? "Restore" : "Maximise");
        };

        // The empty parts of the title bar drag the window; a double-click maximises or restores it
        AddHandler(PointerPressedEvent, (_, e) =>
        {
            if (e.GetPosition(this).Y >= TITLE_BAR_HEIGHT || !IsTitleBarBackground(e.Source) ||
                !e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
                return;
            if (e.ClickCount == 2)
                ToggleMaximised();
            else
                BeginMoveDrag(e);
            e.Handled = true;
        }, Avalonia.Interactivity.RoutingStrategies.Tunnel);
    }

    private bool IsTitleBarBackground(object? source)
    {
        return source == this || source == RootPanel || source == TitleBar || source == WindowTitle ||
               source == TitleIcon;
    }

    private void ToggleMaximised()
    {
        WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
    }

    // "AC 335 (GitHub) - Urras": where the repository lives, so similar names on different hosts stand apart
    private void ShowWindowTitle(string? repository, string? path = null)
    {
        string where = path == null ? string.Empty : " (" + WhereItLives(path) + ")";
        string title = string.IsNullOrEmpty(repository) ? "Urras" : repository + where + " - Urras";
        Title = title;
        WindowTitle.Text = title;
    }

    // GitHub, GitLab, Bitbucket or another host from the origin remote (or the first), or "Local" with none
    private string WhereItLives(string path)
    {
        Dictionary<string, RemoteIdentity> remotes = RemoteIdentity.ReadAll(path);
        if (remotes.Count == 0)
            return "Local";
        RemoteIdentity remote = remotes.TryGetValue(RemoteIdentity.ORIGIN, out RemoteIdentity? origin)
            ? origin
            : remotes.Values.First();
        return PlatformNameFor(remote.Host);
    }
}
