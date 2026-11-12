using System.Text;
using System.Text.RegularExpressions;

namespace Urras.Core.Markdown;

public enum BlockKind
{
    Heading,
    Paragraph,
    Code,
    Quote,
    ListItem,
    Table,
    Rule
}

[Flags]
public enum SpanStyle
{
    None = 0,
    Bold = 1,
    Italic = 2,
    Code = 4,
    Strike = 8,
    Link = 16,
    Image = 32
}

// A run of text with one style; Url is set for links and images, Width for an image given one in HTML
public sealed record MarkdownSpan(string Text, SpanStyle Style, string? Url = null, double? Width = null);

// One block of a Markdown document. Source is the block's original text, used to find changed blocks in a diff.
public sealed class MarkdownBlock
{
    public BlockKind Kind { get; init; }
    public string Source { get; init; } = string.Empty;

    // Heading level 1 to 6, list nesting from 0, or quote depth from 1
    public int Level { get; init; }

    // "1." or "-" for list items, the language for code blocks
    public string? Marker { get; init; }
    public List<MarkdownSpan> Spans { get; init; } = new List<MarkdownSpan>();

    // Code blocks keep their text as it is
    public string Text { get; init; } = string.Empty;

    // Tables: the header row first
    public List<List<List<MarkdownSpan>>> Rows { get; init; } = new List<List<List<MarkdownSpan>>>();
}

// A small Markdown reader for what READMEs use: blocks, tables, and inline styles, links and images
public static partial class MarkdownDocument
{
    private const int MAX_HEADING_LEVEL = 6;
    private const int LIST_INDENT = 2;

    [GeneratedRegex(@"^(?<indent>\s*)(?<marker>[-*+]|\d+[.)])\s+(?<text>.*)$")]
    private static partial Regex ListRegex();

    [GeneratedRegex(@"^\s{0,3}([-*_])(\s*\1){2,}\s*$")]
    private static partial Regex RuleRegex();

    [GeneratedRegex(@"^\s*\|?\s*:?-{3,}:?\s*(\|\s*:?-{3,}:?\s*)*\|?\s*$")]
    private static partial Regex TableSeparatorRegex();

    // A line that starts an HTML block: a block-level tag, a picture, a comment, or a closing tag
    [GeneratedRegex(@"^\s{0,3}(<!--|</?(p|div|details|summary|table|thead|tbody|tr|td|th|img|picture|source|" +
                    @"br|hr|h[1-6]|ul|ol|li|dl|dt|dd|pre|blockquote|section|article|header|footer|nav|aside|" +
                    @"figure|figcaption|center|a)\b)", RegexOptions.IgnoreCase)]
    private static partial Regex HtmlBlockRegex();

    [GeneratedRegex(@"<img\b[^>]*?>", RegexOptions.IgnoreCase)]
    private static partial Regex InlineImageRegex();

    [GeneratedRegex(@"<a\b[^>]*?href\s*=\s*[""']([^""']*)[""'][^>]*>(.*?)</a\s*>", RegexOptions.IgnoreCase)]
    private static partial Regex InlineLinkRegex();

    [GeneratedRegex(@"\b(src|alt)\s*=\s*[""']([^""']*)[""']", RegexOptions.IgnoreCase)]
    private static partial Regex InlineAttributeRegex();

    [GeneratedRegex(@"</?[a-zA-Z][^>]*>")]
    private static partial Regex AnyTagRegex();

