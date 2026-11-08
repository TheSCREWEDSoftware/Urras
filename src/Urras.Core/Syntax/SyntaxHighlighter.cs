namespace Urras.Core.Syntax;

// What carries over from one line to the next: being inside a block comment, a long string, a tag or a code block
public struct SyntaxState
{
    internal enum Mode : byte
    {
        Normal,
        BlockComment,
        LongString,
        Tag,
        FencedCode
    }

    internal Mode Current;
    internal string? Delimiter;

    public readonly bool IsNormal => Current == Mode.Normal;
}

// A small line-at-a-time highlighter: good enough for reading diffs, not a compiler
public static class SyntaxHighlighter
{
    // Longer lines are left plain; they are usually minified or generated
    public const int MAX_LINE_LENGTH = 2000;

    public static SyntaxKind[] Classify(string line, SyntaxLanguage language, ref SyntaxState state)
    {
        SyntaxKind[] kinds = new SyntaxKind[line.Length];
        if (line.Length > MAX_LINE_LENGTH)
            return kinds;

        switch (language.Family)
        {
            case SyntaxFamily.Markup:
                ClassifyMarkup(line, language, kinds, ref state);
                break;
            case SyntaxFamily.Markdown:
                ClassifyMarkdown(line, language, kinds, ref state);
                break;
            default:
                ClassifyCode(line, language, kinds, ref state);
                break;
        }

        return kinds;
    }

    // Every line of a file in order, carrying the state along
    public static List<SyntaxKind[]> ClassifyLines(IEnumerable<string> lines, SyntaxLanguage language)
    {
        SyntaxState state = default;
        List<SyntaxKind[]> result = new List<SyntaxKind[]>();
        foreach (string line in lines)
            result.Add(Classify(line, language, ref state));
        return result;
    }

    private static void Fill(SyntaxKind[] kinds, int start, int end, SyntaxKind kind)
    {
        for (int i = Math.Max(start, 0); i < Math.Min(end, kinds.Length); ++i)
            kinds[i] = kind;
    }

    private static bool At(string line, int index, string text, bool ignoreCase = false)
    {
        return text.Length > 0 && string.Compare(line, index, text, 0, text.Length,
                   ignoreCase ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal) == 0
               && index + text.Length <= line.Length;
    }

    // Finds the end of a block that started earlier; returns the index after it, or -1 when it carries on
    private static int EndOf(string line, int from, string end)
    {
        int index = line.IndexOf(end, from, StringComparison.Ordinal);
        return index < 0 ? -1 : index + end.Length;
    }

    private static int ContinueBlock(string line, SyntaxKind[] kinds, ref SyntaxState state, SyntaxKind kind)
    {
        int end = EndOf(line, 0, state.Delimiter!);
        if (end < 0)
        {
            Fill(kinds, 0, line.Length, kind);
            return line.Length;
        }

        Fill(kinds, 0, end, kind);
        state = default;
        return end;
    }

    private static bool IsWordStart(char character)
    {
        return char.IsLetter(character) || character == '_';
    }

    private static bool IsWordPart(char character)
    {
        return char.IsLetterOrDigit(character) || character == '_';
    }

