using System.Globalization;
using System.Text;
using System.Xml;

namespace JackAll.Core.Format.Move;

/// <summary>
/// One overridable piece of a graph, detached from the file it came from: either a state with its
/// weapon branches elided, or all of one weapon's branches within a state.
/// </summary>
/// <remarks>
/// <see cref="External"/> is what makes a piece portable. A MOVE graph is a tree by ownership but not
/// by reference - 753 of <c>movemgr.bin</c>'s back-references leave the state that holds them, and
/// 687 of those land deep inside another state rather than on its root - and splitting a state again
/// at its branches turns some of its internal references into crossings too. None of those can travel
/// as pointers, so they travel as <see cref="MoveAddress"/> and are re-seated after assembly.
/// </remarks>
public sealed class MoveFragment(MoveUnit unit, List<MoveObject> roots)
{
    /// <summary>Stands in for an elided branch in a <em>parsed</em> remainder. Never written to a
    /// binary: the writer has no layout for it, so an unassembled remainder fails loudly rather than
    /// emitting nonsense.</summary>
    public const string BranchPlaceholder = "#branch";

    public MoveUnit Unit { get; } = unit;

    /// <summary>Set when this is a slice of the manager rather than anything to do with a state.</summary>
    public MoveSection? Section { get; init; }

    /// <summary>
    /// One root for a state or a manager section, several for a branch group.
    /// </summary>
    /// <remarks>
    /// A section's root is synthetic: the manager writes its sections inline, so the root is a holder
    /// for a run of the manager's own ops rather than an object the file contains. The ops inside it
    /// are the real ones, pointers included, so nothing is copied and nothing is mutated.
    /// </remarks>
    public List<MoveObject> Roots { get; } = roots;

    /// <summary>The field each root hung off, parallel to <see cref="Roots"/>; empty for a state.</summary>
    public List<string> RootNames { get; } = [];

    /// <summary>
    /// The branch subtrees this fragment leaves out, when it was lifted from a live graph.
    /// </summary>
    /// <remarks>
    /// Lifting never copies or mutates the graph - it walks the real objects and simply declines to
    /// descend into these. A <em>parsed</em> remainder has no live subtree to decline, so it carries
    /// <see cref="BranchPlaceholder"/> objects instead; both render identically.
    /// </remarks>
    public Dictionary<MoveObject, MoveUnit> Elided { get; } = [];

    public uint StateHash => Unit.StateHash;

    /// <summary>Which pointer ops point out of this fragment, and at what.</summary>
    public Dictionary<(MoveObject Owner, int Index), MoveAddress> External { get; } = [];

    /// <summary>
    /// Every object this fragment owns, in the order a reader recreates them. An elided branch is not
    /// one of them and consumes no id, so a remainder's ids do not depend on what its branches hold.
    /// </summary>
    public List<MoveObject> Objects()
    {
        List<MoveObject> ordered = [];
        foreach (MoveObject root in Roots)
        {
            Visit(root, ordered);
        }

        return ordered;
    }

    public bool IsElided(MoveObject node)
        => node.ClassName == BranchPlaceholder || Elided.ContainsKey(node);

    private void Visit(MoveObject node, List<MoveObject> into)
    {
        if (IsElided(node))
        {
            return;
        }

        into.Add(node);
        foreach (MoveOp op in node.Ops)
        {
            if (op.Kind == MoveOpKind.PointerNew)
            {
                Visit(op.Target!, into);
            }
        }
    }

    /// <summary>The unit an elided child belongs to, however this fragment happens to record it.</summary>
    public uint BranchUnitId(MoveObject node)
        => Elided.TryGetValue(node, out MoveUnit unit)
            ? unit.Id
            : node.Field("unit")
              ?? throw new MoveFormatException("a branch placeholder with no unit");
}

