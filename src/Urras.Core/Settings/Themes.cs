using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Urras.Core.Settings;

// One colour a theme sets: its key in theme files, what it colours, and its Dark and Light defaults
public sealed record ThemeColour(string Key, string Description, string Dark, string Light);

// Every colour a theme can set; a new build adds keys here and theme files get them on the next start
public static class ThemeColours
{
    public const string BACKGROUND = "Background";
    public const string SURFACE = "Surface";
    public const string RAISED = "Raised";
    public const string HOVER = "Hover";
    public const string PRESSED = "Pressed";
    public const string BORDER = "Border";
    public const string ACCENT_TEXT = "AccentText";
    public const string TEXT = "Text";
    public const string MUTED_TEXT = "MutedText";
    public const string ERROR = "Error";
    public const string ATTENTION = "Attention";
    public const string WARNING = "Warning";
    public const string ADDED = "Added";
    public const string REMOVED = "Removed";
    public const string ACCENT = "Accent";
    public const string INFO = "Info";
    public const string DIVIDER = "Divider";
    public const string BADGE = "Badge";
    public const string TREE_GUIDES = "TreeGuides";
    public const string DIFF_ADDED_TEXT = "DiffAddedText";
    public const string DIFF_REMOVED_TEXT = "DiffRemovedText";
    public const string DIFF_HUNK_TEXT = "DiffHunkText";
    public const string DIFF_WHITESPACE = "DiffWhitespace";
    public const string DIFF_ADDED_BACKGROUND = "DiffAddedBackground";
    public const string DIFF_REMOVED_BACKGROUND = "DiffRemovedBackground";
    public const string DIFF_ADDED_WORD = "DiffAddedWord";
    public const string DIFF_REMOVED_WORD = "DiffRemovedWord";
    public const string DIFF_TRAILING_WHITESPACE = "DiffTrailingWhitespace";
    public const string DIFF_HUNK_BACKGROUND = "DiffHunkBackground";
    public const string DIFF_FILE_BACKGROUND = "DiffFileBackground";
    public const string DIFF_EMPTY_SIDE = "DiffEmptySide";
    public const string SYNTAX_KEYWORD = "SyntaxKeyword";
    public const string SYNTAX_TYPE = "SyntaxType";
    public const string SYNTAX_STRING = "SyntaxString";
    public const string SYNTAX_NUMBER = "SyntaxNumber";
    public const string SYNTAX_COMMENT = "SyntaxComment";
    public const string SYNTAX_PREPROCESSOR = "SyntaxPreprocessor";
    public const string SYNTAX_ATTRIBUTE = "SyntaxAttribute";
    public const string SYNTAX_EMPHASIS = "SyntaxEmphasis";

