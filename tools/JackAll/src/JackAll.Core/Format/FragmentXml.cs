using System.Xml;
using System.Xml.Linq;

namespace JackAll.Core.Format;

/// <summary>
/// The shape every staged fragment is written in, whatever container it came from.
/// </summary>
/// <remarks>
/// Two of these settings are load-bearing rather than cosmetic. There is no declaration because a
/// fragment is written UTF-8 to disk, and an <see cref="XmlWriter"/> over a string buffer would
/// announce utf-16. The line ending is <see cref="Environment.NewLine"/> rather than the writer's
/// own unconditional CRLF because a fragment goes through <see cref="Fcb.Diff3"/>, which rejoins
/// merged lines that way - a mismatch rewrites every line of a fragment only one layer touched.
/// Indentation is the caller's, so each format can match the twins the game ships beside it.
/// </remarks>
public static class FragmentXml
{
    public static XmlWriterSettings Settings(string indentChars) => new()
    {
        Indent = true,
        IndentChars = indentChars,
        NewLineChars = Environment.NewLine,
        OmitXmlDeclaration = true,
    };

    /// <summary>One element rendered as a standalone fragment document.</summary>
    public static string Render(XElement root, string indentChars)
    {
        var text = new StringWriter();
        using (XmlWriter writer = XmlWriter.Create(text, Settings(indentChars)))
        {
            new XDocument(root).Save(writer);
        }
        return text.ToString();
    }
}
