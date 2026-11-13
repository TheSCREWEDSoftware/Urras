using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Styling;

namespace Urras.App;

// A website's own small icon (its favicon), downloaded once and kept with the profile pictures
public static class SiteIcons
{
    // GitHub's usual icon is a black octocat, which a dark theme would hide, so its light one is used there
    private const string GITHUB_HOST = "github.com";
    private const string GITHUB_DARK_ICON = "https://github.githubassets.com/favicons/favicon-dark.png";
    private const string GITHUB_LIGHT_ICON = "https://github.githubassets.com/favicons/favicon.png";

    // Null when the site has none, can't be reached, or answers with something that isn't a picture
    public static Task<Bitmap?> GetAsync(string host)
    {
        if (host.Length == 0)
            return Task.FromResult<Bitmap?>(null);
        if (host.Equals(GITHUB_HOST, StringComparison.OrdinalIgnoreCase))
            return AvatarCache.GetAsync(IsDark() ? GITHUB_DARK_ICON : GITHUB_LIGHT_ICON);
        return AvatarCache.GetAsync("https://" + host + "/favicon.ico");
    }

    private static bool IsDark()
    {
        return Application.Current?.ActualThemeVariant != ThemeVariant.Light;
    }
}
