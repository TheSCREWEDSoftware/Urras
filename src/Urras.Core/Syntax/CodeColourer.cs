using TextMateSharp.Grammars;
using TextMateSharp.Registry;
using TextMateSharp.Themes;
using TmTheme = TextMateSharp.Themes.Theme;

namespace Urras.Core.Syntax;

// One character's colour: a kind the Urras theme colours, or a scheme's own 0xRRGGBB marked with SET
public readonly record struct SyntaxColour(SyntaxKind Kind, int Rgb = SyntaxColour.NONE)
{
    // A blank colour (as every array starts) leaves it to the theme, so a real colour carries this mark
    public const int NONE = 0;
    public const int SET = 0x1000000;
    private const int RGB_MASK = 0xFFFFFF;

    public bool HasRgb => (Rgb & SET) != 0;
    public byte Red => (byte)((Rgb & RGB_MASK) >> 16);
    public byte Green => (byte)((Rgb & RGB_MASK) >> 8);
    public byte Blue => (byte)(Rgb & RGB_MASK);
}

// What a file's colouring carries from one line to the next, such as an open comment
public sealed class CodeState
{
    internal SyntaxState Own;
    internal IStateStack? TextMate;
}

// Colours a file line by line with VS Code's grammar for it (via TextMateSharp), or else Urras's own rules
public sealed class CodeColourer
{
    // "Follow the theme": the grammar finds comments, strings and so on, and the Urras theme colours them
    public const string FOLLOW_THEME = "";
    // A line the grammar takes longer than this over is left partly plain rather than holding up the diff
    private static readonly TimeSpan LineTimeLimit = TimeSpan.FromMilliseconds(50);
    private const int RGB_DIGITS = 6;

    private static readonly object Gate = new object();
    private static readonly Dictionary<string, (Registry Registry, RegistryOptions Options)> Registries = new();
    private static readonly Dictionary<string, IGrammar?> Grammars = new Dictionary<string, IGrammar?>();
    // Set once TextMateSharp can't run here (its native part missing), so Urras's own rules take over
    private static bool _textMateFailed;

    private readonly IGrammar? _grammar;
    private readonly TmTheme? _theme;
    private readonly SyntaxLanguage? _own;

    private CodeColourer(IGrammar? grammar, TmTheme? theme, SyntaxLanguage? own)
    {
        _grammar = grammar;
        _theme = theme;
        _own = own;
    }

    // FOLLOW_THEME or one of Schemes; set from Options, or by the theme when Options follows it
    public static string Scheme { get; set; } = FOLLOW_THEME;

    // The colour schemes that come with TextMateSharp, by name: Dark Plus, Monokai, Solarized Dark and so on
    public static IReadOnlyList<string> Schemes { get; } = Enum.GetNames<ThemeName>();

    // "DarkPlus" as "Dark Plus", for menus
    public static string Describe(string scheme)
    {
        return string.Concat(scheme.Select((character, index) =>
            index > 0 && char.IsUpper(character) && !char.IsUpper(scheme[index - 1]) ? " " + character
                                                                                      : character.ToString()));
    }

    // The colourer for a file, or null when neither a grammar nor Urras's own rules know it
    public static CodeColourer? For(string path)
    {
        string scheme = Schemes.Contains(Scheme) ? Scheme : FOLLOW_THEME;
        (IGrammar? grammar, TmTheme? theme) = TextMateFor(path, scheme);
        SyntaxLanguage? own = SyntaxLanguage.ForPath(path);
        if (grammar == null && own == null)
            return null;
        // Urras's own rules stay ready for a line the grammar trips over
        return new CodeColourer(grammar, scheme == FOLLOW_THEME ? null : theme, own);
    }

    public SyntaxColour[] Colour(string line, CodeState state)
    {
        if (_grammar != null && line.Length <= SyntaxHighlighter.MAX_LINE_LENGTH)
        {
            SyntaxColour[]? colours = ColourWithGrammar(line, state);
            if (colours != null)
                return colours;
        }

        SyntaxColour[] plain = new SyntaxColour[line.Length];
        if (_own == null)
            return plain;
        SyntaxKind[] kinds = SyntaxHighlighter.Classify(line, _own, ref state.Own);
        for (int index = 0; index < kinds.Length; index++)
            plain[index] = new SyntaxColour(kinds[index]);
        return plain;
    }

    // Every line of a file in order, carrying the state along
    public List<SyntaxColour[]> ColourLines(IEnumerable<string> lines)
    {
        CodeState state = new CodeState();
        return lines.Select(line => Colour(line, state)).ToList();
    }

    private SyntaxColour[]? ColourWithGrammar(string line, CodeState state)
    {
        SyntaxColour[] colours = new SyntaxColour[line.Length];
        lock (Gate)
        {
            try
            {
                ITokenizeLineResult result = _grammar!.TokenizeLine(line, state.TextMate, LineTimeLimit);
                state.TextMate = result.RuleStack;
                foreach (IToken token in result.Tokens)
                {
                    SyntaxColour colour = new SyntaxColour(KindOf(token.Scopes), RgbOf(token.Scopes));
                    for (int index = token.StartIndex; index < Math.Min(token.EndIndex, line.Length); index++)
                        colours[index] = colour;
                }

                return colours;
            }
            catch (Exception exception) when (exception is not OutOfMemoryException)
            {
                // A grammar that trips over a line leaves it to Urras's own rules
                return null;
            }
        }
    }