    public static List<MarkdownBlock> Parse(string text)
    {
        List<MarkdownBlock> blocks = new List<MarkdownBlock>();
        string[] lines = text.Replace("\r\n", "\n").Split('\n');
        int i = 0;
        while (i < lines.Length)
        {
            string line = lines[i];
            string trimmed = line.Trim();
            if (trimmed.Length == 0)
            {
                ++i;
                continue;
            }

            if (trimmed.StartsWith("```") || trimmed.StartsWith("~~~"))
            {
                i = ReadCode(lines, i, blocks);
                continue;
            }

            if (HtmlBlockRegex().IsMatch(line))
            {
                i = ReadHtml(lines, i, blocks);
                continue;
            }

            int hashes = trimmed.TakeWhile(character => character == '#').Count();
            if (hashes is >= 1 and <= MAX_HEADING_LEVEL && (trimmed.Length == hashes || trimmed[hashes] == ' '))
            {
                blocks.Add(new MarkdownBlock
                {
                    Kind = BlockKind.Heading,
                    Level = hashes,
                    Source = line,
                    Spans = Inline(trimmed[hashes..].Trim().TrimEnd('#').Trim())
                });
                ++i;
                continue;
            }

            if (RuleRegex().IsMatch(line))
            {
                blocks.Add(new MarkdownBlock { Kind = BlockKind.Rule, Source = line });
                ++i;
                continue;
            }

            if (trimmed.StartsWith('>'))
            {
                i = ReadQuote(lines, i, blocks);
                continue;
            }

            Match item = ListRegex().Match(line);
            if (item.Success)
            {
                i = ReadListItem(lines, i, item, blocks);
                continue;
            }

            if (trimmed.Contains('|') && i + 1 < lines.Length && TableSeparatorRegex().IsMatch(lines[i + 1]))
            {
                i = ReadTable(lines, i, blocks);
                continue;
            }

            i = ReadParagraph(lines, i, blocks);
        }

        return blocks;
    }

    // HTML in Markdown (as READMEs use for pictures, <details> and tables) runs to a blank line, as in CommonMark
    private static int ReadHtml(string[] lines, int start, List<MarkdownBlock> blocks)
    {
        int end = start;
        while (end < lines.Length && lines[end].Trim().Length > 0)
            ++end;
        blocks.AddRange(HtmlDocument.Parse(string.Join("\n", lines[start..end])));
        return end;
    }

    private static int ReadCode(string[] lines, int start, List<MarkdownBlock> blocks)
    {
        string fence = lines[start].Trim()[..3];
        string language = lines[start].Trim()[3..].Trim();
        int end = start + 1;
        while (end < lines.Length && !lines[end].Trim().StartsWith(fence))
            ++end;

        blocks.Add(new MarkdownBlock
        {
            Kind = BlockKind.Code,
            Marker = language,
            Text = string.Join('\n', lines[(start + 1)..Math.Min(end, lines.Length)]),
            Source = string.Join('\n', lines[start..Math.Min(end + 1, lines.Length)])
        });
        return end + 1;
    }

    private static int ReadQuote(string[] lines, int start, List<MarkdownBlock> blocks)
    {
        int end = start;
        StringBuilder text = new StringBuilder();
        while (end < lines.Length && lines[end].Trim().StartsWith('>'))
        {
            text.Append(lines[end].Trim().TrimStart('>').Trim()).Append(' ');
            ++end;
        }

        blocks.Add(new MarkdownBlock
        {
            Kind = BlockKind.Quote,
            Level = 1,
            Source = string.Join('\n', lines[start..end]),
            Spans = Inline(text.ToString().Trim())
        });
        return end;
    }

    private static int ReadListItem(string[] lines, int start, Match item, List<MarkdownBlock> blocks)
    {
        // Lines that follow and are indented belong to the same item
        int end = start + 1;
        StringBuilder text = new StringBuilder(item.Groups["text"].Value);
        while (end < lines.Length && lines[end].Trim().Length > 0 && !ListRegex().IsMatch(lines[end]) &&
               lines[end].StartsWith("  "))
        {
            text.Append(' ').Append(lines[end].Trim());
            ++end;
        }

        string marker = item.Groups["marker"].Value;
        blocks.Add(new MarkdownBlock
        {
            Kind = BlockKind.ListItem,
            Level = item.Groups["indent"].Value.Replace("\t", "    ").Length / LIST_INDENT,
            Marker = char.IsDigit(marker[0]) ? marker : "-",
            Source = string.Join('\n', lines[start..end]),
            Spans = Inline(TaskBox(text.ToString()))
        });
        return end;
    }

    // "[x] done" and "[ ] to do" become plain markers
    private static string TaskBox(string text)
    {
        if (text.StartsWith("[x] ", StringComparison.OrdinalIgnoreCase))
            return "[done] " + text[4..];
        return text.StartsWith("[ ] ") ? "[to do] " + text[4..] : text;
    }

