using System.Text.Json;

namespace Urras.Core.Markdown;

// A Jupyter notebook as a page: Markdown cells drawn, code cells as code, and their text and picture output
public static class NotebookDocument
{
    // Output longer than this is cut, so a cell that printed a lot doesn't bury the rest
    private const int MAX_OUTPUT_CHARACTERS = 4000;

    public static List<MarkdownBlock> Parse(string json)
    {
        List<MarkdownBlock> blocks = new List<MarkdownBlock>();
        JsonElement root;
        try
        {
            root = JsonDocument.Parse(json).RootElement;
        }
        catch (JsonException)
        {
            return blocks;
        }

        if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("cells", out JsonElement cells) ||
            cells.ValueKind != JsonValueKind.Array)
            return blocks;

        string language = Language(root);
        foreach (JsonElement cell in cells.EnumerateArray())
        {
            string kind = cell.TryGetProperty("cell_type", out JsonElement type) ? type.GetString() ?? "" : "";
            string source = Text(cell, "source");
            if (kind == "markdown")
                blocks.AddRange(MarkdownDocument.Parse(source));
            else if (kind == "code")
            {
                if (source.Trim().Length > 0)
                    blocks.Add(new MarkdownBlock { Kind = BlockKind.Code, Marker = language, Text = source,
                                                   Source = "code:" + source });
                Outputs(cell, blocks);
            }
            else if (source.Trim().Length > 0)
                blocks.Add(new MarkdownBlock { Kind = BlockKind.Code, Text = source, Source = "raw:" + source });
        }

        return blocks;
    }

    private static string Language(JsonElement root)
    {
        if (root.TryGetProperty("metadata", out JsonElement metadata))
        {
            if (metadata.TryGetProperty("language_info", out JsonElement info) &&
                info.TryGetProperty("name", out JsonElement name))
                return name.GetString() ?? string.Empty;
            if (metadata.TryGetProperty("kernelspec", out JsonElement kernel) &&
                kernel.TryGetProperty("language", out JsonElement kernelLanguage))
                return kernelLanguage.GetString() ?? string.Empty;
        }

        return string.Empty;
    }

    // Printed text and results as quoted text, and pictures (PNG or JPEG) as pictures
    private static void Outputs(JsonElement cell, List<MarkdownBlock> blocks)
    {
        if (!cell.TryGetProperty("outputs", out JsonElement outputs) || outputs.ValueKind != JsonValueKind.Array)
            return;

        foreach (JsonElement output in outputs.EnumerateArray())
        {
            if (Picture(output) is { } picture)
            {
                blocks.Add(picture);
                continue;
            }

            string text = output.TryGetProperty("ename", out JsonElement error)
                ? error.GetString() + ": " + Text(output, "evalue")
                : output.TryGetProperty("data", out JsonElement data) ? Text(data, "text/plain")
                                                                       : Text(output, "text");
            text = text.TrimEnd();
            if (text.Length == 0)
                continue;
            if (text.Length > MAX_OUTPUT_CHARACTERS)
                text = text[..MAX_OUTPUT_CHARACTERS] + "\n…";
            blocks.Add(new MarkdownBlock { Kind = BlockKind.Code, Marker = "output", Text = text,
                                           Source = "output:" + text });
        }
    }

    // A PNG or JPEG the cell drew, as a picture with a data: address
    private static MarkdownBlock? Picture(JsonElement output)
    {
        if (!output.TryGetProperty("data", out JsonElement data))
            return null;
        foreach (string type in new[] { "image/png", "image/jpeg" })
        {
            string base64 = Text(data, type).Replace("\n", string.Empty);
            if (base64.Length == 0)
                continue;
            return new MarkdownBlock
            {
                Kind = BlockKind.Paragraph,
                Source = "picture:" + base64,
                Spans = new List<MarkdownSpan>
                {
                    new MarkdownSpan("output", SpanStyle.Image, "data:" + type + ";base64," + base64)
                }
            };
        }

        return null;
    }

    // Notebook text is a string or a list of lines
    private static string Text(JsonElement element, string property)
    {
        if (!element.TryGetProperty(property, out JsonElement value))
            return string.Empty;
        return value.ValueKind switch
        {
            JsonValueKind.String => value.GetString() ?? string.Empty,
            JsonValueKind.Array => string.Concat(value.EnumerateArray().Select(line => line.ValueKind ==
                                                     JsonValueKind.String ? line.GetString() : string.Empty)),
            _ => string.Empty
        };
    }
}
