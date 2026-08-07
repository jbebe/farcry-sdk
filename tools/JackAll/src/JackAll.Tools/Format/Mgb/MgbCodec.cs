using System.Text;

namespace JackAll.Tools.Format.Mgb;

/// <summary>
/// A direction-agnostic field sink. Every record in this namespace describes its wire format
/// exactly once, in a <c>Serialize(IMgbCodec, MgbContext)</c> method, and that one description
/// drives both reading and writing.
/// </summary>
/// <remarks>
/// This is the whole reason the reader and writer cannot drift apart. The obvious alternative -
/// a <c>Read</c> method and a matching <c>Write</c> method per record - relies on a human keeping
/// ~40 pairs in step, and this format punishes a single mismatched field width by silently
/// corrupting everything downstream of it rather than throwing. With a shared description the
/// round-trip test in MgbRoundTripTests degenerates to a tautology it either satisfies or doesn't.
///
/// Every method takes the field's authored name (recovered from <c>magma::LoadVisitor</c>, see
/// docs/docs/file-formats/mgb.md). The codecs ignore it; it exists so a third implementation could
/// walk the same descriptions to build a UI, and so the call sites read like the format spec.
/// </remarks>
public interface IMgbCodec
{
    /// <summary>True while decoding. Records need this only where the shape itself depends on
    /// direction - e.g. sizing a list before its items can be visited.</summary>
    bool IsReading { get; }

    /// <summary>Byte offset into the package. Only meaningful for diagnostics.</summary>
    int Position { get; }

    void U8(string name, ref byte value);
    void U16(string name, ref ushort value);
    void U32(string name, ref uint value);

    /// <summary>A 4-byte float, carried as raw bits. Floats are never converted on the way through:
    /// a NaN payload or a denormal that survives <c>float</c> arithmetic unchanged is not something
    /// worth betting byte-exact round-tripping on.</summary>
    void F32Bits(string name, ref uint bits);

    /// <summary>One byte on the wire, non-zero meaning true.</summary>
    void Bool(string name, ref bool value);

    /// <summary>A <c>u32</c> byte count followed by that many raw bytes. Kept as bytes rather than
    /// a string because these are ANSI paths whose exact bytes must survive; decode for display
    /// with <see cref="MgbText.Ansi"/>.</summary>
    void AnsiString(string name, ref byte[] value);

    /// <summary>A <c>u32</c> *character* count followed by that many UTF-16 code units, i.e. twice
    /// as many bytes. Same byte-preserving reasoning as <see cref="AnsiString"/>.</summary>
    void Utf16String(string name, ref byte[] value);

    /// <summary>Exactly <paramref name="byteCount"/> raw bytes, with the length carried separately
    /// by the caller.</summary>
    void Blob(string name, ref byte[] value, int byteCount);

    /// <summary>A structural count: the number of items in the list that follows. On write the
    /// caller passes the live collection size, so the two can never disagree.</summary>
    void Count(ref int value);
}

/// <summary>Decodes a <c>.mgb</c> body. Mirrors <c>magma::BinaryReadSerializer</c>; when the
/// header's endian marker is not <c>0xAB</c> the engine swaps in
/// <c>BinaryInvertReadSerializer</c>, which is this with <see cref="_invert"/> set.</summary>
public sealed class MgbReadCodec(byte[] data, bool invert = false) : IMgbCodec
{
    private readonly byte[] _data = data;
    private readonly bool _invert = invert;
    private int _pos;

    public bool IsReading => true;
    public int Position => _pos;
    public int Remaining => _data.Length - _pos;

    private ReadOnlySpan<byte> Take(int n)
    {
        if (n < 0)
        {
            throw new MgbFormatException($"negative read length {n} at offset {_pos}");
        }
        if (_pos + n > _data.Length)
        {
            throw new MgbFormatException(
                $"unexpected end of file: wanted {n} bytes at offset {_pos}, only {_data.Length - _pos} left");
        }
        ReadOnlySpan<byte> span = _data.AsSpan(_pos, n);
        _pos += n;
        return span;
    }

    public void U8(string name, ref byte value) => value = Take(1)[0];

    public void U16(string name, ref ushort value)
    {
        ReadOnlySpan<byte> b = Take(2);
        value = _invert
            ? (ushort)((b[0] << 8) | b[1])
            : (ushort)(b[0] | (b[1] << 8));
    }

