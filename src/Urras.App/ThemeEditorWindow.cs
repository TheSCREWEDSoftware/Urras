using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Urras.Core.Settings;

namespace Urras.App;

// Edits a theme's colours, ready-made and built-in ones included: a swatch, what it colours, and its value
public sealed class ThemeEditorWindow : Window
{
    private const double WINDOW_WIDTH = 640;
    private const double WINDOW_HEIGHT = 680;
    private const double SWATCH_SIZE = 22;
    private const double VALUE_WIDTH = 120;
    private const double NAME_FONT_SIZE = 18;
    // Keeps the colour boxes clear of the list's scroll bar
    private const double SCROLL_BAR_ROOM = 18;

    private readonly Urras.Core.Settings.Theme _theme;
    private readonly ThemeStore _store;
    private readonly Urras.Core.Settings.Theme? _original;
    private readonly ComboBox _base = new ComboBox { ItemsSource = new[] { "Dark", "Light" }, Width = 140 };
    private readonly Dictionary<string, (TextBox Value, Border Swatch)> _rows =
        new Dictionary<string, (TextBox Value, Border Swatch)>();
    private readonly TextBlock _problem = new TextBlock { Foreground = Brushes.IndianRed, IsVisible = false,
                                                          TextWrapping = TextWrapping.Wrap };

    // about says which theme this is, such as "Fel, a ready-made theme"
    public ThemeEditorWindow(Urras.Core.Settings.Theme theme, ThemeStore store, string? about = null)
    {
        _theme = theme;
        _store = store;
        _original = ThemeStore.Original(theme.Name);
        Title = "Editing theme: " + theme.Name;
        Width = WINDOW_WIDTH;
        Height = WINDOW_HEIGHT;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        _base.SelectedIndex = theme.Base == ThemeBase.Light ? 1 : 0;

        StackPanel list = new StackPanel { Spacing = 6 };
        foreach (ThemeColour colour in ThemeColours.All)
            list.Children.Add(Row(colour));

        Button reset = new Button { Content = "Restore defaults" };
        ToolTip.SetTip(reset, _original != null
                                  ? "Every colour back to how " + theme.Name + " comes with Urras"
                                  : "Every colour back to the Dark or Light default chosen below");
        reset.Click += (_, _) =>
        {
            if (_original != null)
                _base.SelectedIndex = _original.Base == ThemeBase.Light ? 1 : 0;
            foreach (ThemeColour colour in ThemeColours.All)
                _rows[colour.Key].Value.Text = _original?.Colour(colour.Key) ??
                                               (_base.SelectedIndex == 1 ? colour.Light : colour.Dark);
        };

        // Which theme this is and what Save does, so it's clear what is being changed
        StackPanel heading = new StackPanel { Spacing = 2, Margin = new Thickness(0, 0, 0, 12) };
        heading.Children.Add(new TextBlock { Text = "Editing " + (about ?? theme.Name),
                                             FontSize = NAME_FONT_SIZE, FontWeight = FontWeight.SemiBold,
                                             TextWrapping = TextWrapping.Wrap });
        heading.Children.Add(new TextBlock
        {
            Text = (_original != null ? "Saving keeps your version; Restore defaults brings back the original. "
                                      : string.Empty) + "Saved as " + store.FileFor(theme.Name),
            Foreground = ThemeBrushes.MutedText,
            TextWrapping = TextWrapping.Wrap
        });

        DockPanel top = new DockPanel { Margin = new Thickness(0, 0, 0, 10) };
        StackPanel baseRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        baseRow.Children.Add(new TextBlock { Text = "Based on", VerticalAlignment = VerticalAlignment.Center });
        baseRow.Children.Add(_base);
        DockPanel.SetDock(reset, Dock.Right);
        top.Children.Add(reset);
        top.Children.Add(baseRow);

        Button save = new Button { Content = "Save", IsDefault = true, Classes = { "accent" } };
        Button cancel = new Button { Content = "Cancel", IsCancel = true };
        save.Click += (_, _) => Save();
        cancel.Click += (_, _) => Close(false);
        StackPanel buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Spacing = 8,
            Margin = new Thickness(0, 10, 0, 0)
        };
        buttons.Children.Add(save);
        buttons.Children.Add(cancel);

        DockPanel root = new DockPanel { Margin = new Thickness(14) };
        DockPanel.SetDock(heading, Dock.Top);
        DockPanel.SetDock(top, Dock.Top);
        DockPanel.SetDock(buttons, Dock.Bottom);
        DockPanel.SetDock(_problem, Dock.Bottom);
        root.Children.Add(heading);
        root.Children.Add(top);
        root.Children.Add(buttons);
        root.Children.Add(_problem);
        list.Margin = new Thickness(0, 0, SCROLL_BAR_ROOM, 0);
        root.Children.Add(new ScrollViewer { Content = list });
        Content = root;
    }

    private Control Row(ThemeColour colour)
    {
        Border swatch = new Border
        {
            Width = SWATCH_SIZE,
            Height = SWATCH_SIZE,
            CornerRadius = new CornerRadius(4),
            BorderThickness = new Thickness(1),
            BorderBrush = ThemeBrushes.Divider,
            Margin = new Thickness(0, 0, 10, 0)
        };
        TextBox value = new TextBox { Text = _theme.Colour(colour.Key), Width = VALUE_WIDTH,
                                      Margin = new Thickness(10, 0, 0, 0) };
        value.TextChanged += (_, _) => Paint(swatch, value.Text);
        Paint(swatch, value.Text);
        _rows[colour.Key] = (value, swatch);

        StackPanel names = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        names.Children.Add(new TextBlock { Text = colour.Description, TextWrapping = TextWrapping.Wrap });
        names.Children.Add(new TextBlock { Text = colour.Key, FontSize = 11, Foreground = ThemeBrushes.MutedText });

        DockPanel row = new DockPanel();
        DockPanel.SetDock(swatch, Dock.Left);
        DockPanel.SetDock(value, Dock.Right);
        row.Children.Add(swatch);
        row.Children.Add(value);
        row.Children.Add(names);
        return row;
    }

    // A value that isn't a colour yet (still being typed) leaves the swatch empty
    private static void Paint(Border swatch, string? text)
    {
        string value = text?.Trim() ?? string.Empty;
        swatch.Background = ThemeColours.IsColour(value) ? new SolidColorBrush(Color.Parse(value)) : null;
    }

    private void Save()
    {
        List<string> broken = _rows.Where(row => !ThemeColours.IsColour(row.Value.Value.Text?.Trim()))
                                   .Select(row => row.Key).ToList();
        if (broken.Count > 0)
        {
            _problem.Text = "These aren't colours (use #RRGGBB or #AARRGGBB): " + string.Join(", ", broken);
            _problem.IsVisible = true;
            return;
        }

        _theme.Base = _base.SelectedIndex == 1 ? ThemeBase.Light : ThemeBase.Dark;
        foreach ((string key, (TextBox value, Border _)) in _rows)
            _theme.Colours[key] = value.Text!.Trim();

        try
        {
            // Back to exactly the shipped colours: drop your version instead of keeping an identical copy
            if (_original != null && _theme.LooksLike(_original))
                _store.RemoveEdits(_theme.Name);
            else
                _store.Save(_theme);
            Close(true);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            _problem.Text = "Couldn't save the theme: " + exception.Message;
            _problem.IsVisible = true;
        }
    }
}
