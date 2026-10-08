using System.Xml.Linq;

namespace JackAll.Core.Format.Fcb;

/// <summary>
/// FCSE's entity-data component as fragment XML: a mod adds it to an entity's <c>Components</c>, one
/// child object per key. See docs/docs/engine-internals/fcse-entity-data-abi.md.
/// </summary>
public static class FcseEntityData
{
    private static readonly string ComponentHash = FcbClassDefinitions.Crc32Ascii("CFCSEDataComponent").ToString("X8");

    /// <summary>Whether this object is the component, spelled by name or by hash.</summary>
    public static bool IsComponent(XElement element)
        => (string?)element.Attribute("type") == "CFCSEDataComponent"
        || string.Equals((string?)element.Attribute("hash"), ComponentHash, StringComparison.OrdinalIgnoreCase);

    /// <summary>Throws when a component in <paramref name="fragment"/> lists one key twice, which
    /// FCSE would read as whichever came last.</summary>
    public static void CheckKeys(XElement fragment)
    {
        foreach (XElement component in fragment.DescendantsAndSelf("object").Where(IsComponent))
        {
            if (component.Elements("object").GroupBy(FcbXml.HashOf).FirstOrDefault(key => key.Count() > 1) is { } twice)
            {
                throw new FormatException(
                    $"Its CFCSEDataComponent lists the key '{FcbXml.KeyOf(twice.First())}' {twice.Count()} times - "
                    + "a key may appear once.");
            }
        }
    }
}
