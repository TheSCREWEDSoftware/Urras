using System.Text;
using System.Text.RegularExpressions;

namespace Urras.Standards;

// The coding standards that can be checked mechanically, applied to one file's contents
public static partial class FileRules
{
    public const int MAX_LINE_LENGTH = 120;
    // Long dashes read as machine-written; text uses the plain hyphen
    private const char EN_DASH = '\u2013';
    private const char EM_DASH = '\u2014';

    private static readonly string[] TextExtensions =
    {
        ".cs", ".axaml", ".xml", ".csproj", ".props", ".targets", ".json", ".yml", ".yaml", ".md", ".txt", ".sh",
        ".bat", ".cmd", ".editorconfig", ".gitattributes", ".gitignore", ".slnx"
    };

    private static readonly string[] CrlfExtensions = { ".bat", ".cmd" };

    // Written by other tools or kept exactly as published
    private static readonly string[] ExemptFiles = { "LICENSE", "Urras.sln" };

    [GeneratedRegex(@"^\s*(?:(?:static|readonly|const)\s+)*var\s+\w+\s*=|\bforeach\s*\(\s*var\s" +
                    @"|\(\s*var\s+\w+\s*[,)]")]
    private static partial Regex VarRegex();

    [GeneratedRegex(@"\(\s+[^\s)]|[^\s(]\s+\)")]
    private static partial Regex ParenthesisSpaceRegex();

    [GeneratedRegex(@"^\s*private\s+(?!static\b|const\b)(?:readonly\s+)?[\w<>\[\],.?() ]+?\s+" +
                    @"(?<name>[A-Za-z]\w*)\s*(?:=|;)")]
    private static partial Regex PrivateFieldRegex();

    [GeneratedRegex(@"^\s*(?:(?:private|public|internal|protected)\s+)?const\s+[\w<>\[\],.?]+\s+(?<name>\w+)\s*=")]
    private static partial Regex ConstRegex();

    [GeneratedRegex(@"^[A-Z][A-Z0-9]*(?:_[A-Z0-9]+)*$")]
    private static partial Regex UpperSnakeRegex();

    [GeneratedRegex("[\u2600-\u26FF\u2700-\u27BF\uFE0F]|[\uD83C-\uD83E][\uDC00-\uDFFF]")]
    private static partial Regex EmojiRegex();

    [GeneratedRegex(@"^\s*(?:case\s.+|default)\s*:\s*$")]
    private static partial Regex CaseLabelRegex();

    [GeneratedRegex(@"^\s*switch\s*\(.*\)\s*$")]
    private static partial Regex SwitchRegex();

    public static bool IsChecked(string path)
    {
        string name = Path.GetFileName(path);
        if (ExemptFiles.Contains(name) || path.Replace('\\', '/').Split('/').Any(part => part is "bin" or "obj"))
            return false;
        return TextExtensions.Any(extension => name.EndsWith(extension, StringComparison.OrdinalIgnoreCase));
    }

    public static List<Violation> Check(string path, byte[] bytes)
    {
        List<Violation> violations = new List<Violation>();
        if (!IsChecked(path))
            return violations;

        if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
            violations.Add(new Violation(path, 0, "encoding", "Save as UTF-8 without a BOM."));

        string text = new UTF8Encoding(false).GetString(bytes).TrimStart('﻿');
        bool crlf = CrlfExtensions.Any(extension => path.EndsWith(extension, StringComparison.OrdinalIgnoreCase));
        CheckLineEndings(path, text, crlf, violations);

        string[] lines = text.Replace("\r\n", "\n").Split('\n');
        for (int i = 0; i < lines.Length; ++i)
            CheckLine(path, i + 1, lines[i], violations);

        if (path.EndsWith(".cs", StringComparison.OrdinalIgnoreCase))
            CSharp(path, lines, violations);
        return violations;
    }

    private static void CheckLineEndings(string path, string text, bool crlf, List<Violation> violations)
    {
        if (text.Length > 0 && !text.EndsWith('\n'))
            violations.Add(new Violation(path, 0, "newline", "End the file with a newline."));

        int crlfCount = text.Split("\r\n").Length - 1;
        int lfCount = text.Count(character => character == '\n');
        if (crlf && crlfCount != lfCount)
            violations.Add(new Violation(path, 0, "line-endings", "Batch files use CRLF line endings."));
        else if (!crlf && crlfCount > 0)
            violations.Add(new Violation(path, 0, "line-endings", "Use LF line endings, not CRLF."));
    }

    private static void CheckLine(string path, int number, string line, List<Violation> violations)
    {
        if (line.Contains('\t'))
            violations.Add(new Violation(path, number, "tabs", "Indent with spaces, not tabs."));
        if (line.Length > 0 && char.IsWhiteSpace(line[^1]))
            violations.Add(new Violation(path, number, "trailing-whitespace", "Remove the spaces at the end."));
        if (line.Length > MAX_LINE_LENGTH && !line.Contains("://"))
            violations.Add(new Violation(path, number, "line-length",
                                         $"{line.Length} columns; wrap at {MAX_LINE_LENGTH}."));
        if (EmojiRegex().IsMatch(line))
            violations.Add(new Violation(path, number, "emoji", "Use words or plain markers instead of emoji."));
        if (line.Contains(EN_DASH) || line.Contains(EM_DASH))
            violations.Add(new Violation(path, number, "dashes", "Use a plain hyphen (-), not a long dash."));
    }

