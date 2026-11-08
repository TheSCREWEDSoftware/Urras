using System.Text;

namespace Urras.Core.Git;

public enum ConflictChoice
{
    Unresolved,
    Ours,
    Theirs,
    OursThenTheirs,
    TheirsThenOurs,

    // Written by hand in the conflict view
    Custom
}

// A piece of a conflicted file: lines both sides agree on, or a conflict between them
public sealed class ConflictBlock
{
    internal ConflictBlock(List<string> common)
    {
        Common = common;
    }

    internal ConflictBlock(List<string> ours, List<string>? baseLines, List<string> theirs, string baseLabel)
    {
        Ours = ours;
        Base = baseLines;
        Theirs = theirs;
        BaseLabel = baseLabel;
    }

    // What git wrote after ||||||| in diff3 style, usually the merge base's commit
    public string BaseLabel { get; } = string.Empty;

    public List<string>? Common { get; }
    public List<string>? Ours { get; }
    public List<string>? Base { get; }
    public List<string>? Theirs { get; }
    public bool IsConflict => Common == null;
    public ConflictChoice Choice { get; set; }

    // The hand-written result when Choice is Custom
    public List<string> Custom { get; private set; } = new List<string>();

    // Lines typed by the user replace the conflict; their line endings follow the file's
    public void SetCustom(string text, bool crlf)
    {
        Custom = text.Replace("\r\n", "\n").Split('\n').Select(line => crlf ? line + "\r" : line).ToList();
        Choice = ConflictChoice.Custom;
    }

    public IEnumerable<string> ResolvedLines => Common ?? Choice switch
    {
        ConflictChoice.Ours => Ours!,
        ConflictChoice.Theirs => Theirs!,
        ConflictChoice.OursThenTheirs => Ours!.Concat(Theirs!),
        ConflictChoice.TheirsThenOurs => Theirs!.Concat(Ours!),
        ConflictChoice.Custom => Custom,
        _ => Enumerable.Empty<string>()
    };
}

// A file with conflict markers, split into blocks so each conflict can be resolved on its own
public sealed class ConflictFile
{
    private const string OURS_MARKER = "<<<<<<<";
    private const string BASE_MARKER = "|||||||";
    private const string SPLIT_MARKER = "=======";
    private const string THEIRS_MARKER = ">>>>>>>";

    private ConflictFile(List<ConflictBlock> blocks, string oursLabel, string theirsLabel, bool endsWithNewline)
    {
        Blocks = blocks;
        OursLabel = oursLabel;
        TheirsLabel = theirsLabel;
        EndsWithNewline = endsWithNewline;
    }

    public List<ConflictBlock> Blocks { get; }

    // What git wrote after the markers, such as "HEAD" and "a1b2c3d (Add feature)"
    public string OursLabel { get; }
    public string TheirsLabel { get; }
    public bool EndsWithNewline { get; }

    // The file uses CRLF line endings, which hand-written results follow
    public bool UsesCrlf => Blocks.SelectMany(block => block.Common ?? block.Ours ?? new List<string>())
                                  .Any(line => line.EndsWith('\r'));

    // Conflict markers left in text written by hand, which would be committed by mistake
    public static bool HasMarkers(string text)
    {
        return text.Replace("\r\n", "\n").Split('\n').Any(line => IsMarker(line, OURS_MARKER) ||
                                                                    IsMarker(line, THEIRS_MARKER));
    }

    public IEnumerable<ConflictBlock> Conflicts => Blocks.Where(block => block.IsConflict);
    public bool IsResolved => Conflicts.All(block => block.Choice != ConflictChoice.Unresolved);

    // Lines keep their CR, so CRLF files are written back as they were. Returns null without any conflict.
    public static ConflictFile? Parse(string text)
    {
        bool endsWithNewline = text.EndsWith('\n');
        List<string> lines = text.Split('\n').ToList();
        if (endsWithNewline)
            lines.RemoveAt(lines.Count - 1);

        List<ConflictBlock> blocks = new List<ConflictBlock>();
        List<string> common = new List<string>();
        string oursLabel = string.Empty;
        string theirsLabel = string.Empty;
        int i = 0;

        while (i < lines.Count)
        {
            if (!IsMarker(lines[i], OURS_MARKER))
            {
                common.Add(lines[i++]);
                continue;
            }

            // Find the rest of this conflict; an unfinished one is treated as plain text
            int start = i;
            int baseStart = -1;
            int split = -1;
            int end = -1;
            for (int j = i + 1; j < lines.Count; ++j)
            {
                if (split < 0 && baseStart < 0 && IsMarker(lines[j], BASE_MARKER))
                    baseStart = j;
                else if (split < 0 && IsMarker(lines[j], SPLIT_MARKER))
                    split = j;
                else if (split >= 0 && IsMarker(lines[j], THEIRS_MARKER))
                {
                    end = j;
                    break;
                }
            }

            if (split < 0 || end < 0)
            {
                common.AddRange(lines.Skip(i));
                break;
            }

            if (common.Count > 0)
            {
                blocks.Add(new ConflictBlock(common));
                common = new List<string>();
            }

            oursLabel = oursLabel.Length > 0 ? oursLabel : Label(lines[start], OURS_MARKER);
            theirsLabel = theirsLabel.Length > 0 ? theirsLabel : Label(lines[end], THEIRS_MARKER);
            int oursEnd = baseStart >= 0 ? baseStart : split;
            blocks.Add(new ConflictBlock(lines[(start + 1)..oursEnd],
                                         baseStart >= 0 ? lines[(baseStart + 1)..split] : null,
                                         lines[(split + 1)..end],
                                         baseStart >= 0 ? Label(lines[baseStart], BASE_MARKER) : string.Empty));
            i = end + 1;
        }

        if (common.Count > 0)
            blocks.Add(new ConflictBlock(common));

        return blocks.Any(block => block.IsConflict)
            ? new ConflictFile(blocks, oursLabel, theirsLabel, endsWithNewline)
            : null;
    }

    // The file with every conflict replaced by its chosen side; unresolved conflicts keep their markers
    public string Render()
    {
        StringBuilder text = new StringBuilder();
        List<string> output = new List<string>();
        foreach (ConflictBlock block in Blocks)
        {
            if (!block.IsConflict || block.Choice != ConflictChoice.Unresolved)
            {
                output.AddRange(block.ResolvedLines);
                continue;
            }

            output.Add((OURS_MARKER + " " + OursLabel).TrimEnd());
            output.AddRange(block.Ours!);
            if (block.Base != null)
            {
                output.Add((BASE_MARKER + " " + block.BaseLabel).TrimEnd());
                output.AddRange(block.Base);
            }

            output.Add(SPLIT_MARKER);
            output.AddRange(block.Theirs!);
            output.Add((THEIRS_MARKER + " " + TheirsLabel).TrimEnd());
        }

        text.AppendJoin('\n', output);
        if (EndsWithNewline && output.Count > 0)
            text.Append('\n');
        return text.ToString();
    }

    public void ChooseAll(ConflictChoice choice)
    {
        foreach (ConflictBlock block in Conflicts)
            block.Choice = choice;
    }

    private static bool IsMarker(string line, string marker)
    {
        string text = line.TrimEnd('\r');
        return text.StartsWith(marker, StringComparison.Ordinal)
               && (text.Length == marker.Length || text[marker.Length] == ' ');
    }

    private static string Label(string line, string marker)
    {
        return line.TrimEnd('\r')[marker.Length..].Trim();
    }
}
