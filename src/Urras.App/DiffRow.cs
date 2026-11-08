using System.Text;
using Avalonia.Media;
using Urras.Core.Git;
using Urras.Core.Syntax;

namespace Urras.App;

// One row of a diff view. The unified, split and raw templates each use the properties they need
public sealed class DiffRow
{
    private const int MAX_ROWS = 20000;
    private const int TAB_WIDTH = 4;

    private static readonly IBrush NormalBrush = Brush("#E6EDF3");
    private static readonly IBrush MutedBrush = Brush("#8B949E");
    private static readonly IBrush WhitespaceBrush = Brush("#6E7681");
    private static readonly IBrush AddedTextBrush = Brush("#7EE787");
    private static readonly IBrush RemovedTextBrush = Brush("#FF7B72");
    private static readonly IBrush HunkTextBrush = Brush("#79C0FF");
    private static readonly IBrush AddedBackground = Brush("#262EA043");
    private static readonly IBrush RemovedBackground = Brush("#26F85149");
    private static readonly IBrush AddedWordBackground = Brush("#662EA043");
    private static readonly IBrush RemovedWordBackground = Brush("#66F85149");
    private static readonly IBrush TrailingWhitespaceBackground = Brush("#59F85149");
    private static readonly IBrush HunkBackground = Brush("#1A388BFD");
    private static readonly IBrush FileBackground = Brush("#1FFFFFFF");
    private static readonly IBrush EmptySideBackground = Brush("#0AFFFFFF");
    private static readonly IReadOnlyList<TextSegment> NoSegments = Array.Empty<TextSegment>();

    // Syntax colours, close to Visual Studio's dark theme so they read well on the red and green line backgrounds
    private static readonly Dictionary<SyntaxKind, IBrush> SyntaxBrushes = new Dictionary<SyntaxKind, IBrush>
    {
        [SyntaxKind.Keyword] = Brush("#569CD6"),
        [SyntaxKind.Type] = Brush("#4EC9B0"),
        [SyntaxKind.String] = Brush("#CE9178"),
        [SyntaxKind.Number] = Brush("#B5CEA8"),
        [SyntaxKind.Comment] = Brush("#7CA668"),
        [SyntaxKind.Preprocessor] = Brush("#C586C0"),
        [SyntaxKind.Tag] = Brush("#569CD6"),
        [SyntaxKind.Attribute] = Brush("#9CDCFE"),
        [SyntaxKind.Heading] = Brush("#569CD6"),
        [SyntaxKind.Emphasis] = Brush("#DCDCAA"),
        [SyntaxKind.Code] = Brush("#CE9178")
    };

    public static IBrush? SyntaxBrush(SyntaxKind kind)
    {
        return SyntaxBrushes.GetValueOrDefault(kind);
    }

    public string OldNumber { get; private init; } = string.Empty;
    public string NewNumber { get; private init; } = string.Empty;
    public string Marker { get; private init; } = string.Empty;

    // Raw view text
    public string Text { get; private init; } = string.Empty;

    // Unified and split views: the line with word highlights and whitespace markers
    public IReadOnlyList<TextSegment> Segments { get; private init; } = NoSegments;
    public IReadOnlyList<TextSegment> LeftSegments { get; private init; } = NoSegments;
    public IReadOnlyList<TextSegment> RightSegments { get; private init; } = NoSegments;

    // Headers and other lines that span the whole width
    public string FullText { get; private init; } = string.Empty;

    public IBrush? Background { get; private init; }
    public IBrush? LeftBackground { get; private init; }
    public IBrush? RightBackground { get; private init; }
    public IBrush Foreground { get; private init; } = NormalBrush;
    public IBrush MarkerForeground { get; private init; } = NormalBrush;
    public FontWeight Weight { get; private init; } = FontWeight.Normal;

    // Lines of git's output this row shows (two in a split row), for staging or discarding selected lines
    public IReadOnlyList<int> RawLines { get; private set; } = Array.Empty<int>();

    private DiffRow WithLines(params DiffLine?[] lines)
    {
        RawLines = lines.Where(line => line != null && line.RawIndex >= 0).Select(line => line!.RawIndex).ToArray();
        return this;
    }

    public static List<DiffRow> Raw(string text)
    {
        List<DiffRow> rows = new List<DiffRow>();
        string[] lines = text.Replace("\r\n", "\n").Split('\n');

        foreach (string line in lines)
        {
            if (rows.Count >= MAX_ROWS)
            {
                rows.Add(new DiffRow { Text = TruncatedText(lines.Length), Foreground = MutedBrush });
                break;
            }

            string expanded = line.Replace("\t", new string(' ', TAB_WIDTH));
            rows.Add(new DiffRow { Text = expanded, Foreground = RawBrush(line), RawLines = new[] { rows.Count } });
        }

        return rows;
    }

