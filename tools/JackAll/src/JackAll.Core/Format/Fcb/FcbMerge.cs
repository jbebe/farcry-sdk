using System.Xml.Linq;

namespace JackAll.Core.Format.Fcb;

/// <summary>
/// A three-way merge of one canonical fragment as a tree, pairing children the way the engine lays a
/// placed instance over its archetype: a value by its name, an object by its type, each object merged
/// again within itself but an FCSE entity-data key, which is one value. Siblings sharing a type are a
/// list, whose items are matched to the ancestor's by content (see <see cref="XmlListMerge"/>).
/// </summary>
public static class FcbMerge
{
    /// <summary>
    /// The merged fragment, and every place the two disagreed, where theirs was kept: a path of names
    /// and types below the root, or the empty path for the root itself. An empty ancestor means both
    /// sides added the fragment.
    /// </summary>
    public static (string Merged, IReadOnlyList<string> Conflicts) Merge(string ancestor, string ours, string theirs)
    {
        (XElement merged, IReadOnlyList<string> conflicts) = Merge(
            ancestor.Length == 0 ? null : XElement.Parse(ancestor), XElement.Parse(ours), XElement.Parse(theirs));
        return (merged.ToString(), conflicts);
    }

    private static (XElement Merged, IReadOnlyList<string> Conflicts) Merge(XElement? ancestor, XElement ours, XElement theirs)
    {
        bool keys = FcseEntityData.IsComponent(ours);
        return XmlListMerge.Merge(ancestor, ours, theirs, KeyOf,
            (a, o, t) => !keys && o.Name == "object" && t.Name == "object" ? Merge(a, o, t) : (t, [""]));
    }

    private static string KeyOf(XElement child) => FcbXml.KeyOf(child) ?? "";
}
