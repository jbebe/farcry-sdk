using System.Buffers.Binary;

namespace JackAll.Core.Format;

/// <summary>
/// Little-endian reader shared by the byte[]-based format codecs: static scalar reads for
/// offset-addressed layouts, plus a bounds-checked forward cursor for sequentially framed ones.
/// Running out of bytes always surfaces as <see cref="InvalidDataException"/>.
/// </summary>
public ref struct ByteCursor
{
    private readonly ReadOnlySpan<byte> _data;

    /// <summary>Next read position. Settable so a caller can seek; every read validates it.</summary>
    public int Position;

    public ByteCursor(ReadOnlySpan<byte> data)
    {
        _data = data;
    }

    public readonly ReadOnlySpan<byte> Data => _data;

    public readonly int Remaining => _data.Length - Position;

    private readonly void EnsureAvailable(int count)
    {
        if (Position < 0 || (long)Position + count > _data.Length)
        {
            throw new InvalidDataException(
                $"Ran out of bytes at offset 0x{Position:X} (needed {count}, only {Math.Max(0, Remaining)} available).");
        }
    }

    public ushort ReadU16()
    {
        EnsureAvailable(2);
        ushort value = BinaryPrimitives.ReadUInt16LittleEndian(_data[Position..]);
        Position += 2;
        return value;
    }

    public uint ReadU32()
    {
        EnsureAvailable(4);
        uint value = BinaryPrimitives.ReadUInt32LittleEndian(_data[Position..]);
        Position += 4;
        return value;
    }

    public float ReadF32()
    {
        EnsureAvailable(4);
        float value = BinaryPrimitives.ReadSingleLittleEndian(_data[Position..]);
        Position += 4;
        return value;
    }

    public ReadOnlySpan<byte> ReadSpan(int length)
    {
        EnsureAvailable(length);
        ReadOnlySpan<byte> value = _data.Slice(Position, length);
        Position += length;
        return value;
    }

    public byte[] ReadBytes(int length) => ReadSpan(length).ToArray();

    public static uint U32(ReadOnlySpan<byte> data, int offset)
        => BinaryPrimitives.ReadUInt32LittleEndian(data[offset..]);

    public static float F32(ReadOnlySpan<byte> data, int offset)
        => BinaryPrimitives.ReadSingleLittleEndian(data[offset..]);

    /// <summary>True when <paramref name="expected"/> appears at <paramref name="offset"/>; false (not
    /// a throw) when the offset is out of range.</summary>
    public static bool Matches(ReadOnlySpan<byte> data, int offset, ReadOnlySpan<byte> expected)
        => offset >= 0 && offset + expected.Length <= data.Length
           && data.Slice(offset, expected.Length).SequenceEqual(expected);
}
