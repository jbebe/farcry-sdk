using System.Text;
using System.Xml.Linq;
using JackAll.Core.Format;

namespace JackAll.Core.Legacy;

/// <summary>One difference between two XML documents, addressed by a path inside them.</summary>
public sealed record XmlChange(ChangeKind Kind, string Path, string? Old, string? New);

/// <summary>
/// Field-level differences between two XML documents, and the base document with only some of
/// them applied - the same walk run twice, so a pick can never disagree with the diff it came from.
/// </summary>
/// <remarks>
/// A path names each element by its label: an <c>.fcb</c> value by its name, an object by its type
/// (and its own Name value, when its siblings repeat the type), anything else by its tag and first
/// key attribute. Siblings sharing a label are aligned by longest
/// common subsequence of their content, so an inserted list entry is one addition rather than every
/// later entry changing; they carry <c>[i]</c>, the base index, or <c>[+i]</c> for an addition, the
/// mod's.
/// </remarks>
public static class XmlTreeDiff
{
    private static readonly string[] KeyAttributes = ["name", "id", "path", "class", "type", "enum", "key", "hash"];

    /// <summary>Every difference, and whether applying all of them rebuilds <paramref name="mod"/>.</summary>
    /// <param name="expand">The document a leaf's text encodes, when it encodes one - a nested format
    /// compared field by field instead of as one opaque value.</param>
    public static (List<XmlChange> Changes, bool Exact) Diff(
        XElement vanilla, XElement mod, Func<XElement, XElement?>? expand = null)
    {
        List<XmlChange> changes = [];
        XElement rebuilt = new Walker(_ => true, changes, expand).Element(vanilla, mod, string.Empty);
        return (changes, Canonical(rebuilt, expand) == Canonical(mod, expand));
    }

    /// <summary><paramref name="vanilla"/> with the differences whose paths <paramref name="take"/>
    /// accepts. An expanded leaf keeps its encoded text unless one of its changes is taken.</summary>
    public static XElement Merge(
        XElement vanilla, XElement mod, Func<string, bool> take, Func<XElement, XElement?>? expand = null)
        => new Walker(take, null, expand).Element(vanilla, mod, string.Empty);

    /// <summary>The element's identity among its siblings, without an index.</summary>
    public static string Label(XElement element) => Label(element, named: false);

    /// <param name="named">Tell an object apart by its own Name value, for a type its siblings repeat.</param>
    private static string Label(XElement element, bool named)
    {
        string tag = element.Name.LocalName;
        if (tag == "value" && element.Attribute("name") is { } name)
        {
            return Clean(name.Value);
        }

        if (tag == "object" && element.Attribute("type") is { } type)
        {
            return named
                   && element.Elements("value").FirstOrDefault(v => (string?)v.Attribute("name") is "Name" or "hidName") is { } own
                   && own.Value.Length > 0
                ? $"{Clean(type.Value)}[{Clean(own.Value)}]"
                : Clean(type.Value);
        }

        foreach (string key in KeyAttributes)
        {
            if (element.Attribute(key) is { } attribute)
            {
                return $"{tag}[{Clean(attribute.Value)}]";
            }
        }

        return tag;
    }

