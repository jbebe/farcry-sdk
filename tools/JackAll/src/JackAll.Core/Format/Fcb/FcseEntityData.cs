using System.Xml.Linq;

namespace JackAll.Core.Format.Fcb;

/// <summary>
/// FCSE's entity-data component as fragment XML: a mod adds it to an entity's <c>Components</c>, one
/// child object per key. See docs/docs/engine-internals/fcse-entity-data-abi.md.
/// </summary>
public static class FcseEntityData
{
    public static bool IsComponent(XElement element) => FcbXml.TypeHashOf(element) == WorldHashes.CFCSEDataComponent;

    /// <summary>Throws when a component in <paramref name="fragment"/> lists one key twice, which
    /// FCSE would read as whichever came last.</summary>
    public static void CheckKeys(XElement fragment)
    {
        if (Objects(fragment).Where(IsComponent)
                .SelectMany(component => component.Elements("object").GroupBy(FcbXml.TypeHashOf))
                .FirstOrDefault(key => key.Count() > 1) is { } twice)
        {
            throw new InvalidDataException(
                $"A CFCSEDataComponent lists the key '{FcbXml.KeyOf(twice.First())}' {twice.Count()} times - "
                + "a key may appear once.");
        }

        // The object tree alone: a value's own content, such as decoded Rml, is not FCB objects.
        static IEnumerable<XElement> Objects(XElement obj) => obj.Elements("object").SelectMany(Objects).Prepend(obj);
    }
}