    public static List<DiffRow> Unified(string text, bool showWhitespace)
    {
        List<DiffLine> lines = DiffParser.Parse(text);
        Dictionary<DiffLine, List<InlineSegment>> words = WordHighlights(DiffParser.ToSplit(lines));
        Dictionary<DiffLine, SyntaxKind[]> syntax = SyntaxColours(lines);
        List<DiffRow> rows = new List<DiffRow>();

        foreach (DiffLine line in lines)
        {
            if (rows.Count >= MAX_ROWS)
            {
                rows.Add(new DiffRow { FullText = TruncatedText(lines.Count), Foreground = MutedBrush });
                break;
            }

            DiffRow row = line.IsCode ? UnifiedCode(line, words.GetValueOrDefault(line),
                                                    syntax.GetValueOrDefault(line), showWhitespace)
                                      : FullWidth(line);
            rows.Add(row.WithLines(line));
        }

        return rows;
    }

    public static List<DiffRow> Split(string text, bool showWhitespace)
    {
        List<DiffLine> lines = DiffParser.Parse(text);
        List<SplitDiffRow> splitRows = DiffParser.ToSplit(lines);
        Dictionary<DiffLine, List<InlineSegment>> words = WordHighlights(splitRows);
        Dictionary<DiffLine, SyntaxKind[]> syntax = SyntaxColours(lines);
        List<DiffRow> rows = new List<DiffRow>();

        foreach (SplitDiffRow splitRow in splitRows)
        {
            if (rows.Count >= MAX_ROWS)
            {
                rows.Add(new DiffRow { FullText = TruncatedText(splitRows.Count), Foreground = MutedBrush });
                break;
            }

            if (splitRow.Full != null)
            {
                rows.Add(FullWidth(splitRow.Full).WithLines(splitRow.Full));
                continue;
            }

            DiffLine? left = splitRow.Left;
            DiffLine? right = splitRow.Right;
            rows.Add(new DiffRow
            {
                OldNumber = Number(left?.OldNumber),
                NewNumber = Number(right?.NewNumber),
                LeftSegments = left == null ? NoSegments : BuildSegments(left, words.GetValueOrDefault(left),
                                                                         syntax.GetValueOrDefault(left),
                                                                         showWhitespace),
                RightSegments = right == null ? NoSegments : BuildSegments(right, words.GetValueOrDefault(right),
                                                                           syntax.GetValueOrDefault(right),
                                                                           showWhitespace),
                LeftBackground = SideBackground(left),
                RightBackground = SideBackground(right)
            }.WithLines(left, right == left ? null : right));
        }

        return rows;
    }

    // Syntax colours for every code line: each file uses its own language, and the old and new sides each carry
    // their own state (such as an open block comment) from line to line
    private static Dictionary<DiffLine, SyntaxKind[]> SyntaxColours(IEnumerable<DiffLine> lines)
    {
        Dictionary<DiffLine, SyntaxKind[]> colours = new Dictionary<DiffLine, SyntaxKind[]>();
        SyntaxLanguage? language = null;
        SyntaxState oldState = default;
        SyntaxState newState = default;

        foreach (DiffLine line in lines)
        {
            if (line.Kind == DiffLineKind.FileHeader)
                language = SyntaxLanguage.ForPath(line.Text);
            if (line.Kind is DiffLineKind.FileHeader or DiffLineKind.HunkHeader)
            {
                oldState = default;
                newState = default;
            }

            if (language == null || !line.IsCode)
                continue;

            if (line.Kind == DiffLineKind.Removed)
            {
                colours[line] = SyntaxHighlighter.Classify(line.Text, language, ref oldState);
                continue;
            }

            if (line.Kind == DiffLineKind.Context)
                SyntaxHighlighter.Classify(line.Text, language, ref oldState);
            colours[line] = SyntaxHighlighter.Classify(line.Text, language, ref newState);
        }

        return colours;
    }

    // Word highlights for every removed line that has a matching added line
    private static Dictionary<DiffLine, List<InlineSegment>> WordHighlights(IEnumerable<SplitDiffRow> rows)
    {
        Dictionary<DiffLine, List<InlineSegment>> words = new Dictionary<DiffLine, List<InlineSegment>>();
        foreach (SplitDiffRow row in rows)
        {
            if (row.Left?.Kind != DiffLineKind.Removed || row.Right?.Kind != DiffLineKind.Added)
                continue;

            (List<InlineSegment> oldWords, List<InlineSegment> newWords) =
                IntralineDiff.Compare(row.Left.Text, row.Right.Text);
            words[row.Left] = oldWords;
            words[row.Right] = newWords;
        }

        return words;
    }

    private static DiffRow UnifiedCode(DiffLine line, List<InlineSegment>? words, SyntaxKind[]? syntax,
                                       bool showWhitespace)
    {
        IReadOnlyList<TextSegment> segments = BuildSegments(line, words, syntax, showWhitespace);
        return line.Kind switch
        {
            DiffLineKind.Added => new DiffRow
            {
                NewNumber = Number(line.NewNumber),
                Marker = "+",
                MarkerForeground = AddedTextBrush,
                Segments = segments,
                Background = AddedBackground
            },
            DiffLineKind.Removed => new DiffRow
            {
                OldNumber = Number(line.OldNumber),
                Marker = "-",
                MarkerForeground = RemovedTextBrush,
                Segments = segments,
                Background = RemovedBackground
            },
            _ => new DiffRow
            {
                OldNumber = Number(line.OldNumber),
                NewNumber = Number(line.NewNumber),
                Segments = segments
            }
        };
    }

