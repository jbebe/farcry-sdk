using System.Xml.Linq;

namespace JackAll.Core.Format.Fcb;

/// <summary>
/// A three-way merge of one canonical fragment as a tree, pairing children the way the engine lays a
/// placed instance over its archetype: a value by its name, an object by its type, each object merged
/// again within itself. Siblings sharing a type are a list with nothing to pair by, so the element
/// holding them merges line by line through <see cref="Diff3"/>.
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
        if (!Repeats(ancestor) && !Repeats(ours) && !Repeats(theirs))
        {
            return XmlListMerge.Merge(ancestor, ours, theirs, KeyOf,
                (a, o, t) => o.Name == "object" && t.Name == "object" ? Merge(a, o, t) : null);
        }

        (string text, bool conflict) = Diff3.Merge(ancestor?.ToString() ?? "", ours.ToString(), theirs.ToString());
        return conflict ? (theirs, [""]) : (XElement.Parse(text), []);
    }

    /// <summary>What a child is paired by: a value's name, an object's type, else the hash spelling
    /// either.</summary>
    private static string KeyOf(XElement child)
        => (string?)child.Attribute(child.Name == "value" ? "name" : "type") ?? (string?)child.Attribute("hash") ?? "";

    private static bool Repeats(XElement? element)
        => element is not null && element.Elements().CountBy(KeyOf).Any(count => count.Value > 1);
}
