using System.Text;

namespace JackAll.Tools.Mgb;

/// <summary>How a list carries its length on the binary wire.</summary>
public enum MgbCountWidth
{
    /// <summary>A <c>u32</c> - the shape almost every list uses.</summary>
    U32,

    /// <summary>A <c>u16</c> - <c>VisitFullLink</c>'s id list.</summary>
    U16,

    /// <summary>A single byte holding <c>count + 1</c> - the header's type table, whose fill loop
    /// runs slots <c>1 .. count-1</c>.</summary>
    U8Plus1,
}

/// <summary>
/// A direction-agnostic field sink. Every record in this namespace describes its wire format
/// exactly once, in a <c>Serialize(IMgbCodec, MgbContext)</c> method, and that one description
/// drives reading, writing, and both directions of the XML interchange format.
/// </summary>
/// <remarks>
/// This is the whole reason the implementations cannot drift apart. The obvious alternative -
/// a <c>Read</c> method and a matching <c>Write</c> method per record - relies on a human keeping
/// ~40 pairs in step, and this format punishes a single mismatched field width by silently
/// corrupting everything downstream of it rather than throwing. With a shared description the
/// round-trip tests in MgbRoundTripTests and MgbXmlTests degenerate to tautologies the code either
/// satisfies or doesn't.
///
/// Every method takes the field's authored name (recovered from <c>magma::LoadVisitor</c>, see
/// docs/docs/file-formats/mgb.md). The binary codecs ignore it; the XML codecs use it as the
/// element or attribute name, which is why the call sites read like the format spec.
///
/// The structural operations - <see cref="Scope"/>, <see cref="Item"/>, <see cref="ListScope"/>,
/// <see cref="Gate"/> - exist purely so a text codec can recover the nesting that the binary format
/// expresses through call structure alone. The binary codecs implement them as no-ops (and
/// <c>ListScope</c> as the count it always was), so their byte output is unchanged.
/// </remarks>
public interface IMgbCodec
{
    /// <summary>True while decoding. Records need this only where the shape itself depends on
    /// direction - e.g. sizing a list before its items can be visited.</summary>
    bool IsReading { get; }

    /// <summary>Byte offset into the package. Only meaningful for diagnostics, and only for the
    /// binary codecs.</summary>
    int Position { get; }

    /// <summary>Enters one sub-record. Binary: nothing. XML: one nested element.</summary>
    IDisposable Scope(string name);

    /// <summary>Enters one item of the list most recently opened with <see cref="ListScope"/>.</summary>
    IDisposable Item(string name);

    /// <summary>Enters a list of records, yielding its length. On write the caller passes the live
    /// collection size, so the stored count can never disagree with the contents; on read
    /// <paramref name="count"/> receives the length. XML derives the length from the number of
    /// child elements and stores no count at all.</summary>
    IDisposable ListScope(string name, ref int count, MgbCountWidth width = MgbCountWidth.U32);

    /// <summary>The length of a list whose items are *values* rather than records, and which the XML
    /// side renders as a single whitespace-separated attribute named <paramref name="name"/>.
    /// Returns the length; on write it is <paramref name="count"/> unchanged.</summary>
    int Count(string name, int count, MgbCountWidth width = MgbCountWidth.U32);

    /// <summary>A presence flag for an optional sub-record. Binary: one byte. XML: whether the
    /// element exists at all, so absence is never confusable with present-but-zero.</summary>
    bool Gate(string name, bool present);

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

    /// <summary>A <c>u32</c> that is a <c>CRC32</c> of an authored name. Identical to
    /// <see cref="U32"/> on the wire; the XML side renders the name where one can be recovered and
    /// verified, and <c>#XXXXXXXX</c> otherwise.</summary>
    void NameId(string name, ref uint hash);

    /// <summary>A <c>u32</c> drawn from one of <c>magma::Util</c>'s named value sets. Identical to
    /// <see cref="U32"/> on the wire; the XML side renders the name when the value is in the table
    /// and the raw number when it is not, so a value the table cannot express is never lost.</summary>
    void EnumU32(string name, ref uint value, MgbEnum group);

