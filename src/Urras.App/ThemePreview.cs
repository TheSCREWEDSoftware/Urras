using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Urras.Core.Settings;

namespace Urras.App;

// A small picture of Urras in a theme's colours, and a strip of its main colours, for choosing a theme
public sealed class ThemePreview : Border
{
    private const double PREVIEW_HEIGHT = 168;
    private const double SIDEBAR_WIDTH = 120;
    private const double SWATCH_SIZE = 14;
    private const double SMALL_TEXT = 11;
    private const double TEXT = 12;
    private const byte SELECTION_ALPHA = 0x40;

    private static readonly string[] SwatchKeys =
    {
        ThemeColours.BACKGROUND, ThemeColours.SURFACE, ThemeColours.RAISED, ThemeColours.ACCENT, ThemeColours.TEXT
    };

    public ThemePreview()
    {
        Height = PREVIEW_HEIGHT;
        CornerRadius = new CornerRadius(ControlLook.OVERLAY_RADIUS);
        BorderThickness = new Thickness(1);
        ClipToBounds = true;
    }

    // Draws the window again in the theme's colours
    public void Show(Theme theme)
    {
        Background = Brush(theme, ThemeColours.BACKGROUND);
        BorderBrush = Brush(theme, ThemeColours.BORDER);

        Grid window = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*"), Margin = new Thickness(10) };
        StackPanel sidebar = new StackPanel
        {
            Width = SIDEBAR_WIDTH,
            Spacing = 4,
            Margin = new Thickness(0, 0, 10, 0)
        };
        sidebar.Children.Add(Text(theme, "Repositories", ThemeColours.TEXT, TEXT, FontWeight.SemiBold));
        sidebar.Children.Add(Repository(theme, "Urras", "main · origin ↑2", true));
        sidebar.Children.Add(Repository(theme, "Echo Flint", "main · origin", false));
        window.Children.Add(sidebar);

        Border card = new Border
        {
            Background = Brush(theme, ThemeColours.SURFACE),
            BorderBrush = Brush(theme, ThemeColours.BORDER),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(ControlLook.CONTROL_RADIUS),
            Padding = new Thickness(10, 6)
        };
        Grid.SetColumn(card, 1);
        DockPanel content = new DockPanel();
        StackPanel tabs = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12 };
        tabs.Children.Add(new Border
        {
            BorderBrush = Brush(theme, ThemeColours.ACCENT),
            BorderThickness = new Thickness(0, 0, 0, 2),
            Child = Text(theme, "History", ThemeColours.TEXT, TEXT, FontWeight.SemiBold)
        });
        tabs.Children.Add(Text(theme, "Changes", ThemeColours.MUTED_TEXT, TEXT, FontWeight.Normal));
        DockPanel.SetDock(tabs, Dock.Top);
        content.Children.Add(tabs);

        StackPanel buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 6,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 6, 0, 0)
        };
        buttons.Children.Add(PretendButton(theme, "Fetch origin", ThemeColours.RAISED, ThemeColours.TEXT, true));
        buttons.Children.Add(PretendButton(theme, "Commit", ThemeColours.ACCENT, ThemeColours.ACCENT_TEXT, false));
        DockPanel.SetDock(buttons, Dock.Bottom);
        content.Children.Add(buttons);

        StackPanel rows = new StackPanel { Margin = new Thickness(0, 6, 0, 0), Spacing = 2 };
        rows.Children.Add(Commit(theme, "feat(UI): Add ready-made themes", true, ThemeColours.HOVER));
        rows.Children.Add(Commit(theme, "fix(UI): Keep the main window in the taskbar", false, null));
        rows.Children.Add(Commit(theme, "docs: Explain the themes", false, null));
        content.Children.Add(rows);
        card.Child = content;
        window.Children.Add(card);
        Child = window;
    }

    // Background, panel, buttons, accent and text side by side, for the theme list
    public static Control Swatches(Theme theme)
    {
        StackPanel strip = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            VerticalAlignment = VerticalAlignment.Center
        };
        foreach (string key in SwatchKeys)
            strip.Children.Add(new Border
            {
                Width = SWATCH_SIZE,
                Height = SWATCH_SIZE,
                Background = Brush(theme, key),
                BorderBrush = Brush(theme, ThemeColours.BORDER),
                BorderThickness = new Thickness(1)
            });
        return new Border { CornerRadius = new CornerRadius(3), ClipToBounds = true, Child = strip };
    }

    private static Control Repository(Theme theme, string name, string detail, bool selected)
    {
        Color accent = Color.Parse(theme.Colour(ThemeColours.ACCENT));
        StackPanel lines = new StackPanel();
        lines.Children.Add(Text(theme, name, ThemeColours.TEXT, TEXT, FontWeight.Normal));
        lines.Children.Add(Text(theme, detail, ThemeColours.ATTENTION, SMALL_TEXT, FontWeight.Normal));
        return new Border
        {
            Background = selected
                ? new SolidColorBrush(Color.FromArgb(SELECTION_ALPHA, accent.R, accent.G, accent.B))
                : Brushes.Transparent,
            CornerRadius = new CornerRadius(ControlLook.CONTROL_RADIUS),
            Padding = new Thickness(6, 3),
            Child = lines
        };
    }

    private static Control Commit(Theme theme, string subject, bool unpushed, string? background)
    {
        DockPanel row = new DockPanel();
        if (unpushed)
        {
            Border mark = new Border
            {
                Background = Brush(theme, ThemeColours.BADGE),
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(6, 0),
                Margin = new Thickness(6, 0, 0, 0),
                Child = Text(theme, "Not pushed", ThemeColours.ATTENTION, SMALL_TEXT, FontWeight.SemiBold)
            };
            DockPanel.SetDock(mark, Dock.Right);
            row.Children.Add(mark);
        }

        row.Children.Add(Text(theme, subject, ThemeColours.TEXT, TEXT, FontWeight.Normal));
        return new Border
        {
            Background = background == null ? Brushes.Transparent : Brush(theme, background),
            CornerRadius = new CornerRadius(ControlLook.CONTROL_RADIUS),
            Padding = new Thickness(6, 2),
            Child = row
        };
    }

    private static Control PretendButton(Theme theme, string label, string background, string foreground, bool edged)
    {
        return new Border
        {
            Background = Brush(theme, background),
            BorderBrush = Brush(theme, ThemeColours.BORDER),
            BorderThickness = new Thickness(edged ? 1 : 0),
            CornerRadius = new CornerRadius(ControlLook.CONTROL_RADIUS),
            Padding = new Thickness(10, 3),
            Child = Text(theme, label, foreground, TEXT, FontWeight.SemiBold)
        };
    }

    private static TextBlock Text(Theme theme, string text, string colour, double size, FontWeight weight)
    {
        return new TextBlock
        {
            Text = text,
            Foreground = Brush(theme, colour),
            FontSize = size,
            FontWeight = weight,
            TextTrimming = TextTrimming.CharacterEllipsis,
            VerticalAlignment = VerticalAlignment.Center
        };
    }

    private static IBrush Brush(Theme theme, string key)
    {
        return new SolidColorBrush(Color.Parse(theme.Colour(key)));
    }
}
