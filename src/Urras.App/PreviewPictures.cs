using Urras.Core.Markdown;
using Urras.Core.Settings;

namespace Urras.App;

// Finds a previewed file's pictures: in the repository at the version shown, or online when Options allows it
public static class PreviewPictures
{
    private const int TIMEOUT_SECONDS = 10;
    // Larger downloads are left out, so a huge picture can't stall the preview
    private const long MAX_DOWNLOAD_BYTES = 10 * 1024 * 1024;

    // Pictures from the internet are fetched once while Urras runs, not again for every refresh or file
    private const int MAX_CACHED = 200;

    private static readonly HttpClient Http = new HttpClient { Timeout = TimeSpan.FromSeconds(TIMEOUT_SECONDS) };
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, Task<byte[]?>> Downloads =
        new System.Collections.Concurrent.ConcurrentDictionary<string, Task<byte[]?>>(StringComparer.Ordinal);

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
                return settings.PreviewImages == PreviewImages.Internet ? await CachedDownloadAsync(source) : null;
            string? path = PreviewFiles.PicturePath(filePath, source);
            return path == null ? null : await (newSide ? newVersion : oldVersion)(path);
        };
    }

    private static Task<byte[]?> CachedDownloadAsync(string address)
    {
        if (Downloads.Count > MAX_CACHED)
            Downloads.Clear();
        Task<byte[]?> download = Downloads.GetOrAdd(address, DownloadAsync);
        // A failed download is tried again next time rather than remembered
        download.ContinueWith(_ => Downloads.TryRemove(address, out Task<byte[]?>? _),
                              TaskContinuationOptions.NotOnRanToCompletion);
        return download;
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
