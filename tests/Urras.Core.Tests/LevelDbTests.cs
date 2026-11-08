using System.Text;
using Urras.Core.Git;

namespace Urras.Core.Tests;

public static class LevelDbTests
{
    private const byte SNAPPY_BLOCK = 1;
    private const byte RAW_BLOCK = 0;
    private const int CHECKSUM_SIZE = 4;
    private const int FOOTER_HANDLES_SIZE = 40;
    private const int REPEAT_LENGTH = 9;
    private const byte REPEAT_TAG = 0x15; // Copy 9 bytes from a one-byte offset
    private const byte REPEAT_OFFSET = 3;
    private const int SPLIT_AT = 5;
    private const byte FULL_RECORD = 1;
    private const byte FIRST_RECORD = 2;
    private const byte LAST_RECORD = 4;
    private const int VARINT_BITS = 7;
    private const byte VARINT_VALUE = 0x7f;
    private const byte VARINT_MORE = 0x80;
    private const byte BYTE_MASK = 0xff;
    private const int BYTE_BITS = 8;

    // "abc" then a copy 3 back for 9 bytes, which overlaps what it writes
    private static readonly byte[] RepeatedAbc =
    {
        12, 0x08, (byte)'a', (byte)'b', (byte)'c', REPEAT_TAG, REPEAT_OFFSET
    };

    public static void DecompressesSnappy()
    {
        Assert.Equal("abcabcabcabc", Encoding.ASCII.GetString(Snappy.Decompress(RepeatedAbc)), "overlapping copy");

        bool refused = false;
        try
        {
            Snappy.Decompress(new byte[] { 4, 0x05, 0x09 });
        }
        catch (FormatException)
        {
            refused = true;
        }

        Assert.True(refused, "a copy before any output is refused");
    }

    public static void ReadsPathsFromCompressedTables()
    {
        string appData = Path.Combine(Path.GetTempPath(), "urras-leveldb-" + Guid.NewGuid().ToString("N"));
        try
        {
            string database = Path.Combine(appData, "GitHub Desktop", "IndexedDB", "file__0.indexeddb.leveldb");
            Directory.CreateDirectory(database);

            // The whole path only exists once the block is decompressed: a literal, then a copy of its last bytes
            byte[] literal = Encoding.Latin1.GetBytes(@"C:\Repos\abc");
            byte[] compressed = new[] { (byte)(literal.Length + REPEAT_LENGTH), (byte)((literal.Length - 1) << 2) }
                                .Concat(literal).Concat(new byte[] { REPEAT_TAG, REPEAT_OFFSET }).ToArray();
            File.WriteAllBytes(Path.Combine(database, "000005.ldb"), Table(compressed, SNAPPY_BLOCK));

            HashSet<string> paths = RepositoryImport.GitHubDesktopPaths(appData);
            Assert.True(paths.Contains(@"C:\Repos\abcabcabcabc"), "path inside a Snappy block");
        }
        finally
        {
            Directory.Delete(appData, true);
        }
    }

    public static void JoinsLogRecordsSplitAcrossBlocks()
    {
        string path = @"D:\Games\Forlorn Hope";
        byte[] text = Encoding.Latin1.GetBytes("\u0001" + path + "\u0000");
        int first = SPLIT_AT;
        List<byte> log = new List<byte>();
        int padding = LevelDbText.LOG_BLOCK_SIZE - LevelDbText.LOG_HEADER_SIZE - first;
        log.AddRange(Record(FULL_RECORD, new byte[padding - LevelDbText.LOG_HEADER_SIZE]));
        log.AddRange(Record(FIRST_RECORD, text.Take(first).ToArray()));
        log.AddRange(Record(LAST_RECORD, text.Skip(first).ToArray()));

        List<byte[]> records = LevelDbText.LogRecords(log.ToArray());
        Assert.Equal(2, records.Count, "records");
        Assert.Equal("\u0001" + path + "\u0000", Encoding.Latin1.GetString(records[1]), "joined record");
    }

    public static void LeavesOtherFilesAlone()
    {
        Assert.True(LevelDbText.TableBlocks(new byte[] { 1, 2, 3 }) == null, "too short for a table");
        Assert.Equal(0, LevelDbText.LogRecords(new byte[3]).Count, "too short for a log");
    }

    private static byte[] Record(byte type, byte[] payload)
    {
        List<byte> record = new List<byte>(new byte[CHECKSUM_SIZE]);
        record.Add((byte)(payload.Length & BYTE_MASK));
        record.Add((byte)(payload.Length >> BYTE_BITS));
        record.Add(type);
        record.AddRange(payload);
        return record.ToArray();
    }

    // A table with one data block, an index pointing at it, an empty meta index and the footer
    private static byte[] Table(byte[] data, byte compression)
    {
        List<byte> table = new List<byte>();
        table.AddRange(data);
        table.Add(compression);
        table.AddRange(new byte[CHECKSUM_SIZE]);

        List<byte> handle = Varint(0).Concat(Varint(data.Length)).ToList();
        List<byte> index = new List<byte> { 0, 1, (byte)handle.Count, (byte)'k' };
        index.AddRange(handle);
        index.AddRange(BitConverter.GetBytes(0));
        index.AddRange(BitConverter.GetBytes(1));
        int indexOffset = table.Count;
        table.AddRange(index);
        table.Add(RAW_BLOCK);
        table.AddRange(new byte[CHECKSUM_SIZE]);

        byte[] meta = BitConverter.GetBytes(0).Concat(BitConverter.GetBytes(1)).ToArray();
        int metaOffset = table.Count;
        table.AddRange(meta);
        table.Add(RAW_BLOCK);
        table.AddRange(new byte[CHECKSUM_SIZE]);

        List<byte> footer = Varint(metaOffset).Concat(Varint(meta.Length)).Concat(Varint(indexOffset))
                                              .Concat(Varint(index.Count)).ToList();
        footer.AddRange(new byte[FOOTER_HANDLES_SIZE - footer.Count]);
        footer.AddRange(BitConverter.GetBytes(LevelDbText.TABLE_MAGIC));
        table.AddRange(footer);
        return table.ToArray();
    }

    private static IEnumerable<byte> Varint(long value)
    {
        do
        {
            byte next = (byte)(value & VARINT_VALUE);
            value >>= VARINT_BITS;
            yield return value == 0 ? next : (byte)(next | VARINT_MORE);
        } while (value != 0);
    }
}