    /// <summary>A packed <c>0xAARRGGBB</c> colour word. Identical to <see cref="U32"/> on the wire;
    /// the XML side renders it as 8 hex digits rather than a decimal, which is a formatting choice
    /// and not a decomposition - the word stays one value.</summary>
    void ColorU32(string name, ref uint argb);

    /// <summary>A type-table slot. One byte on the wire. The XML side writes the raw slot to
    /// <paramref name="slotName"/> - which stays authoritative, since several slots can resolve to
    /// the same class - and the resolved class name to <paramref name="className"/> as decoration.</summary>
    void TypeSlot(string slotName, string className, ref byte slot, MgbTypeTable types);

    /// <summary>A <c>bool</c> gate followed by a <c>u32</c> when set. XML omits the attribute
    /// entirely when null, because null and present-with-zero are distinct on the wire.</summary>
    void OptionalU32(string name, ref uint? value);

    /// <summary>As <see cref="OptionalU32"/>, for a value that is a name hash.</summary>
    void OptionalNameId(string name, ref uint? value);

    /// <summary>A <c>bool</c> gate followed by <paramref name="byteCount"/> raw bytes when set.</summary>
    void OptionalBlob(string name, ref byte[]? value, int byteCount);

    /// <summary>A fixed-length run of <c>u16</c>s with no count on the wire. XML renders it as one
    /// whitespace-separated attribute, which is also how <c>.mgm</c> authors these
    /// (<c>STATICBOX</c> is documented as <c>%d %d %d %d</c>).</summary>
    void U16Array(string name, ushort[] values);

    /// <summary>A fixed-length run of <c>u32</c>s with no count on the wire.</summary>
    void U32Array(string name, uint[] values);

    /// <summary>A fixed-length run of floats-as-bits with no count on the wire.</summary>
    void F32BitsArray(string name, uint[] values);

    /// <summary>The items of a value list whose length was already established by
    /// <see cref="Count"/>. XML renders them as the whitespace-separated attribute
    /// <paramref name="name"/>.</summary>
    void U32Items(string name, List<uint> values);

    /// <summary>As <see cref="U32Items"/>, but each value is a name hash.</summary>
    void NameIdItems(string name, List<uint> values);
}

/// <summary>A scope that does nothing, for the codecs that carry no structure.</summary>
internal sealed class MgbNullScope : IDisposable
{
    public static readonly MgbNullScope Instance = new();

    private MgbNullScope()
    {
    }

    public void Dispose()
    {
    }
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

    public IDisposable Scope(string name) => MgbNullScope.Instance;

    public IDisposable Item(string name) => MgbNullScope.Instance;

    public IDisposable ListScope(string name, ref int count, MgbCountWidth width = MgbCountWidth.U32)
    {
        count = Count(name, count, width);
        return MgbNullScope.Instance;
    }

    public int Count(string name, int count, MgbCountWidth width = MgbCountWidth.U32)
    {
        switch (width)
        {
            case MgbCountWidth.U16:
            {
                ushort n = 0;
                U16(name, ref n);
                return n;
            }
            case MgbCountWidth.U8Plus1:
            {
                byte n = 0;
                U8(name, ref n);
                return n - 1;
            }
            default:
            {
                uint n = 0;
                U32(name, ref n);
                if (n > int.MaxValue)
                {
                    throw new MgbFormatException($"implausible item count {n} at offset {_pos - 4}");
                }
                return (int)n;
            }
        }
    }