    private static void ClassifyCode(string line, SyntaxLanguage language, SyntaxKind[] kinds, ref SyntaxState state)
    {
        int i = 0;
        if (state.Current == SyntaxState.Mode.BlockComment)
            i = ContinueBlock(line, kinds, ref state, SyntaxKind.Comment);
        else if (state.Current == SyntaxState.Mode.LongString)
            i = ContinueBlock(line, kinds, ref state, SyntaxKind.String);

        if (i == 0 && language.Preprocessor && line.TrimStart().StartsWith('#'))
        {
            int comment = line.IndexOf("//", StringComparison.Ordinal);
            Fill(kinds, 0, line.Length, SyntaxKind.Preprocessor);
            if (comment >= 0)
                Fill(kinds, comment, line.Length, SyntaxKind.Comment);
            return;
        }

        while (i < line.Length)
        {
            char character = line[i];
            if (char.IsWhiteSpace(character))
            {
                ++i;
                continue;
            }

            // Block comments first: CMake's #[[ and Lua's --[[ also start like line comments
            if (language.BlockComment is { } block && At(line, i, block.Start))
            {
                int end = EndOf(line, i + block.Start.Length, block.End);
                if (end < 0)
                {
                    Fill(kinds, i, line.Length, SyntaxKind.Comment);
                    state = new SyntaxState { Current = SyntaxState.Mode.BlockComment, Delimiter = block.End };
                    return;
                }

                Fill(kinds, i, end, SyntaxKind.Comment);
                i = end;
                continue;
            }

            if (language.LineComments.Any(comment => At(line, i, comment, language.IgnoreCase)))
            {
                Fill(kinds, i, line.Length, SyntaxKind.Comment);
                return;
            }

            string? longString = language.LongStrings.FirstOrDefault(delimiter => At(line, i, delimiter));
            if (longString != null)
            {
                int end = EndOf(line, i + longString.Length, longString);
                if (end < 0)
                {
                    Fill(kinds, i, line.Length, SyntaxKind.String);
                    state = new SyntaxState { Current = SyntaxState.Mode.LongString, Delimiter = longString };
                    return;
                }

                Fill(kinds, i, end, SyntaxKind.String);
                i = end;
                continue;
            }

            if (language.Quotes.Contains(character))
            {
                // C# verbatim strings (@"...") double their quotes instead of escaping them
                bool verbatim = i > 0 && line[i - 1] == '@';
                int end = StringEnd(line, i, verbatim);
                SyntaxKind kind = IsKey(line, end, language) ? SyntaxKind.Attribute : SyntaxKind.String;
                Fill(kinds, verbatim ? i - 1 : i, end, kind);
                i = end;
                continue;
            }

            if (language.DollarVariables && character == '$' && i + 1 < line.Length)
            {
                int end = VariableEnd(line, i);
                if (end > i + 1)
                {
                    Fill(kinds, i, end, SyntaxKind.Type);
                    i = end;
                    continue;
                }
            }

            if (char.IsDigit(character) || (character == '.' && i + 1 < line.Length && char.IsDigit(line[i + 1])))
            {
                int end = i;
                while (end < line.Length && (IsWordPart(line[end]) || line[end] is '.' or '\''))
                    ++end;
                Fill(kinds, i, end, SyntaxKind.Number);
                i = end;
                continue;
            }

            if (IsWordStart(character))
            {
                // Keys such as runs-on may contain dashes
                bool dashes = language.KeySeparators.Length > 0;
                int end = i;
                while (end < line.Length && (IsWordPart(line[end]) || (dashes && line[end] == '-')))
                    ++end;
                Fill(kinds, i, end, WordKind(line, i, end, language));
                i = end;
                continue;
            }

            ++i;
        }
    }

    private static SyntaxKind WordKind(string line, int start, int end, SyntaxLanguage language)
    {
        string word = line.Substring(start, end - start);
        if (IsKey(line, end, language))
            return SyntaxKind.Attribute;
        if (language.Keywords.Contains(word))
            return SyntaxKind.Keyword;
        if (language.Types.Contains(word))
            return SyntaxKind.Type;

        int next = end;
        while (next < line.Length && line[next] == ' ')
            ++next;
        if (language.CallsAreKeywords && next < line.Length && line[next] == '(')
            return SyntaxKind.Keyword;
        if (language.CapitalizedTypes && char.IsUpper(word[0]) && word.Any(char.IsLower))
            return SyntaxKind.Type;

        return SyntaxKind.Plain;
    }

    // A key is followed by its separator, as in "name": or name = value
    private static bool IsKey(string line, int end, SyntaxLanguage language)
    {
        if (language.KeySeparators.Length == 0)
            return false;

        int next = end;
        while (next < line.Length && line[next] == ' ')
            ++next;
        return next < line.Length && language.KeySeparators.Contains(line[next])
               && (line[next] != ':' || next + 1 == line.Length || line[next + 1] is ' ' or '\t');
    }

