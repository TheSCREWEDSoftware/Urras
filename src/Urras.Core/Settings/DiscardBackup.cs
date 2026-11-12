using System.Globalization;

namespace Urras.Core.Settings;

// Discarding can't be undone in Git, so the files are copied here first, like GitHub Desktop's move to the bin
public static class DiscardBackup
{
    public const string FOLDER_NAME = "discarded";
    // Copies older than this are removed the next time something is discarded
    public static readonly TimeSpan KEEP = TimeSpan.FromDays(7);

    public static string DefaultFolder =>
        Path.Combine(Path.GetDirectoryName(AppSettings.DefaultPath) ?? string.Empty, FOLDER_NAME);

    // Copies the files as they are now into a folder named after the repository and time; null if none existed
    public static string? Save(string repository, IEnumerable<string> paths, string? root = null,
                               DateTime? now = null)
    {
        root ??= DefaultFolder;
        DateTime time = now ?? DateTime.Now;
        Prune(root, time);

        string name = Path.GetFileName(repository.TrimEnd(Path.DirectorySeparatorChar,
                                                          Path.AltDirectorySeparatorChar));
        string stamp = time.ToString("yyyy-MM-dd HH-mm-ss", CultureInfo.InvariantCulture);
        string folder = Path.Combine(root, name + " " + stamp);
        int copied = 0;
        foreach (string path in paths.Distinct())
        {
            string source = Path.Combine(repository, path);
            if (!File.Exists(source))
                continue;

            try
            {
                string target = Path.Combine(folder, path);
                Directory.CreateDirectory(Path.GetDirectoryName(target) ?? folder);
                File.Copy(source, target, true);
                copied++;
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                // A file that can't be copied is still discarded; the others are kept
            }
        }

        return copied > 0 ? folder : null;
    }

    private static void Prune(string root, DateTime now)
    {
        if (!Directory.Exists(root))
            return;

        foreach (string folder in Directory.GetDirectories(root))
        {
            try
            {
                if (now - Directory.GetCreationTime(folder) > KEEP)
                    Directory.Delete(folder, true);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                // Left for the next time
            }
        }
    }
}