/// <summary>Converts one overridable piece of a graph to and from the XML a mod stages.</summary>
/// <remarks>
/// The vocabulary is <see cref="MoveXml"/>'s, so the two forms read alike, with three differences
/// that exist so a fragment never carries whole-file layout:
///
///   - <c>id</c> is the object's position <em>within this fragment</em>, not in the file. A file
///     index shifts whenever anything earlier changes, which would churn every fragment on every
///     unrelated edit - the same reason a `depload` fragment omits <c>childIndex</c>.
///   - a reference leaving the fragment is <c>&lt;xref state="…" path="…"/&gt;</c> rather than
///     <c>&lt;ref&gt;</c>. The path is relative to the assembled state, so a reference between a
///     state's remainder and one of its branches uses the same form as one to another state.
///   - a weapon branch elided out of a state leaves <c>&lt;branch unit="…"/&gt;</c> behind. Sites are
///     matched back up by pre-order, so no fragment records a position.
///
/// See docs/docs/file-formats/move.md.
/// </remarks>
public static class MoveFragmentXml
{
    private const string StateRoot = "MoveState";
    private const string BranchRoot = "MoveBranch";
    private const string SectionRoot = "MoveSection";

    /// <summary>The class name of a section's synthetic holder; never written to a binary.</summary>
    private const string SectionHolder = "#section";

    /// <summary>Lifts one run of the manager's own ops - a section - out of a graph.</summary>
    public static MoveFragment LiftSection(MoveStateIndex index, MoveSection section)
    {
        MoveObject manager = index.File.Objects.FirstOrDefault(o => o.ClassName == "CMoveMgr")
            ?? throw new MoveFormatException("this graph has no CMoveMgr, so it has no sections");

        (int start, int count) = MoveSections.Ranges(manager)[section];
        MoveObject holder = new(SectionHolder);
        holder.Ops.AddRange(manager.Ops.GetRange(start, count));

        MoveFragment fragment = new(default, [holder]) { Section = section };
        Record(index, fragment);
        return fragment;
    }

    /// <summary>Puts a parsed section's ops back where they came from.</summary>
    /// <remarks>
    /// Sections are spliced back to front so that replacing one does not move the next one's start.
    /// </remarks>
    public static void SpliceSections(
        MoveObject manager, IReadOnlyDictionary<MoveSection, MoveFragment> staged)
    {
        IReadOnlyDictionary<MoveSection, (int Start, int Count)> ranges = MoveSections.Ranges(manager);
        foreach (MoveSection section in staged.Keys.OrderByDescending(s => ranges[s].Start))
        {
            (int start, int count) = ranges[section];
            manager.Ops.RemoveRange(start, count);
            manager.Ops.InsertRange(start, staged[section].Roots[0].Ops);
        }
    }

    /// <summary>Lifts a state out of a graph with its weapon branches elided.</summary>
    public static MoveFragment LiftState(MoveStateIndex index, MoveObject state)
    {
        uint hash = MoveStateIndex.NameHashOf(state)
            ?? throw new MoveFormatException(
                $"a {state.ClassName} with no m_stateNameHash cannot be a fragment");

        MoveFragment fragment = new(new MoveUnit(hash, 0, null), [state]);
        foreach (MoveUnits.Site site in MoveUnits.BranchesOf(state, hash))
        {
            fragment.Elided[site.Branch] = site.Unit;
        }

        Record(index, fragment);
        return fragment;
    }

    /// <summary>Lifts every branch of one weapon within a state.</summary>
    public static MoveFragment LiftBranches(MoveStateIndex index, MoveObject state, MoveUnit unit)
    {
        List<MoveUnits.Site> sites =
            [.. MoveUnits.BranchesOf(state, unit.StateHash).Where(s => s.Unit == unit)];
        if (sites.Count == 0)
        {
            throw new MoveFormatException($"state {unit.StateHash:X8} has no branches for {unit}");
        }

        MoveFragment fragment = new(unit, [.. sites.Select(s => s.Branch)]);
        fragment.RootNames.AddRange(sites.Select(s => s.Name));
        Record(index, fragment);
        return fragment;
    }

