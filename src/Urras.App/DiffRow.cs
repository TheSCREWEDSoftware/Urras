using Avalonia.Media;
using Urras.Core.Git;

namespace Urras.App;

// One row of a diff view. The unified, split and raw templates each use the properties they need
public sealed class DiffRow
{
    private const int MAX_ROWS = 20000;

    private static readonly IBrush NormalBrush = Brush("#E6EDF3");
    private static readonly IBrush MutedBrush = Brush("#8B949E");
    private static readonly IBrush AddedTextBrush = Brush("#7EE787");
    private static readonly IBrush RemovedTextBrush = Brush("#FF7B72");
    private static readonly IBrush HunkTextBrush = Brush("#79C0FF");
    private static readonly IBrush AddedBackground = Brush("#262EA043");
    private static readonly IBrush RemovedBackground = Brush("#26F85149");
    private static readonly IBrush HunkBackground = Brush("#1A388BFD");
    private static readonly IBrush FileBackground = Brush("#1FFFFFFF");
    private static readonly IBrush EmptySideBackground = Brush("#0AFFFFFF");

    public string OldNumber { get; private init; } = string.Empty;
    public string NewNumber { get; private init; } = string.Empty;
    public string Marker { get; private init; } = string.Empty;
    public string Text { get; private init; } = string.Empty;
    public string LeftText { get; private init; } = string.Empty;
    public string RightText { get; private init; } = string.Empty;

    // Headers and other lines that span the whole width
    public string FullText { get; private init; } = string.Empty;

    public IBrush? Background { get; private init; }
    public IBrush? LeftBackground { get; private init; }
    public IBrush? RightBackground { get; private init; }
    public IBrush Foreground { get; private init; } = NormalBrush;
    public IBrush MarkerForeground { get; private init; } = NormalBrush;
    public FontWeight Weight { get; private init; } = FontWeight.Normal;

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

            rows.Add(new DiffRow { Text = Tabs(line), Foreground = RawBrush(line) });
        }

        return rows;
    }

    public static List<DiffRow> Unified(string text)
    {
        List<DiffLine> lines = DiffParser.Parse(text);
        List<DiffRow> rows = new List<DiffRow>();

        foreach (DiffLine line in lines)
        {
            if (rows.Count >= MAX_ROWS)
            {
                rows.Add(new DiffRow { FullText = TruncatedText(lines.Count), Foreground = MutedBrush });
                break;
            }

            rows.Add(line.IsCode ? UnifiedCode(line) : FullWidth(line));
        }

        return rows;
    }

    public static List<DiffRow> Split(string text)
    {
        List<SplitDiffRow> splitRows = DiffParser.ToSplit(DiffParser.Parse(text));
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
                rows.Add(FullWidth(splitRow.Full));
                continue;
            }

            DiffLine? left = splitRow.Left;
            DiffLine? right = splitRow.Right;
            rows.Add(new DiffRow
            {
                OldNumber = Number(left?.OldNumber),
                NewNumber = Number(right?.NewNumber),
                LeftText = left == null ? string.Empty : Tabs(left.Text),
                RightText = right == null ? string.Empty : Tabs(right.Text),
                LeftBackground = SideBackground(left),
                RightBackground = SideBackground(right)
            });
        }

        return rows;
    }

    private static DiffRow UnifiedCode(DiffLine line)
    {
        return line.Kind switch
        {
            DiffLineKind.Added => new DiffRow
            {
                NewNumber = Number(line.NewNumber),
                Marker = "+",
                MarkerForeground = AddedTextBrush,
                Text = Tabs(line.Text),
                Background = AddedBackground
            },
            DiffLineKind.Removed => new DiffRow
            {
                OldNumber = Number(line.OldNumber),
                Marker = "-",
                MarkerForeground = RemovedTextBrush,
                Text = Tabs(line.Text),
                Background = RemovedBackground
            },
            _ => new DiffRow
            {
                OldNumber = Number(line.OldNumber),
                NewNumber = Number(line.NewNumber),
                Text = Tabs(line.Text)
            }
        };
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
            _ => new DiffRow { FullText = Tabs(line.Text) }
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

    private static string Tabs(string text)
    {
        return text.Replace("\t", "    ");
    }

    private static IBrush Brush(string color)
    {
        return new SolidColorBrush(Color.Parse(color));
    }
}