    // The scheme's colour for the most specific scope it styles, or NONE when following the theme
    private int RgbOf(List<string> scopes)
    {
        if (_theme == null)
            return SyntaxColour.NONE;
        foreach (ThemeTrieElementRule rule in _theme.Match(scopes))
        {
            if (rule.foreground > 0 && ParseRgb(_theme.GetColor(rule.foreground)) is { } rgb)
                return rgb;
        }

        return SyntaxColour.NONE;
    }

    private static int? ParseRgb(string? colour)
    {
        if (colour is not { Length: > RGB_DIGITS } || colour[0] != '#')
            return null;
        return int.TryParse(colour.AsSpan(1, RGB_DIGITS), System.Globalization.NumberStyles.HexNumber, null,
                            out int rgb) ? rgb | SyntaxColour.SET : null;
    }

    // What VS Code's scope names mean in Urras's terms, from the most specific scope outwards
    public static SyntaxKind KindOf(IReadOnlyList<string> scopes)
    {
        for (int index = scopes.Count - 1; index >= 0; index--)
        {
            if (KindOfScope(scopes[index]) is { } kind)
                return kind;
        }

        return SyntaxKind.Plain;
    }

    private static SyntaxKind? KindOfScope(string scope)
    {
        bool Is(string prefix) => scope == prefix || scope.StartsWith(prefix + ".", StringComparison.Ordinal);

        if (Is("comment"))
            return SyntaxKind.Comment;
        if (Is("string") || Is("constant.character") || Is("markup.inline.raw.string"))
            return SyntaxKind.String;
        if (Is("constant.numeric"))
            return SyntaxKind.Number;
        if (Is("meta.preprocessor") || Is("keyword.preprocessor") || Is("keyword.control.directive"))
            return SyntaxKind.Preprocessor;
        if (Is("entity.name.tag"))
            return SyntaxKind.Tag;
        if (Is("entity.other.attribute-name") || Is("support.type.property-name"))
            return SyntaxKind.Attribute;
        if (Is("markup.heading") || Is("entity.name.section"))
            return SyntaxKind.Heading;
        if (Is("markup.bold") || Is("markup.italic"))
            return SyntaxKind.Emphasis;
        if (Is("markup.inline.raw") || Is("markup.fenced_code") || Is("markup.raw"))
            return SyntaxKind.Code;
        if (Is("entity.name.function") || Is("support.function") || Is("meta.function-call.name"))
            return SyntaxKind.Function;
        if (Is("keyword.type") || Is("storage.type") || Is("entity.name.type") || Is("entity.name.class") ||
            Is("support.type") || Is("support.class"))
            return SyntaxKind.Type;
        if (Is("keyword.operator") || Is("punctuation"))
            return SyntaxKind.Plain;
        if (Is("keyword") || Is("storage") || Is("constant.language") || Is("variable.language"))
            return SyntaxKind.Keyword;
        return null;
    }

    // The grammar and colour scheme for a file, or nulls when no grammar knows it or TextMateSharp can't run
    private static (IGrammar? Grammar, TmTheme? Theme) TextMateFor(string path, string scheme)
    {
        string extension = Path.GetExtension(path);
        if (extension.Length == 0)
            return (null, null);

        lock (Gate)
        {
            if (_textMateFailed)
                return (null, null);
            try
            {
                // The grammars are the same for every scheme, so following the theme reads them with Dark Plus
                string name = scheme == FOLLOW_THEME ? nameof(ThemeName.DarkPlus) : scheme;
                if (!Registries.TryGetValue(name, out (Registry Registry, RegistryOptions Options) loaded))
                {
                    RegistryOptions created = new RegistryOptions(Enum.Parse<ThemeName>(name));
                    loaded = (new Registry(created), created);
                    Registries[name] = loaded;
                }

                (Registry registry, RegistryOptions options) = loaded;
                string key = name + "|" + extension.ToLowerInvariant();
                if (!Grammars.TryGetValue(key, out IGrammar? grammar))
                {
                    string? scope = options.GetScopeByExtension(extension.ToLowerInvariant());
                    grammar = scope == null ? null : registry.LoadGrammar(scope);
                    Grammars[key] = grammar;
                }

                return (grammar, registry.GetTheme());
            }
            catch (Exception exception) when (exception is DllNotFoundException or TypeInitializationException
                                                  or BadImageFormatException or EntryPointNotFoundException)
            {
                _textMateFailed = true;
                return (null, null);
            }
            catch (Exception exception) when (exception is not OutOfMemoryException)
            {
                // One grammar that won't load leaves its files to Urras's own rules; the others still work
                return (null, null);
            }
        }
    }
}
