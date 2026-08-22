using System.Buffers.Binary;

namespace JackAll.Core.Format;

/// <summary>How a writer fills the gap when it aligns.</summary>
/// <remarks>
/// There is no house default, deliberately. An `.xbg` pads with a descending byte counter and a
/// `.mab` pads with zeros, so a shared helper that picked one would silently destroy the other's
/// byte-exact round trip while still producing a file the game loads.
/// </remarks>
public enum AlignFill
{
    Zero,

    /// <summary>Nine bytes of padding are written 09 08 07 06 05 04 03 02 01.</summary>
    DescendingCounter,
}

/// <summary>
/// Little-endian writer, the counterpart to <see cref="ByteCursor"/>, for codecs whose gate is that
/// re-serialising a real file returns its bytes.
/// </summary>
/// <remarks>
/// Sizes and offsets that are only known once a body exists are written as zero and filled in later
/// with <see cref="PatchU32"/>, which is how the chunked formats derive their own framing rather
/// than echoing what they parsed.
/// </remarks>
public sealed class ByteWriter
{
    private readonly List<byte> _buffer = [];

    public int Length => _buffer.Count;

    public void WriteU8(byte value) => _buffer.Add(value);

    public void WriteU16(ushort value)
    {
        Span<byte> bytes = stackalloc byte[2];
        BinaryPrimitives.WriteUInt16LittleEndian(bytes, value);
        _buffer.AddRange(bytes);
    }

    public void WriteI16(short value)
    {
        Span<byte> bytes = stackalloc byte[2];
        BinaryPrimitives.WriteInt16LittleEndian(bytes, value);
        _buffer.AddRange(bytes);
    }

    public void WriteU32(uint value)
    {
        Span<byte> bytes = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32LittleEndian(bytes, value);
        _buffer.AddRange(bytes);
    }

    public void WriteI32(int value)
    {
        Span<byte> bytes = stackalloc byte[4];
        BinaryPrimitives.WriteInt32LittleEndian(bytes, value);
        _buffer.AddRange(bytes);
    }

    public void WriteF32(float value)
    {
        Span<byte> bytes = stackalloc byte[4];
        BinaryPrimitives.WriteSingleLittleEndian(bytes, value);
        _buffer.AddRange(bytes);
    }

    public void WriteU16Array(ReadOnlySpan<ushort> values)
    {
        foreach (ushort value in values)
        {
            WriteU16(value);
        }
    }

    public void WriteI16Array(ReadOnlySpan<short> values)
    {
        foreach (short value in values)
        {
            WriteI16(value);
        }
    }

    public void WriteU32Array(ReadOnlySpan<uint> values)
    {
        foreach (uint value in values)
        {
            WriteU32(value);
        }
    }

    public void WriteF32Array(ReadOnlySpan<float> values)
    {
        foreach (float value in values)
        {
            WriteF32(value);
        }
    }

    public void WriteRaw(ReadOnlySpan<byte> data) => _buffer.AddRange(data);

    /// <summary>A CStringID: CRC32 of the exact-case name, its length, then unterminated characters.</summary>
    public void WriteStringId(string name, uint hash)
    {
        WriteU32(hash);
        WriteU32((uint)name.Length);
        WriteLatin1(name);
    }

    /// <summary>A length-prefixed, NUL-terminated name, as the .xbg chunks store it.</summary>
    public void WriteCString(string name)
    {
        WriteU32((uint)name.Length);
        WriteLatin1(name);
        WriteU8(0);
    }

    public void Align(int boundary, AlignFill fill)
    {
        int padding = (boundary - (_buffer.Count % boundary)) % boundary;
        for (int i = padding; i > 0; i--)
        {
            _buffer.Add(fill == AlignFill.DescendingCounter ? (byte)i : (byte)0);
        }
    }

    public void PatchU32(int offset, uint value)
    {
        Span<byte> bytes = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32LittleEndian(bytes, value);
        for (int i = 0; i < 4; i++)
        {
            _buffer[offset + i] = bytes[i];
        }
    }

    public byte[] ToArray() => [.. _buffer];

    private void WriteLatin1(string name)
    {
        foreach (char c in name)
        {
            _buffer.Add((byte)c);
        }
    }
}
