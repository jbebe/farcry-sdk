using System.Xml.Linq;

namespace JackAll.Core.Format;

/// <summary>
/// A three-way merge of one element whose children form a list, each child matched by a key the
/// caller supplies. Additions from both sides land, the ancestor's order is kept, and only two
/// different versions of one child conflict. Children sharing a key are a list, whose items are
/// matched to the ancestor's by content, then by position.
/// </summary>
public static class XmlListMerge
{
    /// <summary>
    /// Folds <paramref name="theirs"/> into <paramref name="ours"/> attribute by attribute and child
    /// by child. A conflict keeps theirs and is reported by where it sits: <c>@attribute</c>, or a
    /// child's key - a list item's with its index, <c>Link[2]</c> - followed by any path inside it. A
    /// null ancestor means both sides added the element.
    /// </summary>
    /// <param name="mergeChild">Merges a child both sides changed, which otherwise conflicts whole.</param>
    public static (XElement Merged, IReadOnlyList<string> Conflicts) Merge(
        XElement? ancestor, XElement ours, XElement theirs, Func<XElement, string> keyOf,
        Func<XElement?, XElement, XElement, (XElement Merged, IReadOnlyList<string> Conflicts)>? mergeChild = null)
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

        // A key repeated on any side is a list on all three, so each keys its items the same way.
        HashSet<string> lists = [.. new[] { original, ours, theirs }
            .SelectMany(side => side.Elements().CountBy(keyOf)).Where(count => count.Value > 1).Select(count => count.Key)];
        Dictionary<string, XElement> originals = Keyed(original, original, keyOf, lists).ToDictionary(c => c.Key, c => c.Element);
        List<(string Key, string Label, XElement Element)> theirChildren = Keyed(original, theirs, keyOf, lists);
        Dictionary<string, XElement> theirsByKey = theirChildren.ToDictionary(c => c.Key, c => c.Element);

        // Where each of our children ended up, null when the merge drops it, and which only we added.
        var kept = new Dictionary<string, XElement?>();
        var ourAdditions = new HashSet<XElement>();
        foreach ((string key, string label, XElement mine) in Keyed(original, merged, keyOf, lists))
        {
            XElement? before = originals.GetValueOrDefault(key);
            XElement? their = theirsByKey.GetValueOrDefault(key);
            XElement? version = Fold(before, mine, their, XNode.DeepEquals, label, mergeChild);
            if (version is null)
            {
                mine.Remove();
            }
            else if (version != mine)
            {
                if (version.Parent is not null)
                {
                    version = new XElement(version);
                }
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
        foreach ((string key, string label, XElement child) in theirChildren)
        {
            if (kept.TryGetValue(key, out XElement? at))
            {
                anchor = at ?? anchor;
                continue;
            }
            if (Fold(originals.GetValueOrDefault(key), null, child, XNode.DeepEquals, label) is not { } added)
            {
                continue;
            }

            anchor = (anchor is null ? merged.Elements() : anchor.ElementsAfterSelf())
                .TakeWhile(ourAdditions.Contains).LastOrDefault() ?? anchor;

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

        return (merged, conflicts);

        // The changed side's version when only one side changed it, the shared one when both made the
        // same change, and when the two disagree, what within makes of them, else theirs, flagged.
        T? Fold<T>(T? ancestorSide, T? ourSide, T? theirSide, Func<T?, T?, bool> same, string at,
            Func<T?, T, T, (T Merged, IReadOnlyList<string> Conflicts)>? within = null) where T : class
        {
            if (same(theirSide, ancestorSide) || same(ourSide, theirSide))
            {
                return ourSide;
            }
            if (same(ourSide, ancestorSide))
            {
                return theirSide;
            }
            if (ourSide is not null && theirSide is not null && within is not null)
            {
                (T Merged, IReadOnlyList<string> Conflicts) inner = within(ancestorSide, ourSide, theirSide);
                conflicts.AddRange(inner.Conflicts.Select(path => path.Length == 0 ? at : $"{at}/{path}"));
                return inner.Merged;
            }
            conflicts.Add(at);
            return theirSide;
        }
    }

    /// <summary>Every child of <paramref name="side"/> with the key it pairs by and the label a conflict
    /// names it by: a list item keyed by the original item it aligns with, an addition by its content.</summary>
    private static List<(string Key, string Label, XElement Element)> Keyed(
        XElement original, XElement side, Func<XElement, string> keyOf, HashSet<string> lists)
    {
        var items = new Dictionary<XElement, (string Key, string Label)>();
        foreach (IGrouping<string, XElement> list in side.Elements().GroupBy(keyOf).Where(g => lists.Contains(g.Key)))
        {
            List<XElement> after = [.. list];
            IEnumerable<(int? Before, int? After)> pairs = side == original
                ? after.Select((_, i) => ((int?)i, (int?)i))
                : SiblingAlignment.Align(
                    [.. original.Elements().Where(e => keyOf(e) == list.Key)], after, item => Identity(item, keyOf));
            var additions = new Dictionary<string, int>();
            foreach ((int? b, int? a) in pairs)
            {
                if (a is not { } at)
                {
                    continue;
                }
                if (b is { } match)
                {
                    items[after[at]] = ($"{list.Key}#{match}", $"{list.Key}[{match}]");
                    continue;
                }
                string content = Identity(after[at], keyOf);
                int copy = additions[content] = additions.GetValueOrDefault(content) + 1;
                items[after[at]] = ($"{list.Key}+{copy}{content}", $"{list.Key}[+{at}]");
            }
        }

        return [.. side.Elements().Select(child =>
        {
            if (items.TryGetValue(child, out (string Key, string Label) item))
            {
                return (item.Key, item.Label, child);
            }
            string key = keyOf(child);
            return (key, key, child);
        })];
    }

    /// <summary>An element as text in which the order of differently keyed siblings does not count, so
    /// a list item matches its ancestor however its fields are ordered.</summary>
    private static string Identity(XElement element, Func<XElement, string> keyOf)
        => element.HasElements
            ? $"<{element.Name}{string.Concat(element.Attributes().Select(a => $" {a}"))}>"
                + string.Concat(element.Elements().OrderBy(keyOf, StringComparer.Ordinal).Select(child => Identity(child, keyOf)))
                + "</>"
            : element.ToString(SaveOptions.DisableFormatting);
}
