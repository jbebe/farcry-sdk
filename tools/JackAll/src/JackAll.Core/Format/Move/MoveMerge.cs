using System.Text;
using System.Xml.Linq;

namespace JackAll.Core.Format.Move;

/// <summary>
/// A three-way merge of canonical MOVE fragments, op by op, each matched by its field name and a
/// repeated one as a list. Object ids are positions, so a reference names its target by path while the
/// merge runs; a fragment holding references that both sides reshaped keeps theirs whole.
/// </summary>
public static class MoveMerge
{
    public static (string Merged, IReadOnlyList<string> Conflicts) Merge(string ancestor, string ours, string theirs)
    {
        XElement mine = Addressed(ours);
        XElement their = Addressed(theirs);
        (XElement merged, IReadOnlyList<string> conflicts) = XmlListMerge.MergeTree(
            ancestor.Length == 0 ? null : Addressed(ancestor), mine, their, OpKey);

        // A path names the right object only in a shape one side produced.
        if (merged.Descendants("ref").Any())
        {
            string shape = Shape(merged);
            if (shape != Shape(mine) && shape != Shape(their))
            {
                return (theirs, [""]);
            }
        }
        return (Numbered(merged).ToString(), conflicts);
    }

    private static string OpKey(XElement op) => (string)op.Attribute("n")!;

    /// <summary>Every object in id order: the root first, unless it holds a branch's roots.</summary>
    private static List<XElement> Objects(XElement root)
        => root.Name == MoveFragmentXml.BranchRoot ? [.. root.Descendants("obj")] : [root, .. root.Descendants("obj")];

    /// <summary>A fragment with its object ids dropped and each reference naming its target by path.</summary>
    private static XElement Addressed(string xml)
    {
        XElement root = XElement.Parse(xml);
        List<XElement> references = [.. root.Descendants("ref")];
        if (references.Count > 0)
        {
            Dictionary<XElement, string> paths = Paths(root);
            List<XElement> objects = Objects(root);
            foreach (XElement reference in references)
            {
                reference.SetAttributeValue("to", paths[objects[(int)reference.Attribute("id")!]]);
                reference.SetAttributeValue("id", null);
            }
        }
        foreach (XElement obj in root.Descendants("obj"))
        {
            obj.SetAttributeValue("id", null);
        }
        return root;
    }

    /// <summary>The reverse of <see cref="Addressed"/>.</summary>
    private static XElement Numbered(XElement root)
    {
        List<XElement> objects = Objects(root);
        foreach ((int id, XElement obj) in objects.Index())
        {
            if (obj != root)
            {
                obj.SetAttributeValue("id", id);
            }
        }

        List<XElement> references = [.. root.Descendants("ref")];
        if (references.Count > 0)
        {
            Dictionary<XElement, string> paths = Paths(root);
            Dictionary<string, int> ids = objects.Index().ToDictionary(obj => paths[obj.Item], obj => obj.Index);
            foreach (XElement reference in references)
            {
                reference.SetAttributeValue("id", ids[(string)reference.Attribute("to")!]);
                reference.SetAttributeValue("to", null);
            }
        }
        return root;
    }

    /// <summary>Every element's path from the root: each op's field name and its occurrence among
    /// siblings of that name.</summary>
    private static Dictionary<XElement, string> Paths(XElement root)
    {
        var paths = new Dictionary<XElement, string>();
        Walk(root, "");
        return paths;

        void Walk(XElement element, string path)
        {
            paths[element] = path;
            var seen = new Dictionary<string, int>();
            foreach (XElement child in element.Elements())
            {
                string key = OpKey(child);
                int occurrence = seen[key] = seen.GetValueOrDefault(key, -1) + 1;
                Walk(child, $"{path}/{key}#{occurrence}");
            }
        }
    }

    /// <summary>Where a fragment's pointers sit and what each reference names, values left out.</summary>
    private static string Shape(XElement root)
    {
        var text = new StringBuilder();
        Append(root);
        return text.ToString();

        void Append(XElement element)
        {
            text.Append("\0<").Append(element.Name);
            foreach (string attribute in (string[])["n", "class", "unit", "to"])
            {
                text.Append('\0').Append((string?)element.Attribute(attribute));
            }
            foreach (XElement op in element.Elements().Where(op => op.Name.LocalName is "obj" or "null" or "branch" or "ref" or "xref"))
            {
                Append(op);
            }
            text.Append("\0/");
        }
    }
}
