using System.Text.RegularExpressions;

namespace Urras.Core.Markdown;

// AsciiDoc as READMEs use it: headings, lists, code, quotes, notes, pictures, tables and inline markup
public static partial class AsciiDocDocument
{
    [GeneratedRegex(@"^(=+|#+)\s+(.*)$")]
    private static partial Regex HeadingRegex();

    [GeneratedRegex(@"^(\*+|-|\.+)\s+(.*)$")]
    private static partial Regex ListRegex();

    [GeneratedRegex(@"^(NOTE|TIP|IMPORTANT|WARNING|CAUTION):\s+(.*)$")]
    private static partial Regex AdmonitionRegex();

    [GeneratedRegex(@"^image::([^\[]+)\[([^\]]*)\]")]
    private static partial Regex ImageRegex();

    [GeneratedRegex(@"(?:link:)?((?:https?|ftp)://[^\s\[]+)\[([^\]]*)\]")]
    private static partial Regex LinkRegex();

    [GeneratedRegex(@"(?<![\w*])\*([^*\n]+)\*(?![\w*])")]
    private static partial Regex BoldRegex();

    [GeneratedRegex(@"(?<![\w_])_([^_\n]+)_(?![\w_])")]
    private static partial Regex ItalicRegex();

    [GeneratedRegex(@"^\[source(?:,\s*([\w+#-]+))?.*\]$")]
    private static partial Regex SourceRegex();

    public static List<MarkdownBlock> Parse(string text)
    {
        List<MarkdownBlock> blocks = new List<MarkdownBlock>();
        string[] lines = text.Replace("\r\n", "\n").Split('\n');
        string? language = null;
        int i = 0;
        while (i < lines.Length)
        {
            string line = lines[i];
            string trimmed = line.Trim();
            // Blank lines, comments and attributes (":toc:") show nothing
            if (trimmed.Length == 0 || trimmed.StartsWith("//") ||
                (trimmed.StartsWith(':') && trimmed.IndexOf(':', 1) > 0))
            {
                ++i;
                continue;
            }

            Match source = SourceRegex().Match(trimmed);
            if (source.Success)
            {
                language = source.Groups[1].Success ? source.Groups[1].Value : null;
                ++i;
                continue;
            }

            if (trimmed is "----" or "...." or "____" or "|===")
            {
                int end = i + 1;
                while (end < lines.Length && lines[end].Trim() != trimmed)
                    ++end;
                string body = string.Join("\n", lines[(i + 1)..Math.Min(end, lines.Length)]);
                blocks.AddRange(DelimitedBlock(trimmed, body, language));
                language = null;
                i = end + 1;
                continue;
            }

            Match heading = HeadingRegex().Match(trimmed);
            if (heading.Success)
            {
                blocks.Add(new MarkdownBlock
                {
                    Kind = BlockKind.Heading,
                    Level = Math.Min(heading.Groups[1].Value.Length, 6),
                    Source = line,
                    Spans = Inline(heading.Groups[2].Value)
                });
                ++i;
                continue;
            }

            if (trimmed is "'''" or "---" or "***")
            {
                blocks.Add(new MarkdownBlock { Kind = BlockKind.Rule, Source = line });
                ++i;
                continue;
            }

            Match image = ImageRegex().Match(trimmed);
            if (image.Success)
            {
                string alt = image.Groups[2].Value.Split(',')[0];
                blocks.Add(new MarkdownBlock
                {
                    Kind = BlockKind.Paragraph,
                    Source = line,
                    Spans = new List<MarkdownSpan>
                    {
                        new MarkdownSpan(alt.Length > 0 ? alt : image.Groups[1].Value, SpanStyle.Image,
                                         image.Groups[1].Value)
                    }
                });
                ++i;
                continue;
            }

            Match admonition = AdmonitionRegex().Match(trimmed);
            if (admonition.Success)
            {
                List<MarkdownSpan> spans = new List<MarkdownSpan>
                {
                    new MarkdownSpan(admonition.Groups[1].Value[0] +
                                     admonition.Groups[1].Value[1..].ToLowerInvariant() + ": ", SpanStyle.Bold)
                };
                spans.AddRange(Inline(admonition.Groups[2].Value));
                blocks.Add(new MarkdownBlock { Kind = BlockKind.Quote, Level = 1, Source = line, Spans = spans });
                ++i;
                continue;
            }

            Match item = ListRegex().Match(trimmed);
            if (item.Success)
            {
                string marker = item.Groups[1].Value;
                blocks.Add(new MarkdownBlock
                {
                    Kind = BlockKind.ListItem,
                    Level = marker.Length - 1,
                    Marker = marker[0] == '.' ? "•" : "-",
                    Source = line,
                    Spans = Inline(item.Groups[2].Value)
                });
                ++i;
                continue;
            }

            int stop = i;
            while (stop < lines.Length && lines[stop].Trim().Length > 0 &&
                   !HeadingRegex().IsMatch(lines[stop].Trim()) && !ListRegex().IsMatch(lines[stop].Trim()) &&
                   lines[stop].Trim() is not ("----" or "...." or "____"))
                ++stop;
            stop = Math.Max(stop, i + 1);
            blocks.Add(new MarkdownBlock
            {
                Kind = BlockKind.Paragraph,
                Source = string.Join("\n", lines[i..stop]),
                Spans = Inline(string.Join(" ", lines[i..stop].Select(part => part.Trim())))
            });
            i = stop;
        }

        return blocks;
    }

    private static IEnumerable<MarkdownBlock> DelimitedBlock(string delimiter, string body, string? language)
    {
        switch (delimiter)
        {
            case "____":
                return new[]
                {
                    new MarkdownBlock
                    {
                        Kind = BlockKind.Quote,
                        Level = 1,
                        Source = body,
                        Spans = Inline(string.Join(" ", body.Split('\n').Select(line => line.Trim())))
                    }
                };
            case "|===":
            {
                // Cells start with |; a row is a line, or the cells of the header's width
                List<List<List<MarkdownSpan>>> rows = body.Split('\n').Where(line => line.Trim().StartsWith('|'))
                    .Select(line => line.Trim()[1..].Split('|').Select(cell => Inline(cell.Trim())).ToList())
                    .ToList();
                return rows.Count == 0
                    ? Array.Empty<MarkdownBlock>()
                    : new[] { new MarkdownBlock { Kind = BlockKind.Table, Rows = rows, Source = body } };
            }
            default:
                return new[]
                {
                    new MarkdownBlock { Kind = BlockKind.Code, Marker = language, Text = body, Source = body }
                };
        }
    }

    // AsciiDoc's inline markup turned into Markdown's, which the Markdown reader then draws
    private static List<MarkdownSpan> Inline(string text)
    {
        text = LinkRegex().Replace(text, link => "[" + (link.Groups[2].Value.Length > 0 ? link.Groups[2].Value
                                                                                         : link.Groups[1].Value) +
                                                 "](" + link.Groups[1].Value + ")");
        text = BoldRegex().Replace(text, bold => "**" + bold.Groups[1].Value + "**");
        text = ItalicRegex().Replace(text, italic => "*" + italic.Groups[1].Value + "*");
        return MarkdownDocument.Inline(text);
    }
}
