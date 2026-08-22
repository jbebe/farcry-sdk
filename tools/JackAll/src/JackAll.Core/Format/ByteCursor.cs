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

    public byte ReadU8()
    {
        EnsureAvailable(1);
        return _data[Position++];
    }

    public ushort ReadU16()
    {
        EnsureAvailable(2);
        ushort value = BinaryPrimitives.ReadUInt16LittleEndian(_data[Position..]);
        Position += 2;
        return value;
    }

    public short ReadI16()
    {
        EnsureAvailable(2);
        short value = BinaryPrimitives.ReadInt16LittleEndian(_data[Position..]);
        Position += 2;
        return value;
    }

    public int ReadI32()
    {
        EnsureAvailable(4);
        int value = BinaryPrimitives.ReadInt32LittleEndian(_data[Position..]);
        Position += 4;
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

    public ushort[] ReadU16Array(int count)
    {
        var values = new ushort[count];
        for (int i = 0; i < count; i++)
        {
            values[i] = ReadU16();
        }
        return values;
    }

    public short[] ReadI16Array(int count)
    {
        var values = new short[count];
        for (int i = 0; i < count; i++)
        {
            values[i] = ReadI16();
        }
        return values;
    }

    public uint[] ReadU32Array(int count)
    {
        var values = new uint[count];
        for (int i = 0; i < count; i++)
        {
            values[i] = ReadU32();
        }
        return values;
    }

    public float[] ReadF32Array(int count)
    {
        var values = new float[count];
        for (int i = 0; i < count; i++)
        {
            values[i] = ReadF32();
        }
        return values;
    }

    /// <summary>A CStringID: CRC32 of the exact-case name, its length, then unterminated characters.</summary>
    public (uint Hash, string Name) ReadStringId()
    {
        uint hash = ReadU32();
        return (hash, Latin1(ReadSpan((int)ReadU32())));
    }

    /// <summary>A length-prefixed, NUL-terminated name, as the .xbg chunks store it.</summary>
    public string ReadCString()
    {
        string name = Latin1(ReadSpan((int)ReadU32()));
        Position++;
        return name;
    }

    private static string Latin1(ReadOnlySpan<byte> bytes)
    {
        var chars = new char[bytes.Length];
        for (int i = 0; i < bytes.Length; i++)
        {
            chars[i] = (char)bytes[i];
        }
        return new string(chars);
    }

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
