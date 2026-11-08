namespace Urras.Core.Git;

public static class ImageFiles
{
    // Formats the image diff can show; others, such as .tga and .psd, fall back to the text diff
    private static readonly HashSet<string> Extensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        ".png", ".jpg", ".jpeg", ".gif", ".bmp", ".webp", ".ico"
    };

    public static bool IsImage(string path)
    {
        return Extensions.Contains(Path.GetExtension(path));
    }
}

public static class MarkdownFiles
{
    public static bool IsMarkdown(string path)
    {
        string extension = Path.GetExtension(path);
        return extension.Equals(".md", StringComparison.OrdinalIgnoreCase) ||
               extension.Equals(".markdown", StringComparison.OrdinalIgnoreCase);
    }
}
