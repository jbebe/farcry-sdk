using System.Xml.Linq;

namespace JackAll.Core.Format;

/// <summary>
/// Splits a Dunia .xbt texture into its engine-specific header and the embedded, fully valid .dds
/// payload, and reassembles the two back into a byte-identical .xbt.
/// </summary>
/// <remarks>
/// Field layout confirmed directly against the shipped engine (Dunia.dll, <c>Xbt_ParseHeader</c> @
/// 0x10339b40, via GhidraMCP): "TBX\0" signature, a format <c>Version</c> (11 in every real sample;
/// 10 is an older, shorter variant the loader still accepts), a <c>HeaderSize</c> field that IS the
/// byte offset of the embedded DDS payload — the engine computes <c>dds = buffer + HeaderSize</c>
/// directly, it never scans for "DDS " the way this class used to — a <c>Reserved</c> dword, and,
/// for v11 only, a 12-byte <c>Hash</c>. Whatever bytes remain up to <c>HeaderSize</c> are a
/// null-terminated embedded path: when present, it's the archive-relative path of the file's own
/// "_mip0.xbt" streaming companion (empty for textures with no separate mip stream).
///
/// <c>Reserved</c> and <c>Hash</c> are NOT understood well enough to synthesize. Traced every one of
/// Xbt_ParseHeader's 5 callers in Dunia.dll: 4 of them (including the plain 2D/cube texture creation
/// path) only ever read the computed DDS pointer/size and ignore Reserved/Hash entirely, but one —
/// the streaming-texture loader that decides whether to pull in a "_mip0" companion — genuinely
/// consumes <c>Reserved</c> as a bitfield: bit 0x100 is a flag that resets two LOD-tracking fields on
/// the resource object, and the low byte is stored into the object and consumed later for streaming
/// decisions elsewhere in that class. So a wrong <c>Reserved</c> value is not just untidy, it can
/// alter real streaming/LOD behavior. A survey of ~130 real .xbt files shows it varies per-asset (1,
/// 2, and 4 all appear) with no correlation found yet to DDS format, mip-companion presence, or
/// texture naming. <c>Hash</c>'s leading 4 bytes are a stable per-asset ID (shared between a
/// texture's resolution tiers, e.g. a file and its own "_mip0" sibling) that does not match a CRC32
/// of the resource path or any other tested derivation, and — unlike Reserved — no traced caller
/// reads it back at all; its purpose is still unknown. Because of both gaps, this class deliberately
/// does NOT offer a "build an XBT from just a DDS" path — every header byte here comes from a real
/// .xbt, either read via <see cref="Split"/> or restored from XML via <see cref="HeaderFromXml"/>.
/// </remarks>
public static class XbtTexture
{
    private const uint Signature = 0x00584254; // "TBX\0", little-endian
    private const uint DdsMagic = 0x20534444; // "DDS ", little-endian
    private const uint CurrentVersion = 11;
    private const uint LegacyVersion = 10;

    /// <summary>Fixed portion of a v11 header: signature(4) + version(4) + headerSize(4) + reserved(4) + hash(12).</summary>
    private const int V11FixedHeaderSize = 28;

    /// <summary>Fixed portion of a v10 header: signature(4) + version(4) + headerSize(4) + reserved(4) + one dword(4).</summary>
    private const int V10FixedHeaderSize = 20;

    /// <summary>Splits raw .xbt bytes into the header (everything before the DDS payload) and the DDS payload.</summary>
    public static (byte[] Header, byte[] Dds) Split(byte[] xbt)
    {
        if (xbt.Length < 16 || ReadU32(xbt, 0) != Signature)
        {
            throw new InvalidDataException("Not an XBT file (missing 'TBX\\0' signature).");
        }

        uint version = ReadU32(xbt, 4);
        if (version != LegacyVersion && version != CurrentVersion)
        {
            throw new InvalidDataException($"Unsupported XBT header version {version} (expected 10 or 11).");
        }

        uint headerSize = ReadU32(xbt, 8);
        if (headerSize > (uint)xbt.Length - 4 || ReadU32(xbt, (int)headerSize) != DdsMagic)
        {
            throw new InvalidDataException("XBT header's HeaderSize field doesn't point at a 'DDS ' payload.");
        }

        return (xbt[..(int)headerSize], xbt[(int)headerSize..]);
    }

    /// <summary>
    /// Reassembles an .xbt file from a header (as produced by <see cref="Split"/> or <see cref="HeaderFromXml"/>)
    /// and a DDS payload.
    /// </summary>
    public static byte[] Combine(byte[] header, byte[] dds)
    {
        byte[] result = new byte[header.Length + dds.Length];
        header.CopyTo(result, 0);
        dds.CopyTo(result, header.Length);
        return result;
    }

    /// <summary>
    /// Renders the header as a companion XML file: its fully decoded fields, plus the raw bytes as
    /// hex for lossless round-tripping via <see cref="HeaderFromXml"/>.
    /// </summary>
    public static string ToXml(byte[] header)
    {
        var metadata = new XElement("Metadata", new XElement("HeaderSize", header.Length));

        if (header.Length >= 16)
        {
            uint version = ReadU32(header, 4);
            metadata.Add(
                new XElement("Version", version),
                new XElement("StoredHeaderSize", ReadU32(header, 8)),
                new XElement("Reserved", ReadU32(header, 12)));

            int fixedEnd = version == LegacyVersion ? V10FixedHeaderSize : V11FixedHeaderSize;
            if (version != LegacyVersion && header.Length >= V11FixedHeaderSize)
            {
                metadata.Add(new XElement("Hash", Convert.ToHexString(header.AsSpan(16, 12))));
            }

            string? embeddedPath = ReadEmbeddedPath(header, fixedEnd);
            if (embeddedPath is not null)
            {
                metadata.Add(new XElement("EmbeddedPath", embeddedPath));
            }
        }

        var root = new XElement("XBTHeader", metadata, new XElement("RawHeaderData", Convert.ToHexString(header)));
        return new XDocument(root).ToString();
    }

    /// <summary>Recovers the raw header bytes from a companion XML file produced by <see cref="ToXml"/>.</summary>
    public static byte[] HeaderFromXml(string xml)
    {
        XElement? root = XDocument.Parse(xml).Root;
        if (root is not { Name.LocalName: "XBTHeader" })
        {
            throw new InvalidDataException("Not an XBT header XML file.");
        }

        string? hex = root.Element("RawHeaderData")?.Value;
        if (string.IsNullOrWhiteSpace(hex))
        {
            throw new InvalidDataException("XBT header XML is missing its RawHeaderData element.");
        }

        return Convert.FromHexString(hex.Trim());
    }

    private static uint ReadU32(byte[] data, int offset)
        => (uint)(data[offset] | (data[offset + 1] << 8) | (data[offset + 2] << 16) | (data[offset + 3] << 24));

    /// <summary>A null-terminated ASCII path occupying [fixedEnd, header.Length); empty in every sample seen so far.</summary>
    private static string? ReadEmbeddedPath(byte[] header, int fixedEnd)
    {
        if (header.Length <= fixedEnd)
        {
            return null;
        }

        ReadOnlySpan<byte> tail = header.AsSpan(fixedEnd);
        int nullPos = tail.IndexOf((byte)0);
        int length = nullPos < 0 ? tail.Length : nullPos;
        if (length <= 0)
        {
            return null;
        }

        string path = System.Text.Encoding.ASCII.GetString(tail[..length]);
        return string.IsNullOrWhiteSpace(path) ? null : path;
    }
}
