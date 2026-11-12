using Avalonia.Controls;
using Avalonia.Media;
using Urras.Core.Settings;

namespace Urras.App;

// The font and text size in use: the chosen theme's own when Options sets them, else the ones for every theme
public static class TextLook
{
    // Windows 11's own font, then Windows 10's, then a common open font elsewhere
    public const string DEFAULT_FONTS = "Segoe UI Variable Text, Segoe UI, Noto Sans";
    public const string FONT_RESOURCE = "ContentControlThemeFontFamily";
    public const string SIZE_RESOURCE = "ControlContentThemeFontSize";
    private const double FONT_LIST_HEIGHT = 400;

    public static void Apply(AppSettings settings, IResourceDictionary resources)
    {
        string family = settings.FontFamilyFor(settings.ThemeKey);
        resources[FONT_RESOURCE] = new FontFamily(family.Length > 0 ? family : DEFAULT_FONTS);
        resources[SIZE_RESOURCE] = settings.FontSizeFor(settings.ThemeKey);
    }

    // The fonts installed on this computer, by name
    public static List<string> SystemFonts()
    {
        try
        {
            return FontManager.Current.SystemFonts.Select(font => font.Name).Where(name => name.Length > 0)
                              .Distinct(StringComparer.OrdinalIgnoreCase).Order(StringComparer.OrdinalIgnoreCase)
                              .ToList();
        }
        catch (InvalidOperationException)
        {
            return new List<string>();
        }
    }

    // Installed fonts, each shown in itself, after a first entry meaning none chosen
    public static ComboBox FontPicker(string unchosen, List<string> fonts)
    {
        ComboBox picker = new ComboBox
        {
            ItemsSource = fonts.Prepend(unchosen).ToList(),
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Stretch,
            MaxDropDownHeight = FONT_LIST_HEIGHT
        };
        picker.ItemTemplate = new Avalonia.Controls.Templates.FuncDataTemplate<string>((name, _) =>
            new TextBlock { Text = name, FontFamily = name == unchosen ? FontFamily.Default : new FontFamily(name) });
        return picker;
    }

    public static void ChooseFont(ComboBox picker, List<string> fonts, string? font)
    {
        picker.SelectedIndex = string.IsNullOrEmpty(font) ? 0 : Math.Max(0, fonts.FindIndex(name => string.Equals(
            name, font, StringComparison.OrdinalIgnoreCase)) + 1);
    }

    // The chosen font, or null for the first entry
    public static string? ChosenFont(ComboBox picker)
    {
        return picker.SelectedIndex > 0 ? picker.SelectedItem as string : null;
    }

    // Text sizes in points, with an optional first entry meaning none chosen
    public static ComboBox SizePicker(string? unchosen)
    {
        IEnumerable<string> sizes = AppSettings.FontSizes.Select(size => size + " pt");
        return new ComboBox
        {
            ItemsSource = (unchosen == null ? sizes : sizes.Prepend(unchosen)).ToList(),
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Stretch
        };
    }

    public static void ChooseSize(ComboBox picker, bool hasUnchosen, double? size)
    {
        int index = size == null ? -1 : Array.IndexOf(AppSettings.FontSizes, size.Value);
        int fallback = hasUnchosen ? -1 : Array.IndexOf(AppSettings.FontSizes, AppSettings.DEFAULT_FONT_SIZE);
        picker.SelectedIndex = (index < 0 ? fallback : index) + (hasUnchosen ? 1 : 0);
    }

    // The chosen size, or null for the "none chosen" entry
    public static double? ChosenSize(ComboBox picker, bool hasUnchosen)
    {
        int index = picker.SelectedIndex - (hasUnchosen ? 1 : 0);
        return index < 0 ? null : AppSettings.FontSizes[Math.Min(index, AppSettings.FontSizes.Length - 1)];
    }
}
