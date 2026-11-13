namespace Urras.Core.Syntax;

// What a run of characters is, for colouring code
public enum SyntaxKind : byte
{
    Plain,
    Keyword,
    Type,
    String,
    Number,
    Comment,
    Preprocessor,
    Tag,
    Attribute,
    Heading,
    Emphasis,
    Code,
    // Function and method names, which VS Code's grammars find (Urras's own rules don't)
    Function
}

public enum SyntaxFamily
{
    // Keywords, strings, numbers and comments: C#, C++, JSON, CMake, Python and most others
    Code,
    Markup,
    Markdown
}

// A language the built-in highlighter understands, described as data so adding one is a table entry
public sealed class SyntaxLanguage
{
    private SyntaxLanguage(string name, SyntaxFamily family = SyntaxFamily.Code)
    {
        Name = name;
        Family = family;
    }

    public string Name { get; }
    public SyntaxFamily Family { get; }
    public IReadOnlySet<string> Keywords { get; private init; } = new HashSet<string>();
    public IReadOnlySet<string> Types { get; private init; } = new HashSet<string>();
    public string[] LineComments { get; private init; } = Array.Empty<string>();
    public (string Start, string End)? BlockComment { get; private init; }
    public string Quotes { get; private init; } = "\"'";

    // Strings that may run over several lines, such as Python's """ and C#'s raw """
    public string[] LongStrings { get; private init; } = Array.Empty<string>();

    // A # at the start of a line begins a preprocessor directive (C, C++, C#)
    public bool Preprocessor { get; private init; }

    // Capitalised names are types (C#, Java-like naming)
    public bool CapitalizedTypes { get; private init; }

    // A name followed by ( is a command (CMake)
    public bool CallsAreKeywords { get; private init; }

    // A word or string followed by one of these is a key: ":" for JSON and YAML, "=" for INI and TOML
    public string KeySeparators { get; private init; } = "";

    // $NAME and ${NAME} are variables (shell, CMake)
    public bool DollarVariables { get; private init; }

    public bool IgnoreCase { get; private init; }

    public static SyntaxLanguage? ForPath(string path)
    {
        string name = Path.GetFileName(path.Replace('\\', '/').TrimEnd('/'));
        if (name.Equals("CMakeLists.txt", StringComparison.OrdinalIgnoreCase))
            return CMake;
        if (name is "Dockerfile" or "Makefile" or ".gitignore" or ".gitattributes" or ".editorconfig")
            return name == "Dockerfile" ? Shell : Config;

        return Path.GetExtension(name).ToLowerInvariant() switch
        {
            ".cs" or ".csx" => CSharp,
            ".c" or ".h" or ".cpp" or ".hpp" or ".cc" or ".cxx" or ".hh" or ".hxx" or ".inl" or ".ipp" => Cpp,
            ".json" or ".jsonc" or ".json5" => Json,
            ".xml" or ".xaml" or ".axaml" or ".csproj" or ".vcxproj" or ".props" or ".targets" or ".svg" or ".resx"
                or ".html" or ".htm" or ".config" or ".nuspec" or ".slnx" or ".manifest" => Xml,
            ".cmake" => CMake,
            ".md" or ".markdown" => Markdown,
            ".js" or ".mjs" or ".cjs" or ".ts" or ".tsx" or ".jsx" => JavaScript,
            ".py" => Python,
            ".sh" or ".bash" or ".zsh" => Shell,
            ".ps1" or ".psm1" => PowerShell,
            ".bat" or ".cmd" => Batch,
            ".yml" or ".yaml" => Yaml,
            ".toml" or ".ini" or ".cfg" => Config,
            ".lua" => Lua,
            ".sql" => Sql,
            ".glsl" or ".vert" or ".frag" or ".hlsl" or ".shader" => Cpp,
            _ => null
        };
    }

