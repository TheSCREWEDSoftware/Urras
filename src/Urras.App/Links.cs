using Avalonia;
using Avalonia.Controls;

namespace Urras.App;

// Where Urras's documents live online, and links in the interface that open a page or a folder
public static class Links
{
    public const string REPOSITORY = "https://github.com/TheSCREWEDSoftware/Urras";
    private const string DOCUMENT_PATH = "/blob/main/";

    // A file in the repository, such as docs/oauth.md, on its web page
    public static string Document(string path)
    {
        return REPOSITORY + DOCUMENT_PATH + path;
    }

    public static HyperlinkButton ToPage(string text, string url)
    {
        return Create(text, url, () => SystemShell.OpenUrl(url));
    }

    // Opens the folder in the file explorer, creating it first so the link always goes somewhere
    public static HyperlinkButton ToFolder(string text, string folder)
    {
        return Create(text, folder, () =>
        {
            Directory.CreateDirectory(folder);
            return SystemShell.OpenFolder(folder);
        });
    }

    private static HyperlinkButton Create(string text, string tip, Func<string?> open)
    {
        HyperlinkButton link = new HyperlinkButton { Content = text, Padding = new Thickness(0) };
        ToolTip.SetTip(link, tip);
        link.Click += (_, _) => open();
        return link;
    }
}
