using System.Xml.Linq;

namespace JackAll.Core.Format;

/// <summary>
/// A three-way merge of one element whose children form a list, each child matched by a key the
/// caller supplies. Additions from both sides land, the ancestor's order is kept, and only two
/// different versions of one child conflict.
/// </summary>
public static class XmlListMerge
{
    /// <summary>
    /// Folds <paramref name="theirs"/> into <paramref name="ours"/> attribute by attribute and child
    /// by child. A conflict keeps theirs and is flagged. A null ancestor means both sides added the
    /// element.
    /// </summary>
    public static (XElement Merged, bool Conflict) Merge(
        XElement? ancestor, XElement ours, XElement theirs, Func<XElement, string> keyOf)
    {
        var merged = new XElement(ours);
        XElement original = ancestor ?? new XElement(theirs.Name);
        bool conflict = false;

        foreach (XName name in original.Attributes().Concat(merged.Attributes()).Concat(theirs.Attributes())
            .Select(a => a.Name).Distinct().ToList())
        {
            merged.SetAttributeValue(name, Fold(
                (string?)original.Attribute(name), (string?)merged.Attribute(name), (string?)theirs.Attribute(name),
                string.Equals));
        }

        Dictionary<string, XElement> originals = Keyed(original, keyOf).ToDictionary(c => c.Key, c => c.Element);
        List<(string Key, XElement Element)> theirChildren = Keyed(theirs, keyOf);
        Dictionary<string, XElement> theirsByKey = theirChildren.ToDictionary(c => c.Key, c => c.Element);

        // Where each of our children ended up, null when the merge drops it.
        var kept = new Dictionary<string, XElement?>();
        foreach ((string key, XElement mine) in Keyed(merged, keyOf))
        {
            XElement? version = Fold(originals.GetValueOrDefault(key), mine, theirsByKey.GetValueOrDefault(key),
                XNode.DeepEquals);
            if (version is null)
            {
                mine.Remove();
            }
            else if (version != mine)
            {
                version = new XElement(version);
                mine.ReplaceWith(version);
            }
            kept[key] = version;
        }

        // Each addition follows the last of their children already in the merge.
        XElement? anchor = null;
        foreach ((string key, XElement child) in theirChildren)
        {
            if (kept.TryGetValue(key, out XElement? at))
            {
                anchor = at ?? anchor;
                continue;
            }
            if (Fold(originals.GetValueOrDefault(key), null, child, XNode.DeepEquals) is not { } added)
            {
                continue;
            }

            var addition = new XElement(added);
            if (anchor is not null)
            {
                anchor.AddAfterSelf(addition);
            }
            else if (merged.Elements().FirstOrDefault() is { } first)
            {
                first.AddBeforeSelf(addition);
            }
            else
            {
                merged.Add(addition);
            }
            anchor = addition;
        }

        return (merged, conflict);

        // The changed side's version when only one side changed it, the shared one when both made the
        // same change, and theirs, flagged, when the two disagree.
        T? Fold<T>(T? ancestorSide, T? ourSide, T? theirSide, Func<T?, T?, bool> same) where T : class
        {
            if (same(theirSide, ancestorSide) || same(ourSide, theirSide))
            {
                return ourSide;
            }
            if (!same(ourSide, ancestorSide))
            {
                conflict = true;
            }
            return theirSide;
        }
    }

    /// <summary>Every child under its key, a repeated key numbered by occurrence so identical siblings
    /// stay distinct.</summary>
    private static List<(string Key, XElement Element)> Keyed(XElement parent, Func<XElement, string> keyOf)
    {
        var occurrences = new Dictionary<string, int>();
        var keyed = new List<(string Key, XElement Element)>();
        foreach (XElement child in parent.Elements())
        {
            string key = keyOf(child);
            int occurrence = occurrences[key] = occurrences.GetValueOrDefault(key) + 1;
            keyed.Add(($"{key}#{occurrence}", child));
        }
        return keyed;
    }
}
