using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Urras.Core.Settings;

namespace Urras.App;

// Edits a custom theme: its font, text size and colours (each as #RRGGBB or #AARRGGBB)
public sealed class ThemeEditorWindow : Window
{
    private const double WINDOW_WIDTH = 640;
    private const double WINDOW_HEIGHT = 680;
    private const double SWATCH_SIZE = 22;
    private const double VALUE_WIDTH = 120;
    private const double PICKER_WIDTH = 220;
    private const double CHOICE_GAP = 8;
    private const double NAME_FONT_SIZE = 18;
    // Keeps the colour boxes clear of the list's scroll bar
    private const double SCROLL_BAR_ROOM = 18;
    private const string GENERAL = "Same as Options";

    private readonly Urras.Core.Settings.Theme _theme;
    private readonly ThemeStore _store;
    private readonly ComboBox _base = new ComboBox { ItemsSource = new[] { "Dark", "Light" }, Width = 140 };
    private readonly ComboBox _font;
    private readonly ComboBox _fontSize;
    private readonly Dictionary<string, (TextBox Value, Border Swatch)> _rows =
        new Dictionary<string, (TextBox Value, Border Swatch)>();
    private readonly TextBlock _problem = new TextBlock { Foreground = Brushes.IndianRed, IsVisible = false,
                                                          TextWrapping = TextWrapping.Wrap };

    public ThemeEditorWindow(Urras.Core.Settings.Theme theme, ThemeStore store, AppSettings settings)
    {
        _theme = theme;
        _store = store;
        Title = "Editing theme: " + theme.Name;
        Width = WINDOW_WIDTH;
        Height = WINDOW_HEIGHT;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        _base.SelectedIndex = theme.Base == ThemeBase.Light ? 1 : 0;

        StackPanel list = new StackPanel { Spacing = 6 };
        foreach (ThemeColour colour in ThemeColours.All)
            list.Children.Add(Row(colour));

        string general = GENERAL + " (" + (settings.FontFamily ?? "default") + ")";
        _font = TextLook.FontPicker(general, theme.FontFamily);
        _fontSize = TextLook.SizePicker(GENERAL + " (" + TextLook.Size(settings.FontSize) + " pt)", theme.FontSize);
        _font.Width = PICKER_WIDTH;
        _fontSize.Width = PICKER_WIDTH;

        Urras.Core.Settings.Theme? original = theme.Original();
        Button restore = new Button { Content = original == null ? "Use the base's colours" : "Restore original" };
        ToolTip.SetTip(restore, original == null
                                    ? "Every colour back to the Dark or Light default chosen below"
                                    : "Every colour, the base, font and text size back to " + original.Name + "'s");
        restore.Click += (_, _) => Restore(original);

        // Which theme this is, and where it came from, so it's clear what Save changes
        StackPanel heading = new StackPanel { Spacing = 2 };
        heading.Children.Add(new TextBlock { Text = theme.Name, FontSize = NAME_FONT_SIZE,
                                             FontWeight = FontWeight.SemiBold });
        heading.Children.Add(new TextBlock
        {
            Text = (original == null ? "Your theme" : "Your copy of " + original.Name + ", which stays as it is") +
                   "; saved as " + store.FileFor(theme.Name),
            Foreground = ThemeBrushes.MutedText,
            TextWrapping = TextWrapping.Wrap
        });
        DockPanel title = new DockPanel { Margin = new Thickness(0, 0, 0, 12) };
        DockPanel.SetDock(restore, Dock.Right);
        restore.VerticalAlignment = VerticalAlignment.Top;
        title.Children.Add(restore);
        title.Children.Add(heading);

        Grid choices = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("Auto,Auto"),
            RowDefinitions = new RowDefinitions("Auto,Auto,Auto")
        };
        AddChoice(choices, 0, "Based on", _base);
        AddChoice(choices, 1, "Font", _font);
        AddChoice(choices, 2, "Text size", _fontSize);

        StackPanel top = new StackPanel { Margin = new Thickness(0, 0, 0, 10) };
        top.Children.Add(title);
        top.Children.Add(choices);

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
        list.Margin = new Thickness(0, 0, SCROLL_BAR_ROOM, 0);
        root.Children.Add(new ScrollViewer { Content = list });
        Content = root;
    }

    private static void AddChoice(Grid grid, int row, string label, Control control)
    {
        TextBlock text = new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center,
                                         Margin = new Thickness(0, 0, CHOICE_GAP, CHOICE_GAP) };
        control.Margin = new Thickness(0, 0, 0, CHOICE_GAP);
        Grid.SetRow(text, row);
        Grid.SetRow(control, row);
        Grid.SetColumn(control, 1);
        grid.Children.Add(text);
        grid.Children.Add(control);
    }

    // Back to the theme it was copied from, or to the base's default colours when there is none
    private void Restore(Urras.Core.Settings.Theme? original)
    {
        if (original != null)
        {
            _base.SelectedIndex = original.Base == ThemeBase.Light ? 1 : 0;
            _font.SelectedIndex = 0;
            _fontSize.SelectedIndex = 0;
        }

        foreach (ThemeColour colour in ThemeColours.All)
            _rows[colour.Key].Value.Text = original?.Colour(colour.Key) ??
                                           (_base.SelectedIndex == 1 ? colour.Light : colour.Dark);
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
        _theme.FontFamily = TextLook.ChosenFont(_font);
        _theme.FontSize = TextLook.ChosenSize(_fontSize, true);
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