    public void U32(string name, ref uint value)
    {
        ReadOnlySpan<byte> b = Take(4);
        value = _invert
            ? ((uint)b[0] << 24) | ((uint)b[1] << 16) | ((uint)b[2] << 8) | b[3]
            : b[0] | ((uint)b[1] << 8) | ((uint)b[2] << 16) | ((uint)b[3] << 24);
    }

    public void F32Bits(string name, ref uint bits) => U32(name, ref bits);
    public void Bool(string name, ref bool value) => value = Take(1)[0] != 0;

    public void AnsiString(string name, ref byte[] value)
    {
        uint length = 0;
        U32(name, ref length);
        value = length == 0 ? [] : Take(checked((int)length)).ToArray();
    }

    public void Utf16String(string name, ref byte[] value)
    {
        uint chars = 0;
        U32(name, ref chars);
        value = chars == 0 ? [] : Take(checked((int)chars * 2)).ToArray();
    }

    public void Blob(string name, ref byte[] value, int byteCount)
        => value = byteCount == 0 ? [] : Take(byteCount).ToArray();

    public void Count(ref int value)
    {
        uint n = 0;
        U32("count", ref n);
        if (n > int.MaxValue)
        {
            throw new MgbFormatException($"implausible item count {n} at offset {_pos - 4}");
        }
        value = (int)n;
    }
}

/// <summary>Encodes a <c>.mgb</c> body, byte for byte the inverse of <see cref="MgbReadCodec"/>.</summary>
public sealed class MgbWriteCodec(bool invert = false) : IMgbCodec
{
    private readonly List<byte> _bytes = [];
    private readonly bool _invert = invert;

    public bool IsReading => false;
    public int Position => _bytes.Count;

    public byte[] ToArray() => [.. _bytes];

    public void U8(string name, ref byte value) => _bytes.Add(value);

    public void U16(string name, ref ushort value)
    {
        if (_invert)
        {
            _bytes.Add((byte)(value >> 8));
            _bytes.Add((byte)value);
        }
        else
        {
            _bytes.Add((byte)value);
            _bytes.Add((byte)(value >> 8));
        }
    }

    public void U32(string name, ref uint value)
    {
        if (_invert)
        {
            _bytes.Add((byte)(value >> 24));
            _bytes.Add((byte)(value >> 16));
            _bytes.Add((byte)(value >> 8));
            _bytes.Add((byte)value);
        }
        else
        {
            _bytes.Add((byte)value);
            _bytes.Add((byte)(value >> 8));
            _bytes.Add((byte)(value >> 16));
            _bytes.Add((byte)(value >> 24));
        }
    }

    public void F32Bits(string name, ref uint bits) => U32(name, ref bits);
    public void Bool(string name, ref bool value) => _bytes.Add(value ? (byte)1 : (byte)0);

    public void AnsiString(string name, ref byte[] value)
    {
        uint length = (uint)value.Length;
        U32(name, ref length);
        _bytes.AddRange(value);
    }

    public void Utf16String(string name, ref byte[] value)
    {
        if ((value.Length & 1) != 0)
        {
            throw new MgbFormatException(
                $"UTF-16 field '{name}' has an odd byte length ({value.Length}) and cannot be written");
        }
        uint chars = (uint)(value.Length / 2);
        U32(name, ref chars);
        _bytes.AddRange(value);
    }

    public void Blob(string name, ref byte[] value, int byteCount)
    {
        if (value.Length != byteCount)
        {
            throw new MgbFormatException(
                $"blob field '{name}' is {value.Length} bytes but {byteCount} were declared");
        }
        _bytes.AddRange(value);
    }

    public void Count(ref int value)
    {
        uint n = (uint)value;
        U32("count", ref n);
    }
}

/// <summary>Thrown for anything that makes a package unreadable or unwritable.</summary>
public sealed class MgbFormatException(string message) : Exception(message);

/// <summary>Display-only decoding for the byte-preserving string fields.</summary>
public static class MgbText
{
    /// <summary>Latin-1 rather than ASCII or UTF-8: it is the only single-byte encoding that
    /// round-trips every possible byte, so a path with an unexpected high byte still shows
    /// something rather than a replacement character.</summary>
    public static string Ansi(byte[] bytes) => Encoding.Latin1.GetString(bytes);

    public static byte[] ToAnsi(string text) => Encoding.Latin1.GetBytes(text);

    public static string Utf16(byte[] bytes) => Encoding.Unicode.GetString(bytes);

    public static byte[] ToUtf16(string text) => Encoding.Unicode.GetBytes(text);
}
