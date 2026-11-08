using System.Security.Cryptography;
using System.Text;
using Avalonia.Media.Imaging;

namespace Urras.App;

// Profile pictures, downloaded once and kept on disk so History doesn't fetch them again
public static class AvatarCache
{
    private static readonly HttpClient Http = CreateClient();
    private static readonly Dictionary<string, Task<Bitmap?>> Loading = new Dictionary<string, Task<Bitmap?>>();
    private static readonly TimeSpan MaxAge = TimeSpan.FromDays(7);
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(15);

    private static string Folder => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Urras", "avatars");

    private static HttpClient CreateClient()
    {
        HttpClient client = new HttpClient { Timeout = Timeout };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("Urras/0.1");
        return client;
    }

    // One download per address, shared by every row asking for it; null when it can't be had
    public static Task<Bitmap?> GetAsync(string url)
    {
        // Pictures only come over HTTPS, so a platform answer can't point Urras at local files or plain HTTP
        if (!url.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            return Task.FromResult<Bitmap?>(null);

        lock (Loading)
        {
            if (!Loading.TryGetValue(url, out Task<Bitmap?>? task))
            {
                task = LoadAsync(url);
                Loading[url] = task;
            }

            return task;
        }
    }

    private static async Task<Bitmap?> LoadAsync(string url)
    {
        string file = Path.Combine(Folder, Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(url)))[..32]);
        try
        {
            if (!File.Exists(file) || DateTime.UtcNow - File.GetLastWriteTimeUtc(file) > MaxAge)
            {
                byte[] bytes = await Http.GetByteArrayAsync(url);
                Directory.CreateDirectory(Folder);
                await File.WriteAllBytesAsync(file, bytes);
            }

            await using FileStream stream = File.OpenRead(file);
            return new Bitmap(stream);
        }
        catch (Exception exception) when (exception is HttpRequestException or IOException or TaskCanceledException
                                              or ArgumentException or InvalidOperationException
                                              or UnauthorizedAccessException)
        {
            // Offline, or not a picture: initials are shown instead
            return File.Exists(file) ? TryOpen(file) : null;
        }
    }

    private static Bitmap? TryOpen(string file)
    {
        try
        {
            return new Bitmap(file);
        }
        catch (Exception exception) when (exception is IOException or ArgumentException
                                              or InvalidOperationException)
        {
            return null;
        }
    }
}
