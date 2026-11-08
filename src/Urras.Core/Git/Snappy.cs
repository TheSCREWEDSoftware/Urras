namespace Urras.Core.Git;

// Decompresses Snappy, the compression LevelDB uses for its table blocks
public static class Snappy
{
    public const int MAX_OUTPUT = 64 * 1024 * 1024;

    private const int TAG_KIND = 0b11;
    private const int TAG_LITERAL = 0;
    private const int TAG_COPY_1 = 1;
    private const int TAG_COPY_2 = 2;
    private const int TAG_COPY_4 = 3;
    private const int TAG_SHIFT = 2;
    private const int LONG_LITERAL = 60;
    private const int COPY_1_LENGTH_MASK = 0b111;
    private const int COPY_1_MIN_LENGTH = 4;
    private const int COPY_1_OFFSET_SHIFT = 5;
    private const int BYTE_BITS = 8;
    private const int VARINT_BITS = 7;
    private const int VARINT_MORE = 0x80;
    private const int VARINT_VALUE = 0x7f;
    private const int MAX_VARINT_SHIFT = 28;

    public static byte[] Decompress(ReadOnlySpan<byte> input)
    {
        int position = 0;
        int length = ReadLength(input, ref position);
        if (length > MAX_OUTPUT)
            throw new FormatException("The compressed block claims to be too large.");

        byte[] output = new byte[length];
        int written = 0;
        while (position < input.Length)
        {
            int tag = input[position++];
            switch (tag & TAG_KIND)
            {
                case TAG_LITERAL:
                {
                    int size = tag >> TAG_SHIFT;
                    if (size >= LONG_LITERAL)
                    {
                        // The length follows in 1 to 4 little-endian bytes
                        int bytes = size - LONG_LITERAL + 1;
                        size = (int)ReadLittleEndian(input, ref position, bytes);
                    }

                    ++size;
                    if (size < 0 || position + size > input.Length || written + size > length)
                        throw new FormatException("A literal runs past the end of the block.");
                    input.Slice(position, size).CopyTo(output.AsSpan(written));
                    position += size;
                    written += size;
                    break;
                }
                case TAG_COPY_1:
                {
                    int size = ((tag >> TAG_SHIFT) & COPY_1_LENGTH_MASK) + COPY_1_MIN_LENGTH;
                    int offset = ((tag >> COPY_1_OFFSET_SHIFT) << BYTE_BITS) | Next(input, ref position);
                    Copy(output, ref written, offset, size);
                    break;
                }
                case TAG_COPY_2:
                {
                    int size = (tag >> TAG_SHIFT) + 1;
                    Copy(output, ref written, (int)ReadLittleEndian(input, ref position, 2), size);
                    break;
                }
                case TAG_COPY_4:
                {
                    int size = (tag >> TAG_SHIFT) + 1;
                    Copy(output, ref written, (int)ReadLittleEndian(input, ref position, 4), size);
                    break;
                }
                default:
                    break;
            }
        }

        if (written != length)
            throw new FormatException("The block is shorter than it claims.");
        return output;
    }

    // Copies byte by byte, since a copy may overlap what it is writing (that is how runs are stored)
    private static void Copy(byte[] output, ref int written, int offset, int size)
    {
        if (offset <= 0 || offset > written || written + size > output.Length)
            throw new FormatException("A copy points outside the block.");
        for (int i = 0; i < size; ++i, ++written)
            output[written] = output[written - offset];
    }

    private static int Next(ReadOnlySpan<byte> input, ref int position)
    {
        if (position >= input.Length)
            throw new FormatException("The block ends in the middle of an element.");
        return input[position++];
    }

    private static uint ReadLittleEndian(ReadOnlySpan<byte> input, ref int position, int bytes)
    {
        uint value = 0;
        for (int i = 0; i < bytes; ++i)
            value |= (uint)Next(input, ref position) << (BYTE_BITS * i);
        return value;
    }

    private static int ReadLength(ReadOnlySpan<byte> input, ref int position)
    {
        int value = 0;
        for (int shift = 0; shift <= MAX_VARINT_SHIFT; shift += VARINT_BITS)
        {
            int next = Next(input, ref position);
            value |= (next & VARINT_VALUE) << shift;
            if ((next & VARINT_MORE) == 0)
                return value < 0 ? throw new FormatException("The block's length is broken.") : value;
        }

        throw new FormatException("The block's length is too long.");
    }
}
