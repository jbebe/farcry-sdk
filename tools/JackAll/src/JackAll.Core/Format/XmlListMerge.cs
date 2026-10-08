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
    /// by child. A conflict keeps theirs and is reported by where it sits: <c>@attribute</c>, or a
    /// child's key followed by any path <paramref name="mergeChild"/> reports inside it. A null
    /// ancestor means both sides added the element.
    /// </summary>
    /// <param name="mergeChild">Merges a child both sides changed within itself, or returns null to
    /// leave it a conflict as a whole. The paths it returns are relative to that child.</param>
    public static (XElement Merged, IReadOnlyList<string> Conflicts) Merge(
        XElement? ancestor, XElement ours, XElement theirs, Func<XElement, string> keyOf,
        Func<XElement?, XElement, XElement, (XElement Merged, IReadOnlyList<string> Conflicts)?>? mergeChild = null)
    {
        var merged = new XElement(ours);
        XElement original = ancestor ?? new XElement(theirs.Name);
        List<string> conflicts = [];

        foreach (XName name in original.Attributes().Concat(merged.Attributes()).Concat(theirs.Attributes())
            .Select(a => a.Name).Distinct().ToList())
        {
            merged.SetAttributeValue(name, Fold(
                (string?)original.Attribute(name), (string?)merged.Attribute(name), (string?)theirs.Attribute(name),
                string.Equals, $"@{name.LocalName}"));
        }

        Dictionary<string, XElement> originals = Keyed(original, keyOf).ToDictionary(c => c.Key, c => c.Element);
        List<(string Key, XElement Element)> theirChildren = Keyed(theirs, keyOf);
        Dictionary<string, XElement> theirsByKey = theirChildren.ToDictionary(c => c.Key, c => c.Element);

        // Where each of our children ended up, null when the merge drops it, and which only we added.
        var kept = new Dictionary<string, XElement?>();
        var ourAdditions = new HashSet<XElement>();
        foreach ((string key, XElement mine) in Keyed(merged, keyOf))
        {
            XElement? before = originals.GetValueOrDefault(key);
            XElement? their = theirsByKey.GetValueOrDefault(key);
            XElement? version = Fold(before, mine, their, XNode.DeepEquals, keyOf(mine), mergeChild);
            if (version is null)
            {
                mine.Remove();
            }
            else if (version != mine)
            {
                version = new XElement(version);
                mine.ReplaceWith(version);
            }
            else if (before is null && their is null)
            {
                ourAdditions.Add(mine);
            }
            kept[key] = version;
        }

        // Each addition follows the last of their children already in the merge, after any of ours
        // added at the same place.
        XElement? anchor = null;
        foreach ((string key, XElement child) in theirChildren)
        {
            if (kept.TryGetValue(key, out XElement? at))
            {
                anchor = at ?? anchor;
                continue;
            }
            if (Fold(originals.GetValueOrDefault(key), null, child, XNode.DeepEquals, keyOf(child)) is not { } added)
            {
                continue;
            }

            XElement? next = anchor is null ? merged.Elements().FirstOrDefault() : anchor.ElementsAfterSelf().FirstOrDefault();
            while (next is not null && ourAdditions.Contains(next))
            {
                anchor = next;
                next = next.ElementsAfterSelf().FirstOrDefault();
            }

            var addition = new XElement(added);
            if (anchor is not null)
            {
                anchor.AddAfterSelf(addition);
            }
            else if (next is not null)
            {
                next.AddBeforeSelf(addition);
            }
            else
            {
                merged.Add(addition);
            }
            anchor = addition;
        }

        return (merged, conflicts);

        // The changed side's version when only one side changed it, the shared one when both made the
        // same change, and when the two disagree, what within makes of them, else theirs, flagged.
        T? Fold<T>(T? ancestorSide, T? ourSide, T? theirSide, Func<T?, T?, bool> same, string at,
            Func<T?, T, T, (T Merged, IReadOnlyList<string> Conflicts)?>? within = null) where T : class
        {
            if (same(theirSide, ancestorSide) || same(ourSide, theirSide))
            {
                return ourSide;
            }
            if (same(ourSide, ancestorSide))
            {
                return theirSide;
            }
            if (ourSide is not null && theirSide is not null && within?.Invoke(ancestorSide, ourSide, theirSide) is { } inner)
            {
                conflicts.AddRange(inner.Conflicts.Select(path => path.Length == 0 ? at : $"{at}/{path}"));
                return inner.Merged;
            }
            conflicts.Add(at);
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