    private static int ReadTable(string[] lines, int start, List<MarkdownBlock> blocks)
    {
        List<List<List<MarkdownSpan>>> rows = new List<List<List<MarkdownSpan>>> { Cells(lines[start]) };
        int end = start + 2;
        while (end < lines.Length && lines[end].Contains('|') && lines[end].Trim().Length > 0)
            rows.Add(Cells(lines[end++]));

        blocks.Add(new MarkdownBlock
        {
            Kind = BlockKind.Table,
            Rows = rows,
            Source = string.Join('\n', lines[start..end])
        });
        return end;
    }

    private static List<List<MarkdownSpan>> Cells(string line)
    {
        string row = line.Trim();
        if (row.StartsWith('|'))
            row = row[1..];
        if (row.EndsWith('|'))
            row = row[..^1];
        return row.Split('|').Select(cell => Inline(cell.Trim())).ToList();
    }

    private static int ReadParagraph(string[] lines, int start, List<MarkdownBlock> blocks)
    {
        int end = start;
        List<string> parts = new List<string>();
        while (end < lines.Length && lines[end].Trim().Length > 0 && !StartsBlock(lines, end))
            parts.Add(lines[end++].Trim());
        if (end == start)
            parts.Add(lines[end++].Trim());

        blocks.Add(new MarkdownBlock
        {
            Kind = BlockKind.Paragraph,
            Source = string.Join('\n', lines[start..end]),
            Spans = Inline(string.Join(' ', parts))
        });
        return end;
    }

    private static bool StartsBlock(string[] lines, int index)
    {
        string trimmed = lines[index].Trim();
        return trimmed.StartsWith('#') || trimmed.StartsWith('>') || trimmed.StartsWith("```") ||
               trimmed.StartsWith("~~~") || ListRegex().IsMatch(lines[index]) || RuleRegex().IsMatch(lines[index]) ||
               HtmlBlockRegex().IsMatch(lines[index]);
    }

    // Inline styles: **bold**, *italic* or _italic_, `code`, ~~strike~~, [text](url) and ![alt](url)
    public static List<MarkdownSpan> Inline(string text)
    {
        List<MarkdownSpan> spans = new List<MarkdownSpan>();
        ReadInline(text.Contains('<') ? WithoutHtml(text) : text, SpanStyle.None, spans);
        return Merge(spans);
    }

    // HTML inside a line of Markdown becomes the Markdown for the same thing; other tags are dropped
    private static string WithoutHtml(string text)
    {
        text = InlineImageRegex().Replace(text, image =>
        {
            Dictionary<string, string> attributes = InlineAttributeRegex().Matches(image.Value)
                .ToDictionary(match => match.Groups[1].Value.ToLowerInvariant(), match => match.Groups[2].Value);
            return "![" + attributes.GetValueOrDefault("alt", "image") + "](" +
                   attributes.GetValueOrDefault("src", string.Empty) + ")";
        });
        text = InlineLinkRegex().Replace(text, link => "[" + link.Groups[2].Value + "](" + link.Groups[1].Value + ")");
        text = Regex.Replace(text, @"<br\s*/?>", "\n", RegexOptions.IgnoreCase);
        text = Regex.Replace(text, @"</?(b|strong)>", "**", RegexOptions.IgnoreCase);
        text = Regex.Replace(text, @"</?(i|em)>", "*", RegexOptions.IgnoreCase);
        text = Regex.Replace(text, @"</?(code|kbd)>", "`", RegexOptions.IgnoreCase);
        return System.Net.WebUtility.HtmlDecode(AnyTagRegex().Replace(text, string.Empty));
    }