    public bool Gate(string name, bool present)
    {
        bool value = false;
        Bool(name, ref value);
        return value;
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

    public void NameId(string name, ref uint hash) => U32(name, ref hash);

    public void EnumU32(string name, ref uint value, MgbEnum group) => U32(name, ref value);

    public void ColorU32(string name, ref uint argb) => U32(name, ref argb);

    public void TypeSlot(string slotName, string className, ref byte slot, MgbTypeTable types)
        => U8(slotName, ref slot);

    public void OptionalU32(string name, ref uint? value)
    {
        bool present = false;
        Bool(name, ref present);
        if (!present)
        {
            value = null;
            return;
        }
        uint scalar = 0;
        U32(name, ref scalar);
        value = scalar;
    }

    public void OptionalNameId(string name, ref uint? value) => OptionalU32(name, ref value);

    public void OptionalBlob(string name, ref byte[]? value, int byteCount)
    {
        bool present = false;
        Bool(name, ref present);
        if (!present)
        {
            value = null;
            return;
        }
        byte[] bytes = [];
        Blob(name, ref bytes, byteCount);
        value = bytes;
    }

    public void U16Array(string name, ushort[] values)
    {
        for (int i = 0; i < values.Length; i++)
        {
            U16(name, ref values[i]);
        }
    }

    public void U32Array(string name, uint[] values)
    {
        for (int i = 0; i < values.Length; i++)
        {
            U32(name, ref values[i]);
        }
    }

    public void F32BitsArray(string name, uint[] values) => U32Array(name, values);

    public void U32Items(string name, List<uint> values)
    {
        for (int i = 0; i < values.Count; i++)
        {
            uint v = 0;
            U32(name, ref v);
            values[i] = v;
        }
    }

    public void NameIdItems(string name, List<uint> values) => U32Items(name, values);
}

/// <summary>Encodes a <c>.mgb</c> body, byte for byte the inverse of <see cref="MgbReadCodec"/>.</summary>
public sealed class MgbWriteCodec(bool invert = false) : IMgbCodec
{
    private readonly List<byte> _bytes = [];
    private readonly bool _invert = invert;

    public bool IsReading => false;
    public int Position => _bytes.Count;

    public byte[] ToArray() => [.. _bytes];

    public IDisposable Scope(string name) => MgbNullScope.Instance;

    public IDisposable Item(string name) => MgbNullScope.Instance;

    public IDisposable ListScope(string name, ref int count, MgbCountWidth width = MgbCountWidth.U32)
    {
        Count(name, count, width);
        return MgbNullScope.Instance;
    }

    public int Count(string name, int count, MgbCountWidth width = MgbCountWidth.U32)
    {
        switch (width)
        {
            case MgbCountWidth.U16:
            {
                ushort n = checked((ushort)count);
                U16(name, ref n);
                break;
            }
            case MgbCountWidth.U8Plus1:
            {
                byte n = checked((byte)(count + 1));
                U8(name, ref n);
                break;
            }
            default:
            {
                uint n = (uint)count;
                U32(name, ref n);
                break;
            }
        }
        return count;
    }

    public bool Gate(string name, bool present)
    {
        Bool(name, ref present);
        return present;
    }

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

    public void NameId(string name, ref uint hash) => U32(name, ref hash);

    public void EnumU32(string name, ref uint value, MgbEnum group) => U32(name, ref value);

    public void ColorU32(string name, ref uint argb) => U32(name, ref argb);

    public void TypeSlot(string slotName, string className, ref byte slot, MgbTypeTable types)
        => U8(slotName, ref slot);

    public void OptionalU32(string name, ref uint? value)
    {
        bool present = value.HasValue;
        Bool(name, ref present);
        if (!present)
        {
            return;
        }
        uint scalar = value!.Value;
        U32(name, ref scalar);
    }

    public void OptionalNameId(string name, ref uint? value) => OptionalU32(name, ref value);

    public void OptionalBlob(string name, ref byte[]? value, int byteCount)
    {
        bool present = value is not null;
        Bool(name, ref present);
        if (!present)
        {
            return;
        }
        byte[] bytes = value!;
        Blob(name, ref bytes, byteCount);
    }

    public void U16Array(string name, ushort[] values)
    {
        for (int i = 0; i < values.Length; i++)
        {
            U16(name, ref values[i]);
        }
    }

    public void U32Array(string name, uint[] values)
    {
        for (int i = 0; i < values.Length; i++)
        {
            U32(name, ref values[i]);
        }
    }

    public void F32BitsArray(string name, uint[] values) => U32Array(name, values);

    public void U32Items(string name, List<uint> values)
    {
        for (int i = 0; i < values.Count; i++)
        {
            uint v = values[i];
            U32(name, ref v);
        }
    }

    public void NameIdItems(string name, List<uint> values) => U32Items(name, values);
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
