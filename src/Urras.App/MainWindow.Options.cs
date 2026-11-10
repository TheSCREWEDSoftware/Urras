using Avalonia;
using Avalonia.Controls;
using Avalonia.Styling;
using Urras.Core.Settings;

namespace Urras.App;

// Options: opening the window, and applying what it saved
public partial class MainWindow
{
    private Task OpenSettingsAsync()
    {
        return OpenOptionsAsync(OptionsWindow.INTEGRATIONS);
    }

    private async Task OpenOptionsAsync(string section)
    {
        if (_accounts == null)
            return;

        OptionsWindow window = new OptionsWindow(_settings, _accounts, AddAccountAsync, section);
        if (!await window.ShowDialog<bool>(this))
            return;

        ApplyPreferences();
        RestartAutoFetch();
        // The choice for all forks may have changed, so each fork's effective choice is read again
        _forkSettings.Clear();
        UpdateViewOnlineButton();
        UpdateAssistantButtons();
        SetStatus("Options saved.", false);
        await RefreshAsync();
        await UpdateNotificationCountAsync();
    }

    // Keeps every scroll bar at full width, so it can be clicked without hovering over it first
    private sealed class FullScrollBarsStyle : Style
    {
        public FullScrollBarsStyle() : base(selector => selector.OfType<ScrollViewer>())
        {
            Setters.Add(new Setter(ScrollViewer.AllowAutoHideProperty, false));
        }
    }

    // Every window but the main one stays out of the taskbar; styles see all windows as Window, so a class marks it
    private sealed class OneTaskbarEntryStyle : Style
    {
        public OneTaskbarEntryStyle() : base(selector => selector.Is<Window>().Not(other => other.Class(MAIN_CLASS)))
        {
            Setters.Add(new Setter(Window.ShowInTaskbarProperty, false));
        }
    }

    private const string MAIN_CLASS = "main-window";

    // Adds or removes an application style, so a setting applies to every window, open or not
    private static void UseStyle<T>(Styles styles, bool wanted) where T : Style, new()
    {
        bool present = styles.OfType<T>().Any();
        if (wanted && !present)
            styles.Add(new T());
        else if (!wanted)
        {
            foreach (T style in styles.OfType<T>().ToList())
                styles.Remove(style);
        }
    }

    // Formats, tab size, theme and the summary counter, at startup and after Options is saved
    private void ApplyPreferences()
    {
        DisplayFormats.DateFormat = DisplayFormats.Dates.Contains(_settings.DateFormat) ? _settings.DateFormat
                                                                                        : DisplayFormats.DEFAULT_DATE;
        DisplayFormats.TimeFormat = DisplayFormats.Times.Contains(_settings.TimeFormat) ? _settings.TimeFormat
                                                                                        : DisplayFormats.DEFAULT_TIME;
        DiffRow.TabWidth = Math.Clamp(_settings.TabSize, 1, 16);
        if (Application.Current != null)
        {
            Application.Current.RequestedThemeVariant = _settings.Theme switch
            {
                ThemeChoice.Light => ThemeVariant.Light,
                ThemeChoice.System => ThemeVariant.Default,
                _ => ThemeVariant.Dark
            };
            Urras.Core.Settings.Theme theme = ChooseTheme(Application.Current);
            // A chosen theme (ready-made or a file) brings its own dark or light base
            if (theme.Name is not (Urras.Core.Settings.Theme.DARK or Urras.Core.Settings.Theme.LIGHT))
                Application.Current.RequestedThemeVariant = theme.Base == ThemeBase.Light ? ThemeVariant.Light
                                                                                          : ThemeVariant.Dark;
            ThemeBrushes.Use(theme, Application.Current.Resources);

            // A style belongs to one application, so each gets its own (the interface tests start several)
            Styles styles = Application.Current.Styles;
            UseStyle<FullScrollBarsStyle>(styles, _settings.AlwaysShowScrollBars);
            UseStyle<OneTaskbarEntryStyle>(styles, !_settings.ShowWindowsInTaskbar);
        }

        SubjectCounter.IsVisible = _settings.Prompts.ShowCommitLengthWarning;
        HideUntrackedBox.IsChecked = _settings.HideUntracked;
    }

    // The chosen ready-made or custom theme, or the built-in colours for the dark or light look in use
    private Urras.Core.Settings.Theme ChooseTheme(Application application)
    {
        if (_settings.CustomTheme is { Length: > 0 } name &&
            (ThemePresets.Find(name) ?? ThemeStore.Default.Find(name)) is { } chosen)
            return chosen;
        bool light = application.ActualThemeVariant == ThemeVariant.Light;
        return Urras.Core.Settings.Theme.BuiltIn(light ? ThemeBase.Light : ThemeBase.Dark);
    }

    // "VS Code" style name of the chosen editor, for menus
    private string EditorName => _settings.Editor.IsCustom
        ? Path.GetFileNameWithoutExtension(_settings.Editor.Path) is { Length: > 0 } name ? name : "editor"
        : _settings.Editor.Name;

    // Asks only when the user wants to be asked for this kind of action
    private Task<bool> ConfirmIfAsync(bool ask, string title, string message, string action)
    {
        return ask ? Dialogs.ConfirmAsync(this, title, message, action) : Task.FromResult(true);
    }
}