    private static void ReadInline(string text, SpanStyle style, List<MarkdownSpan> spans)
    {
        StringBuilder plain = new StringBuilder();
        int i = 0;
        void Flush()
        {
            if (plain.Length > 0)
                spans.Add(new MarkdownSpan(plain.ToString(), style));
            plain.Clear();
        }

        while (i < text.Length)
        {
            char character = text[i];
            if (character == '\\' && i + 1 < text.Length)
            {
                plain.Append(text[i + 1]);
                i += 2;
                continue;
            }

            if (character == '`')
            {
                int close = text.IndexOf('`', i + 1);
                if (close > i)
                {
                    Flush();
                    spans.Add(new MarkdownSpan(text[(i + 1)..close], style | SpanStyle.Code));
                    i = close + 1;
                    continue;
                }
            }

            if ((character == '[' || (character == '!' && i + 1 < text.Length && text[i + 1] == '[')) &&
                TryLink(text, i, out int linkEnd, out string label, out string url))
            {
                Flush();
                bool image = character == '!';
                ReadInline(image ? (label.Length > 0 ? label : "image") : label,
                           style | (image ? SpanStyle.Image : SpanStyle.Link), spans);
                if (spans.Count > 0)
                    spans[^1] = spans[^1] with { Url = url };
                i = linkEnd;
                continue;
            }

            (string delimiter, SpanStyle add)? emphasis = Emphasis(text, i);
            if (emphasis is { } found)
            {
                int close = text.IndexOf(found.delimiter, i + found.delimiter.Length, StringComparison.Ordinal);

                // In "**bold *both***" the bold ends at the last two stars of the run
                while (close > 0 && close + found.delimiter.Length < text.Length &&
                       text[close + found.delimiter.Length] == found.delimiter[0])
                    ++close;
                if (close > i + found.delimiter.Length)
                {
                    Flush();
                    ReadInline(text[(i + found.delimiter.Length)..close], style | found.add, spans);
                    i = close + found.delimiter.Length;
                    continue;
                }
            }

            plain.Append(character);
            ++i;
        }

        Flush();
    }

    private static (string, SpanStyle)? Emphasis(string text, int i)
    {
        foreach ((string delimiter, SpanStyle style) in new[]
                 {
                     ("**", SpanStyle.Bold), ("__", SpanStyle.Bold), ("~~", SpanStyle.Strike),
                     ("*", SpanStyle.Italic), ("_", SpanStyle.Italic)
                 })
        {
            if (string.CompareOrdinal(text, i, delimiter, 0, delimiter.Length) != 0)
                continue;

            // Underscores inside words (snake_case) are not emphasis
            if (delimiter[0] == '_' && i > 0 && char.IsLetterOrDigit(text[i - 1]))
                return null;
            int after = i + delimiter.Length;
            if (after < text.Length && !char.IsWhiteSpace(text[after]))
                return (delimiter, style);
            return null;
        }

        return null;
    }

    private static bool TryLink(string text, int start, out int end, out string label, out string url)
    {
        end = 0;
        label = url = string.Empty;
        int open = text[start] == '!' ? start + 1 : start;
        int closeBracket = text.IndexOf(']', open);
        if (closeBracket < 0 || closeBracket + 1 >= text.Length || text[closeBracket + 1] != '(')
            return false;

        int closeParen = text.IndexOf(')', closeBracket);
        if (closeParen < 0)
            return false;

        label = text[(open + 1)..closeBracket];
        url = text[(closeBracket + 2)..closeParen].Split(' ')[0];
        end = closeParen + 1;
        return true;
    }

    private static List<MarkdownSpan> Merge(List<MarkdownSpan> spans)
    {
        List<MarkdownSpan> merged = new List<MarkdownSpan>();
        foreach (MarkdownSpan span in spans)
        {
            if (merged.Count > 0 && merged[^1].Style == span.Style && merged[^1].Url == span.Url)
                merged[^1] = merged[^1] with { Text = merged[^1].Text + span.Text };
            else
                merged.Add(span);
        }

        return merged;
    }

    // Which blocks of each side have no matching block on the other side, by longest common subsequence
    public static (HashSet<int> Removed, HashSet<int> Added) Changes(List<MarkdownBlock> before,
                                                                     List<MarkdownBlock> after)
    {
        int[,] lengths = new int[before.Count + 1, after.Count + 1];
        for (int i = before.Count - 1; i >= 0; --i)
        {
            for (int j = after.Count - 1; j >= 0; --j)
            {
                lengths[i, j] = before[i].Source.Trim() == after[j].Source.Trim()
                    ? lengths[i + 1, j + 1] + 1
                    : Math.Max(lengths[i + 1, j], lengths[i, j + 1]);
            }
        }

        HashSet<int> removed = new HashSet<int>();
        HashSet<int> added = new HashSet<int>();
        int x = 0;
        int y = 0;
        while (x < before.Count && y < after.Count)
        {
            if (before[x].Source.Trim() == after[y].Source.Trim())
            {
                ++x;
                ++y;
            }
            else if (lengths[x + 1, y] >= lengths[x, y + 1])
                removed.Add(x++);
            else
                added.Add(y++);
        }

        while (x < before.Count)
            removed.Add(x++);
        while (y < after.Count)
            added.Add(y++);
        return (removed, added);
    }
}
