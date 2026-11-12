namespace Urras.Core.Settings;

// Ready-made themes, the same as Anarres's; only what differs from the dark or light base is listed
public static class ThemePresets
{
    public const string ANARRES = "Anarres";
    public const string FROST = "Frost";
    public const string FEL = "Fel";
    public const string HORDE = "Horde";
    public const string ALLIANCE = "Alliance";
    public const string PARCHMENT = "Parchment";
    public const string GITHUB_DARK = "GitHub Dark";
    public const string GITHUB_DIMMED = "GitHub Dark Dimmed";
    public const string GITHUB_LIGHT = "GitHub Light";
    public const string GITLAB_DARK = "GitLab Dark";
    public const string SOURCEGIT_DARK = "SourceGit Dark";
    public const string FORK_DARK = "Fork Dark";

    private sealed record Preset(string Name, ThemeBase Base, Dictionary<string, string> Colours);

    private static readonly Preset[] Presets =
    {
        // Anarres's own accent, for both apps to look alike
        Dark(ANARRES, "#14161B", "#1B1E24", "#252930", "#2E333B", "#383E47", "#30353D", "#A27BFF", "#E6EDF3",
             "#8B949E"),
        Dark(FROST, "#0F1620", "#152030", "#1D2B3E", "#253750", "#2E4360", "#26384F", "#5AB7FF", "#E3F1FF",
             "#88A2BD"),
        Dark(FEL, "#0F1712", "#151F18", "#1D2B21", "#26382B", "#2F4535", "#27392C", "#7CDB3A", "#E6F5E1",
             "#8FA996"),
        Dark(HORDE, "#1A1212", "#221717", "#2E1F1F", "#3A2828", "#463131", "#3B2A2A", "#E5484D", "#F5E6E6",
             "#B09595"),
        Dark(ALLIANCE, "#10141F", "#161C2B", "#1F273A", "#28324A", "#323D5A", "#2A3550", "#4C8DFF", "#E8EEF8",
             "#909CB4"),
        new Preset(PARCHMENT, ThemeBase.Light, Colours("#EFE6D2", "#FAF5E8", "#FFFBF0", "#EBE0C8", "#E0D3B6",
                                                       "#D6C7A4", "#8C4A1E", "#2B2216", "#6E604A")),
        // The same ready-made themes as Anarres, in the colours of other Git apps
        Dark(GITHUB_DARK, "#010409", "#0D1117", "#21262D", "#30363D", "#282E33", "#30363D", "#1F6FEB", "#E6EDF3",
             "#7D8590"),
        Dark(GITHUB_DIMMED, "#1C2128", "#22272E", "#2D333B", "#373E47", "#444C56", "#444C56", "#316DCA", "#ADBAC7",
             "#768390"),
        new Preset(GITHUB_LIGHT, ThemeBase.Light, Colours("#F6F8FA", "#FFFFFF", "#F6F8FA", "#EAEEF2", "#DFE3E8",
                                                          "#D0D7DE", "#0969DA", "#1F2328", "#656D76")),
        Dark(GITLAB_DARK, "#18171D", "#1F1E24", "#28272D", "#333238", "#434248", "#3A383F", "#7759C2", "#ECECEF",
             "#A4A3A8"),
        Dark(SOURCEGIT_DARK, "#1F1F1F", "#252525", "#303030", "#3A3A3A", "#444444", "#404040", "#4295FF", "#DDDDDD",
             "#9A9A9A"),
        Dark(FORK_DARK, "#1E2128", "#262A31", "#30353D", "#3A3F48", "#444A54", "#3A3F48", "#3D8AF7", "#D8DEE9",
             "#8C95A3")
    };

    public static IReadOnlyList<string> Names => Presets.Select(preset => preset.Name).ToList();

    public static bool IsPreset(string? name)
    {
        return Presets.Any(preset => string.Equals(preset.Name, name, StringComparison.OrdinalIgnoreCase));
    }

    // A fresh copy each time, with every colour filled in, so changing it never changes the preset
    public static Theme? Find(string? name)
    {
        Preset? preset = Presets.FirstOrDefault(candidate => string.Equals(candidate.Name, name,
                                                                           StringComparison.OrdinalIgnoreCase));
        if (preset == null)
            return null;

        Theme theme = new Theme
        {
            Name = preset.Name,
            Base = preset.Base,
            Colours = new Dictionary<string, string>(preset.Colours)
        };
        theme.FillMissing();
        return theme;
    }

    private static Preset Dark(string name, string background, string surface, string raised, string hover,
                               string pressed, string border, string accent, string text, string muted)
    {
        return new Preset(name, ThemeBase.Dark, Colours(background, surface, raised, hover, pressed, border, accent,
                                                        text, muted));
    }

    private static Dictionary<string, string> Colours(string background, string surface, string raised, string hover,
                                                      string pressed, string border, string accent, string text,
                                                      string muted)
    {
        return new Dictionary<string, string>
        {
            [ThemeColours.BACKGROUND] = background,
            [ThemeColours.SURFACE] = surface,
            [ThemeColours.RAISED] = raised,
            [ThemeColours.HOVER] = hover,
            [ThemeColours.PRESSED] = pressed,
            [ThemeColours.BORDER] = border,
            [ThemeColours.ACCENT] = accent,
            [ThemeColours.TEXT] = text,
            [ThemeColours.MUTED_TEXT] = muted
        };
    }
}
