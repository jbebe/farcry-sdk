using System.Buffers.Binary;
using System.Diagnostics.CodeAnalysis;
using System.Text;
using System.Xml.Linq;

namespace JackAll.Core.Format.Rml;

/// <summary>
/// Reads and writes the Dunia .rml binary format: table-of-contents / resource-manifest XML
/// (research/file_manifest.md §10), and the shape an FCB <see cref="Fcb.FcbMemberType.Rml"/> value's
/// bytes take when it's an actual embedded document rather than something else entirely (see
/// <see cref="Fcb.FcbXml"/>).
/// </summary>
/// <remarks>
/// Ported directly from Gibbed's <c>XmlResourceFile</c> (github.com/gibbed/Gibbed.Dunia, vendored in
/// this repo at tools/Gibbed.Dunia/projects/Gibbed.Dunia.FileFormats/XmlResourceFile.cs, plus the
/// packed-varint helpers next to it in that project's own StreamHelpers.cs) rather than shelling out
/// to it - same rationale as <see cref="Fcb.FcbDocument"/>. Unlike .fcb, this format maps directly onto
/// an <see cref="XElement"/> tree with no external class-definitions table needed to make sense of a
/// value's bytes (a node's "value" is just XPath-style concatenated text, exactly what
/// <see cref="XElement.Value"/> already means), so this class talks XML directly instead of going
/// through an intermediate object model. Verified round-trip against a real shipped sample
/// (downloadcontent/dlc_jungle/toc.rml) in RmlDocumentTests.
///
/// Wire format: signature byte = 0(u8), a reserved byte(u8) that's always 0 in every sample seen and
/// not otherwise meaningful (Gibbed's own writer hard-codes it to 0 too, ignoring whatever was read),
/// then stringTableSize(packed u32), totalNodeCount(packed u32), totalAttributeCount(packed u32) -
/// both counts are cross-checked against what's actually read, same strictness as Gibbed's own reader -
/// then the root node, then a string table blob of stringTableSize bytes: back-to-back
/// null-terminated UTF-8 strings, referenced elsewhere by byte offset into this blob.
///
/// Each node is: nameOffset(packed u32) + valueOffset(packed u32) into the string table, then
/// attributeCount(packed u32) + childCount(packed u32), then that many attributes, then that many
/// child nodes recursively. An attribute is: a reserved packed u32 that must be 0 (checked strictly,
/// same as Gibbed's own reader), then nameOffset + valueOffset exactly like a node's.
///
/// "Packed u32": one byte if the value is under 0xFE, else 0xFF followed by a plain little-endian u32.
/// 0xFE alone is invalid here - unlike .fcb's very similar-looking count encoding, nothing in .rml
/// uses it for backreferences; the only dedup this format does is string-table interning (see
/// <see cref="StringTableWriter"/>).
///
/// The string table is built in a fixed traversal order - a node's own name then value, then each
/// attribute's name/value, then children recursively, left to right - matching Gibbed's writer
/// exactly, so a real shipped .rml re-serializes byte-for-byte identical after a round trip through
/// this class.
/// </remarks>
public static class RmlDocument
{
    private const byte LargeValueMarker = 0xFF;
    private const byte InvalidMarker = 0xFE;

    /// <summary>Throwing wrapper around <see cref="TryDeserialize"/> for callers that only ever see
    /// deliberately-chosen, top-level .rml files (a standalone .rml the user opened, an Import) where a
    /// failure really is exceptional and worth surfacing as an error message.</summary>
    public static XElement Deserialize(byte[] rml)
        => TryDeserialize(rml, out XElement? result)
            ? result
            : throw new InvalidDataException("Not a valid .rml document (malformed or truncated).");

