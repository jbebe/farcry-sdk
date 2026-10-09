using System.Xml.Linq;
using JackAll.Core.Format;

namespace JackAll.Tools.Xbt;

/// <summary>
/// Splits a Dunia .xbt texture into its engine-specific header and the embedded, fully valid .dds
/// payload, and reassembles the two back into a byte-identical .xbt.
/// </summary>
/// <remarks>
/// The header is "TBX\0", <c>Version</c>, <c>HeaderSize</c> (the DDS payload's offset), a flags
/// dword, a 12-byte hash (v11 only) and the null-terminated path of the "_mip0.xbt" companion, or
/// an empty one. A header comes from a real .xbt via <see cref="Split"/> or <see cref="HeaderFromXml"/>,
/// or is written fresh by <see cref="NewHeader"/>; docs/docs/file-formats/xbt.md has what the engine reads.
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

    /// <summary>A resolution factor of 1, what the engine's own writer and 92% of retail textures carry.</summary>
    public const uint DefaultFlags = 1;

    /// <summary>The flags dword: the resolution factor in the low byte, and 0x100 pinning the mip chain.</summary>
    public static uint Flags(byte[] header) => ByteCursor.U32(header, 12);

    /// <summary>
    /// A v11 header written from scratch, the way the engine's own writer makes one: a zero hash,
    /// which the engine never reads, and no companion, so the file carries the whole mip chain.
    /// </summary>
    public static byte[] NewHeader(uint flags = DefaultFlags)
    {
        var w = new ByteWriter();
        w.WriteU32(Signature);
        w.WriteU32(CurrentVersion);
        w.WriteU32(V11FixedHeaderSize + 4);
        w.WriteU32(flags);
        w.WriteRaw(new byte[12 + 4]);
        return w.ToArray();
    }

    /// <summary>Splits raw .xbt bytes into the header (everything before the DDS payload) and the DDS payload.</summary>
    public static (byte[] Header, byte[] Dds) Split(byte[] xbt)
    {
        if (xbt.Length < 16 || ByteCursor.U32(xbt, 0) != Signature)
        {
            throw new InvalidDataException("Not an XBT file (missing 'TBX\\0' signature).");
        }

        uint version = ByteCursor.U32(xbt, 4);
        if (version != LegacyVersion && version != CurrentVersion)
        {
            throw new InvalidDataException($"Unsupported XBT header version {version} (expected 10 or 11).");
        }

        uint headerSize = ByteCursor.U32(xbt, 8);
        if (headerSize > (uint)xbt.Length - 4 || ByteCursor.U32(xbt, (int)headerSize) != DdsMagic)
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
            uint version = ByteCursor.U32(header, 4);
            metadata.Add(
                new XElement("Version", version),
                new XElement("StoredHeaderSize", ByteCursor.U32(header, 8)),
                new XElement("Reserved", Flags(header)));

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

    /// <summary>
    /// The archive-relative path of this file's <c>_mip0.xbt</c> streaming companion, or null when the
    /// header names none. That companion is not an optional extra: it holds the texture's real top mip
    /// level, and the file naming it starts one level down. Half the shipped graphics tree is stored
    /// this way, so a reader that ignores it renders every one of those at half resolution.
    /// </summary>
    /// <param name="header">The header <see cref="Split"/> returned.</param>
    public static string? CompanionPath(byte[] header)
    {
        if (header.Length < 16)
        {
            return null;
        }

        uint version = ByteCursor.U32(header, 4);
        return ReadEmbeddedPath(header, version == LegacyVersion ? V10FixedHeaderSize : V11FixedHeaderSize);
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

    /// <summary>A null-terminated ASCII path occupying [fixedEnd, header.Length); present on 960 of the
    /// 1,947 textures in the shipped graphics tree and empty on the rest.</summary>
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
