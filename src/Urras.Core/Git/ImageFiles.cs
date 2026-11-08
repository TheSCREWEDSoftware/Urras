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