    // Splits a line into coloured segments: syntax, changed words, whitespace markers and trailing whitespace
    private static IReadOnlyList<TextSegment> BuildSegments(DiffLine line, List<InlineSegment>? words,
                                                             SyntaxKind[]? syntax, bool showWhitespace)
    {
        string text = line.Text;
        IBrush? wordBackground = line.Kind switch
        {
            DiffLineKind.Added => AddedWordBackground,
            DiffLineKind.Removed => RemovedWordBackground,
            _ => null
        };

        // Which characters belong to a changed word
        bool[] changed = new bool[text.Length];
        if (words != null)
        {
            int position = 0;
            foreach (InlineSegment word in words)
            {
                for (int i = 0; i < word.Text.Length && position < text.Length; ++i)
                    changed[position++] = word.Changed;
            }
        }

        int trailingStart = text.TrimEnd(' ', '\t').Length;
        List<TextSegment> segments = new List<TextSegment>();
        StringBuilder current = new StringBuilder();
        (bool Changed, bool Whitespace, bool Trailing, SyntaxKind Kind) currentStyle = default;

        for (int i = 0; i < text.Length; ++i)
        {
            char character = text[i];
            bool whitespace = showWhitespace && (character == ' ' || character == '\t');
            bool trailing = showWhitespace && i >= trailingStart;
            SyntaxKind kind = syntax != null && i < syntax.Length ? syntax[i] : SyntaxKind.Plain;
            (bool, bool, bool, SyntaxKind) style = (changed[i], whitespace, trailing, kind);

            if (current.Length > 0 && style != currentStyle)
            {
                segments.Add(Segment(current.ToString(), currentStyle, wordBackground));
                current.Clear();
            }

            currentStyle = style;
            current.Append(character switch
            {
                ' ' when showWhitespace => "·",
                '\t' when showWhitespace => "→" + new string(' ', TAB_WIDTH - 1),
                '\t' => new string(' ', TAB_WIDTH),
                _ => character.ToString()
            });
        }

        if (current.Length > 0)
            segments.Add(Segment(current.ToString(), currentStyle, wordBackground));

        if (showWhitespace)
        {
            string ending = line.MissingNewline ? " ⊘ no line ending" : line.HasCarriageReturn ? " ␍↵ CRLF" : " ↵ LF";
            segments.Add(new TextSegment(ending, WhitespaceBrush));
        }

        return segments;
    }

    private static TextSegment Segment(string text,
                                       (bool Changed, bool Whitespace, bool Trailing, SyntaxKind Kind) style,
                                       IBrush? wordBackground)
    {
        IBrush? background = style.Trailing ? TrailingWhitespaceBackground :
                             style.Changed ? wordBackground : null;
        return new TextSegment(text, style.Whitespace ? WhitespaceBrush : SyntaxBrush(style.Kind), background);
    }

    private static DiffRow FullWidth(DiffLine line)
    {
        return line.Kind switch
        {
            DiffLineKind.FileHeader => new DiffRow
            {
                FullText = line.Text,
                Background = FileBackground,
                Weight = FontWeight.SemiBold
            },
            DiffLineKind.HunkHeader => new DiffRow
            {
                FullText = line.Text,
                Background = HunkBackground,
                Foreground = HunkTextBrush
            },
            DiffLineKind.Meta => new DiffRow { FullText = line.Text, Foreground = MutedBrush },
            _ => new DiffRow { FullText = line.Text.Replace("\t", new string(' ', TAB_WIDTH)) }
        };
    }

    private static IBrush? SideBackground(DiffLine? line)
    {
        if (line == null)
            return EmptySideBackground;

        return line.Kind switch
        {
            DiffLineKind.Added => AddedBackground,
            DiffLineKind.Removed => RemovedBackground,
            _ => null
        };
    }

    private static IBrush RawBrush(string line)
    {
        if (line.StartsWith("+++", StringComparison.Ordinal) || line.StartsWith("---", StringComparison.Ordinal) ||
            line.StartsWith("diff ", StringComparison.Ordinal) || line.StartsWith("index ", StringComparison.Ordinal))
            return MutedBrush;
        if (line.StartsWith("@@", StringComparison.Ordinal))
            return HunkTextBrush;
        if (line.StartsWith('+'))
            return AddedTextBrush;
        if (line.StartsWith('-'))
            return RemovedTextBrush;

        return NormalBrush;
    }

    private static string TruncatedText(int total)
    {
        return "… diff truncated (" + total + " lines)";
    }

    private static string Number(int? number)
    {
        return number?.ToString() ?? string.Empty;
    }

    private static IBrush Brush(string color)
    {
        return new SolidColorBrush(Color.Parse(color));
    }
}
