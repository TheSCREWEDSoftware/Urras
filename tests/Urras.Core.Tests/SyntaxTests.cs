using Urras.Core.Syntax;

namespace Urras.Core.Tests;

public static class SyntaxTests
{
    // Kind of the first character of `part` in `line`
    private static SyntaxKind KindOf(string line, string part, SyntaxLanguage language)
    {
        SyntaxState state = default;
        SyntaxKind[] kinds = SyntaxHighlighter.Classify(line, language, ref state);
        int index = line.IndexOf(part, StringComparison.Ordinal);
        Assert.True(index >= 0, "'" + part + "' is in the line");
        return kinds[index];
    }

    public static void PicksLanguagesByFileName()
    {
        Assert.Equal("C#", SyntaxLanguage.ForPath("src/App/Main.cs")?.Name, "C#");
        Assert.Equal("C++", SyntaxLanguage.ForPath("include\\EchoFlint\\Core.hpp")?.Name, "C++ header");
        Assert.Equal("CMake", SyntaxLanguage.ForPath("engine/CMakeLists.txt")?.Name, "CMakeLists");
        Assert.Equal("XML", SyntaxLanguage.ForPath("Urras.App.csproj")?.Name, "project file");
        Assert.Equal("Markdown", SyntaxLanguage.ForPath("README.md")?.Name, "Markdown");
        Assert.Equal("JSON", SyntaxLanguage.ForPath("settings.json")?.Name, "JSON");
        Assert.True(SyntaxLanguage.ForPath("photo.png") == null, "no language for images");
    }

    public static void ColoursCSharp()
    {
        SyntaxLanguage cs = SyntaxLanguage.CSharp;
        const string LINE = "public static int Count(string name) => name.Length + 42; // done";
        Assert.Equal(SyntaxKind.Keyword, KindOf(LINE, "public", cs), "keyword");
        Assert.Equal(SyntaxKind.Type, KindOf(LINE, "int", cs), "built-in type");
        Assert.Equal(SyntaxKind.Type, KindOf(LINE, "Count", cs), "capitalised name");
        Assert.Equal(SyntaxKind.Plain, KindOf(LINE, "name)", cs), "plain name");
        Assert.Equal(SyntaxKind.Number, KindOf(LINE, "42", cs), "number");
        Assert.Equal(SyntaxKind.Comment, KindOf(LINE, "done", cs), "line comment");

        const string STRINGS = "var path = @\"C:\\\"\"x\"\"\" + \"a \\\" b\" + 'c'; int after = 1;";
        Assert.Equal(SyntaxKind.String, KindOf(STRINGS, "@", cs), "verbatim string with doubled quotes");
        Assert.Equal(SyntaxKind.String, KindOf(STRINGS, "b\"", cs), "escaped quote stays in the string");
        Assert.Equal(SyntaxKind.Keyword, KindOf(STRINGS, "var", cs), "before the strings");
        Assert.Equal(SyntaxKind.Plain, KindOf(STRINGS, "after", cs), "after the strings");
        Assert.Equal(SyntaxKind.Preprocessor, KindOf("  #region Tests", "region", cs), "directive");
    }

    public static void CarriesBlockCommentsAndLongStringsAcrossLines()
    {
        List<SyntaxKind[]> lines = SyntaxHighlighter.ClassifyLines(
            new[] { "int a; /* start", "still comment", "end */ int b;" }, SyntaxLanguage.Cpp);
        Assert.Equal(SyntaxKind.Type, lines[0][0], "code before the comment");
        Assert.Equal(SyntaxKind.Comment, lines[0][7], "comment start");
        Assert.True(lines[1].All(kind => kind == SyntaxKind.Comment), "the middle line is all comment");
        Assert.Equal(SyntaxKind.Comment, lines[2][0], "comment end");
        Assert.Equal(SyntaxKind.Type, lines[2][7], "code after the comment");

        List<SyntaxKind[]> python = SyntaxHighlighter.ClassifyLines(
            new[] { "doc = \"\"\"First", "def inside", "\"\"\" ; def after():" }, SyntaxLanguage.Python);
        Assert.Equal(SyntaxKind.String, python[1][0], "inside a long string");
        Assert.Equal(SyntaxKind.Keyword, python[2][6], "def after the string ends");
    }