    // Removes strings, characters and comments, so the rules only look at code
    public static string CodeOnly(string line)
    {
        StringBuilder code = new StringBuilder();
        char? quote = null;
        for (int i = 0; i < line.Length; ++i)
        {
            char character = line[i];
            if (quote != null)
            {
                if (character == '\\' && i + 1 < line.Length)
                    ++i;
                else if (character == quote)
                {
                    quote = null;
                    code.Append(character);
                }

                continue;
            }

            if (character == '/' && i + 1 < line.Length && line[i + 1] == '/')
                break;
            if (character is '"' or '\'')
                quote = character;
            code.Append(character);
        }

        return code.ToString();
    }

    private static void CSharp(string path, string[] lines, List<Violation> violations)
    {
        bool fileScoped = false;
        List<string> usings = new List<string>();
        int previousComment = -2;

        for (int i = 0; i < lines.Length; ++i)
        {
            string line = lines[i];
            string code = CodeOnly(line);
            string trimmed = line.Trim();
            int number = i + 1;

            if (trimmed.StartsWith("namespace ") && trimmed.EndsWith(';'))
                fileScoped = true;
            if (trimmed.StartsWith("using ") && trimmed.EndsWith(';') && !trimmed.Contains('=') &&
                !trimmed.Contains('('))
                usings.Add(trimmed.TrimEnd(';')[6..]);

            if (trimmed.StartsWith("//"))
            {
                if (previousComment == i - 1)
                    violations.Add(new Violation(path, number, "comments", "Keep comments to one line."));
                if (trimmed.Contains("http://") || trimmed.Contains("https://"))
                    violations.Add(new Violation(path, number, "comments",
                                                 "Put links in the pull request, not in comments."));
                previousComment = i;
            }

            if (VarRegex().IsMatch(code))
                violations.Add(new Violation(path, number, "var", "Write the type instead of var."));
            if (ParenthesisSpaceRegex().IsMatch(code.TrimEnd()) && !code.TrimEnd().EndsWith('('))
                violations.Add(new Violation(path, number, "parentheses", "No spaces just inside parentheses."));

            Match field = PrivateFieldRegex().Match(code);
            if (field.Success && !code.Contains("=>") && !code.Contains('('))
                violations.Add(new Violation(path, number, "naming",
                                             "Private fields start with _ and a lower-case letter: _" +
                                                 char.ToLowerInvariant(field.Groups["name"].Value[0]) +
                                                 field.Groups["name"].Value[1..] + "."));
            Match constant = ConstRegex().Match(code);
            if (constant.Success && !UpperSnakeRegex().IsMatch(constant.Groups["name"].Value))
                violations.Add(new Violation(path, number, "naming",
                                             "Constants are UPPER_SNAKE_CASE: " + constant.Groups["name"].Value +
                                                 "."));

            if (SwitchRegex().IsMatch(code) && i + 1 < lines.Length && lines[i + 1].Trim() == "{")
                CheckSwitch(path, lines, i + 1, violations);
        }

        if (!fileScoped && lines.Any(line => line.TrimStart().StartsWith("namespace ")))
            violations.Add(new Violation(path, 0, "namespace", "Use a file-scoped namespace: namespace X;"));

        List<string> sorted = usings.OrderBy(name => name == "System" || name.StartsWith("System.") ? 0 : 1)
                                    .ThenBy(name => name, StringComparer.Ordinal).ToList();
        if (!usings.SequenceEqual(sorted))
            violations.Add(new Violation(path, 0, "usings", "Put System usings first, then the rest alphabetically."));
    }

    // The switch body starts at the line holding its opening brace
    private static void CheckSwitch(string path, string[] lines, int open, List<Violation> violations)
    {
        int indent = Indent(lines[open]);
        int close = open + 1;
        while (close < lines.Length && !(lines[close].Trim() == "}" && Indent(lines[close]) == indent))
            ++close;

        bool hasDefault = false;
        for (int i = open + 1; i < close; ++i)
        {
            if (Indent(lines[i]) != indent + 4 || !CaseLabelRegex().IsMatch(lines[i]))
                continue;
            if (lines[i].Trim().StartsWith("default"))
                hasDefault = true;
            if (i + 1 < close && lines[i + 1].Trim() == "{")
                continue;

            // Statements directly in the case, until the next label or the end of the switch
            int statements = 0;
            for (int j = i + 1; j < close && !(Indent(lines[j]) <= indent + 4 && lines[j].Trim().Length > 0); ++j)
            {
                // Closing braces of an initializer or lambda belong to the statement above
                string statement = lines[j].Trim();
                if (Indent(lines[j]) == indent + 8 && StartsStatement(statement))
                    ++statements;
            }

            string last = LastStatement(lines, i + 1, close, indent + 8);
            if (statements > 2 || (statements == 2 && !last.StartsWith("break") && !last.StartsWith("return") &&
                                   !last.StartsWith("continue") && !last.StartsWith("throw")))
                violations.Add(new Violation(path, i + 1, "switch",
                                             "Put braces around a case with several statements."));
        }

        if (!hasDefault)
            violations.Add(new Violation(path, open, "switch", "Add a default case, even if it only breaks."));
    }

    private static string LastStatement(string[] lines, int from, int to, int indent)
    {
        string last = string.Empty;
        for (int j = from; j < to && !(Indent(lines[j]) < indent && lines[j].Trim().Length > 0); ++j)
        {
            if (Indent(lines[j]) == indent && StartsStatement(lines[j].Trim()))
                last = lines[j].Trim();
        }

        return last;
    }

    private static bool StartsStatement(string line)
    {
        return line.Length > 0 && line[0] is not ('{' or '}' or ')');
    }

    private static int Indent(string line)
    {
        return line.Length - line.TrimStart(' ').Length;
    }
}
