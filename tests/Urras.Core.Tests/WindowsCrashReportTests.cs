using Urras.Core.Settings;

namespace Urras.Core.Tests;

public static class WindowsCrashReportTests
{
    public static void AsksForCrashesSinceTheRunStarted()
    {
        string query = WindowsCrashReport.Query(new DateTime(2026, 11, 9, 22, 34, 5, DateTimeKind.Utc));
        Assert.True(query.Contains("TimeCreated[@SystemTime>='2026-11-09T22:34:05.000Z']"), "from the start");
        Assert.True(query.Contains(".NET Runtime") && query.Contains("Application Error"), "both reports");
    }

    public static void KeepsOnlyUrrassEvents()
    {
        string text = "Event[0]:\n  Source: .NET Runtime\n  Description:\nApplication: Urras.exe\n" +
                      "Description: The process was terminated due to stack overflow.\n\n" +
                      "Event[1]:\n  Source: Application Error\n  Description:\n" +
                      "Faulting application name: game.exe\n\n" +
                      "Event[2]:\n  Source: Application Error\n  Description:\nFaulting application name: Urras.exe\n" +
                      "Faulting module name: libSkiaSharp.dll\n";
        List<string> events = WindowsCrashReport.AboutUrras(text);
        Assert.Equal(2, events.Count, "the other program's crash is left out");
        Assert.True(events[0].Contains("stack overflow"), ".NET's report");
        Assert.True(events[1].Contains("libSkiaSharp.dll"), "the faulting module");
        Assert.Equal(0, WindowsCrashReport.AboutUrras(string.Empty).Count, "nothing recorded");
    }
}
