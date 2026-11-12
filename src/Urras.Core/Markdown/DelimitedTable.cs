using System.Text;

namespace Urras.Core.Markdown;

// CSV and TSV as a table: quoted fields may hold the separator, quotes ("") and line breaks
public static class DelimitedTable
{
    // Rows beyond this are left out of the preview, which says how many there are
    public const int MAX_ROWS = 500;

    // Marks a block as one row of a table drawn together with the rows next to it; level 0 is the header
    public const string ROW_MARKER = "row";

    public static List<MarkdownBlock> Parse(string text, char separator)
    {
        List<List<string>> rows = Rows(text, separator);
        if (rows.Count == 0)
            return new List<MarkdownBlock>();

        List<MarkdownBlock> blocks = new List<MarkdownBlock>();
        // Each row is its own block, so a diff marks just the rows that changed; the first is the header
        for (int index = 0; index < Math.Min(rows.Count, MAX_ROWS); ++index)
        {
            blocks.Add(new MarkdownBlock
            {
                Kind = BlockKind.Table,
                Level = index == 0 ? 0 : 1,
                Marker = ROW_MARKER,
                Source = string.Join("\u001F", rows[index]),
                Rows = new List<List<List<MarkdownSpan>>>
                {
                    rows[index].Select(cell => new List<MarkdownSpan> { new MarkdownSpan(cell, SpanStyle.None) })
                               .ToList()
                }
            });
        }

        if (rows.Count > MAX_ROWS)
        {
            string note = $"Showing the first {MAX_ROWS} of {rows.Count} rows.";
            blocks.Add(new MarkdownBlock
            {
                Kind = BlockKind.Paragraph,
                Source = note,
                Spans = new List<MarkdownSpan> { new MarkdownSpan(note, SpanStyle.Italic) }
            });
        }

        return blocks;
    }

    public static List<List<string>> Rows(string text, char separator)
    {
        List<List<string>> rows = new List<List<string>>();
        List<string> row = new List<string>();
        StringBuilder field = new StringBuilder();
        bool quoted = false;
        bool any = false;

        for (int i = 0; i < text.Length; ++i)
        {
            char c = text[i];
            if (quoted)
            {
                if (c == '"' && i + 1 < text.Length && text[i + 1] == '"')
                {
                    field.Append('"');
                    ++i;
                }
                else if (c == '"')
                    quoted = false;
                else
                    field.Append(c);
                continue;
            }

            if (c == '"' && field.Length == 0)
            {
                quoted = true;
                any = true;
            }
            else if (c == separator)
            {
                row.Add(field.ToString());
                field.Clear();
                any = true;
            }
            else if (c == '\n' || c == '\r')
            {
                if (c == '\r' && i + 1 < text.Length && text[i + 1] == '\n')
                    ++i;
                if (any || field.Length > 0)
                {
                    row.Add(field.ToString());
                    rows.Add(row);
                }

                row = new List<string>();
                field.Clear();
                any = false;
            }
            else
            {
                field.Append(c);
                any = true;
            }
        }

        if (any || field.Length > 0)
        {
            row.Add(field.ToString());
            rows.Add(row);
        }

        return rows;
    }
}
