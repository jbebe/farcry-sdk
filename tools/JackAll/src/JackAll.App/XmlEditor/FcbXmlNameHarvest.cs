using System.Xml.Linq;
using JackAll.Core.Format.Fcb;

namespace JackAll.App.XmlEditor;

/// <summary>
/// Recovers the hash -&gt; (name, declared value type) pairs an already-rendered FCB XML document embeds
/// directly in its own <c>type="ClassName"</c>/<c>name="fieldName"</c> attributes, by walking the same
/// text a second time and re-hashing each attribute the exact way <see cref="FcbXml.FromXml"/> itself does
/// (<see cref="FcbClassDefinitions.Crc32Ascii"/>). <see cref="FcbXml.FromXml"/> deliberately discards these
/// strings once parsed - a value's name/class becomes just a hash on <see cref="FcbObject"/> (see that
/// method's own remarks) - which is fine for an ordinary mod fragment (its name always came from the same
/// <see cref="FcbClassDefinitions"/> a viewer re-resolves it with afterward, so nothing is actually lost),
/// but not for a save's PersistenceDB tree: <c>SaveGameXmlRenderer</c> resolves a real fraction of its
/// names/types from save-specific dictionaries entitylibrary defs don't know about at all
/// (<c>SaveGameFieldCatalog</c>, <c>SaveGamePersistenceTags</c>, <c>SaveGameCompiledFieldNames</c>), so
/// re-deriving through defs alone after the round trip would show "hash XXXXXXXX"/raw BinHex again for
/// exactly the fields that document already named. This harvest is how <see cref="FcbObjectNodeView"/>
/// gets that information back without JackAll.App.SaveGames' save-only dictionaries having to be threaded
/// all the way down into a Core-level XML reader.
/// </summary>
public static class FcbXmlNameHarvest
{
    public readonly record struct Entry(string Name, FcbMemberType? ValueType);

    public static IReadOnlyDictionary<uint, Entry> Harvest(string xml)
    {
        var entries = new Dictionary<uint, Entry>();
        if (XDocument.Parse(xml).Root is { } root)
        {
            Walk(root, entries);
        }
        return entries;
    }

    private static void Walk(XElement el, Dictionary<uint, Entry> entries)
    {
        if ((string?)el.Attribute("type") is { Length: > 0 } typeName)
        {
            // On an <object>, "type" is the class name. On a <value>, it's the value's own wire type
            // (an FcbMemberType, not a name) - only the object case belongs in this name dictionary.
            if (el.Name == "object")
            {
                entries[FcbClassDefinitions.Crc32Ascii(typeName)] = new Entry(typeName, null);
            }
        }

        if ((string?)el.Attribute("name") is { Length: > 0 } fieldName)
        {
            FcbMemberType? valueType = (string?)el.Attribute("type") is { } wireTypeText
                && Enum.TryParse(wireTypeText, out FcbMemberType parsed)
                ? parsed
                : null;
            entries[FcbClassDefinitions.Crc32Ascii(fieldName)] = new Entry(fieldName, valueType);
        }

        foreach (XElement child in el.Elements())
        {
            Walk(child, entries);
        }
    }
}
