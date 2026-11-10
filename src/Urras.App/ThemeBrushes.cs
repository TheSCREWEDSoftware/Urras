using Avalonia.Controls;
using Avalonia.Media;
using Urras.Core.Settings;

namespace Urras.App;

// The current theme's colours as brushes for code; XAML reads the same ones as Urras.<Key> resources
public static class ThemeBrushes
{
    public const string RESOURCE_PREFIX = "Urras.";
    public const string FAINT_SUFFIX = "Faint";

    // Banners and highlights use their colour at this opacity as a background
    private const byte FAINT_ALPHA = 0x33;

    private static readonly string[] FaintKeys =
    {
        ThemeColours.WARNING, ThemeColours.ACCENT, ThemeColours.INFO, ThemeColours.ADDED
    };

    private static Dictionary<string, IBrush> _brushes = Build(Theme.BuiltIn(ThemeBase.Dark));

    public static Theme Current { get; private set; } = Theme.BuiltIn(ThemeBase.Dark);

    public static IBrush Text => Get(ThemeColours.TEXT);
    public static IBrush MutedText => Get(ThemeColours.MUTED_TEXT);
    public static IBrush Error => Get(ThemeColours.ERROR);
    public static IBrush Attention => Get(ThemeColours.ATTENTION);
    public static IBrush Warning => Get(ThemeColours.WARNING);
    public static IBrush Added => Get(ThemeColours.ADDED);
    public static IBrush Removed => Get(ThemeColours.REMOVED);
    public static IBrush Info => Get(ThemeColours.INFO);
    public static IBrush Divider => Get(ThemeColours.DIVIDER);
    public static IBrush TreeGuides => Get(ThemeColours.TREE_GUIDES);

    // Switches every colour; controls built from now on, and everything bound in XAML, use the new ones
    public static void Use(Theme theme, IResourceDictionary? resources)
    {
        Current = theme;
        _brushes = Build(theme);
        if (resources == null)
            return;

        foreach ((string key, IBrush brush) in _brushes)
            resources[RESOURCE_PREFIX + key] = brush;
        ControlLook.Apply(theme, resources);
    }

    public static IBrush Get(string key)
    {
        return _brushes.TryGetValue(key, out IBrush? brush) ? brush : Brushes.Magenta;
    }

    private static Dictionary<string, IBrush> Build(Theme theme)
    {
        Dictionary<string, IBrush> brushes = new Dictionary<string, IBrush>(StringComparer.Ordinal);
        foreach (ThemeColour colour in ThemeColours.All)
            brushes[colour.Key] = new SolidColorBrush(Color.Parse(theme.Colour(colour.Key)));
        foreach (string key in FaintKeys)
            brushes[key + FAINT_SUFFIX] = new SolidColorBrush(Color.Parse(ThemeColours.WithAlpha(theme.Colour(key),
                                                                                                  FAINT_ALPHA)));
        return brushes;
    }
}
