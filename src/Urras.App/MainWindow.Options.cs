using Avalonia;
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
        UpdateAssistantButtons();
        SetStatus("Options saved.", false);
        await RefreshAsync();
        await UpdateNotificationCountAsync();
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
        }

        SubjectCounter.IsVisible = _settings.Prompts.ShowCommitLengthWarning;
        HideUntrackedBox.IsChecked = _settings.HideUntracked;
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