    public static void ColoursJsonCMakeAndShell()
    {
        const string JSON = "  \"name\": \"Urras\", \"count\": 3, \"on\": true";
        Assert.Equal(SyntaxKind.Attribute, KindOf(JSON, "\"name\"", SyntaxLanguage.Json), "key");
        Assert.Equal(SyntaxKind.String, KindOf(JSON, "\"Urras\"", SyntaxLanguage.Json), "value");
        Assert.Equal(SyntaxKind.Number, KindOf(JSON, "3", SyntaxLanguage.Json), "number");
        Assert.Equal(SyntaxKind.Keyword, KindOf(JSON, "true", SyntaxLanguage.Json), "literal");

        const string CMAKE = "target_link_libraries(${PROJECT_NAME} PUBLIC raylib) # link";
        Assert.Equal(SyntaxKind.Keyword, KindOf(CMAKE, "target_link", SyntaxLanguage.CMake), "command");
        Assert.Equal(SyntaxKind.Type, KindOf(CMAKE, "${", SyntaxLanguage.CMake), "variable");
        Assert.Equal(SyntaxKind.Keyword, KindOf(CMAKE, "PUBLIC", SyntaxLanguage.CMake), "keyword");
        Assert.Equal(SyntaxKind.Comment, KindOf(CMAKE, "# link", SyntaxLanguage.CMake), "comment");
        List<SyntaxKind[]> bracket = SyntaxHighlighter.ClassifyLines(new[] { "#[[ a", "b ]] set(x)" },
                                                                     SyntaxLanguage.CMake);
        Assert.Equal(SyntaxKind.Keyword, bracket[1][5], "code after a bracket comment");

        Assert.Equal(SyntaxKind.Type, KindOf("echo \"$HOME\" $USER", "$USER", SyntaxLanguage.Shell), "variable");
        Assert.Equal(SyntaxKind.Attribute, KindOf("runs-on: ubuntu-latest", "runs", SyntaxLanguage.Yaml), "key");
        Assert.Equal(SyntaxKind.Attribute, KindOf("max_line_length = 120", "max", SyntaxLanguage.Config), "ini key");
    }

    public static void ColoursMarkup()
    {
        SyntaxLanguage xml = SyntaxLanguage.Xml;
        const string LINE = "<Button x:Name=\"Go\" Content=\"&amp; go\"/> text <!-- note -->";
        Assert.Equal(SyntaxKind.Tag, KindOf(LINE, "<Button", xml), "tag");
        Assert.Equal(SyntaxKind.Attribute, KindOf(LINE, "x:Name", xml), "attribute");
        Assert.Equal(SyntaxKind.String, KindOf(LINE, "\"Go\"", xml), "value");
        Assert.Equal(SyntaxKind.Tag, KindOf(LINE, "/>", xml), "tag end");
        Assert.Equal(SyntaxKind.Plain, KindOf(LINE, "text", xml), "text between tags");
        Assert.Equal(SyntaxKind.Comment, KindOf(LINE, "note", xml), "comment");

        List<SyntaxKind[]> multi = SyntaxHighlighter.ClassifyLines(
            new[] { "<Grid", "  Margin=\"4\">", "<!-- a", "b -->" }, xml);
        Assert.Equal(SyntaxKind.Attribute, multi[1][2], "attribute on the next line of a tag");
        Assert.Equal(SyntaxKind.Tag, multi[1][^1], "tag closes on that line");
        Assert.Equal(SyntaxKind.Comment, multi[3][0], "comment carries on");
    }

    public static void ColoursMarkdown()
    {
        SyntaxLanguage md = SyntaxLanguage.Markdown;
        Assert.Equal(SyntaxKind.Heading, KindOf("## Building", "Building", md), "heading");
        Assert.Equal(SyntaxKind.Plain, KindOf("#hashtag", "hashtag", md), "not a heading without a space");
        Assert.Equal(SyntaxKind.Comment, KindOf("> quoted", "quoted", md), "quote");

        const string LINE = "- Run `dotnet build` then **check** the [docs](https://example.org)";
        Assert.Equal(SyntaxKind.Keyword, KindOf(LINE, "-", md), "list marker");
        Assert.Equal(SyntaxKind.Code, KindOf(LINE, "dotnet", md), "inline code");
        Assert.Equal(SyntaxKind.Emphasis, KindOf(LINE, "check", md), "bold");
        Assert.Equal(SyntaxKind.Attribute, KindOf(LINE, "docs", md), "link text");
        Assert.Equal(SyntaxKind.String, KindOf(LINE, "https", md), "link target");
        Assert.Equal(SyntaxKind.Plain, KindOf("snake_case_name", "case", md), "underscores inside words");

        List<SyntaxKind[]> fenced = SyntaxHighlighter.ClassifyLines(
            new[] { "```cs", "# not a heading", "```", "# heading" }, md);
        Assert.Equal(SyntaxKind.Code, fenced[1][0], "inside a code block");
        Assert.Equal(SyntaxKind.Heading, fenced[3][0], "after the code block");
    }

    public static void LeavesVeryLongLinesPlain()
    {
        SyntaxState state = default;
        string line = "int " + new string('x', SyntaxHighlighter.MAX_LINE_LENGTH);
        Assert.True(SyntaxHighlighter.Classify(line, SyntaxLanguage.Cpp, ref state).All(k => k == SyntaxKind.Plain),
                    "too long to colour");
    }
}