    private static int StringEnd(string line, int start, bool verbatim)
    {
        char quote = line[start];
        int i = start + 1;
        while (i < line.Length)
        {
            if (verbatim && line[i] == quote && i + 1 < line.Length && line[i + 1] == quote)
            {
                i += 2;
                continue;
            }

            if (!verbatim && line[i] == '\\')
            {
                i += 2;
                continue;
            }

            if (line[i] == quote)
                return i + 1;
            ++i;
        }

        return line.Length;
    }

    private static int VariableEnd(string line, int start)
    {
        if (line[start + 1] == '{')
        {
            int close = line.IndexOf('}', start + 2);
            return close < 0 ? line.Length : close + 1;
        }

        int end = start + 1;
        while (end < line.Length && IsWordPart(line[end]))
            ++end;
        return end;
    }

    // XML, XAML, project files and HTML: tags, attributes, values, comments and &entities;
    private static void ClassifyMarkup(string line, SyntaxLanguage language, SyntaxKind[] kinds,
                                       ref SyntaxState state)
    {
        (string commentStart, string commentEnd) = language.BlockComment!.Value;
        int i = 0;
        if (state.Current == SyntaxState.Mode.BlockComment)
            i = ContinueBlock(line, kinds, ref state, SyntaxKind.Comment);

        while (i < line.Length)
        {
            if (state.Current == SyntaxState.Mode.Tag)
            {
                i = ClassifyInsideTag(line, i, kinds, ref state);
                continue;
            }

            if (At(line, i, commentStart))
            {
                int end = EndOf(line, i + commentStart.Length, commentEnd);
                if (end < 0)
                {
                    Fill(kinds, i, line.Length, SyntaxKind.Comment);
                    state = new SyntaxState { Current = SyntaxState.Mode.BlockComment, Delimiter = commentEnd };
                    return;
                }

                Fill(kinds, i, end, SyntaxKind.Comment);
                i = end;
                continue;
            }

            if (line[i] == '<')
            {
                int end = i + 1;
                while (end < line.Length && (IsWordPart(line[end]) || line[end] is '/' or '?' or '!' or ':' or '.'
                                             or '-'))
                    ++end;
                Fill(kinds, i, end, SyntaxKind.Tag);
                state = new SyntaxState { Current = SyntaxState.Mode.Tag };
                i = end;
                continue;
            }

            if (line[i] == '&')
            {
                int end = line.IndexOf(';', i);
                if (end > i && end - i < 12)
                {
                    Fill(kinds, i, end + 1, SyntaxKind.Keyword);
                    i = end + 1;
                    continue;
                }
            }

            ++i;
        }
    }

    private static int ClassifyInsideTag(string line, int i, SyntaxKind[] kinds, ref SyntaxState state)
    {
        char character = line[i];
        if (character == '>' || At(line, i, "/>") || At(line, i, "?>"))
        {
            int length = character == '>' ? 1 : 2;
            Fill(kinds, i, i + length, SyntaxKind.Tag);
            state = default;
            return i + length;
        }

        if (character is '"' or '\'')
        {
            int end = line.IndexOf(character, i + 1);
            end = end < 0 ? line.Length : end + 1;
            Fill(kinds, i, end, SyntaxKind.String);
            return end;
        }

        if (IsWordStart(character))
        {
            int end = i;
            while (end < line.Length && (IsWordPart(line[end]) || line[end] is ':' or '.' or '-'))
                ++end;
            Fill(kinds, i, end, SyntaxKind.Attribute);
            return end;
        }

        return i + 1;
    }

