using System.Text;

namespace JackAll.Core.Format;

/// <summary>
/// A forward-only byte cursor over a .mgb body, with one method per reader-vtable slot identified
/// while reverse-engineering the format (see reverse/dunia/mgb_format.md, "Reader-vtable slots"):
/// <c>+0x8</c>=<see cref="ReadValue"/>, <c>+0xc</c>=<see cref="ReadInt"/>, <c>+0x10</c>=
/// <see cref="ReadU16"/>, <c>+0x14</c>/<c>+0x18</c>=probable overloads (<see cref="ReadU16B"/>/
/// <see cref="ReadByteB"/>), <c>+0x1c</c>=<see cref="ReadByte"/>, <c>+0x20</c>=<see cref="ReadReal"/>,
/// <c>+0x24</c>=<see cref="ReadBool"/>, <c>+0x28</c>=<see cref="ReadBytes"/>, <c>+0x2c</c>=
/// <see cref="ReadUtf16"/>.
/// </summary>
public sealed class MgbReader(byte[] data, int start)
{
    public int Position { get; private set; } = start;

    /// <summary><c>+0x8</c> - generic untyped 4-byte read (float or u32 depending on the caller's use).</summary>
    public uint ReadValue() => ReadU32Core();

    /// <summary>Same wire read as <see cref="ReadValue"/>, reinterpreted as a float - for the <c>+0x8</c>
    /// call sites the doc identifies as float-shaped (e.g. <c>RectShape</c>'s scalar, <c>Text</c>'s
    /// offset pair) even though the slot itself is untyped.</summary>
    public float ReadValueAsFloat() => BitConverter.Int32BitsToSingle((int)ReadU32Core());

    /// <summary><c>+0xc</c>.</summary>
    public uint ReadInt() => ReadU32Core();

    /// <summary><c>+0x10</c>.</summary>
    public ushort ReadU16() => ReadU16Core();

    /// <summary><c>+0x14</c> - probable overload of <see cref="ReadU16"/>, byte-stream-identical at
    /// every call site seen so far; not proven distinct.</summary>
    public ushort ReadU16B() => ReadU16Core();

    /// <summary><c>+0x1c</c>.</summary>
    public byte ReadByte() => ReadByteCore();

    /// <summary><c>+0x18</c> - probable overload of <see cref="ReadByte"/>, same caveat as
    /// <see cref="ReadU16B"/>.</summary>
    public byte ReadByteB() => ReadByteCore();

    /// <summary><c>+0x20</c> - a dedicated float read, confirmed distinct from <see cref="ReadValue"/>
    /// by multiple independent call sites (see the doc).</summary>
    public float ReadReal() => BitConverter.Int32BitsToSingle((int)ReadU32Core());

    /// <summary><c>+0x24</c> - 1 byte on the wire, confirmed by the header's byte-13 gap.</summary>
    public bool ReadBool() => ReadByteCore() != 0;

    /// <summary><c>+0x28</c> - raw/ANSI bytes, paired with a length read via <see cref="ReadInt"/> at
    /// every call site.</summary>
    public byte[] ReadBytes(int count)
    {
        if (count < 0) throw new InvalidDataException($"Negative byte count at offset 0x{Position:X}.");
        EnsureAvailable(count);
        byte[] result = data[Position..(Position + count)];
        Position += count;
        return result;
    }

    /// <summary><c>+0x2c</c> - UTF-16LE characters, paired with a char count read via
    /// <see cref="ReadInt"/> at every call site.</summary>
    public string ReadUtf16(int charCount)
    {
        byte[] bytes = ReadBytes(charCount * 2);
        return Encoding.Unicode.GetString(bytes);
    }

    /// <summary>ANSI/Latin1 decode for a <see cref="ReadBytes"/> result - every string blob in this
    /// format is a plain narrow string (file paths, resource names), never UTF-8.</summary>
    public static string DecodeAnsi(byte[] bytes) => Encoding.Latin1.GetString(bytes);

    public bool AtEnd => Position >= data.Length;

    public int BytesRemaining => data.Length - Position;

    private uint ReadU32Core()
    {
        EnsureAvailable(4);
        uint value = (uint)(data[Position] | (data[Position + 1] << 8) | (data[Position + 2] << 16) | (data[Position + 3] << 24));
        Position += 4;
        return value;
    }

    private ushort ReadU16Core()
    {
        EnsureAvailable(2);
        ushort value = (ushort)(data[Position] | (data[Position + 1] << 8));
        Position += 2;
        return value;
    }

    private byte ReadByteCore()
    {
        EnsureAvailable(1);
        return data[Position++];
    }

    private void EnsureAvailable(int count)
    {
        if (Position + count > data.Length)
        {
            throw new InvalidDataException(
                $"Ran out of bytes at offset 0x{Position:X} (needed {count}, only {data.Length - Position} left) - " +
                "the parser has desynced from the file, or this file uses a variant this parser doesn't cover yet.");
        }
    }
}