    /// <summary>
    /// Puts a state back together from its remainder and its branch groups, returning the state and
    /// every reference the pieces make outside themselves.
    /// </summary>
    /// <remarks>
    /// Sites are matched to subtrees by pre-order - the k-th <c>&lt;branch unit="U"/&gt;</c> takes the
    /// k-th root of U's fragment - so neither side records a position, and a fragment stays valid when
    /// an unrelated branch of the same state grows or shrinks.
    ///
    /// Assembly runs <em>before</em> addresses are resolved, so a reference from a state's remainder
    /// into one of its own branches is resolved against the finished state exactly like a reference
    /// to another state. That is what lets one address space serve both.
    /// </remarks>
    public static (MoveObject State, List<(MoveObject Owner, int Index, MoveAddress Address)> External)
        Assemble(MoveFragment remainder, IReadOnlyDictionary<uint, MoveFragment> branches)
    {
        Dictionary<uint, int> taken = [];
        List<(MoveObject, int, MoveAddress)> external =
            [.. remainder.External.Select(e => (e.Key.Owner, e.Key.Index, e.Value))];

        Splice(remainder.Roots[0]);

        foreach ((uint id, MoveFragment group) in branches)
        {
            if (taken.GetValueOrDefault(id) != group.Roots.Count)
            {
                throw new InvalidDataException(
                    $"unit {id} supplies {group.Roots.Count} branches but state "
                    + $"{remainder.StateHash} has {taken.GetValueOrDefault(id)} sites for it. Adding "
                    + "or removing a branch means editing the state fragment too, so that its "
                    + "<branch> markers still match.");
            }
        }

        return (remainder.Roots[0], external);

        void Splice(MoveObject node)
        {
            for (int i = 0; i < node.Ops.Count; i++)
            {
                MoveOp op = node.Ops[i];
                if (op.Kind != MoveOpKind.PointerNew)
                {
                    continue;
                }

                if (!remainder.IsElided(op.Target!))
                {
                    Splice(op.Target!);
                    continue;
                }

                uint id = remainder.BranchUnitId(op.Target!);
                if (!branches.TryGetValue(id, out MoveFragment? group))
                {
                    throw new InvalidDataException(
                        $"state {remainder.StateHash} keeps a branch in unit {id}, but no fragment "
                        + "supplies it. Stage that unit alongside the state, or leave the state's "
                        + "branch sites alone.");
                }

                int at = taken.GetValueOrDefault(id);
                if (at >= group.Roots.Count)
                {
                    throw new InvalidDataException(
                        $"unit {id} supplies {group.Roots.Count} branches but state "
                        + $"{remainder.StateHash} has more sites for it. The two fragments disagree "
                        + "about how many branches this weapon has.");
                }

                taken[id] = at + 1;
                node.Ops[i] = op.WithTarget(group.Roots[at]);
                if (at == 0)
                {
                    external.AddRange(
                        group.External.Select(e => (e.Key.Owner, e.Key.Index, e.Value)));
                }

                Splice(group.Roots[at]);
            }
        }
    }

