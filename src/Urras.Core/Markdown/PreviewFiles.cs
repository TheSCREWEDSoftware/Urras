namespace Urras.Core.Markdown;

// Files the diff can show as a rendered page with Preview: Markdown, HTML, and CSV or TSV tables
public enum PreviewKind
{
    None,
    Markdown,
    Html,
    Table
}

public static class PreviewFiles
{
    private static readonly Dictionary<string, PreviewKind> Extensions =
        new Dictionary<string, PreviewKind>(StringComparer.OrdinalIgnoreCase)
        {
            [".md"] = PreviewKind.Markdown,
            [".markdown"] = PreviewKind.Markdown,
            [".html"] = PreviewKind.Html,
            [".htm"] = PreviewKind.Html,
            [".xhtml"] = PreviewKind.Html,
            [".csv"] = PreviewKind.Table,
            [".tsv"] = PreviewKind.Table
        };

    public static PreviewKind KindOf(string path)
    {
        return Extensions.GetValueOrDefault(Path.GetExtension(path), PreviewKind.None);
    }

    public static bool CanPreview(string path)
    {
        return KindOf(path) != PreviewKind.None;
    }

    // The file's text as blocks the preview draws, whatever kind of file it is
    public static List<MarkdownBlock> Parse(PreviewKind kind, string path, string text)
    {
        return kind switch
        {
            PreviewKind.Html => HtmlDocument.Parse(text),
            PreviewKind.Table => DelimitedTable.Parse(text, Path.GetExtension(path).Equals(
                ".tsv", StringComparison.OrdinalIgnoreCase) ? '\t' : ','),
            _ => MarkdownDocument.Parse(text)
        };
    }
}
