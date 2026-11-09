using System.Globalization;

namespace Urras.Core.Settings;

// Each start's steps with their times, so a window that never appears shows where starting stopped
public static class StartupLog
{
    public const string FILE_NAME = "startup.log";
    private static readonly object Lock = new object();
    private static DateTime _started = DateTime.UtcNow;

    public static string DefaultPath =>
        Path.Combine(Path.GetDirectoryName(AppSettings.DefaultPath) ?? string.Empty, FILE_NAME);

    // Starts the file again, so it only ever holds the latest start
    public static void Begin(string? path = null)
    {
        _started = DateTime.UtcNow;
        Write("Urras " + Environment.ProcessId + " starting " +
              DateTimeOffset.Now.ToString("yyyy-MM-dd HH:mm:ss zzz", CultureInfo.InvariantCulture), path, false);
    }

    // "+1.234s  Main window created"
    public static void Step(string step, string? path = null)
    {
        string elapsed = (DateTime.UtcNow - _started).TotalSeconds.ToString("0.000", CultureInfo.InvariantCulture);
        Write("+" + elapsed + "s  " + step, path, true);
    }

    private static void Write(string line, string? path, bool append)
    {
        path ??= DefaultPath;
        lock (Lock)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path) ?? ".");
                if (append)
                    File.AppendAllText(path, line + Environment.NewLine);
                else
                    File.WriteAllText(path, line + Environment.NewLine);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                // The log only helps find problems; starting doesn't depend on it
            }
        }
    }
}
