using System.Globalization;
using System.Text;
using System.Xml;

namespace JackAll.Core.Format.Move;

/// <summary>
/// One state and everything it owns, detached from the file it came from.
/// </summary>
/// <remarks>
/// <see cref="External"/> is what makes the subtree portable. A MOVE graph is a tree by ownership but
/// not by reference: 753 of <c>movemgr.bin</c>'s back-references leave the state that holds them, and
/// 687 of those land deep inside another state rather than on its root. Those cannot travel as
/// pointers, so they travel as <see cref="MoveAddress"/> and are re-seated when the fragment is
/// spliced back in.
/// </remarks>
public sealed class MoveFragment(uint stateHash, MoveObject root)
{
    public uint StateHash { get; } = stateHash;

    public MoveObject Root { get; } = root;

    /// <summary>Which pointer ops point out of this fragment, and at what.</summary>
    public Dictionary<(MoveObject Owner, int Index), MoveAddress> External { get; } = [];

    /// <summary>Every object this fragment owns, in the order a reader recreates them.</summary>
    public List<MoveObject> Objects()
    {
        List<MoveObject> ordered = [];
        Visit(Root, ordered);
        return ordered;
    }

    private static void Visit(MoveObject node, List<MoveObject> into)
    {
        into.Add(node);
        foreach (MoveOp op in node.Ops)
        {
            if (op.Kind == MoveOpKind.PointerNew)
            {
                Visit(op.Target!, into);
            }
        }
    }
}

/// <summary>Converts one state's subtree to and from the XML a mod stages.</summary>
/// <remarks>
/// The vocabulary is <see cref="MoveXml"/>'s, so the two forms read alike, with two differences that
/// exist so a fragment never carries whole-file layout:
///
///   - <c>id</c> is the object's position <em>within this fragment</em>, not in the file. A file
///     index shifts whenever anything earlier changes, which would churn every fragment on every
///     unrelated edit - the same reason a `depload` fragment omits <c>childIndex</c>.
///   - a reference leaving the fragment is <c>&lt;xref state="…" path="…"/&gt;</c> rather than
///     <c>&lt;ref&gt;</c>.
///
/// See docs/docs/file-formats/move.md.
/// </remarks>
public static class MoveFragmentXml
{
    private const string RootName = "MoveState";

    /// <summary>Lifts one state out of a graph, turning every reference that leaves it into an
    /// address.</summary>
    public static MoveFragment Lift(MoveStateIndex index, MoveObject state)
    {
        uint hash = MoveStateIndex.NameHashOf(state)
            ?? throw new MoveFormatException(
                $"a {state.ClassName} with no m_stateNameHash cannot be a fragment");

        MoveFragment fragment = new(hash, state);
        HashSet<MoveObject> mine = [.. fragment.Objects()];

        foreach (MoveObject obj in fragment.Objects())
        {
            for (int i = 0; i < obj.Ops.Count; i++)
            {
                MoveOp op = obj.Ops[i];
                if (op.Kind != MoveOpKind.PointerRef || mine.Contains(op.Target!))
                {
                    continue;
                }

                fragment.External[(obj, i)] = index.AddressOf(op.Target!)
                    ?? throw new MoveFormatException(
                        $"state {hash:X8} references a {op.Target!.ClassName} that belongs to no "
                        + "state, which this format has no way to name");
            }
        }

        return fragment;
    }

    public static string Render(MoveFragment fragment)
    {
        Dictionary<MoveObject, int> ids = [];
        List<MoveObject> ordered = fragment.Objects();
        for (int i = 0; i < ordered.Count; i++)
        {
            ids[ordered[i]] = i;
        }

        // Matches DepLoadXml.Render: a fragment is written UTF-8 to disk, so the declaration an
        // XmlWriter over a StringBuilder would emit ("utf-16") is worse than none; and Diff3 rejoins
        // on Environment.NewLine, so a mismatch there rewrites every line of a fragment only one
        // layer touched.
        StringBuilder text = new();
        XmlWriterSettings settings = new()
        {
            Indent = true,
            IndentChars = "  ",
            NewLineChars = Environment.NewLine,
            OmitXmlDeclaration = true,
        };
        using (XmlWriter writer = XmlWriter.Create(text, settings))
        {
            writer.WriteStartElement(RootName);
            writer.WriteAttributeString(
                "state", fragment.StateHash.ToString(CultureInfo.InvariantCulture));
            writer.WriteAttributeString("class", fragment.Root.ClassName);
            WriteBody(writer, fragment, fragment.Root, ids);
            writer.WriteEndElement();
        }

        return text.ToString();
    }

