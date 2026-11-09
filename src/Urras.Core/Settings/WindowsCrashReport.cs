using System.Diagnostics;
using System.Globalization;

namespace Urras.Core.Settings;

// Urras's crashes from Windows' Application log, for those .NET can't log itself (stack overflow, native faults)
public static class WindowsCrashReport
{
    private const int MAX_EVENTS = 20;
    private const int WAIT_MILLISECONDS = 10000;
    private const string EVENT_START = "Event[";
    private const string APP_NAME = "Urras";

    // The events that describe a crashed program: .NET's own report and Windows' report of the fault
    public static string Query(DateTime sinceUtc)
    {
        return "*[System[(Provider[@Name='.NET Runtime'] or Provider[@Name='Application Error']) and " +
               "TimeCreated[@SystemTime>='" +
               sinceUtc.ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", CultureInfo.InvariantCulture) + "']]]";
    }

    // wevtutil's text lists events one after another, each starting "Event[n]:"; only Urras's are kept
    public static List<string> AboutUrras(string text)
    {
        List<string> events = new List<string>();
        int start = text.IndexOf(EVENT_START, StringComparison.Ordinal);
        while (start >= 0)
        {
            int next = text.IndexOf(Environment.NewLine + EVENT_START, start + 1, StringComparison.Ordinal);
            if (next < 0)
                next = text.IndexOf("\n" + EVENT_START, start + 1, StringComparison.Ordinal);
            string entry = (next < 0 ? text[start..] : text[start..next]).Trim();
            if (entry.Contains(APP_NAME, StringComparison.OrdinalIgnoreCase))
                events.Add(entry);
            start = next < 0 ? -1 : text.IndexOf(EVENT_START, next, StringComparison.Ordinal);
        }

        return events;
    }

    // Urras's crash reports since the given time, newest first; null when there are none or this isn't Windows
    public static async Task<string?> CollectAsync(DateTime sinceUtc)
    {
        if (!OperatingSystem.IsWindows())
            return null;

        ProcessStartInfo start = new ProcessStartInfo("wevtutil")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        foreach (string argument in new[]
                 {
                     "qe", "Application", "/q:" + Query(sinceUtc), "/c:" + MAX_EVENTS, "/rd:true", "/f:text"
                 })
            start.ArgumentList.Add(argument);

        try
        {
            using Process process = Process.Start(start) ?? throw new InvalidOperationException("wevtutil");
            Task<string> output = process.StandardOutput.ReadToEndAsync();
            using CancellationTokenSource wait = new CancellationTokenSource(WAIT_MILLISECONDS);
            await process.WaitForExitAsync(wait.Token);
            List<string> events = AboutUrras(await output);
            return events.Count == 0 ? null : string.Join(Environment.NewLine + Environment.NewLine, events);
        }
        catch (Exception exception) when (exception is InvalidOperationException or OperationCanceledException or
                                              System.ComponentModel.Win32Exception)
        {
            return null;
        }
    }
}
