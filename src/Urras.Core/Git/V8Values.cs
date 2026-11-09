using System.Text;

namespace Urras.Core.Git;

// Plain values written by V8's ValueSerializer, the format Chromium's IndexedDB stores records in
public static class V8Values
{
    private const byte PADDING = 0x00;
    private const byte VERIFY_OBJECT_COUNT = (byte)'?';
    private const byte THE_HOLE = (byte)'-';
    private const byte UNDEFINED = (byte)'_';
    private const byte NULL = (byte)'0';
    private const byte TRUE = (byte)'T';
    private const byte FALSE = (byte)'F';
    private const byte INT32 = (byte)'I';
    private const byte UINT32 = (byte)'U';
    private const byte DOUBLE = (byte)'N';
    private const byte DATE = (byte)'D';
    private const byte UTF8_STRING = (byte)'S';
    private const byte ONE_BYTE_STRING = (byte)'"';
    private const byte TWO_BYTE_STRING = (byte)'c';
    private const byte OBJECT_REFERENCE = (byte)'^';
    private const byte BEGIN_OBJECT = (byte)'o';
    private const byte END_OBJECT = (byte)'{';
    private const byte BEGIN_SPARSE_ARRAY = (byte)'a';
    private const byte END_SPARSE_ARRAY = (byte)'@';
    private const byte BEGIN_DENSE_ARRAY = (byte)'A';
    private const byte END_DENSE_ARRAY = (byte)'$';

    private const int MAX_DEPTH = 16;
    private const int MAX_LENGTH = 1 << 20;
    private const int DOUBLE_SIZE = 8;
    private const int VARINT_BITS = 7;
    private const int VARINT_MORE = 0x80;
    private const int VARINT_VALUE = 0x7f;
    private const int MAX_VARINT_SHIFT = 35;

    // Every object that reads cleanly; V8 writes each object's property count, so random bytes rarely pass
    public static IEnumerable<Dictionary<string, object?>> ObjectsIn(byte[] bytes)
    {
        int position = 0;
        while (position < bytes.Length)
        {
            int start = Array.IndexOf(bytes, BEGIN_OBJECT, position);
            if (start < 0)
                yield break;

            int end = start;
            if (TryRead(bytes, ref end, 0, out object? value) && value is Dictionary<string, object?> found)
            {
                yield return found;
                position = end;
            }
            else
            {
                position = start + 1;
            }
        }
    }

    private static bool TryRead(byte[] bytes, ref int position, int depth, out object? value)
    {
        value = null;
        if (depth > MAX_DEPTH)
            return false;

        while (position < bytes.Length && bytes[position] is PADDING)
            position++;
        if (position >= bytes.Length)
            return false;

        byte tag = bytes[position++];
        switch (tag)
        {
            case VERIFY_OBJECT_COUNT:
            {
                return ReadVarint(bytes, ref position, out _) && TryRead(bytes, ref position, depth, out value);
            }
            case UNDEFINED:
            case NULL:
            case THE_HOLE:
            case OBJECT_REFERENCE when ReadVarint(bytes, ref position, out _):
            {
                return true;
            }
            case TRUE:
            {
                value = true;
                return true;
            }
            case FALSE:
            {
                value = false;
                return true;
            }
            case INT32:
            {
                if (!ReadVarint(bytes, ref position, out ulong zigzag))
                    return false;
                value = (long)(zigzag >> 1) ^ -(long)(zigzag & 1);
                return true;
            }
            case UINT32:
            {
                bool read = ReadVarint(bytes, ref position, out ulong number);
                value = (long)number;
                return read;
            }
            case DOUBLE:
            case DATE:
            {
                if (position + DOUBLE_SIZE > bytes.Length)
                    return false;
                value = BitConverter.ToDouble(bytes, position);
                position += DOUBLE_SIZE;
                return true;
            }
            case UTF8_STRING:
            case ONE_BYTE_STRING:
            case TWO_BYTE_STRING:
            {
                if (!ReadVarint(bytes, ref position, out ulong length) || length > MAX_LENGTH ||
                    position + (int)length > bytes.Length || (tag == TWO_BYTE_STRING && length % 2 != 0))
                    return false;
                Encoding encoding = tag switch
                {
                    UTF8_STRING => Encoding.UTF8,
                    ONE_BYTE_STRING => Encoding.Latin1,
                    _ => Encoding.Unicode
                };
                value = encoding.GetString(bytes, position, (int)length);
                position += (int)length;
                return true;
            }
            case BEGIN_OBJECT:
            {
                bool read = ReadProperties(bytes, ref position, depth, END_OBJECT, out Dictionary<string, object?> map);
                value = map;
                return read;
            }
            case BEGIN_SPARSE_ARRAY:
            {
                bool read = ReadVarint(bytes, ref position, out _) &&
                            ReadProperties(bytes, ref position, depth, END_SPARSE_ARRAY, out _) &&
                            ReadVarint(bytes, ref position, out _);
                value = new List<object?>();
                return read;
            }
            case BEGIN_DENSE_ARRAY:
            {
                return ReadDenseArray(bytes, ref position, depth, out value);
            }
            default:
            {
                return false;
            }
        }
    }

    // Key and value pairs up to the end tag, whose count must match how many were read
    private static bool ReadProperties(byte[] bytes, ref int position, int depth, byte endTag,
                                       out Dictionary<string, object?> map)
    {
        map = new Dictionary<string, object?>(StringComparer.Ordinal);
        int count = 0;
        while (true)
        {
            while (position < bytes.Length && bytes[position] is PADDING)
                position++;
            if (position >= bytes.Length)
                return false;
            if (bytes[position] == endTag)
            {
                position++;
                return ReadVarint(bytes, ref position, out ulong expected) && expected == (ulong)count;
            }

            if (!TryRead(bytes, ref position, depth + 1, out object? key) || key is not (string or long or double) ||
                !TryRead(bytes, ref position, depth + 1, out object? item))
                return false;
            map[Convert.ToString(key, System.Globalization.CultureInfo.InvariantCulture)!] = item;
            count++;
        }
    }

    private static bool ReadDenseArray(byte[] bytes, ref int position, int depth, out object? value)
    {
        value = null;
        if (!ReadVarint(bytes, ref position, out ulong length) || length > MAX_LENGTH)
            return false;

        List<object?> items = new List<object?>();
        for (ulong index = 0; index < length; index++)
        {
            if (!TryRead(bytes, ref position, depth + 1, out object? item))
                return false;
            items.Add(item);
        }

        value = items;
        return ReadProperties(bytes, ref position, depth, END_DENSE_ARRAY, out _) &&
               ReadVarint(bytes, ref position, out ulong total) && total == length;
    }

    private static bool ReadVarint(byte[] bytes, ref int position, out ulong value)
    {
        value = 0;
        for (int shift = 0; shift <= MAX_VARINT_SHIFT && position < bytes.Length; shift += VARINT_BITS)
        {
            byte next = bytes[position++];
            value |= (ulong)(next & VARINT_VALUE) << shift;
            if ((next & VARINT_MORE) == 0)
                return true;
        }

        return false;
    }
}
