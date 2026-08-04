using System.Text;

namespace JackAll.Tools.Format;

/// <summary>
/// A forward-only byte sink for building a .mgb body, mirroring <see cref="MgbReader"/>'s
/// reader-vtable-slot methods one for one so a call sequence copied from <see cref="MgbBody"/>'s
/// parse logic (read this, then this, then this) can be turned into a write sequence with the same
/// shape, field for field. See <see cref="MgbFileBuilder"/> and <see cref="MgbPageEditor"/> for the
/// actual element-shape assembly built on top of this.
/// </summary>
public sealed class MgbWriter
{
    private readonly List<byte> _bytes = [];

    public int Position => _bytes.Count;

    /// <summary><c>+0x8</c> - generic untyped 4-byte write.</summary>
    public void WriteValue(uint v) => WriteU32(v);

    /// <summary>Same wire write as <see cref="WriteValue(uint)"/>, for the <c>+0x8</c> call sites
    /// that are float-shaped.</summary>
    public void WriteValueAsFloat(float v) => WriteU32((uint)BitConverter.SingleToInt32Bits(v));

    /// <summary><c>+0xc</c>.</summary>
    public void WriteInt(uint v) => WriteU32(v);

    /// <summary><c>+0x10</c>.</summary>
    public void WriteU16(ushort v)
    {
        _bytes.Add((byte)v);
        _bytes.Add((byte)(v >> 8));
    }

    /// <summary><c>+0x1c</c>.</summary>
    public void WriteByte(byte b) => _bytes.Add(b);

    /// <summary><c>+0x20</c>.</summary>
    public void WriteReal(float v) => WriteU32((uint)BitConverter.SingleToInt32Bits(v));

    /// <summary><c>+0x24</c> - 1 byte on the wire.</summary>
    public void WriteBool(bool b) => _bytes.Add((byte)(b ? 1 : 0));

    /// <summary><c>+0x28</c> - raw/ANSI bytes, no length prefix of its own (callers pair it with a
    /// separate <see cref="WriteInt"/>, same as every real call site in the format).</summary>
    public void WriteBytes(byte[] data) => _bytes.AddRange(data);

    /// <summary><c>+0x2c</c> - UTF-16LE characters, no length prefix of its own.</summary>
    public void WriteUtf16(string s) => WriteBytes(Encoding.Unicode.GetBytes(s));

    /// <summary>Convenience for the format's extremely common "u32 length, then that many raw ANSI
    /// bytes" shape (materials' texture names, font paths, the package's default material, ...) -
    /// matches <see cref="MgbReader.DecodeAnsi"/>/<c>ReadLengthPrefixedAnsi</c> on the read side.</summary>
    public void WriteLengthPrefixedAnsi(string s)
    {
        byte[] bytes = Encoding.Latin1.GetBytes(s);
        WriteInt((uint)bytes.Length);
        WriteBytes(bytes);
    }

    /// <summary>Raw ASCII, no length prefix - only used for the fixed header magic/sentinel bytes.</summary>
    public void WriteAscii(string s) => WriteBytes(Encoding.ASCII.GetBytes(s));

    public byte[] ToArray() => [.. _bytes];

    private void WriteU32(uint v)
    {
        _bytes.Add((byte)v);
        _bytes.Add((byte)(v >> 8));
        _bytes.Add((byte)(v >> 16));
        _bytes.Add((byte)(v >> 24));
    }
}
