namespace Urras.Core.Markdown;

// Files the diff can show as a rendered page with Preview
public enum PreviewKind
{
    None,
    Markdown,
    Html,
    Table,
    Notebook,
    RestructuredText,
    AsciiDoc
}

public static class PreviewFiles
{
    private static readonly Dictionary<string, PreviewKind> Extensions =
        new Dictionary<string, PreviewKind>(StringComparer.OrdinalIgnoreCase)
        {
            [".md"] = PreviewKind.Markdown,
            [".markdown"] = PreviewKind.Markdown,
            [".mdown"] = PreviewKind.Markdown,
            [".mkd"] = PreviewKind.Markdown,
            [".mkdn"] = PreviewKind.Markdown,
            [".mdx"] = PreviewKind.Markdown,
            [".html"] = PreviewKind.Html,
            [".htm"] = PreviewKind.Html,
            [".xhtml"] = PreviewKind.Html,
            [".csv"] = PreviewKind.Table,
            [".tsv"] = PreviewKind.Table,
            [".ipynb"] = PreviewKind.Notebook,
            [".rst"] = PreviewKind.RestructuredText,
            [".adoc"] = PreviewKind.AsciiDoc,
            [".asciidoc"] = PreviewKind.AsciiDoc
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
            PreviewKind.Notebook => NotebookDocument.Parse(text),
            PreviewKind.RestructuredText => RestructuredTextDocument.Parse(text),
            PreviewKind.AsciiDoc => AsciiDocDocument.Parse(text),
            PreviewKind.Table => DelimitedTable.Parse(text, Path.GetExtension(path).Equals(
                ".tsv", StringComparison.OrdinalIgnoreCase) ? '\t' : ','),
            _ => MarkdownDocument.Parse(text)
        };
    }

    // A picture's path in the repository, with /; null for internet addresses, data: and paths leaving the repository
    public static string? PicturePath(string filePath, string source)
    {
        if (source.Length == 0 || source.Contains("://") || source.StartsWith("data:", StringComparison.Ordinal) ||
            source.StartsWith("//", StringComparison.Ordinal) || source.StartsWith('#'))
            return null;

        string path = Uri.UnescapeDataString(source.Split('?', '#')[0]).Replace('\\', '/');
        string folder = Path.GetDirectoryName(filePath.Replace('\\', '/'))?.Replace('\\', '/') ?? string.Empty;
        List<string> parts = path.StartsWith('/') ? new List<string>()
                                                  : folder.Split('/', StringSplitOptions.RemoveEmptyEntries).ToList();
        foreach (string part in path.Split('/', StringSplitOptions.RemoveEmptyEntries))
        {
            if (part == ".")
                continue;
            if (part != "..")
                parts.Add(part);
            else if (parts.Count == 0)
                return null;
            else
                parts.RemoveAt(parts.Count - 1);
        }

        return parts.Count == 0 ? null : string.Join("/", parts);
    }

    // The bytes of a "data:image/png;base64,…" picture; null for anything else
    public static byte[]? DataPicture(string source)
    {
        const string BASE64 = ";base64,";
        int comma = source.IndexOf(BASE64, StringComparison.Ordinal);
        if (!source.StartsWith("data:image/", StringComparison.Ordinal) || comma < 0)
            return null;
        try
        {
            return Convert.FromBase64String(source[(comma + BASE64.Length)..]);
        }
        catch (FormatException)
        {
            return null;
        }
    }
}
