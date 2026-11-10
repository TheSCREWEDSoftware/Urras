namespace Urras.Core.Settings;

// A file that exists while Urras runs: still there at the next start means Urras didn't close normally
public static class RunMarker
{
    public const string FILE_NAME = "running";

    public static string DefaultPath =>
        Path.Combine(Path.GetDirectoryName(AppSettings.DefaultPath) ?? string.Empty, FILE_NAME);

    // Marks this run as started; returns when the run before it started if it ended without closing normally
    public static DateTime? Start(string? path = null)
    {
        path ??= DefaultPath;
        DateTime? crashed = File.Exists(path) && !StillRunning(path) ? File.GetLastWriteTimeUtc(path) : null;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path) ?? ".");
            File.WriteAllText(path, Environment.ProcessId.ToString(System.Globalization.CultureInfo.InvariantCulture));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // Without the marker the next start just can't tell; nothing else depends on it
        }

        return crashed;
    }

    // A second Urras finds the first one's marker; that one is open, not crashed
    private static bool StillRunning(string path)
    {
        try
        {
            if (!int.TryParse(File.ReadAllText(path).Trim(), out int id) || id == Environment.ProcessId)
                return false;
            using System.Diagnostics.Process process = System.Diagnostics.Process.GetProcessById(id);
            return process.ProcessName.Contains("Urras", StringComparison.OrdinalIgnoreCase) ||
                   process.ProcessName.Contains("dotnet", StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or
                                              ArgumentException or InvalidOperationException)
        {
            return false;
        }
    }

    // Called when Urras closes normally; a second Urras closing leaves the first one's marker alone
    public static void Stop(string? path = null)
    {
        path ??= DefaultPath;
        try
        {
            if (File.Exists(path) && File.ReadAllText(path).Trim() !=
                Environment.ProcessId.ToString(System.Globalization.CultureInfo.InvariantCulture))
                return;
            File.Delete(path);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // The next start will think this run crashed, which only shows a message
        }
    }

    // Whether the crash log was written after the given start, so the crash left its error behind
    public static bool CrashLogWrittenSince(DateTime started, string? crashLog = null)
    {
        crashLog ??= CrashLog.DefaultPath;
        return File.Exists(crashLog) && File.GetLastWriteTimeUtc(crashLog) >= started;
    }
}