    private static HashSet<string> Words(string words, bool ignoreCase = false)
    {
        return new HashSet<string>(words.Split(' ', StringSplitOptions.RemoveEmptyEntries),
                                   ignoreCase ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
    }

    public static readonly SyntaxLanguage CSharp = new SyntaxLanguage("C#")
    {
        Keywords = Words("abstract as async await base break case catch checked class const continue default " +
                         "delegate do else enum event explicit extern false finally fixed for foreach get goto if " +
                         "implicit in init interface internal is lock namespace new null operator out override " +
                         "params partial private protected public readonly record ref required return sealed set " +
                         "sizeof stackalloc static struct switch this throw true try typeof unchecked unsafe using " +
                         "value var virtual void volatile when where while with yield and or not file scoped"),
        Types = Words("bool byte char decimal double dynamic float int long nint nuint object sbyte short string " +
                      "uint ulong ushort"),
        LineComments = new[] { "//" },
        BlockComment = ("/*", "*/"),
        LongStrings = new[] { "\"\"\"" },
        Preprocessor = true,
        CapitalizedTypes = true
    };

    public static readonly SyntaxLanguage Cpp = new SyntaxLanguage("C++")
    {
        Keywords = Words("alignas alignof and asm auto break case catch class co_await co_return co_yield concept " +
                         "const consteval constexpr constinit const_cast continue decltype default delete do " +
                         "dynamic_cast else enum explicit export extern false final for friend goto if inline " +
                         "mutable namespace new noexcept not nullptr operator or override private protected " +
                         "public register reinterpret_cast requires return sizeof static static_assert " +
                         "static_cast struct switch template this thread_local throw true try typedef typeid " +
                         "typename union using virtual volatile while"),
        Types = Words("bool char char8_t char16_t char32_t double float int long short signed unsigned void " +
                      "wchar_t size_t int8_t int16_t int32_t int64_t uint8_t uint16_t uint32_t uint64_t " +
                      "std string vector map unordered_map array optional unique_ptr shared_ptr"),
        LineComments = new[] { "//" },
        BlockComment = ("/*", "*/"),
        Preprocessor = true,
        CapitalizedTypes = true
    };

    public static readonly SyntaxLanguage Json = new SyntaxLanguage("JSON")
    {
        Keywords = Words("true false null"),
        LineComments = new[] { "//" },
        BlockComment = ("/*", "*/"),
        Quotes = "\"",
        KeySeparators = ":"
    };

    public static readonly SyntaxLanguage CMake = new SyntaxLanguage("CMake")
    {
        Keywords = Words("if elseif else endif foreach endforeach while endwhile function endfunction macro " +
                         "endmacro return break continue AND OR NOT TRUE FALSE ON OFF PUBLIC PRIVATE INTERFACE " +
                         "REQUIRED STATUS FATAL_ERROR WARNING CACHE STRING BOOL PATH FORCE", ignoreCase: false),
        LineComments = new[] { "#" },
        BlockComment = ("#[[", "]]"),
        Quotes = "\"",
        CallsAreKeywords = true,
        DollarVariables = true,
        IgnoreCase = false
    };

    public static readonly SyntaxLanguage JavaScript = new SyntaxLanguage("JavaScript")
    {
        Keywords = Words("async await break case catch class const continue debugger default delete do else " +
                         "export extends false finally for from function if import in instanceof let new null of " +
                         "return static super switch this throw true try typeof undefined var void while with " +
                         "yield interface type enum implements private public protected readonly as"),
        Types = Words("number string boolean any unknown never object"),
        LineComments = new[] { "//" },
        BlockComment = ("/*", "*/"),
        Quotes = "\"'",
        LongStrings = new[] { "`" },
        CapitalizedTypes = true
    };

    public static readonly SyntaxLanguage Python = new SyntaxLanguage("Python")
    {
        Keywords = Words("and as assert async await break class continue def del elif else except False finally " +
                         "for from global if import in is lambda None nonlocal not or pass raise return True try " +
                         "while with yield match case self"),
        Types = Words("int float str bool list dict set tuple bytes object"),
        LineComments = new[] { "#" },
        LongStrings = new[] { "\"\"\"", "'''" },
        CapitalizedTypes = true
    };

    public static readonly SyntaxLanguage Shell = new SyntaxLanguage("Shell")
    {
        Keywords = Words("if then else elif fi for in do done while until case esac function return local " +
                         "export set echo exit FROM RUN COPY ADD WORKDIR ENV ARG CMD ENTRYPOINT EXPOSE"),
        LineComments = new[] { "#" },
        DollarVariables = true
    };

    public static readonly SyntaxLanguage PowerShell = new SyntaxLanguage("PowerShell")
    {
        Keywords = Words("begin break catch class continue data do dynamicparam else elseif end exit filter " +
                         "finally for foreach function if in param process return switch throw trap try until " +
                         "while", ignoreCase: true),
        LineComments = new[] { "#" },
        BlockComment = ("<#", "#>"),
        DollarVariables = true,
        IgnoreCase = true
    };

    // Standard SQL with MySQL's and MariaDB's extras (# comments, backtick names), as AzerothCore uses
    public static readonly SyntaxLanguage Sql = new SyntaxLanguage("SQL")
    {
        Keywords = Words("add all alter and as asc auto_increment begin between by cascade case change character " +
                         "charset check collate column commit constraint create cross database default delete " +
                         "desc distinct drop else end engine exists foreign from full group having if ignore in " +
                         "index inner insert into is join key left like limit modify not null on or order outer " +
                         "primary procedure references rename replace right rollback select set table temporary " +
                         "then transaction trigger truncate union unique unsigned update use using values view when " +
                         "where with true false comment row_format", ignoreCase: true),
        Types = Words("tinyint smallint mediumint int integer bigint float double decimal numeric bit bool boolean " +
                      "char varchar binary varbinary tinytext text mediumtext longtext tinyblob blob mediumblob " +
                      "longblob date time datetime timestamp year enum json", ignoreCase: true),
        LineComments = new[] { "--", "#" },
        BlockComment = ("/*", "*/"),
        Quotes = "'\"`",
        IgnoreCase = true
    };

    public static readonly SyntaxLanguage Batch = new SyntaxLanguage("Batch")
    {
        Keywords = Words("echo set if else goto call exit for in do not exist defined errorlevel setlocal " +
                         "endlocal pause cd pushd popd start", ignoreCase: true),
        LineComments = new[] { "::", "rem ", "REM ", "@rem ", "@REM " },
        Quotes = "\"",
        IgnoreCase = true
    };

    public static readonly SyntaxLanguage Yaml = new SyntaxLanguage("YAML")
    {
        Keywords = Words("true false null yes no on off"),
        LineComments = new[] { "#" },
        KeySeparators = ":"
    };

    public static readonly SyntaxLanguage Config = new SyntaxLanguage("Config")
    {
        Keywords = Words("true false"),
        LineComments = new[] { "#", ";" },
        KeySeparators = "=:"
    };

    public static readonly SyntaxLanguage Lua = new SyntaxLanguage("Lua")
    {
        Keywords = Words("and break do else elseif end false for function goto if in local nil not or repeat " +
                         "return then true until while"),
        LineComments = new[] { "--" },
        BlockComment = ("--[[", "]]")
    };

    public static readonly SyntaxLanguage Xml = new SyntaxLanguage("XML", SyntaxFamily.Markup)
    {
        BlockComment = ("<!--", "-->")
    };

    public static readonly SyntaxLanguage Markdown = new SyntaxLanguage("Markdown", SyntaxFamily.Markdown)
    {
        BlockComment = ("<!--", "-->")
    };
}