    /// <summary>Turns every reference leaving this fragment into an address.</summary>
    private static void Record(MoveStateIndex index, MoveFragment fragment)
    {
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
                        $"{fragment.Unit} references a {op.Target!.ClassName} that belongs to no "
                        + "state, which this format has no way to name");
            }
        }
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
            MoveUnit unit = fragment.Unit;
            if (fragment.Section is { } section)
            {
                writer.WriteStartElement(SectionRoot);
                writer.WriteAttributeString("name", MoveSections.NameOf(section));
                WriteBody(writer, fragment, fragment.Roots[0], ids);
            }
            else if (unit.IsRemainder)
            {
                writer.WriteStartElement(StateRoot);
                Attribute(writer, "state", unit.StateHash);
                writer.WriteAttributeString("class", fragment.Roots[0].ClassName);
                WriteBody(writer, fragment, fragment.Roots[0], ids);
            }
            else
            {
                writer.WriteStartElement(BranchRoot);
                Attribute(writer, "unit", unit.Id);
                Attribute(writer, "state", unit.StateHash);
                Attribute(writer, "channel", (uint)unit.Channel);
                Attribute(writer, "weapon", (uint)unit.Weapon!.Value);
                for (int i = 0; i < fragment.Roots.Count; i++)
                {
                    MoveObject root = fragment.Roots[i];
                    writer.WriteStartElement("obj");
                    writer.WriteAttributeString(
                        "n", i < fragment.RootNames.Count ? fragment.RootNames[i] : string.Empty);
                    writer.WriteAttributeString("class", root.ClassName);
                    writer.WriteAttributeString(
                        "id", ids[root].ToString(CultureInfo.InvariantCulture));
                    WriteBody(writer, fragment, root, ids);
                    writer.WriteEndElement();
                }
            }

            writer.WriteEndElement();
        }

        return text.ToString();
    }

    private static void Attribute(XmlWriter writer, string name, uint value)
        => writer.WriteAttributeString(name, value.ToString(CultureInfo.InvariantCulture));

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

                case MoveOpKind.PointerNew when fragment.IsElided(op.Target!):
                    writer.WriteStartElement("branch");
                    writer.WriteAttributeString("n", op.Name);
                    Attribute(writer, "unit", fragment.BranchUnitId(op.Target!));
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
                        Attribute(writer, "state", address.StateHash);
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
        List<MoveObject> roots = [];
        List<string> rootNames = [];
        MoveObject? current = null;
        MoveUnit unit = default;
        bool branchDocument = false;
        MoveSection? section = null;

        while (reader.Read())
        {
            if (reader.NodeType == XmlNodeType.EndElement)
            {
                if (reader.Name == "obj" && stack.Count > 0)
                {
                    current = stack.Pop();
                }
                else if (reader.Name == "obj")
                {
                    current = null;
                }

                continue;
            }

            if (reader.NodeType != XmlNodeType.Element)
            {
                continue;
            }

            if (reader.Name == StateRoot)
            {
                unit = new MoveUnit(Number(reader, "state"), 0, null);
                MoveObject state = new(Required(reader, "class"));
                byId[0] = state;
                roots.Add(state);
                current = state;
                continue;
            }

            if (reader.Name == SectionRoot)
            {
                section = MoveSections.ByName(Required(reader, "name"))
                    ?? throw new MoveFormatException(
                        $"<{SectionRoot} name=\"{reader.GetAttribute("name")}\"> names no section");
                MoveObject holder = new(SectionHolder);
                byId[0] = holder;
                roots.Add(holder);
                current = holder;
                continue;
            }

            if (reader.Name == BranchRoot)
            {
                unit = new MoveUnit(
                    Number(reader, "state"), (int)Number(reader, "channel"),
                    (int)Number(reader, "weapon"));
                branchDocument = true;
                continue;
            }

            string name = reader.GetAttribute("n") ?? string.Empty;

            // A branch document's top-level <obj>s are its roots, not children of anything.
            if (branchDocument && current is null && reader.Name == "obj")
            {
                MoveObject root = new(Required(reader, "class"));
                byId[int.Parse(Required(reader, "id"), CultureInfo.InvariantCulture)] = root;
                roots.Add(root);
                rootNames.Add(name);
                if (!reader.IsEmptyElement)
                {
                    current = root;
                }

                continue;
            }

            if (current is null)
            {
                throw new MoveFormatException($"<{reader.Name}> outside the fragment's root element");
            }

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

                case "branch":
                    MoveObject marker = new(MoveFragment.BranchPlaceholder);
                    marker.Ops.Add(MoveOp.Integer(MoveOpKind.U32, "unit", Number(reader, "unit")));
                    current.Ops.Add(MoveOp.Pointer(MoveOpKind.PointerNew, name, marker));
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
                        Number(reader, "state"), reader.GetAttribute("path") ?? string.Empty)));
                    current.Ops.Add(MoveOp.Pointer(MoveOpKind.PointerRef, name, null));
                    break;

                default:
                    throw new MoveFormatException($"unexpected element <{reader.Name}>");
            }
        }

        if (roots.Count == 0)
        {
            throw new MoveFormatException($"no <{StateRoot}> or <{BranchRoot}> element");
        }

        MoveFragment fragment = new(unit, roots) { Section = section };
        fragment.RootNames.AddRange(rootNames);
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

    private static uint Number(XmlReader reader, string attribute)
        => uint.Parse(Required(reader, attribute), NumberStyles.None, CultureInfo.InvariantCulture);

    private static string Required(XmlReader reader, string attribute)
        => reader.GetAttribute(attribute)
           ?? throw new MoveFormatException($"<{reader.Name}> has no {attribute} attribute");
}
