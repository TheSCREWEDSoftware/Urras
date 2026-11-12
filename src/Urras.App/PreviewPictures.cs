using Urras.Core.Markdown;
using Urras.Core.Settings;

namespace Urras.App;

// Finds a previewed file's pictures: in the repository at the version shown, or online when Options allows it
public static class PreviewPictures
{
    private const int TIMEOUT_SECONDS = 10;
    // Larger downloads are left out, so a huge picture can't stall the preview
    private const long MAX_DOWNLOAD_BYTES = 10 * 1024 * 1024;

    private static readonly HttpClient Http = new HttpClient { Timeout = TimeSpan.FromSeconds(TIMEOUT_SECONDS) };

    // oldVersion and newVersion read a file of the repository at each side's version; null when previews show none
    public static MarkdownDiffView.ImageLoader? For(AppSettings settings, string filePath,
                                                    Func<string, Task<byte[]?>> oldVersion,
                                                    Func<string, Task<byte[]?>> newVersion)
    {
        if (settings.PreviewImages == PreviewImages.Off)
            return null;

        return async (source, newSide) =>
        {
            if (PreviewFiles.DataPicture(source) is { } data)
                return data;
            if (source.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
                source.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
                return settings.PreviewImages == PreviewImages.Internet ? await DownloadAsync(source) : null;
            string? path = PreviewFiles.PicturePath(filePath, source);
            return path == null ? null : await (newSide ? newVersion : oldVersion)(path);
        };
    }

    private static async Task<byte[]?> DownloadAsync(string address)
    {
        using HttpResponseMessage response = await Http.GetAsync(address, HttpCompletionOption.ResponseHeadersRead);
        if (!response.IsSuccessStatusCode || response.Content.Headers.ContentLength > MAX_DOWNLOAD_BYTES)
            return null;
        byte[] bytes = await response.Content.ReadAsByteArrayAsync();
        return bytes.Length > MAX_DOWNLOAD_BYTES ? null : bytes;
    }
}
