using System.Text.RegularExpressions;

namespace Urras.Core.Markdown;

// reStructuredText as READMEs use it: headings, lists, literal and code blocks, images, notes and inline markup
public static partial class RestructuredTextDocument
{
    [GeneratedRegex(@"^([=\-~^""'`#*+:.])\1{2,}\s*$")]
    private static partial Regex AdornmentRegex();

    [GeneratedRegex(@"^(\s*)([*\-+•]|\d+\.|#\.|\(\d+\))\s+(.*)$")]
    private static partial Regex ListRegex();

    [GeneratedRegex(@"^\.\.\s+([\w-]+)::\s*(.*)$")]
    private static partial Regex DirectiveRegex();

    [GeneratedRegex(@"`([^`<]+?)\s*<([^>]+)>`__?")]
    private static partial Regex LinkRegex();

    [GeneratedRegex(@"``([^`]+)``")]
    private static partial Regex LiteralRegex();

    [GeneratedRegex(@":\w+:`([^`]+)`")]
    private static partial Regex RoleRegex();

    public static List<MarkdownBlock> Parse(string text)
    {
        List<MarkdownBlock> blocks = new List<MarkdownBlock>();
        List<char> headingOrder = new List<char>();
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

            // A heading: text underlined (and maybe overlined) with one punctuation character; levels go by order
            int textLine = AdornmentRegex().IsMatch(line) && i + 2 < lines.Length &&
                           AdornmentRegex().IsMatch(lines[i + 2]) ? i + 1 : i;
            if (textLine + 1 < lines.Length && lines[textLine].Trim().Length > 0 &&
                !AdornmentRegex().IsMatch(lines[textLine]) && AdornmentRegex().IsMatch(lines[textLine + 1]) &&
                lines[textLine + 1].Trim().Length >= lines[textLine].Trim().Length)
            {
                char mark = lines[textLine + 1].Trim()[0];
                if (!headingOrder.Contains(mark))
                    headingOrder.Add(mark);
                blocks.Add(new MarkdownBlock
                {
                    Kind = BlockKind.Heading,
                    Level = Math.Min(headingOrder.IndexOf(mark) + 1, 6),
                    Source = lines[textLine],
                    Spans = Inline(lines[textLine].Trim())
                });
                i = textLine + 2;
                continue;
            }

            if (AdornmentRegex().IsMatch(line) && trimmed.Length >= 4)
            {
                blocks.Add(new MarkdownBlock { Kind = BlockKind.Rule, Source = line });
                ++i;
                continue;
            }

            Match directive = DirectiveRegex().Match(trimmed);
            if (directive.Success)
            {
                i = ReadDirective(lines, i, directive, blocks);
                continue;
            }

            // A comment, or a link target, shows nothing
            if (trimmed.StartsWith(".."))
            {
                i = SkipIndented(lines, i + 1);
                continue;
            }

            Match item = ListRegex().Match(line);
            if (item.Success)
            {
                string marker = item.Groups[2].Value is "*" or "-" or "+" or "•" ? "-" : item.Groups[2].Value;
                int end = i + 1;
                List<string> itemText = new List<string> { item.Groups[3].Value };
                while (end < lines.Length && lines[end].Trim().Length > 0 && !ListRegex().IsMatch(lines[end]) &&
                       char.IsWhiteSpace(lines[end][0]))
                    itemText.Add(lines[end++].Trim());
                blocks.Add(new MarkdownBlock
                {
                    Kind = BlockKind.ListItem,
                    Level = item.Groups[1].Value.Length / 2,
                    Marker = marker == "#." ? "•" : marker,
                    Source = string.Join("\n", lines[i..end]),
                    Spans = Inline(string.Join(" ", itemText))
                });
                i = end;
                continue;
            }

            i = ReadParagraph(lines, i, blocks);
        }

