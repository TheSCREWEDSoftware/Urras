using Avalonia.Controls;
using Avalonia.Media;
using Urras.Core.Settings;

namespace Urras.App;

// The font and text size in use: the theme's own when it sets them, else the general ones from Options
public static class TextLook
{
    // Windows 11's own font, then Windows 10's, then a common open font elsewhere
    public const string DEFAULT_FONTS = "Segoe UI Variable Text, Segoe UI, Noto Sans";
    public const string FONT_RESOURCE = "ContentControlThemeFontFamily";
    public const string SIZE_RESOURCE = "ControlContentThemeFontSize";
    private const double FONT_LIST_HEIGHT = 400;

    public static void Apply(AppSettings settings, Theme theme, IResourceDictionary resources)
    {
        string? family = theme.FontFamily ?? settings.FontFamily;
        resources[FONT_RESOURCE] = new FontFamily(string.IsNullOrWhiteSpace(family) ? DEFAULT_FONTS : family);
        resources[SIZE_RESOURCE] = Size(theme.FontSize ?? settings.FontSize);
    }

    // A size from a hand-edited file stays within the sizes Options offers
    public static double Size(double size)
    {
        return Math.Clamp(size, AppSettings.FontSizes[0], AppSettings.FontSizes[^1]);
    }

    // The fonts installed on this computer, by name
    public static List<string> SystemFonts()
    {
        return FontManager.Current.SystemFonts.Select(font => font.Name).Distinct(StringComparer.OrdinalIgnoreCase)
                          .Order(StringComparer.OrdinalIgnoreCase).ToList();
    }

    // Installed fonts, each shown in itself, after a first entry meaning none chosen
    public static ComboBox FontPicker(string unchosen, string? chosen)
    {
        List<string> fonts = SystemFonts().Prepend(unchosen).ToList();
        int index = chosen == null ? 0 : fonts.FindIndex(name => string.Equals(name, chosen,
                                                                                StringComparison.OrdinalIgnoreCase));
        ComboBox picker = new ComboBox
        {
            ItemsSource = fonts,
            SelectedIndex = Math.Max(0, index),
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Stretch,
            MaxDropDownHeight = FONT_LIST_HEIGHT
        };
        picker.ItemTemplate = new Avalonia.Controls.Templates.FuncDataTemplate<string>((name, _) =>
            new TextBlock { Text = name, FontFamily = name == unchosen ? FontFamily.Default : new FontFamily(name) });
        return picker;
    }

    // The chosen font, or null for the first entry
    public static string? ChosenFont(ComboBox picker)
    {
        return picker.SelectedIndex > 0 ? picker.SelectedItem as string : null;
    }

    // Text sizes in points, with an optional first entry for "not chosen"
    public static ComboBox SizePicker(string? unchosen, double? chosen)
    {
        List<string> sizes = AppSettings.FontSizes.Select(size => size == AppSettings.DEFAULT_FONT_SIZE
                                                                      ? size + " pt (default)" : size + " pt")
                                      .ToList();
        int offset = unchosen == null ? 0 : 1;
        if (unchosen != null)
            sizes.Insert(0, unchosen);
        int index = chosen == null ? -1 : Array.IndexOf(AppSettings.FontSizes, Size(chosen.Value));
        return new ComboBox
        {
            ItemsSource = sizes,
            SelectedIndex = index < 0 ? (unchosen == null ? Array.IndexOf(AppSettings.FontSizes,
                                                                          AppSettings.DEFAULT_FONT_SIZE) : 0)
                                      : index + offset,
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Stretch
        };
    }

    // The chosen size, or null for the "not chosen" entry
    public static double? ChosenSize(ComboBox picker, bool hasUnchosen)
    {
        int index = picker.SelectedIndex - (hasUnchosen ? 1 : 0);
        return index < 0 ? null : AppSettings.FontSizes[Math.Min(index, AppSettings.FontSizes.Length - 1)];
    }
}
