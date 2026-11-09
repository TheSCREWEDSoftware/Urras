using System.Globalization;

namespace Urras.Core.Settings;

// Writes unexpected errors next to the settings, so a crash or a caught error can be looked into afterwards
public static class CrashLog
{
    public const string FILE_NAME = "crash.log";
    public const long MAX_SIZE = 1024 * 1024;

    public static string DefaultPath =>
        Path.Combine(Path.GetDirectoryName(AppSettings.DefaultPath) ?? string.Empty, FILE_NAME);

    // Appends the error with the time and where it came from; returns the file, or null if it couldn't be written
    public static string? Write(Exception exception, string source, string? path = null)
    {
        return WriteText(exception.ToString(), source, path);
    }

    // The same for a report that isn't an exception, such as Windows' own record of a crash
    public static string? WriteText(string text, string source, string? path = null)
    {
        path ??= DefaultPath;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path) ?? ".");
            // A log that keeps growing is started again rather than filling the disk
            if (File.Exists(path) && new FileInfo(path).Length > MAX_SIZE)
                File.Delete(path);

            string entry = DateTimeOffset.Now.ToString("yyyy-MM-dd HH:mm:ss zzz", CultureInfo.InvariantCulture) +
                           "  " + source + Environment.NewLine + text + Environment.NewLine +
                           Environment.NewLine;
            File.AppendAllText(path, entry);
            return path;
        }
        catch (Exception problem) when (problem is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }
}