        return blocks;
    }

    private static int ReadParagraph(string[] lines, int start, List<MarkdownBlock> blocks)
    {
        int end = start;
        while (end < lines.Length && lines[end].Trim().Length > 0 && !ListRegex().IsMatch(lines[end]) &&
               !(end > start && end + 1 < lines.Length && AdornmentRegex().IsMatch(lines[end + 1])))
            ++end;
        string text = string.Join(" ", lines[start..end].Select(line => line.Trim()));
        bool literal = text.EndsWith("::");
        // "Text::" ends with one colon and starts an indented literal block; a lone "::" shows nothing
        text = literal ? text[..^1].TrimEnd() : text;
        if (text.Length > 0 && text != ":")
            blocks.Add(new MarkdownBlock
            {
                Kind = BlockKind.Paragraph,
                Source = string.Join("\n", lines[start..end]),
                Spans = Inline(text)
            });
        return literal ? ReadIndented(lines, end, null, blocks) : end;
    }

    private static int ReadDirective(string[] lines, int start, Match directive, List<MarkdownBlock> blocks)
    {
        string name = directive.Groups[1].Value.ToLowerInvariant();
        string argument = directive.Groups[2].Value.Trim();
        switch (name)
        {
            case "code-block" or "code" or "sourcecode":
                return ReadIndented(lines, start + 1, argument, blocks);
            case "image" or "figure":
            {
                blocks.Add(new MarkdownBlock
                {
                    Kind = BlockKind.Paragraph,
                    Source = lines[start],
                    Spans = new List<MarkdownSpan> { new MarkdownSpan(argument, SpanStyle.Image, argument) }
                });
                return SkipIndented(lines, start + 1);
            }
            case "note" or "warning" or "tip" or "important" or "caution" or "attention" or "hint" or "danger":
            {
                int end = start + 1;
                List<string> body = new List<string>();
                if (argument.Length > 0)
                    body.Add(argument);
                while (end < lines.Length && (lines[end].Trim().Length == 0 || char.IsWhiteSpace(lines[end][0])))
                    body.Add(lines[end++].Trim());
                List<MarkdownSpan> spans = new List<MarkdownSpan>
                {
                    new MarkdownSpan(char.ToUpperInvariant(name[0]) + name[1..] + ": ", SpanStyle.Bold)
                };
                spans.AddRange(Inline(string.Join(" ", body.Where(part => part.Length > 0))));
                blocks.Add(new MarkdownBlock { Kind = BlockKind.Quote, Level = 1, Spans = spans,
                                               Source = string.Join("\n", lines[start..end]) });
                return end;
            }
            default:
                return SkipIndented(lines, start + 1);
        }
    }

    // An indented block after "::" or a code directive, kept as it is
    private static int ReadIndented(string[] lines, int start, string? language, List<MarkdownBlock> blocks)
    {
        int end = start;
        while (end < lines.Length && lines[end].Trim().StartsWith(':') && lines[end].Trim().Contains(": "))
            ++end;
        int first = end;
        while (end < lines.Length && (lines[end].Trim().Length == 0 || char.IsWhiteSpace(lines[end][0])))
            ++end;
        List<string> body = lines[first..end].ToList();
        while (body.Count > 0 && body[^1].Trim().Length == 0)
            body.RemoveAt(body.Count - 1);
        while (body.Count > 0 && body[0].Trim().Length == 0)
            body.RemoveAt(0);
        int indent = body.Where(line => line.Trim().Length > 0)
                         .Select(line => line.Length - line.TrimStart().Length).DefaultIfEmpty(0).Min();
        string code = string.Join("\n", body.Select(line => line.Length >= indent ? line[indent..] : line.Trim()));
        if (code.Length > 0)
            blocks.Add(new MarkdownBlock { Kind = BlockKind.Code, Marker = language, Text = code, Source = code });
        return end;
    }

    private static int SkipIndented(string[] lines, int start)
    {
        int end = start;
        while (end < lines.Length && lines[end].Length > 0 && char.IsWhiteSpace(lines[end][0]))
            ++end;
        return end;
    }

    // reStructuredText's inline markup turned into Markdown's, which the Markdown reader then draws
    private static List<MarkdownSpan> Inline(string text)
    {
        text = LinkRegex().Replace(text, link => "[" + link.Groups[1].Value + "](" + link.Groups[2].Value + ")");
        text = LiteralRegex().Replace(text, literal => "`" + literal.Groups[1].Value + "`");
        text = RoleRegex().Replace(text, role => "`" + role.Groups[1].Value + "`");
        return MarkdownDocument.Inline(text);
    }
}
