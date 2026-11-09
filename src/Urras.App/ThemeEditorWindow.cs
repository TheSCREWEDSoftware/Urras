using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Urras.Core.Settings;

namespace Urras.App;

// Edits a custom theme's colours: a swatch, what it colours, and its value as #RRGGBB or #AARRGGBB
public sealed class ThemeEditorWindow : Window
{
    private const double WINDOW_WIDTH = 640;
    private const double WINDOW_HEIGHT = 680;
    private const double SWATCH_SIZE = 22;
    private const double VALUE_WIDTH = 120;

    private readonly Urras.Core.Settings.Theme _theme;
    private readonly ThemeStore _store;
    private readonly ComboBox _base = new ComboBox { ItemsSource = new[] { "Dark", "Light" }, Width = 140 };
    private readonly Dictionary<string, (TextBox Value, Border Swatch)> _rows =
        new Dictionary<string, (TextBox Value, Border Swatch)>();
    private readonly TextBlock _problem = new TextBlock { Foreground = Brushes.IndianRed, IsVisible = false,
                                                          TextWrapping = TextWrapping.Wrap };

    public ThemeEditorWindow(Urras.Core.Settings.Theme theme, ThemeStore store)
    {
        _theme = theme;
        _store = store;
        Title = "Theme: " + theme.Name;
        Width = WINDOW_WIDTH;
        Height = WINDOW_HEIGHT;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        _base.SelectedIndex = theme.Base == ThemeBase.Light ? 1 : 0;

        StackPanel list = new StackPanel { Spacing = 6 };
        foreach (ThemeColour colour in ThemeColours.All)
            list.Children.Add(Row(colour));

        Button reset = new Button { Content = "Use the base's colours" };
        ToolTip.SetTip(reset, "Every colour back to the Dark or Light default chosen above");
        reset.Click += (_, _) =>
        {
            foreach (ThemeColour colour in ThemeColours.All)
                _rows[colour.Key].Value.Text = _base.SelectedIndex == 1 ? colour.Light : colour.Dark;
        };

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
        DockPanel.SetDock(top, Dock.Top);
        DockPanel.SetDock(buttons, Dock.Bottom);
        DockPanel.SetDock(_problem, Dock.Bottom);
        root.Children.Add(top);
        root.Children.Add(buttons);
        root.Children.Add(_problem);
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
