using System.Text;
using Urras.Core.Git;

namespace Urras.Core.Tests;

public static class DesktopForkTests
{
    // V8's ValueSerializer, by hand: one-byte and two-byte strings, small integers and objects
    private static IEnumerable<byte> OneByte(string text)
    {
        return new[] { (byte)'"', (byte)text.Length }.Concat(Encoding.Latin1.GetBytes(text));
    }

    private static IEnumerable<byte> TwoByte(string text)
    {
        byte[] utf16 = Encoding.Unicode.GetBytes(text);
        return new[] { (byte)'c', (byte)utf16.Length }.Concat(utf16);
    }

    private static IEnumerable<byte> Integer(int value)
    {
        return new[] { (byte)'I', (byte)(value << 1) };
    }

    private static IEnumerable<byte> Record(int id, IEnumerable<byte> path, string? target)
    {
        List<byte> bytes = new List<byte> { 0xFF, 0x14, 0xFF, 0x0F, (byte)'o' };
        bytes.AddRange(OneByte("id"));
        bytes.AddRange(Integer(id));
        bytes.AddRange(OneByte("path"));
        bytes.AddRange(path);
        bytes.AddRange(OneByte("missing"));
        bytes.Add((byte)'F');
        int count = 3;
        if (target != null)
        {
            bytes.AddRange(OneByte("workflowPreferences"));
            bytes.Add((byte)'o');
            bytes.AddRange(OneByte("forkContributionTarget"));
            bytes.AddRange(OneByte(target));
            bytes.AddRange(new[] { (byte)'{', (byte)1 });
            count++;
        }

        bytes.AddRange(new[] { (byte)'{', (byte)count });
        return bytes;
    }

    public static void ReadsV8Objects()
    {
        byte[] bytes = Record(7, OneByte(@"C:\Code\wiki"), "parent").ToArray();
        Dictionary<string, object?> record = V8Values.ObjectsIn(bytes).Single();
        Assert.Equal(7L, record["id"], "integer");
        Assert.Equal(@"C:\Code\wiki", record["path"], "string");
        Assert.Equal(false, record["missing"], "boolean");
        Assert.Equal("parent", ((Dictionary<string, object?>)record["workflowPreferences"]!)["forkContributionTarget"],
                     "nested object");

        // A wrong property count means it wasn't an object after all
        bytes[^1] = 9;
        Assert.Equal(0, V8Values.ObjectsIn(bytes).Count(found => found.ContainsKey("path")), "count checked");
    }

    public static void ReadsGitHubDesktopsForkChoices()
    {
        string appData = Path.Combine(Path.GetTempPath(), "urras-desktop-" + Guid.NewGuid().ToString("N"));
        string database = Path.Combine(appData, "GitHub Desktop", "IndexedDB", "file__0.indexeddb.leveldb");
        Directory.CreateDirectory(database);
        try
        {
            // Junk around the records, as in a real file; the later file's record for wiki is the newer one
            byte[] older = new byte[] { 1, 2, (byte)'o', 3 }.Concat(Record(1, OneByte(@"C:\Code\wiki"), "parent"))
                .Concat(Record(2, TwoByte(@"C:\Código\core"), "self"))
                .Concat(Record(3, OneByte(@"C:\Code\plain"), null)).Concat(new byte[] { 9, 9 }).ToArray();
            File.WriteAllBytes(Path.Combine(database, "000003.bin"), older);
            File.WriteAllBytes(Path.Combine(database, "000005.bin"),
                               Record(1, OneByte(@"C:\Code\wiki\"), "self").ToArray());

            Dictionary<string, ForkTarget> choices = RepositoryImport.GitHubDesktopForkChoices(appData);
            Assert.Equal(ForkTarget.Own, choices[@"C:\Code\wiki"], "newer file wins, trailing slash ignored");
            Assert.Equal(ForkTarget.Own, choices[@"C:\Código\core"], "two-byte string");
            Assert.True(!choices.ContainsKey(@"C:\Code\plain"), "no choice made, none taken");
        }
        finally
        {
            TestFolders.Delete(appData);
        }
    }
}
