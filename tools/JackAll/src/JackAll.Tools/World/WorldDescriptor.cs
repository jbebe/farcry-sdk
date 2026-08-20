using System.Text;
using System.Xml.Linq;
using JackAll.Core.Format.Rml;

namespace JackAll.Tools.World;

/// <summary>
/// Opens a world's descriptor - <c>worlds\&lt;name&gt;\generated\&lt;name&gt;.game.xml</c> - which
/// despite the extension ships as binary RML on some worlds and plain XML on others.
/// </summary>
public static class WorldDescriptor
{
    /// <summary>The parsed root, or null when the world ships no descriptor or an unreadable one.</summary>
    public static XElement? TryLoadRoot(string mapName, Func<string, byte[]?> readByPath)
    {
        byte[]? bytes = readByPath($@"worlds\{mapName}\generated\{mapName}.game.xml");
        return bytes is null ? null : Parse(bytes);
    }

    /// <summary>Worlds ship this file in either form, so both are tried before giving up.</summary>
    private static XElement? Parse(byte[] bytes)
    {
        if (RmlDocument.TryDeserialize(bytes, out XElement? rml))
        {
            return rml;
        }

        try
        {
            return XDocument.Parse(Encoding.UTF8.GetString(bytes)).Root;
        }
        catch (Exception e) when (e is System.Xml.XmlException or ArgumentException)
        {
            return null;
        }
    }
}