    /// <summary>
    /// Tries to parse <paramref name="rml"/>, without throwing for the ordinary "this isn't a well-formed
    /// .rml document" case.
    /// </summary>
    /// <remarks>
    /// Exists because <see cref="Fcb.FcbXml"/>'s Rml-value decoding calls this speculatively, often twice
    /// per value, across every Rml-typed value in a whole .fcb (thousands, for a big entity library) -
    /// "doesn't parse" is an expected, common outcome there (one of the two candidate byte shapes it
    /// tries is *supposed* to fail whenever a value doesn't carry the FCB-layer pad byte, see
    /// <c>FcbXml.TryDecodeRmlShape</c>'s remarks), not a truly exceptional condition. Routing that through
    /// <see cref="Deserialize"/>'s throw/catch would mean paying .NET's exception cost (stack trace
    /// capture, unwinding) on every one of those - measured as thousands of exceptions loading a single
    /// heavy-Rml mod. Every failure path below is a plain bounds/validity check instead, so parsing a
    /// value that turns out to be the wrong shape costs a handful of comparisons, not a throw.
    ///
    /// The one exception still possible here: turning a decoded string into an <see cref="XElement"/>/
    /// <see cref="XAttribute"/> name can throw if it isn't a legal XML name - not something a cheap
    /// bounds check can rule out ahead of time, and rare enough (garbage or coincidentally-.rml-shaped
    /// bytes) that it doesn't need the same treatment as the byte-level checks.
    /// </remarks>
    public static bool TryDeserialize(byte[] rml, [NotNullWhen(true)] out XElement? result)
    {
        result = null;
        var reader = new Cursor(rml);

        if (!reader.TryReadU8(out byte signature) || signature != 0)
        {
            return false; // not an .rml file - missing (or wrong) leading zero byte
        }
        if (!reader.TryReadU8(out _))
        {
            return false; // reserved byte - see class remarks
        }

        if (!reader.TryReadPackedU32(out uint stringTableSize)
            || !reader.TryReadPackedU32(out uint totalNodeCount)
            || !reader.TryReadPackedU32(out uint totalAttributeCount))
        {
            return false;
        }

        uint actualNodeCount = 1, actualAttributeCount = 0;
        if (!TryDeserializeNode(ref reader, ref actualNodeCount, ref actualAttributeCount, out RawNode? root)
            || actualNodeCount != totalNodeCount || actualAttributeCount != totalAttributeCount)
        {
            return false; // corrupt/truncated tree, or its node/attribute counts don't match the header
        }

        if (!reader.TryReadExact((int)stringTableSize, out byte[]? stringTableData)
            || !TryReadStringTable(stringTableData!, out Dictionary<uint, string>? strings))
        {
            return false; // string table ran short, or one of its entries is missing its NUL terminator
        }

        try
        {
            return TryToXElement(root!, strings!, out result);
        }
        catch (Exception ex) when (ex is ArgumentException or FormatException)
        {
            result = null;
            return false; // a decoded string isn't a legal XML element/attribute name
        }
    }

    public static byte[] Serialize(XElement root)
    {
        var stringTable = new StringTableWriter();

        using var body = new MemoryStream();
        uint totalNodeCount = 1, totalAttributeCount = 0;
        SerializeNode(root, body, stringTable, ref totalNodeCount, ref totalAttributeCount);
        byte[] stringTableData = stringTable.ToBytes();

        using var output = new MemoryStream();
        WriteU8(output, 0);
        WriteU8(output, 0);
        WritePackedU32(output, (uint)stringTableData.Length);
        WritePackedU32(output, totalNodeCount);
        WritePackedU32(output, totalAttributeCount);
        body.Position = 0;
        body.CopyTo(output);
        output.Write(stringTableData, 0, stringTableData.Length);
        return output.ToArray();
    }

    /// <summary>A node with its name/value still as raw string-table offsets - resolved to text only
    /// once the whole tree has been read and the trailing string table is available.</summary>
    private sealed class RawNode
    {
        public uint NameOffset;
        public uint ValueOffset;
        public readonly List<(uint NameOffset, uint ValueOffset)> Attributes = [];
        public readonly List<RawNode> Children = [];
    }

    private static bool TryDeserializeNode(
        ref Cursor reader, ref uint totalNodeCount, ref uint totalAttributeCount, out RawNode? node)
    {
        node = null;

        if (!reader.TryReadPackedU32(out uint nameOffset) || !reader.TryReadPackedU32(out uint valueOffset)
            || !reader.TryReadPackedU32(out uint attributeCount) || !reader.TryReadPackedU32(out uint childCount))
        {
            return false;
        }

        var result = new RawNode { NameOffset = nameOffset, ValueOffset = valueOffset };
        totalNodeCount += childCount;
        totalAttributeCount += attributeCount;

        for (int i = 0; i < attributeCount; i++)
        {
            if (!reader.TryReadPackedU32(out uint reserved) || reserved != 0
                || !reader.TryReadPackedU32(out uint attrNameOffset) || !reader.TryReadPackedU32(out uint attrValueOffset))
            {
                return false; // ran out of data, or an attribute's reserved field is non-zero
            }
            result.Attributes.Add((attrNameOffset, attrValueOffset));
        }

        for (int i = 0; i < childCount; i++)
        {
            if (!TryDeserializeNode(ref reader, ref totalNodeCount, ref totalAttributeCount, out RawNode? child))
            {
                return false;
            }
            result.Children.Add(child!);
        }

        node = result;
        return true;
    }

    private static bool TryToXElement(RawNode node, Dictionary<uint, string> strings, out XElement? element)
    {
        element = null;
        if (!strings.TryGetValue(node.NameOffset, out string? name))
        {
            return false;
        }

        var result = new XElement(name);

        foreach ((uint nameOffset, uint valueOffset) in node.Attributes)
        {
            if (!strings.TryGetValue(nameOffset, out string? attrName) || !strings.TryGetValue(valueOffset, out string? attrValue))
            {
                return false;
            }
            result.SetAttributeValue(attrName, attrValue);
        }

        foreach (RawNode child in node.Children)
        {
            if (!TryToXElement(child, strings, out XElement? childEl))
            {
                return false;
            }
            result.Add(childEl);
        }

        if (!strings.TryGetValue(node.ValueOffset, out string? value))
        {
            return false;
        }

        // Matches Gibbed's WriteNode: the node's own text is appended after its children, and only
        // when non-empty - XElement.Value concatenates all descendant text regardless of position, so
        // this reproduces the original value on a later round trip either way.
        if (value.Length > 0)
        {
            result.Add(new XText(value));
        }

        element = result;
        return true;
    }

