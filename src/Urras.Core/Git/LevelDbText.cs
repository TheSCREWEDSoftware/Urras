namespace Urras.Core.Git;

// Reads the stored bytes out of LevelDB files, decompressed, so text in them can be searched
public static class LevelDbText
{
    public const int LOG_BLOCK_SIZE = 32768;
    public const int LOG_HEADER_SIZE = 7;
    public const int BLOCK_TRAILER_SIZE = 5;
    public const int FOOTER_SIZE = 48;
    public const ulong TABLE_MAGIC = 0xdb4775248b80fb57;

    private const int LOG_LENGTH_OFFSET = 4;
    private const int LOG_TYPE_OFFSET = 6;
    private const byte RECORD_FULL = 1;
    private const byte RECORD_FIRST = 2;
    private const byte RECORD_MIDDLE = 3;
    private const byte RECORD_LAST = 4;
    private const byte BLOCK_RAW = 0;
    private const byte BLOCK_SNAPPY = 1;
    private const int MAGIC_SIZE = 8;
    private const int RESTART_SIZE = 4;
    private const int VARINT_BITS = 7;
    private const int VARINT_MORE = 0x80;
    private const int VARINT_VALUE = 0x7f;
    private const int MAX_VARINT_SHIFT = 63;

    // Each chunk of stored data in a .ldb table or a .log file; anything unreadable is returned as it is
    public static IEnumerable<byte[]> Contents(string file)
    {
        byte[] bytes;
        try
        {
            // LevelDB keeps its files open for writing, so they have to be shared both ways to be read
            using FileStream stream = new FileStream(file, FileMode.Open, FileAccess.Read,
                                                     FileShare.ReadWrite | FileShare.Delete);
            using MemoryStream copy = new MemoryStream();
            stream.CopyTo(copy);
            bytes = copy.ToArray();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return Array.Empty<byte[]>();
        }

        if (file.EndsWith(".ldb", StringComparison.OrdinalIgnoreCase) ||
            file.EndsWith(".sst", StringComparison.OrdinalIgnoreCase))
            return TableBlocks(bytes) ?? new List<byte[]> { bytes };
        // The raw log is searched too, in case a record doesn't follow the usual layout
        if (file.EndsWith(".log", StringComparison.OrdinalIgnoreCase))
            return LogRecords(bytes).Append(bytes);
        return new List<byte[]> { bytes };
    }

    // Records in a write-ahead log, joined back together where they were split across 32 KB blocks
    public static List<byte[]> LogRecords(byte[] bytes)
    {
        List<byte[]> records = new List<byte[]>();
        List<byte> pending = new List<byte>();
        int position = 0;
        while (position + LOG_HEADER_SIZE <= bytes.Length)
        {
            int left = LOG_BLOCK_SIZE - position % LOG_BLOCK_SIZE;
            if (left < LOG_HEADER_SIZE)
            {
                position += left;
                continue;
            }

            int length = bytes[position + LOG_LENGTH_OFFSET] | (bytes[position + LOG_LENGTH_OFFSET + 1] << 8);
            byte type = bytes[position + LOG_TYPE_OFFSET];
            int start = position + LOG_HEADER_SIZE;
            if (type == 0 || start + length > bytes.Length || length > left - LOG_HEADER_SIZE)
            {
                // Zero padding or a torn write at the end; skip to the next block
                position += left;
                pending.Clear();
                continue;
            }

            ReadOnlySpan<byte> payload = bytes.AsSpan(start, length);
            switch (type)
            {
                case RECORD_FULL:
                    records.Add(payload.ToArray());
                    break;
                case RECORD_FIRST:
                {
                    pending.Clear();
                    pending.AddRange(payload.ToArray());
                    break;
                }
                case RECORD_MIDDLE:
                    pending.AddRange(payload.ToArray());
                    break;
                case RECORD_LAST:
                {
                    pending.AddRange(payload.ToArray());
                    records.Add(pending.ToArray());
                    pending.Clear();
                    break;
                }
                default:
                    break;
            }

            position = start + length;
        }

        return records;
    }

    // Every data block of a sorted table, decompressed; null when the file isn't a table
    public static List<byte[]>? TableBlocks(byte[] bytes)
    {
        if (bytes.Length < FOOTER_SIZE ||
            BitConverter.ToUInt64(bytes, bytes.Length - MAGIC_SIZE) != TABLE_MAGIC || !BitConverter.IsLittleEndian)
            return null;

        try
        {
            int footer = bytes.Length - FOOTER_SIZE;
            ReadVarint(bytes, ref footer); // The meta index block holds no data
            ReadVarint(bytes, ref footer);
            long indexOffset = (long)ReadVarint(bytes, ref footer);
            long indexSize = (long)ReadVarint(bytes, ref footer);
            byte[] index = ReadBlock(bytes, indexOffset, indexSize);

            List<byte[]> blocks = new List<byte[]>();
            foreach (byte[] handle in BlockValues(index))
            {
                int at = 0;
                long offset = (long)ReadVarint(handle, ref at);
                long size = (long)ReadVarint(handle, ref at);
                blocks.Add(ReadBlock(bytes, offset, size));
            }

            return blocks;
        }
        catch (Exception exception) when (exception is FormatException or ArgumentException or
                                              IndexOutOfRangeException or OverflowException)
        {
            return null;
        }
    }

    private static byte[] ReadBlock(byte[] bytes, long offset, long size)
    {
        if (offset < 0 || size < 0 || offset + size + BLOCK_TRAILER_SIZE > bytes.Length)
            throw new FormatException("A block runs past the end of the table.");

        ReadOnlySpan<byte> block = bytes.AsSpan((int)offset, (int)size);
        return bytes[offset + size] switch
        {
            BLOCK_RAW => block.ToArray(),
            BLOCK_SNAPPY => Snappy.Decompress(block),
            _ => throw new FormatException("The block uses a compression this reader doesn't know.")
        };
    }

    // The values of a block's entries; keys share prefixes, but only the values are needed here
    private static List<byte[]> BlockValues(byte[] block)
    {
        if (block.Length < RESTART_SIZE)
            throw new FormatException("The block is too short.");

        int restarts = BitConverter.ToInt32(block, block.Length - RESTART_SIZE);
        long end = block.Length - RESTART_SIZE - (long)restarts * RESTART_SIZE;
        if (restarts < 0 || end < 0)
            throw new FormatException("The block's restart list is broken.");

        List<byte[]> values = new List<byte[]>();
        int position = 0;
        while (position < end)
        {
            ReadVarint(block, ref position); // Bytes shared with the previous key
            int unshared = checked((int)ReadVarint(block, ref position));
            int length = checked((int)ReadVarint(block, ref position));
            position = checked(position + unshared);
            if (position + length > end)
                throw new FormatException("An entry runs past the end of the block.");
            values.Add(block.AsSpan(position, length).ToArray());
            position += length;
        }

        return values;
    }

    private static ulong ReadVarint(byte[] bytes, ref int position)
    {
        ulong value = 0;
        for (int shift = 0; shift <= MAX_VARINT_SHIFT; shift += VARINT_BITS)
        {
            byte next = bytes[position++];
            value |= (ulong)(next & VARINT_VALUE) << shift;
            if ((next & VARINT_MORE) == 0)
                return value;
        }

        throw new FormatException("A number in the table is too long.");
    }
}
