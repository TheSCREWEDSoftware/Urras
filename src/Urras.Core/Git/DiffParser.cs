using System.Globalization;
using System.Text.RegularExpressions;

namespace Urras.Core.Git;

public enum DiffLineKind
{
    // Text outside any file diff, such as the commit header and stat of `git show`
    Text,
    FileHeader,
    Meta,
    HunkHeader,
    Context,
    Added,
    Removed
}

public sealed class DiffLine
{
    public DiffLine(DiffLineKind kind, string text, int? oldNumber = null, int? newNumber = null)
    {
        Kind = kind;
        Text = text;
        OldNumber = oldNumber;
        NewNumber = newNumber;
    }

    public DiffLineKind Kind { get; }
    public string Text { get; }
    public int? OldNumber { get; }
    public int? NewNumber { get; }

    // The line ended in CRLF in the file
    public bool HasCarriageReturn { get; init; }

    // The last line of the file, without a line ending ("\ No newline at end of file")
    public bool MissingNewline { get; internal set; }

    public bool IsCode => Kind is DiffLineKind.Context or DiffLineKind.Added or DiffLineKind.Removed;

    // Line number in git's output, counted from 0; used to stage or discard selected lines
    public int RawIndex { get; internal set; } = -1;
    public bool IsChange => Kind is DiffLineKind.Added or DiffLineKind.Removed;
}

// One row of a side-by-side diff: either a full-width line (Full) or an old line and/or a new line
public sealed class SplitDiffRow
{
    public SplitDiffRow(DiffLine? left, DiffLine? right, DiffLine? full = null)
    {
        Left = left;
        Right = right;
        Full = full;
    }

    public DiffLine? Left { get; }
    public DiffLine? Right { get; }
    public DiffLine? Full { get; }
}

// Turns `git diff` and `git show` output into lines with old and new line numbers
public static partial class DiffParser
{
    [GeneratedRegex(@"^@@ -(\d+)(?:,\d+)? \+(\d+)(?:,\d+)? @@(.*)$")]
    private static partial Regex HunkHeaderRegex();

    public static List<DiffLine> Parse(string text)
    {
        List<DiffLine> lines = new List<DiffLine>();

        // Split on LF only, so the CR of CRLF files is kept and can be shown
        string[] rawLines = text.Split('\n');

        // git ends its output with a newline, which would otherwise become an empty context line
        int count = rawLines.Length;
        if (count > 0 && rawLines[count - 1].Length == 0)
            --count;

        bool inFile = false;
        bool inHunk = false;
        bool combined = false;
        int oldNumber = 0;
        int newNumber = 0;

        int i;
        void Add(DiffLine line)
        {
            line.RawIndex = i;
            lines.Add(line);
        }

        for (i = 0; i < count; ++i)
        {
            string line = rawLines[i];
            bool carriageReturn = line.EndsWith('\r');
            if (carriageReturn)
                line = line.Substring(0, line.Length - 1);

            if (line.StartsWith("diff --git ", StringComparison.Ordinal) ||
                line.StartsWith("diff --cc ", StringComparison.Ordinal) ||
                line.StartsWith("diff --combined ", StringComparison.Ordinal))
            {
                inFile = true;
                inHunk = false;
                combined = !line.StartsWith("diff --git ", StringComparison.Ordinal);
                Add(new DiffLine(DiffLineKind.FileHeader, FileNameFromHeader(line)));
                continue;
            }

            if (!inFile)
            {
                Add(new DiffLine(DiffLineKind.Text, line));
                continue;
            }

            if (combined)
            {
                // Merge diffs have one marker column per parent; show them as they are
                Add(new DiffLine(line.StartsWith("@@", StringComparison.Ordinal) ? DiffLineKind.HunkHeader
                                                                                       : DiffLineKind.Context,
                                       line));
                continue;
            }

            Match hunk = HunkHeaderRegex().Match(line);
            if (hunk.Success)
            {
                inHunk = true;
                oldNumber = int.Parse(hunk.Groups[1].Value, CultureInfo.InvariantCulture);
                newNumber = int.Parse(hunk.Groups[2].Value, CultureInfo.InvariantCulture);
                Add(new DiffLine(DiffLineKind.HunkHeader, line));
                continue;
            }

            if (!inHunk)
            {
                // The paths and blob ids are already shown by the file header
                if (line.StartsWith("index ", StringComparison.Ordinal) ||
                    line.StartsWith("--- ", StringComparison.Ordinal) ||
                    line.StartsWith("+++ ", StringComparison.Ordinal))
                    continue;

                Add(new DiffLine(DiffLineKind.Meta, line));
                continue;
            }

            if (line.StartsWith('+'))
                Add(new DiffLine(DiffLineKind.Added, line.Substring(1), null, newNumber++)
                {
                    HasCarriageReturn = carriageReturn
                });
            else if (line.StartsWith('-'))
                Add(new DiffLine(DiffLineKind.Removed, line.Substring(1), oldNumber++, null)
                {
                    HasCarriageReturn = carriageReturn
                });
            else if (line.StartsWith('\\'))
            {
                // Marks the line before it as the end of the file without a line ending
                if (lines.Count > 0 && lines[^1].IsCode)
                    lines[^1].MissingNewline = true;
                Add(new DiffLine(DiffLineKind.Meta, line));
            }
            else if (line.StartsWith(' ') || line.Length == 0)
                Add(new DiffLine(DiffLineKind.Context, line.Length == 0 ? line : line.Substring(1), oldNumber++,
                                       newNumber++) { HasCarriageReturn = carriageReturn });
            else
            {
                // Anything else ends the hunk, such as the next commit in a multi-commit show
                inHunk = false;
                Add(new DiffLine(DiffLineKind.Meta, line));
            }
        }

        return lines;
    }

    // Pairs each block of removed lines with the added lines that follow it, like GitHub's split view
    public static List<SplitDiffRow> ToSplit(IReadOnlyList<DiffLine> lines)
    {
        List<SplitDiffRow> rows = new List<SplitDiffRow>();
        int i = 0;

        while (i < lines.Count)
        {
            DiffLine line = lines[i];
            switch (line.Kind)
            {
                case DiffLineKind.Context:
                    rows.Add(new SplitDiffRow(line, line));
                    ++i;
                    break;
                case DiffLineKind.Removed:
                case DiffLineKind.Added:
                {
                    List<DiffLine> removed = new List<DiffLine>();
                    List<DiffLine> added = new List<DiffLine>();
                    while (i < lines.Count && lines[i].Kind == DiffLineKind.Removed)
                        removed.Add(lines[i++]);
                    while (i < lines.Count && lines[i].Kind == DiffLineKind.Added)
                        added.Add(lines[i++]);

                    int pairs = Math.Max(removed.Count, added.Count);
                    for (int pair = 0; pair < pairs; ++pair)
                    {
                        rows.Add(new SplitDiffRow(pair < removed.Count ? removed[pair] : null,
                                                  pair < added.Count ? added[pair] : null));
                    }
                    break;
                }
                default:
                    rows.Add(new SplitDiffRow(null, null, line));
                    ++i;
                    break;
            }
        }

        return rows;
    }

    // "diff --git a/src/old.txt b/src/new.txt" becomes "src/new.txt"
    private static string FileNameFromHeader(string header)
    {
        int separator = header.LastIndexOf(" b/", StringComparison.Ordinal);
        if (separator >= 0)
            return header.Substring(separator + " b/".Length);

        int space = header.LastIndexOf(' ');
        return space >= 0 ? header.Substring(space + 1) : header;
    }
}