    private static bool TryReadStringTable(byte[] data, out Dictionary<uint, string>? strings)
    {
        var result = new Dictionary<uint, string>();
        int position = 0;
        while (position < data.Length)
        {
            int start = position;
            int end = Array.IndexOf(data, (byte)0, position);
            if (end < 0)
            {
                strings = null;
                return false;
            }
            result[(uint)start] = Encoding.UTF8.GetString(data, start, end - start);
            position = end + 1;
        }
        strings = result;
        return true;
    }

    private static void SerializeNode(
        XElement element, Stream output, StringTableWriter stringTable,
        ref uint totalNodeCount, ref uint totalAttributeCount)
    {
        WritePackedU32(output, stringTable.Write(element.Name.LocalName));
        WritePackedU32(output, stringTable.Write(element.Value));

        XAttribute[] attributes = [.. element.Attributes()];
        XElement[] children = [.. element.Elements()];

        totalAttributeCount += (uint)attributes.Length;
        totalNodeCount += (uint)children.Length;

        WritePackedU32(output, (uint)attributes.Length);
        WritePackedU32(output, (uint)children.Length);

        foreach (XAttribute attribute in attributes)
        {
            WritePackedU32(output, 0);
            WritePackedU32(output, stringTable.Write(attribute.Name.LocalName));
            WritePackedU32(output, stringTable.Write(attribute.Value));
        }

        foreach (XElement child in children)
        {
            SerializeNode(child, output, stringTable, ref totalNodeCount, ref totalAttributeCount);
        }
    }

    /// <summary>Interns strings in first-seen order, matching Gibbed's own <c>StringTable</c> - later
    /// repeats of an already-written string reuse its offset instead of duplicating it.</summary>
    private sealed class StringTableWriter
    {
        private readonly MemoryStream _data = new();
        private readonly Dictionary<string, uint> _offsets = [];

        public uint Write(string value)
        {
            if (_offsets.TryGetValue(value, out uint existing))
            {
                return existing;
            }

            var offset = (uint)_data.Position;
            _offsets.Add(value, offset);
            byte[] utf8 = Encoding.UTF8.GetBytes(value);
            _data.Write(utf8, 0, utf8.Length);
            _data.WriteByte(0);
            return offset;
        }

        public byte[] ToBytes() => _data.ToArray();
    }

    /// <summary>A plain byte-array cursor for the read side - deliberately not a <see cref="Stream"/>
    /// (unlike the write side's <see cref="MemoryStream"/>): every read here is a bounds check plus an
    /// index/span slice, no virtual dispatch, and every "ran out of data" case is a <c>false</c> return
    /// instead of an <see cref="EndOfStreamException"/> - see <see cref="TryDeserialize"/>'s remarks for
    /// why that matters. Always passed by <c>ref</c> so recursive calls (<see cref="TryDeserializeNode"/>)
    /// see each other's advances to <see cref="Position"/>.</summary>
    private struct Cursor(byte[] data)
    {
        private readonly byte[] _data = data;
        public int Position;

        public bool TryReadU8(out byte value)
        {
            if ((uint)Position >= (uint)_data.Length)
            {
                value = 0;
                return false;
            }
            value = _data[Position];
            Position++;
            return true;
        }

        public bool TryReadU32(out uint value)
        {
            if (Position + 4 > _data.Length)
            {
                value = 0;
                return false;
            }
            value = BinaryPrimitives.ReadUInt32LittleEndian(_data.AsSpan(Position, 4));
            Position += 4;
            return true;
        }

        public bool TryReadPackedU32(out uint value)
        {
            if (!TryReadU8(out byte marker))
            {
                value = 0;
                return false;
            }
            if (marker < InvalidMarker)
            {
                value = marker;
                return true;
            }
            if (marker == InvalidMarker) // 0xFE - not a valid marker here, see class remarks
            {
                value = 0;
                return false;
            }
            return TryReadU32(out value);
        }

        public bool TryReadExact(int count, out byte[]? bytes)
        {
            if (count < 0 || Position + count > _data.Length)
            {
                bytes = null;
                return false;
            }
            bytes = _data.AsSpan(Position, count).ToArray();
            Position += count;
            return true;
        }
    }

    private static void WritePackedU32(Stream output, uint value)
    {
        if (value >= InvalidMarker)
        {
            WriteU8(output, LargeValueMarker);
            WriteU32(output, value);
            return;
        }
        WriteU8(output, (byte)value);
    }

    private static void WriteU8(Stream output, byte value) => output.WriteByte(value);

    private static void WriteU32(Stream output, uint value)
    {
        Span<byte> buffer = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32LittleEndian(buffer, value);
        output.Write(buffer);
    }
}