    // Markdown: headings, code blocks and `code`, emphasis, links, quotes and list markers
    private static void ClassifyMarkdown(string line, SyntaxLanguage language, SyntaxKind[] kinds,
                                         ref SyntaxState state)
    {
        string trimmed = line.TrimStart();
        int indent = line.Length - trimmed.Length;

        if (state.Current == SyntaxState.Mode.FencedCode)
        {
            Fill(kinds, 0, line.Length, SyntaxKind.Code);
            if (trimmed.StartsWith(state.Delimiter!, StringComparison.Ordinal))
                state = default;
            return;
        }

        if (state.Current == SyntaxState.Mode.BlockComment)
        {
            if (ContinueBlock(line, kinds, ref state, SyntaxKind.Comment) == line.Length)
                return;
        }

        if (trimmed.StartsWith("```", StringComparison.Ordinal) || trimmed.StartsWith("~~~", StringComparison.Ordinal))
        {
            Fill(kinds, 0, line.Length, SyntaxKind.Code);
            state = new SyntaxState { Current = SyntaxState.Mode.FencedCode, Delimiter = trimmed[..3] };
            return;
        }

        int hashes = trimmed.TakeWhile(character => character == '#').Count();
        if (hashes is >= 1 and <= 6 && (trimmed.Length == hashes || trimmed[hashes] == ' '))
        {
            Fill(kinds, 0, line.Length, SyntaxKind.Heading);
            return;
        }

        if (trimmed.StartsWith('>'))
        {
            Fill(kinds, 0, line.Length, SyntaxKind.Comment);
            return;
        }

        if (trimmed.StartsWith(language.BlockComment!.Value.Start, StringComparison.Ordinal))
        {
            int end = EndOf(line, indent + 4, language.BlockComment.Value.End);
            Fill(kinds, indent, end < 0 ? line.Length : end, SyntaxKind.Comment);
            if (end < 0)
                state = new SyntaxState { Current = SyntaxState.Mode.BlockComment,
                                          Delimiter = language.BlockComment.Value.End };
            return;
        }

        // List markers and table pipes
        int marker = ListMarkerLength(trimmed);
        Fill(kinds, indent, indent + marker, SyntaxKind.Keyword);
        ClassifyInline(line, indent + marker, kinds);
    }

    private static int ListMarkerLength(string trimmed)
    {
        if (trimmed.Length >= 2 && trimmed[0] is '-' or '*' or '+' && trimmed[1] == ' ')
            return 1;

        int digits = trimmed.TakeWhile(char.IsDigit).Count();
        return digits > 0 && digits + 1 < trimmed.Length && trimmed[digits] is '.' or ')' && trimmed[digits + 1] == ' '
            ? digits + 1
            : 0;
    }

    private static void ClassifyInline(string line, int start, SyntaxKind[] kinds)
    {
        int i = start;
        while (i < line.Length)
        {
            char character = line[i];
            if (character == '`')
            {
                int ticks = 1;
                while (i + ticks < line.Length && line[i + ticks] == '`')
                    ++ticks;
                int close = line.IndexOf(new string('`', ticks), i + ticks, StringComparison.Ordinal);
                int end = close < 0 ? i + ticks : close + ticks;
                Fill(kinds, i, end, SyntaxKind.Code);
                i = end;
                continue;
            }

            if (character is '*' or '_' && i + 1 < line.Length && !char.IsWhiteSpace(line[i + 1]))
            {
                bool strong = i + 1 < line.Length && line[i + 1] == character;
                string delimiter = strong ? new string(character, 2) : character.ToString();
                int close = line.IndexOf(delimiter, i + delimiter.Length, StringComparison.Ordinal);
                if (close > i + delimiter.Length && (character == '*' || i == 0 || !IsWordPart(line[i - 1])))
                {
                    Fill(kinds, i, close + delimiter.Length, SyntaxKind.Emphasis);
                    i = close + delimiter.Length;
                    continue;
                }
            }

            if (character == '[' || (character == '!' && i + 1 < line.Length && line[i + 1] == '['))
            {
                int open = character == '!' ? i + 1 : i;
                int closeBracket = line.IndexOf(']', open);
                if (closeBracket > open && closeBracket + 1 < line.Length && line[closeBracket + 1] == '(')
                {
                    int closeParen = line.IndexOf(')', closeBracket);
                    int end = closeParen < 0 ? line.Length : closeParen + 1;
                    Fill(kinds, i, closeBracket + 1, SyntaxKind.Attribute);
                    Fill(kinds, closeBracket + 1, end, SyntaxKind.String);
                    i = end;
                    continue;
                }
            }

            if (character == '|')
                kinds[i] = SyntaxKind.Keyword;
            ++i;
        }
    }
}
