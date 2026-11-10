using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Urras.Core.Settings;

namespace Urras.App;

// Fluent's controls read their colours and corners from named resources; these give them Urras's softer look
public static class ControlLook
{
    public const double CONTROL_RADIUS = 6;
    public const double OVERLAY_RADIUS = 10;

    // Accent shades for hover and pressed, mixed towards white or black by these amounts
    private const double SHADE_STEP = 0.12;
    private const int SHADES = 3;
    // Selected rows show the accent at this opacity
    private const byte SELECTION_ALPHA = 0x40;
    private const byte SELECTION_HOVER_ALPHA = 0x55;

    public static void Apply(Theme theme, IResourceDictionary resources)
    {
        Color accent = Color.Parse(theme.Colour(ThemeColours.ACCENT));
        resources["SystemAccentColor"] = accent;
        for (int shade = 1; shade <= SHADES; shade++)
        {
            resources["SystemAccentColorLight" + shade] = Mix(accent, Colors.White, SHADE_STEP * shade);
            resources["SystemAccentColorDark" + shade] = Mix(accent, Colors.Black, SHADE_STEP * shade);
        }

        resources["ControlCornerRadius"] = new CornerRadius(CONTROL_RADIUS);
        resources["OverlayCornerRadius"] = new CornerRadius(OVERLAY_RADIUS);

        IBrush background = Brush(theme, ThemeColours.BACKGROUND);
        IBrush raised = Brush(theme, ThemeColours.RAISED);
        IBrush hover = Brush(theme, ThemeColours.HOVER);
        IBrush pressed = Brush(theme, ThemeColours.PRESSED);
        IBrush border = Brush(theme, ThemeColours.BORDER);
        IBrush text = Brush(theme, ThemeColours.TEXT);
        IBrush muted = Brush(theme, ThemeColours.MUTED_TEXT);
        IBrush accentBrush = new SolidColorBrush(accent);
        IBrush accentHover = new SolidColorBrush(Mix(accent, Colors.White, SHADE_STEP));
        IBrush accentPressed = new SolidColorBrush(Mix(accent, Colors.Black, SHADE_STEP));
        IBrush accentText = Brush(theme, ThemeColours.ACCENT_TEXT);
        IBrush selection = new SolidColorBrush(Color.FromArgb(SELECTION_ALPHA, accent.R, accent.G, accent.B));
        IBrush selectionHover = new SolidColorBrush(Color.FromArgb(SELECTION_HOVER_ALPHA, accent.R, accent.G,
                                                                   accent.B));

        Set(resources, background, "SystemRegionBrush", "SystemControlBackgroundAltHighBrush");

        Set(resources, raised, "ButtonBackground", "ToggleButtonBackground", "RepeatButtonBackground",
            "TextControlBackground", "TextControlBackgroundPointerOver", "ComboBoxBackground",
            "ComboBoxDropDownBackground", "MenuFlyoutPresenterBackground", "FlyoutPresenterBackground",
            "ToolTipBackground", "ButtonBackgroundDisabled", "TextControlBackgroundDisabled");
        Set(resources, hover, "ButtonBackgroundPointerOver", "ToggleButtonBackgroundPointerOver",
            "RepeatButtonBackgroundPointerOver", "ComboBoxBackgroundPointerOver",
            "MenuFlyoutItemBackgroundPointerOver", "MenuFlyoutSubItemBackgroundPointerOver",
            "SystemControlHighlightListLowBrush", "TreeViewItemBackgroundPointerOver",
            "ComboBoxItemBackgroundPointerOver", "TabItemHeaderBackgroundUnselectedPointerOver");
        Set(resources, pressed, "ButtonBackgroundPressed", "ToggleButtonBackgroundPressed",
            "RepeatButtonBackgroundPressed", "ComboBoxBackgroundPressed", "MenuFlyoutItemBackgroundPressed",
            "SystemControlHighlightListMediumBrush", "TreeViewItemBackgroundPressed",
            "ComboBoxItemBackgroundPressed", "TabItemHeaderBackgroundUnselectedPressed");
        Set(resources, border, "ButtonBorderBrush", "ButtonBorderBrushPointerOver", "ButtonBorderBrushPressed",
            "ToggleButtonBorderBrush", "ToggleButtonBorderBrushPointerOver", "RepeatButtonBorderBrush",
            "TextControlBorderBrush", "TextControlBorderBrushPointerOver", "ComboBoxBorderBrush",
            "ComboBoxBorderBrushPointerOver", "ComboBoxDropDownBorderBrush", "MenuFlyoutPresenterBorderBrush",
            "FlyoutBorderThemeBrush", "ToolTipBorderBrush", "ButtonBorderBrushDisabled");
        Set(resources, background, "TextControlBackgroundFocused");
        Set(resources, accentBrush, "TextControlBorderBrushFocused", "ComboBoxBorderBrushFocused",
            "TabItemHeaderSelectedPipeFill", "AccentButtonBackground", "ToggleButtonBackgroundChecked",
            "CheckBoxCheckBackgroundFillChecked", "CheckBoxCheckBackgroundStrokeChecked",
            "SystemControlHighlightAccentBrush", "ProgressBarForeground", "SliderTrackValueFill");
        Set(resources, accentHover, "AccentButtonBackgroundPointerOver", "ToggleButtonBackgroundCheckedPointerOver",
            "CheckBoxCheckBackgroundFillCheckedPointerOver", "CheckBoxCheckBackgroundStrokeCheckedPointerOver");
        Set(resources, accentPressed, "AccentButtonBackgroundPressed", "ToggleButtonBackgroundCheckedPressed",
            "CheckBoxCheckBackgroundFillCheckedPressed");
        Set(resources, accentText, "AccentButtonForeground", "AccentButtonForegroundPointerOver",
            "AccentButtonForegroundPressed", "ToggleButtonForegroundChecked",
            "ToggleButtonForegroundCheckedPointerOver", "CheckBoxCheckGlyphForegroundChecked");
        Set(resources, selection, "SystemControlHighlightListAccentLowBrush", "TreeViewItemBackgroundSelected",
            "ComboBoxItemBackgroundSelected");
        Set(resources, selectionHover, "SystemControlHighlightListAccentMediumBrush",
            "SystemControlHighlightListAccentHighBrush", "TreeViewItemBackgroundSelectedPointerOver",
            "TreeViewItemBackgroundSelectedPressed", "ComboBoxItemBackgroundSelectedPointerOver");
        Set(resources, text, "TabItemHeaderForegroundSelected", "TabItemHeaderForegroundSelectedPointerOver",
            "ToolTipForeground");
        Set(resources, muted, "TabItemHeaderForegroundUnselected", "TextControlPlaceholderForeground",
            "TextControlPlaceholderForegroundPointerOver", "TextControlPlaceholderForegroundFocused");
    }

    private static IBrush Brush(Theme theme, string key)
    {
        return new SolidColorBrush(Color.Parse(theme.Colour(key)));
    }

    private static void Set(IResourceDictionary resources, IBrush brush, params string[] keys)
    {
        foreach (string key in keys)
            resources[key] = brush;
    }

    // amount 0 keeps the colour, 1 gives the other one
    private static Color Mix(Color colour, Color other, double amount)
    {
        return Color.FromArgb(colour.A, Channel(colour.R, other.R, amount), Channel(colour.G, other.G, amount),
                              Channel(colour.B, other.B, amount));
    }

    private static byte Channel(byte from, byte to, double amount)
    {
        return (byte)Math.Round(from + (to - from) * amount);
    }
}