    private static void WriteBody(
        XmlWriter writer, MoveFragment fragment, MoveObject obj, Dictionary<MoveObject, int> ids)
    {
        for (int i = 0; i < obj.Ops.Count; i++)
        {
            MoveOp op = obj.Ops[i];
            if (MoveXmlPrimitives.TryWrite(writer, op))
            {
                continue;
            }

            switch (op.Kind)
            {
                case MoveOpKind.PointerNull:
                    writer.WriteStartElement("null");
                    writer.WriteAttributeString("n", op.Name);
                    writer.WriteEndElement();
                    break;

                case MoveOpKind.PointerNew:
                    writer.WriteStartElement("obj");
                    writer.WriteAttributeString("n", op.Name);
                    writer.WriteAttributeString("class", op.Target!.ClassName);
                    writer.WriteAttributeString(
                        "id", ids[op.Target!].ToString(CultureInfo.InvariantCulture));
                    WriteBody(writer, fragment, op.Target!, ids);
                    writer.WriteEndElement();
                    break;

                case MoveOpKind.PointerRef:
                    if (fragment.External.TryGetValue((obj, i), out MoveAddress address))
                    {
                        writer.WriteStartElement("xref");
                        writer.WriteAttributeString("n", op.Name);
                        writer.WriteAttributeString(
                            "state", address.StateHash.ToString(CultureInfo.InvariantCulture));
                        writer.WriteAttributeString("path", address.Path);
                    }
                    else
                    {
                        writer.WriteStartElement("ref");
                        writer.WriteAttributeString("n", op.Name);
                        writer.WriteAttributeString(
                            "id", ids[op.Target!].ToString(CultureInfo.InvariantCulture));
                    }

                    writer.WriteEndElement();
                    break;

                default:
                    throw new MoveFormatException($"unrenderable op {op.Kind} in {obj.ClassName}");
            }
        }
    }

    public static MoveFragment Parse(string xml)
    {
        using XmlReader reader = XmlReader.Create(
            new StringReader(xml), new XmlReaderSettings { IgnoreWhitespace = true });

        Dictionary<int, MoveObject> byId = [];
        List<(MoveObject Owner, int Index, int TargetId)> pending = [];
        List<(MoveObject Owner, int Index, MoveAddress Address)> external = [];
        Stack<MoveObject> stack = new();
        MoveObject? root = null;
        MoveObject? current = null;
        uint stateHash = 0;

        while (reader.Read())
        {
            if (reader.NodeType == XmlNodeType.EndElement)
            {
                if (reader.Name == "obj" && stack.Count > 0)
                {
                    current = stack.Pop();
                }

                continue;
            }

            if (reader.NodeType != XmlNodeType.Element)
            {
                continue;
            }

            if (reader.Name == RootName)
            {
                stateHash = uint.Parse(
                    Required(reader, "state"), NumberStyles.None, CultureInfo.InvariantCulture);
                root = new MoveObject(Required(reader, "class"));
                byId[0] = root;
                current = root;
                continue;
            }

            if (current is null)
            {
                throw new MoveFormatException($"<{reader.Name}> outside the fragment's root element");
            }

            string name = reader.GetAttribute("n") ?? string.Empty;
            if (MoveXmlPrimitives.TryRead(reader, name) is { } leaf)
            {
                current.Ops.Add(leaf);
                continue;
            }

            switch (reader.Name)
            {
                case "obj":
                    MoveObject created = new(Required(reader, "class"));
                    byId[int.Parse(Required(reader, "id"), CultureInfo.InvariantCulture)] = created;
                    current.Ops.Add(MoveOp.Pointer(MoveOpKind.PointerNew, name, created));
                    if (!reader.IsEmptyElement)
                    {
                        stack.Push(current);
                        current = created;
                    }

                    break;

                case "null":
                    current.Ops.Add(MoveOp.Pointer(MoveOpKind.PointerNull, name, null));
                    break;

                case "ref":
                    pending.Add((current, current.Ops.Count,
                        int.Parse(Required(reader, "id"), CultureInfo.InvariantCulture)));
                    current.Ops.Add(MoveOp.Pointer(MoveOpKind.PointerRef, name, null));
                    break;

                case "xref":
                    external.Add((current, current.Ops.Count, new MoveAddress(
                        uint.Parse(Required(reader, "state"), NumberStyles.None, CultureInfo.InvariantCulture),
                        reader.GetAttribute("path") ?? string.Empty)));
                    current.Ops.Add(MoveOp.Pointer(MoveOpKind.PointerRef, name, null));
                    break;

                default:
                    throw new MoveFormatException($"unexpected element <{reader.Name}>");
            }
        }

        if (root is null)
        {
            throw new MoveFormatException($"no <{RootName}> element");
        }

        MoveFragment fragment = new(stateHash, root);
        foreach ((MoveObject owner, int index, int targetId) in pending)
        {
            if (!byId.TryGetValue(targetId, out MoveObject? target))
            {
                throw new MoveFormatException($"<ref id=\"{targetId}\"> names no object here");
            }

            owner.Ops[index] = owner.Ops[index].WithTarget(target);
        }

        foreach ((MoveObject owner, int index, MoveAddress address) in external)
        {
            fragment.External[(owner, index)] = address;
        }

        return fragment;
    }

    private static string Required(XmlReader reader, string attribute)
        => reader.GetAttribute(attribute)
           ?? throw new MoveFormatException($"<{reader.Name}> has no {attribute} attribute");
}