    public static readonly IReadOnlyList<ThemeColour> All = new List<ThemeColour>
    {
        new ThemeColour(BACKGROUND, "Window, title bar and repository list", "#14161B", "#F2F3F5"),
        new ThemeColour(SURFACE, "The panel with changes, history and diffs", "#1B1E24", "#FFFFFF"),
        new ThemeColour(RAISED, "Buttons, boxes, menus and tooltips", "#252930", "#FFFFFF"),
        new ThemeColour(HOVER, "Under the pointer", "#2E333B", "#ECEEF1"),
        new ThemeColour(PRESSED, "Pressed buttons", "#383E47", "#E1E4E8"),
        new ThemeColour(BORDER, "Edges of buttons, boxes and panels", "#30353D", "#D6DAE0"),
        new ThemeColour(ACCENT_TEXT, "Text on accent-coloured buttons", "#FFFFFF", "#FFFFFF"),
        new ThemeColour(TEXT, "Text", "#E6EDF3", "#1F2328"),
        new ThemeColour(MUTED_TEXT, "Dimmed text: hints, dates, details", "#8B949E", "#59636E"),
        new ThemeColour(ERROR, "Errors and missing repositories", "#FF7B72", "#CF222E"),
        new ThemeColour(ATTENTION, "Commits to push or pull, changes waiting", "#79C0FF", "#0969DA"),
        new ThemeColour(WARNING, "Warnings, local-only branches, conflicts", "#E3B341", "#9A6700"),
        new ThemeColour(ADDED, "Added, in statistics", "#57AB5A", "#1A7F37"),
        new ThemeColour(REMOVED, "Removed, in statistics", "#E5534B", "#CF222E"),
        new ThemeColour(ACCENT, "Accent: selection, main buttons, checkboxes, bisect banner", "#8B7CF6", "#6E56CF"),
        new ThemeColour(INFO, "Bars in statistics", "#539BF5", "#0969DA"),
        new ThemeColour(DIVIDER, "Lines between areas", "#33FFFFFF", "#33000000"),
        new ThemeColour(BADGE, "Count badges", "#33FFFFFF", "#1A000000"),
        new ThemeColour(TREE_GUIDES, "The | - - guides in the repository list", "#A5ADB8", "#6E7781"),
        new ThemeColour(DIFF_ADDED_TEXT, "Diff: added line's marker", "#7EE787", "#1A7F37"),
        new ThemeColour(DIFF_REMOVED_TEXT, "Diff: removed line's marker", "#FF7B72", "#CF222E"),
        new ThemeColour(DIFF_HUNK_TEXT, "Diff: @@ hunk headers", "#79C0FF", "#0969DA"),
        new ThemeColour(DIFF_WHITESPACE, "Diff: shown spaces and tabs", "#6E7681", "#8C959F"),
        new ThemeColour(DIFF_ADDED_BACKGROUND, "Diff: added line", "#262EA043", "#DAFBE1"),
        new ThemeColour(DIFF_REMOVED_BACKGROUND, "Diff: removed line", "#26F85149", "#FFEBE9"),
        new ThemeColour(DIFF_ADDED_WORD, "Diff: added words", "#662EA043", "#ACEEBB"),
        new ThemeColour(DIFF_REMOVED_WORD, "Diff: removed words", "#66F85149", "#FFCECB"),
        new ThemeColour(DIFF_TRAILING_WHITESPACE, "Diff: spaces at the end of a line", "#59F85149", "#FF8182"),
        new ThemeColour(DIFF_HUNK_BACKGROUND, "Diff: hunk header line", "#1A388BFD", "#DDF4FF"),
        new ThemeColour(DIFF_FILE_BACKGROUND, "Diff: file header line", "#1FFFFFFF", "#F6F8FA"),
        new ThemeColour(DIFF_EMPTY_SIDE, "Diff: empty side in split view", "#0AFFFFFF", "#F6F8FA"),
        new ThemeColour(SYNTAX_KEYWORD, "Code: keywords, tags, headings", "#569CD6", "#CF222E"),
        new ThemeColour(SYNTAX_TYPE, "Code: types", "#4EC9B0", "#953800"),
        new ThemeColour(SYNTAX_STRING, "Code: strings and inline code", "#CE9178", "#0A3069"),
        new ThemeColour(SYNTAX_NUMBER, "Code: numbers", "#B5CEA8", "#0550AE"),
        new ThemeColour(SYNTAX_COMMENT, "Code: comments", "#7CA668", "#6E7781"),
        new ThemeColour(SYNTAX_PREPROCESSOR, "Code: preprocessor lines", "#C586C0", "#8250DF"),
        new ThemeColour(SYNTAX_ATTRIBUTE, "Code: attributes", "#9CDCFE", "#0550AE"),
        new ThemeColour(SYNTAX_EMPHASIS, "Code: emphasis in Markdown", "#DCDCAA", "#953800")
    };

    // #RGB, #RRGGBB or #AARRGGBB
    public static bool IsColour(string? value)
    {
        if (value is not { Length: > 1 } || value[0] != '#')
            return false;
        string digits = value[1..];
        return digits.Length is 3 or 6 or 8 && digits.All(Uri.IsHexDigit);
    }

    // The same colour at another opacity, such as a faint banner background from its border colour
    public static string WithAlpha(string colour, byte alpha)
    {
        string digits = colour.TrimStart('#');
        if (digits.Length == 3)
            digits = string.Concat(digits.Select(digit => new string(digit, 2)));
        if (digits.Length == 8)
            digits = digits[2..];
        return "#" + alpha.ToString("X2", CultureInfo.InvariantCulture) + digits.ToUpperInvariant();
    }
}

public enum ThemeBase
{
    Dark,
    Light
}

// A set of colours on top of the dark or light base; built-in ones are Dark and Light, others are files
public sealed class Theme
{
    public const string DARK = "Dark";
    public const string LIGHT = "Light";

