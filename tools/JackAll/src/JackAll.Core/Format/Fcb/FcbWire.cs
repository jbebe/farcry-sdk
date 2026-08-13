namespace JackAll.Core.Format.Fcb;

/// <summary>
/// Byte layouts <see cref="FcbXml"/> and <see cref="FcbValueCodec"/> both speak — NUL-terminated
/// strings and 4-byte-count-prefixed fixed-size-item arrays — shared so the two can't drift apart.
/// </summary>
internal static class FcbWire
{
    internal static byte[] NullTerminate(byte[] utf8)
    {
        byte[] result = new byte[utf8.Length + 1]; // trailing byte is already 0 from the allocation
        utf8.CopyTo(result, 0);
        return result;
    }

    /// <summary>
    /// Reads a count-prefixed array of fixed-size items, or returns false when the 4-byte count
    /// prefix disagrees with the payload length.
    /// </summary>
    internal static bool TryReadFixedArray<T>(byte[] value, int itemSize, Func<byte[], int, T> readItem, out T[] items)
    {
        if (value.Length >= 4)
        {
            int count = BitConverter.ToInt32(value, 0);
            if (count >= 0 && value.Length == 4 + (count * itemSize))
            {
                items = new T[count];
                for (int i = 0, offset = 4; i < count; i++, offset += itemSize)
                {
                    items[i] = readItem(value, offset);
                }
                return true;
            }
        }

        items = [];
        return false;
    }

    /// <summary>Reverse of <see cref="TryReadFixedArray{T}"/>: packs items behind a 4-byte count prefix.</summary>
    internal static byte[] WriteFixedArray<T>(T[] values, int itemSize, Action<byte[], int, T> writeItem)
    {
        byte[] result = new byte[4 + (values.Length * itemSize)];
        BitConverter.GetBytes(values.Length).CopyTo(result, 0);
        for (int i = 0; i < values.Length; i++)
        {
            writeItem(result, 4 + (i * itemSize), values[i]);
        }
        return result;
    }
}
