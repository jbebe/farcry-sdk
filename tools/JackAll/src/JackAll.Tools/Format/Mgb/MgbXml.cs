using System.Xml.Linq;

namespace JackAll.Tools.Format.Mgb;

/// <summary>
/// Converts a <c>.mgb</c> package to and from an editable XML document.
/// </summary>
/// <remarks>
/// This is an interchange format, not a format the game loads - the same relationship
/// <c>.fcb</c> has with the XML Gibbed's converter produces. Export it, edit it, build it back to
/// a binary <c>.mgb</c>.
///
/// It is deliberately *not* <c>.mgm</c>, Magma's own XML source format. <c>.mgm</c> is a source
/// format that compiles into a <c>.mgb</c>, so the two loaders are siblings rather than inverses,
/// and it has no construct for the per-file type table, the pool-count block, the header's
/// sentinel and flag bytes, or embedded font blobs. It also authors names as strings that the
/// engine hashes, and CRC32 does not invert - so exporting a shipped package to strict <c>.mgm</c>
/// would mean inventing names and breaking every cross-reference. See
/// docs/docs/file-formats/mgb.md.
///
/// What it borrows from <c>.mgm</c> is the vocabulary: element and attribute names are the
/// engine's own authored names, recovered by joining <c>BinaryLoadVisitor</c>'s wire order against
/// <c>LoadVisitor</c>'s XML element names.
/// </remarks>
public static class MgbXml
{
    private const string RootName = "MagmaPackage";
    private const string SentinelAttribute = "sentinel";
    private const string BigEndianAttribute = "bigEndian";

    /// <summary>Renders a package as an XML document.</summary>
    public static string ToXml(MgbPackage package)
    {
        var root = new XElement(RootName);

        // Bytes 5-8 sit ahead of everything Serialize describes, and 5-7 are consumed but never
        // examined by the engine, so they are carried verbatim rather than reconstructed.
        root.Add(new XAttribute(SentinelAttribute, Convert.ToHexString(package.Sentinel)));
        if (package.Invert)
        {
            root.Add(new XAttribute(BigEndianAttribute, "true"));
        }

        var codec = new MgbXmlWriteCodec(root, MgbNameLookup.For(package));
        package.SerializeBody(codec);
        return new XDocument(root).ToString();
    }

    /// <summary>Rebuilds a package from a document produced by <see cref="ToXml"/>.</summary>
    public static MgbPackage FromXml(string xml)
    {
        XDocument document;
        try
        {
            document = XDocument.Parse(xml);
        }
        catch (System.Xml.XmlException ex)
        {
            throw new MgbFormatException($"not well-formed XML: {ex.Message}");
        }

        XElement root = document.Root
            ?? throw new MgbFormatException("the XML document is empty");
        if (root.Name.LocalName != RootName)
        {
            throw new MgbFormatException(
                $"expected a <{RootName}> document element, found <{root.Name.LocalName}>");
        }

        var package = new MgbPackage
        {
            Sentinel = ParseSentinel((string?)root.Attribute(SentinelAttribute)),
            Invert = (string?)root.Attribute(BigEndianAttribute) == "true",
        };

        var codec = new MgbXmlReadCodec(root, SentinelAttribute, BigEndianAttribute);
        package.SerializeBody(codec);
        codec.Finish();
        return package;
    }

    /// <summary>Convenience for the common export path: binary in, XML out.</summary>
    public static string Decode(byte[] mgb) => ToXml(MgbPackage.Read(mgb));

    /// <summary>Convenience for the common build path: XML in, binary out.</summary>
    public static byte[] Encode(string xml) => FromXml(xml).Write();

    private static byte[] ParseSentinel(string? text)
    {
        if (text is null)
        {
            return [0xCD, 0x00, 0x00, 0xAB];
        }
        byte[] bytes;
        try
        {
            bytes = Convert.FromHexString(text);
        }
        catch (FormatException)
        {
            throw new MgbFormatException($"'{SentinelAttribute}' is not hex: \"{text}\"");
        }
        if (bytes.Length != 4)
        {
            throw new MgbFormatException(
                $"'{SentinelAttribute}' must be 4 bytes (8 hex digits), got {bytes.Length}");
        }
        return bytes;
    }
}