    // Bright pink for a key no build knows, so it stands out instead of hiding
    private const string UNKNOWN_COLOUR = "#FF00FF";

    private static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions { WriteIndented = true };

    public string Name { get; set; } = DARK;

    [JsonConverter(typeof(JsonStringEnumConverter<ThemeBase>))]
    public ThemeBase Base { get; set; } = ThemeBase.Dark;

    public Dictionary<string, string> Colours { get; set; } = new Dictionary<string, string>();

    [JsonIgnore]
    // Dark, Light and the ready-made themes, which aren't files
    public bool IsBuiltIn => Name is DARK or LIGHT || ThemePresets.IsPreset(Name);

    public static Theme BuiltIn(ThemeBase themeBase)
    {
        Theme theme = new Theme { Name = themeBase == ThemeBase.Light ? LIGHT : DARK, Base = themeBase };
        theme.FillMissing();
        return theme;
    }

    // A colour by key; a missing or broken one falls back to the base's default
    public string Colour(string key)
    {
        if (Colours.TryGetValue(key, out string? value) && ThemeColours.IsColour(value))
            return value;
        ThemeColour? known = ThemeColours.All.FirstOrDefault(colour => colour.Key == key);
        return known == null ? UNKNOWN_COLOUR : Base == ThemeBase.Light ? known.Light : known.Dark;
    }

    // Keys this build knows that the theme doesn't have yet get the base's default; true when any were added
    public bool FillMissing()
    {
        bool added = false;
        foreach (ThemeColour colour in ThemeColours.All)
        {
            if (Colours.TryGetValue(colour.Key, out string? value) && ThemeColours.IsColour(value))
                continue;
            Colours[colour.Key] = Base == ThemeBase.Light ? colour.Light : colour.Dark;
            added = true;
        }

        return added;
    }

    public Theme Copy(string name)
    {
        return new Theme { Name = name, Base = Base, Colours = new Dictionary<string, string>(Colours) };
    }

    public string ToJson()
    {
        return JsonSerializer.Serialize(this, JsonOptions);
    }

    public static Theme? FromJson(string json)
    {
        try
        {
            return JsonSerializer.Deserialize<Theme>(json, JsonOptions);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}

// Theme files in the themes folder beside the settings, one .json each
public sealed class ThemeStore
{
    public const string FOLDER_NAME = "themes";
    private const string EXTENSION = ".json";
    private const string UNSAFE_CHARACTERS = "\\/:*?\"<>|";

    public ThemeStore(string folder)
    {
        Folder = folder;
    }

    public string Folder { get; }

    public static ThemeStore Default => new ThemeStore(Path.Combine(
        Path.GetDirectoryName(AppSettings.DefaultPath) ?? ".", FOLDER_NAME));

    // The custom themes, by name; files that can't be read are left out
    public List<Theme> LoadAll()
    {
        if (!Directory.Exists(Folder))
            return new List<Theme>();

        List<Theme> themes = new List<Theme>();
        foreach (string file in Directory.EnumerateFiles(Folder, "*" + EXTENSION).Order(StringComparer.Ordinal))
        {
            if (Load(file) is { } theme)
                themes.Add(theme);
        }

        return themes;
    }

    // Reads a theme and, if this build knows colours it lacks, writes them into the file
    public Theme? Load(string file)
    {
        try
        {
            Theme? theme = Theme.FromJson(File.ReadAllText(file));
            if (theme == null)
                return null;
            if (string.IsNullOrWhiteSpace(theme.Name) || theme.IsBuiltIn)
                theme.Name = Path.GetFileNameWithoutExtension(file);
            if (theme.FillMissing())
                File.WriteAllText(file, theme.ToJson());
            return theme;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    public Theme? Find(string name)
    {
        return LoadAll().FirstOrDefault(theme => string.Equals(theme.Name, name, StringComparison.OrdinalIgnoreCase));
    }

    public string FileFor(string name)
    {
        // Characters Windows refuses in file names, on every system, so theme files can be shared
        string safe = string.Concat(name.Select(character => UNSAFE_CHARACTERS.Contains(character) ||
                                                             char.IsControl(character) ? '-' : character));
        return Path.Combine(Folder, safe + EXTENSION);
    }

    public void Save(Theme theme)
    {
        Directory.CreateDirectory(Folder);
        File.WriteAllText(FileFor(theme.Name), theme.ToJson());
    }
}
