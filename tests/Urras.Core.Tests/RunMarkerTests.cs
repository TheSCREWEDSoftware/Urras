using Urras.Core.Settings;

namespace Urras.Core.Tests;

public static class RunMarkerTests
{
    public static void NoticesARunThatDidntCloseNormally()
    {
        string folder = Path.Combine(Path.GetTempPath(), "urras-marker-" + Guid.NewGuid().ToString("N"));
        string marker = Path.Combine(folder, RunMarker.FILE_NAME);
        string log = Path.Combine(folder, CrashLog.FILE_NAME);
        try
        {
            Assert.True(RunMarker.Start(marker) == null, "first run");
            DateTime? crashed = RunMarker.Start(marker);
            Assert.True(crashed != null, "the run before never stopped");
            Assert.True(!RunMarker.CrashLogWrittenSince(crashed!.Value, log), "and left no log");
            CrashLog.Write(new InvalidOperationException("boom"), "test", log);
            Assert.True(RunMarker.CrashLogWrittenSince(crashed.Value, log), "now it has");

            RunMarker.Stop(marker);
            Assert.True(RunMarker.Start(marker) == null, "closed normally");

            // Another Urras took the marker over: this one closing leaves it for that one
            File.WriteAllText(marker, "1");
            RunMarker.Stop(marker);
            Assert.True(File.Exists(marker), "the other run's marker stays");
        }
        finally
        {
            TestFolders.Delete(folder);
        }
    }
}