    /// <summary>How each of these siblings is labelled: by its type alone, unless the type repeats in
    /// either list, which a Name value then tells apart better than a position.</summary>
    private static Func<XElement, string> Labels(params IEnumerable<XElement>[] lists)
    {
        HashSet<string> repeated = [.. lists.SelectMany(list => list
            .GroupBy(Label)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key))];
        return element => repeated.Contains(Label(element)) ? Label(element, named: true) : Label(element);
    }

    /// <summary>Path separators inside a label would split it into segments.</summary>
    private static string Clean(string label) => label.Replace('/', '_').Replace('#', '_').Replace('@', '_');

    /// <summary>A form that is equal exactly when two elements mean the same: attribute order, the
    /// order of differently-labelled siblings, formatting and a nested document's encoding are not
    /// significant.</summary>
    public static string Canonical(XElement element, Func<XElement, XElement?>? expand = null)
    {
        var text = new StringBuilder();
        AppendCanonical(Expanded(element, expand), text, expand);
        return text.ToString();
    }

    /// <summary>A leaf with what it encodes as its child in place of its text; anything else as is.</summary>
    private static XElement Expanded(XElement element, Func<XElement, XElement?>? expand)
        => !element.HasElements && expand?.Invoke(element) is { } inner
            ? new XElement(element.Name, element.Attributes(), inner)
            : element;

    private static void AppendCanonical(XElement element, StringBuilder text, Func<XElement, XElement?>? expand)
    {
        text.Append('<').Append(element.Name.LocalName);
        foreach (XAttribute attribute in element.Attributes().OrderBy(a => a.Name.LocalName, StringComparer.Ordinal))
        {
            text.Append(' ').Append(attribute.Name.LocalName).Append("=\"").Append(attribute.Value).Append('"');
        }

        text.Append('>');
        if (element.HasElements)
        {
            foreach (XElement child in element.Elements().OrderBy(Labels(element.Elements()), StringComparer.Ordinal))
            {
                AppendCanonical(Expanded(child, expand), text, expand);
            }
        }
        else
        {
            text.Append(element.Value);
        }

        text.Append("</>");
    }

    private sealed class Walker(Func<string, bool> take, List<XmlChange>? sink, Func<XElement, XElement?>? expand)
    {
        private int _taken;

        public XElement Element(XElement vanilla, XElement mod, string path)
        {
            if (expand is not null && !XNode.DeepEquals(vanilla, mod))
            {
                XElement before = Expanded(vanilla, expand);
                XElement after = Expanded(mod, expand);
                if (before != vanilla || after != mod)
                {
                    int taken = _taken;
                    XElement merged = Element(before, after, path);
                    return _taken == taken ? new XElement(vanilla) : merged;
                }
            }

            // An empty element is a list with nothing in it yet; only text against children is a
            // change of shape.
            if ((vanilla.HasElements && mod.Value.Length > 0 && !mod.HasElements)
                || (mod.HasElements && vanilla.Value.Length > 0 && !vanilla.HasElements))
            {
                return Record(ChangeKind.Field, path, vanilla.ToString(), mod.ToString())
                    ? new XElement(mod)
                    : new XElement(vanilla);
            }

            var result = new XElement(vanilla.Name);
            Attributes(vanilla, mod, path, result);

            if (!vanilla.HasElements && !mod.HasElements)
            {
                string value = vanilla.Value;
                if (value != mod.Value && Record(ChangeKind.Field, path, vanilla.Value, mod.Value))
                {
                    value = mod.Value;
                }

                if (value.Length > 0)
                {
                    result.Value = value;
                }

                return result;
            }

            Children(vanilla, mod, path, result);
            return result;
        }

        private void Attributes(XElement vanilla, XElement mod, string path, XElement result)
        {
            IEnumerable<XName> names = vanilla.Attributes().Select(a => a.Name)
                .Concat(mod.Attributes().Select(a => a.Name))
                .Distinct();
            foreach (XName name in names)
            {
                string? before = vanilla.Attribute(name)?.Value;
                string? after = mod.Attribute(name)?.Value;
                string? value = before != after && Record(ChangeKind.Field, $"{path}@{name.LocalName}", before, after)
                    ? after
                    : before;
                if (value is not null)
                {
                    result.SetAttributeValue(name, value);
                }
            }
        }

        private void Children(XElement vanilla, XElement mod, string path, XElement result)
        {
            List<XElement> vanillaChildren = [.. vanilla.Elements()];
            List<XElement> modChildren = [.. mod.Elements()];

            // What each base child becomes (null when its removal is taken), which base child each
            // mod child was paired with, and the additions taken.
            var outcome = new Dictionary<XElement, XElement?>();
            var pairedWith = new Dictionary<XElement, XElement>();
            var added = new Dictionary<XElement, XElement>();

            Func<XElement, string> labelOf = Labels(vanillaChildren, modChildren);
            var vanillaByLabel = vanillaChildren.GroupBy(labelOf).ToDictionary(g => g.Key, g => g.ToList());
            var modByLabel = modChildren.GroupBy(labelOf).ToDictionary(g => g.Key, g => g.ToList());
            foreach (string label in vanillaByLabel.Keys.Concat(modByLabel.Keys).Distinct())
            {
                List<XElement> before = vanillaByLabel.GetValueOrDefault(label) ?? [];
                List<XElement> after = modByLabel.GetValueOrDefault(label) ?? [];
                bool indexed = before.Count > 1 || after.Count > 1;

                foreach ((int? b, int? a) in SiblingAlignment.Align(before, after, e => e.ToString(SaveOptions.DisableFormatting)))
                {
                    if (b is { } bi && a is { } ai)
                    {
                        pairedWith[after[ai]] = before[bi];
                        outcome[before[bi]] = XNode.DeepEquals(before[bi], after[ai])
                            ? new XElement(before[bi])
                            : Element(before[bi], after[ai], Join(path, indexed ? $"{label}[{bi}]" : label));
                    }
                    else if (b is { } removed)
                    {
                        string at = Join(path, indexed ? $"{label}[{removed}]" : label);
                        outcome[before[removed]] = Record(ChangeKind.Remove, at, before[removed].ToString(), null)
                            ? null
                            : new XElement(before[removed]);
                    }
                    else if (a is { } addedAt)
                    {
                        string at = Join(path, indexed ? $"{label}[+{addedAt}]" : label);
                        if (Record(ChangeKind.Add, at, null, after[addedAt].ToString()))
                        {
                            added[after[addedAt]] = new XElement(after[addedAt]);
                        }
                    }
                }
            }

            // An addition goes after the base child its nearest preceding mod sibling was paired with.
            var addedAfter = new Dictionary<XElement, List<XElement>>();
            List<XElement> leading = [];
            XElement? anchor = null;
            foreach (XElement child in modChildren)
            {
                if (pairedWith.TryGetValue(child, out XElement? paired))
                {
                    anchor = paired;
                }
                else if (added.TryGetValue(child, out XElement? copy))
                {
                    if (anchor is null)
                    {
                        leading.Add(copy);
                    }
                    else
                    {
                        (addedAfter.TryGetValue(anchor, out List<XElement>? list) ? list : addedAfter[anchor] = []).Add(copy);
                    }
                }
            }

            result.Add(leading);
            foreach (XElement child in vanillaChildren)
            {
                if (outcome[child] is { } kept)
                {
                    result.Add(kept);
                }

                if (addedAfter.TryGetValue(child, out List<XElement>? following))
                {
                    result.Add(following);
                }
            }
        }

        private bool Record(ChangeKind kind, string path, string? before, string? after)
        {
            sink?.Add(new XmlChange(kind, path, before, after));
            bool taken = take(path);
            _taken += taken ? 1 : 0;
            return taken;
        }

        private static string Join(string path, string segment) => path.Length == 0 ? segment : $"{path}/{segment}";
    }
}
