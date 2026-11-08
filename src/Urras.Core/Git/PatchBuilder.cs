using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace Urras.Core.Git;

// Builds a patch from some of a diff's lines (by DiffLine.RawIndex) for git apply
public static partial class PatchBuilder
{
    [GeneratedRegex(@"^@@ -(\d+)(?:,\d+)? \+(\d+)(?:,\d+)? @@(.*)$")]
    private static partial Regex HunkHeaderRegex();

    // reverse is for -R: unselected + lines stay as context and unselected - lines go; null if none chosen
    public static string? Build(string diff, IReadOnlySet<int> selected, bool reverse)
    {
        string[] lines = diff.Split('\n');
        StringBuilder patch = new StringBuilder();

        foreach ((int start, int end) in Files(lines))
        {
            string? file = BuildFile(lines, start, end, selected, reverse);
            if (file != null)
                patch.Append(file);
        }

        return patch.Length == 0 ? null : patch.ToString();
    }

    // The changed lines of the hunk containing a line, for "stage this hunk"
    public static HashSet<int> HunkOf(string diff, int rawIndex)
    {
        string[] lines = diff.Split('\n');
        HashSet<int> hunk = new HashSet<int>();
        if (rawIndex < 0 || rawIndex >= lines.Length)
            return hunk;

        int start = rawIndex;
        while (start >= 0 && !lines[start].StartsWith("@@", StringComparison.Ordinal))
        {
            if (lines[start].StartsWith("diff ", StringComparison.Ordinal))
                return hunk;
            --start;
        }

        if (start < 0)
            return hunk;

        for (int i = start + 1; i < lines.Length && IsHunkBody(lines[i]); ++i)
        {
            if (lines[i].StartsWith('+') || lines[i].StartsWith('-'))
                hunk.Add(i);
        }

        return hunk;
    }

    // Every changed line of the diff
    public static HashSet<int> AllChanges(string diff)
    {
        string[] lines = diff.Split('\n');
        HashSet<int> changes = new HashSet<int>();
        bool inHunk = false;
        for (int i = 0; i < lines.Length; ++i)
        {
            if (lines[i].StartsWith("@@", StringComparison.Ordinal))
                inHunk = true;
            else if (!IsHunkBody(lines[i]))
                inHunk = false;
            else if (inHunk && (lines[i].StartsWith('+') || lines[i].StartsWith('-')))
                changes.Add(i);
        }

        return changes;
    }

    private static bool IsHunkBody(string line)
    {
        return line.Length == 0 || line[0] is ' ' or '+' or '-' or '\\' or '\r';
    }

    // Each file's part of the diff, from its "diff --git" line to the next one
    private static IEnumerable<(int Start, int End)> Files(string[] lines)
    {
        int start = -1;
        for (int i = 0; i < lines.Length; ++i)
        {
            if (!lines[i].StartsWith("diff --git ", StringComparison.Ordinal))
                continue;
            if (start >= 0)
                yield return (start, i);
            start = i;
        }

        if (start >= 0)
            yield return (start, lines.Length);
    }

    private static string? BuildFile(string[] lines, int start, int end, IReadOnlySet<int> selected, bool reverse)
    {
        int firstHunk = start;
        while (firstHunk < end && !lines[firstHunk].StartsWith("@@", StringComparison.Ordinal))
            ++firstHunk;
        if (firstHunk == end)
            return null;

        List<string> header = lines[start..firstHunk].ToList();
        StringBuilder hunks = new StringBuilder();
        bool everyChangeSelected = true;

        int i = firstHunk;
        while (i < end && lines[i].StartsWith("@@", StringComparison.Ordinal))
        {
            int bodyEnd = i + 1;
            while (bodyEnd < end && IsHunkBody(lines[bodyEnd]) && !(bodyEnd == end - 1 && lines[bodyEnd].Length == 0))
                ++bodyEnd;

            (string? hunk, bool all) = BuildHunk(lines, i, bodyEnd, selected, reverse);
            everyChangeSelected &= all;
            if (hunk != null)
                hunks.Append(hunk);
            i = bodyEnd;
        }

        if (hunks.Length == 0)
            return null;

        // A partly staged new or deleted file is no longer created or deleted by the patch
        if (!everyChangeSelected)
            FixPartialHeader(header, reverse);

        return string.Join('\n', header) + '\n' + hunks;
    }

    private static (string? Hunk, bool AllSelected) BuildHunk(string[] lines, int headerIndex, int end,
                                                              IReadOnlySet<int> selected, bool reverse)
    {
        Match match = HunkHeaderRegex().Match(lines[headerIndex].TrimEnd('\r'));
        if (!match.Success)
            return (null, true);

        List<string> body = new List<string>();
        int oldCount = 0;
        int newCount = 0;
        bool anySelected = false;
        bool allSelected = true;
        bool lastKept = false;

        for (int i = headerIndex + 1; i < end; ++i)
        {
            string line = lines[i];
            char marker = line.Length == 0 ? ' ' : line[0];
            if (marker == '\\')
            {
                // "\ No newline at end of file" belongs to the line before it
                if (lastKept)
                    body.Add(line);
                continue;
            }

            bool isSelected = selected.Contains(i);
            string text = line.Length == 0 ? string.Empty : line[1..];
            lastKept = true;
            switch (marker)
            {
                case '+' when isSelected:
                {
                    anySelected = true;
                    body.Add(line);
                    ++newCount;
                    break;
                }
                case '-' when isSelected:
                {
                    anySelected = true;
                    body.Add(line);
                    ++oldCount;
                    break;
                }
                case '+':
                {
                    allSelected = false;
                    if (reverse)
                        AddContext(body, text, ref oldCount, ref newCount);
                    else
                        lastKept = false;
                    break;
                }
                case '-':
                {
                    allSelected = false;
                    if (reverse)
                        lastKept = false;
                    else
                        AddContext(body, text, ref oldCount, ref newCount);
                    break;
                }
                default:
                    AddContext(body, text, ref oldCount, ref newCount);
                    break;
            }
        }

        if (!anySelected)
            return (null, allSelected);

        int oldStart = int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture);
        int newStart = int.Parse(match.Groups[2].Value, CultureInfo.InvariantCulture);
        string header = $"@@ -{oldStart},{oldCount} +{newStart},{newCount} @@{match.Groups[3].Value}";
        return (header + '\n' + string.Join('\n', body) + '\n', allSelected);
    }

    private static void AddContext(List<string> body, string text, ref int oldCount, ref int newCount)
    {
        body.Add(" " + text);
        ++oldCount;
        ++newCount;
    }

    private static void FixPartialHeader(List<string> header, bool reverse)
    {
        string mode = reverse ? "new file mode" : "deleted file mode";
        int modeLine = header.FindIndex(line => line.StartsWith(mode, StringComparison.Ordinal));
        if (modeLine < 0)
            return;

        // The file keeps existing; its blob ids no longer describe the patch, so they go too
        header.RemoveAt(modeLine);
        header.RemoveAll(line => line.StartsWith("index ", StringComparison.Ordinal));

        string other = header.FirstOrDefault(line => line.StartsWith(reverse ? "+++ " : "--- ",
                                                                     StringComparison.Ordinal)) ?? string.Empty;
        string path = other.Length > 6 ? other[6..] : string.Empty;
        for (int i = 0; i < header.Count; ++i)
        {
            if (reverse && header[i].StartsWith("--- /dev/null", StringComparison.Ordinal))
                header[i] = "--- a/" + path;
            else if (!reverse && header[i].StartsWith("+++ /dev/null", StringComparison.Ordinal))
                header[i] = "+++ b/" + path;
        }
    }
}
