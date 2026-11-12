using System.Text;
using System.Xml;
using System.Xml.Linq;

namespace JackAll.Core.Format;

/// <summary>A shipped XML file read and written back as the game has it.</summary>
public static class XmlFileText
{
    /// <summary>The file as its declaration says it is encoded, UTF-8 without one.</summary>
    public static XDocument Load(byte[] bytes, LoadOptions options)
    {
        using var stream = new MemoryStream(bytes);
        return XDocument.Load(stream, options);
    }

    /// <summary>The file in the encoding its declaration names, without a byte order mark, and with
    /// the CRLF line endings the shipped files use.</summary>
    public static byte[] Save(XDocument document)
    {
        Encoding encoding = document.Declaration?.Encoding is { Length: > 0 } name
            ? Encoding.GetEncoding(name)
            : Encoding.UTF8;
        var settings = new XmlWriterSettings
        {
            Encoding = encoding is UTF8Encoding ? new UTF8Encoding(false) : encoding,
            OmitXmlDeclaration = document.Declaration is null,
            NewLineChars = "\r\n",
        };

        using var stream = new MemoryStream();
        using (XmlWriter writer = XmlWriter.Create(stream, settings))
        {
            document.Save(writer);
        }
        return stream.ToArray();
    }
}
