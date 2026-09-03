using JackAll.Core.Format.Fcb;
using JackAll.Core.Format.Move;
using JackAll.Core.Mods;

namespace JackAll.Tests;

/// <summary>
/// The MOVE graph as an overridable container: taking every state out and putting them back has to
/// reproduce the file, and two mods touching different states must not meet.
/// </summary>
public sealed class MoveContainerSplitterTests
{
    private readonly MoveContainerSplitter _splitter = MoveContainerSplitter.Instance;

    public static TheoryData<string> CorpusFiles() => MoveStateIndexTests.CorpusFiles();

    [Theory]
    [InlineData("movemgr.bin", true)]
    [InlineData("dlc1.bin", true)]
    [InlineData("MoveMgr.bin", true)]
    [InlineData("movemgrnamed.bin", false)]
    [InlineData("dlc1named.bin", false)]
    [InlineData("particles.bin", false)]
    [InlineData("world1_depload.dat", false)]
    public void Only_a_move_graph_is_a_container_not_every_bin(string fileName, bool expected)
        => Assert.Equal(expected, MoveContainerSplitter.IsMoveGraph(fileName));

    /// <summary>
    /// The gate the whole design turns on. The writer renumbers every back-reference from object
    /// identity, so a byte-identical result also proves the fragment id model is right - one
    /// mis-scoped subtree derails the walk within a few hundred bytes.
    /// </summary>
    [Theory]
    [MemberData(nameof(CorpusFiles))]
    public void Every_state_extracts_and_splices_back_unchanged(string path)
    {
        if (path.Length == 0) return;

        byte[] original = File.ReadAllBytes(path);
        IContainerTree tree = _splitter.Open(original);

        Dictionary<string, string> everyFragment = tree.List()
            .ToDictionary(row => row.Id, row => tree.Extract(row.Id)!);
        byte[] rebuilt = _splitter.Apply(original, everyFragment);

        int at = Fc2Corpus.FirstDifference(original, rebuilt);
        Assert.True(at < 0, Fc2Corpus.DescribeDifference(path, original, rebuilt));
    }

    /// <summary>
    /// Every listed row extracts, and nothing else does. A row is a unit: one per state, plus one per
    /// weapon whose branches that state holds.
    /// </summary>
    [Theory]
    [MemberData(nameof(CorpusFiles))]
    public void Every_state_and_weapon_branch_is_listed_as_a_fragment(string path)
    {
        if (path.Length == 0) return;

        byte[] original = File.ReadAllBytes(path);
        MoveStateIndex index = MoveStateIndex.Build(MoveCodec.Load(original));
        IContainerTree tree = _splitter.Open(original);

        int sections = tree.List().Count(r => r.Id.StartsWith('_'));
        int expected = index.TopLevelStates.Sum(
            s => MoveUnits.UnitsOf(s, MoveStateIndex.NameHashOf(s)!.Value).Count);
        Assert.Equal(expected + sections, tree.List().Count);
        Assert.True(expected > index.TopLevelStates.Count(), "the corpus is expected to hold branches");
        Assert.All(tree.List(), row => Assert.NotNull(tree.Extract(row.Id)));

        foreach (MoveObject nested in index.Slots.Where(index.IsNested))
        {
            uint hash = MoveStateIndex.NameHashOf(nested)!.Value;
            Assert.Null(tree.Extract(MoveContainerSplitter.IdOf(new MoveUnit(hash, 0, null))));
        }
    }

    /// <summary>
    /// The reason a whole state was the wrong unit. What a mod is forced to ship is the largest
    /// fragment it touches, and splitting a state at its weapon branches has to bring that down.
    /// </summary>
    /// <remarks>
    /// Measured: <c>movemgr.bin</c>'s worst case falls from 2,515 objects to 606, and the median unit
    /// is 3. <c>dlc1.bin</c> improves far less - one state holds 3,992 of its 5,723 objects and most
    /// of that is a single weapon's branch - so the claim worth pinning is the direction, not a
    /// number that only the base graph meets.
    /// </remarks>
    [Theory]
    [MemberData(nameof(CorpusFiles))]
    public void Splitting_a_state_at_its_branches_shrinks_the_largest_fragment(string path)
    {
        if (path.Length == 0) return;

        byte[] original = File.ReadAllBytes(path);
        MoveStateIndex index = MoveStateIndex.Build(MoveCodec.Load(original));
        IContainerTree tree = _splitter.Open(original);

        long largestUnit = tree.List().Max(r => r.Size);
        long largestState = index.TopLevelStates.Max(Weigh);

        Assert.True(
            largestUnit < largestState,
            $"largest unit {largestUnit} should be under the largest whole state {largestState}");
    }

    private static long Weigh(MoveObject node)
    {
        long total = 1;
        foreach (MoveOp op in node.Ops)
        {
            if (op.Kind == MoveOpKind.PointerNew)
            {
                total += Weigh(op.Target!);
            }
        }

        return total;
    }

    /// <summary>
    /// Rewriting one clip hash inside one state changes that state and nothing else - the property a
    /// weapon mod actually needs.
    /// </summary>
    [Theory]
    [MemberData(nameof(CorpusFiles))]
    public void Splicing_one_fragment_leaves_every_other_state_alone(string path)
    {
        if (path.Length == 0) return;

        byte[] original = File.ReadAllBytes(path);
        IContainerTree tree = _splitter.Open(original);
        (string id, string xml, uint clip) = FirstFragmentWithAClip(tree);

        const uint replacement = 0x0BADC0DE;
        string edited = xml.Replace(
            $"<u32 n=\"m_animNameHash\" v=\"{clip}\" />",
            $"<u32 n=\"m_animNameHash\" v=\"{replacement}\" />");
        Assert.NotEqual(xml, edited);

        MoveFile after = MoveCodec.Load(
            _splitter.Apply(original, new Dictionary<string, string> { [id] = edited }));
        IContainerTree rebuilt = _splitter.Open(MoveCodec.Save(after));

        foreach (FcbFragmentInfo row in tree.List())
        {
            Assert.Equal(row.Id == id ? edited : tree.Extract(row.Id), rebuilt.Extract(row.Id));
        }
    }

    /// <summary>A state the graph does not have is appended, and the machine's slot count grows with
    /// it.</summary>
    [Theory]
    [MemberData(nameof(CorpusFiles))]
    public void A_state_the_container_lacks_is_appended_and_nbState_grows(string path)
    {
        if (path.Length == 0) return;

        byte[] original = File.ReadAllBytes(path);
        IContainerTree tree = _splitter.Open(original);
        MoveStateIndex before = MoveStateIndex.Build(MoveCodec.Load(original));

        // Clone a state with no outbound references, renamed - the cheapest legal addition.
        (string _, string xml, uint _) = FirstSelfContainedFragment(tree);
        const uint fresh = 0x5EEDBEEF;
        uint oldHash = uint.Parse(Between(xml, "<MoveState state=\"", "\""));
        string clone = xml
            .Replace($"<MoveState state=\"{oldHash}\"", $"<MoveState state=\"{fresh}\"")
            .Replace($"<u32 n=\"m_stateNameHash\" v=\"{oldHash}\" />",
                     $"<u32 n=\"m_stateNameHash\" v=\"{fresh}\" />");

        MoveStateIndex after = MoveStateIndex.Build(MoveCodec.Load(_splitter.Apply(
            original, new Dictionary<string, string> { [MoveContainerSplitter.IdOf(new MoveUnit(fresh, 0, null))] = clone })));

        Assert.Equal(before.Slots.Count + 1, after.Slots.Count);
        Assert.Equal((uint)after.Slots.Count, after.StateMachine.Field("nbState"));
        Assert.NotNull(after.ByHash(fresh));
        Assert.Equal(fresh, MoveStateIndex.NameHashOf(after.Slots[^1]));
    }

    [Theory]
    [MemberData(nameof(CorpusFiles))]
    public void A_fragment_filed_under_the_wrong_id_is_refused(string path)
    {
        if (path.Length == 0) return;

        byte[] original = File.ReadAllBytes(path);
        IContainerTree tree = _splitter.Open(original);
        (string _, string xml, uint _) = FirstFragmentWithAClip(tree);

        InvalidDataException error = Assert.Throws<InvalidDataException>(() =>
            _splitter.Apply(original, new Dictionary<string, string> { ["999.xml"] = xml }));
        Assert.Contains("999", error.Message);
    }

    /// <summary>Every spelling of one state names the same fragment - the number binds, the label is
    /// decoration.</summary>
    [Theory]
    [InlineData("3882209901.xml")]
    [InlineData("dragunov.3882209901.xml")]
    [InlineData("anything at all.3882209901.xml")]
    public void Every_spelling_of_one_state_names_the_same_fragment(string id)
        => Assert.Equal(3882209901u, MoveContainerSplitter.UnitOf(id));

    [Theory]
    [MemberData(nameof(CorpusFiles))]
    public void A_fragment_reads_by_its_label_and_binds_by_its_number(string path)
    {
        if (path.Length == 0) return;

        IContainerTree tree = _splitter.Open(File.ReadAllBytes(path));
        // A reserved section id has no number to bind by, so this rule is about the other rows.
        string bare = tree.List().First(r => !r.Id.StartsWith('_')).Id;
        uint hash = MoveContainerSplitter.UnitOf(bare)!.Value;

        Assert.Equal(tree.Extract(bare), tree.Extract(MoveContainerSplitter.IdOf(new MoveUnit(hash, 0, null), "Pawn_Aim")));
        Assert.True(FcbFragments.IdComparer.Equals(bare, $"Pawn_Aim.{hash}.xml"));
    }

    [Theory]
    [MemberData(nameof(CorpusFiles))]
    public void Canonicalizing_normalises_formatting_before_a_merge_sees_it(string path)
    {
        if (path.Length == 0) return;

        IContainerTree tree = _splitter.Open(File.ReadAllBytes(path));
        string xml = tree.Extract(tree.List().First(r => !r.Id.StartsWith((char)95)).Id)!;

        string reflowed = xml.Replace("  <", "      <").Replace("\r\n", "\n");
        Assert.Equal(xml, _splitter.Canonicalize("state.xml", reflowed));
        Assert.Equal(xml, _splitter.Canonicalize("state.xml", xml));
    }

    [Fact]
    public void An_unreadable_fragment_names_the_problem()
        => Assert.ThrowsAny<Exception>(() => _splitter.Canonicalize("state.xml", "not xml at all"));

    /// <summary>
    /// The manager's four sections are reserved ids, and an expansion - a bare state machine with no
    /// manager at all - offers none of them.
    /// </summary>
    [Theory]
    [MemberData(nameof(CorpusFiles))]
    public void Manager_sections_are_listed_only_when_the_graph_has_a_manager(string path)
    {
        if (path.Length == 0) return;

        byte[] original = File.ReadAllBytes(path);
        IContainerTree tree = _splitter.Open(original);
        bool hasManager = MoveCodec.Load(original).Objects.Any(o => o.ClassName == "CMoveMgr");

        string[] reserved =
            ["_channels.xml", "_packages.xml", "_blendsets.xml", "_transitions.xml"];
        foreach (string id in reserved)
        {
            Assert.Equal(hasManager, tree.List().Any(r => r.Id == id));
            Assert.Equal(hasManager, tree.Extract(id) is not null);
        }
    }

    /// <summary>The section a mod actually edits: registering a new weapon's animation package.</summary>
    [Theory]
    [MemberData(nameof(CorpusFiles))]
    public void A_package_can_be_added_without_touching_any_state(string path)
    {
        if (path.Length == 0) return;

        byte[] original = File.ReadAllBytes(path);
        IContainerTree tree = _splitter.Open(original);
        if (tree.Extract("_packages.xml") is not { } packages) return;

        uint before = uint.Parse(Between(packages, "<u32 n=\"size\" v=\"", "\""));
        MoveFile after = MoveCodec.Load(_splitter.Apply(
            original, new Dictionary<string, string> { ["_packages.xml"] = WithPackageAdded(packages) }));

        MoveObject manager = after.Objects.First(o => o.ClassName == "CMoveMgr");
        Assert.Equal(before + 1, manager.Field("size"));
        Assert.Contains(manager.Ops, op =>
            op.Name == "Name" && op.Bytes is { } b
            && System.Text.Encoding.ASCII.GetString(b) == "dlc1_vss_vintorez");
    }

    /// <summary>
    /// The hardest constraint in the format: <c>MSAnim::LoadMoves</c> compares the channel count
    /// against a hardcoded 105 and drops the file otherwise, which in game is no animation at all and
    /// no diagnostic.
    /// </summary>
    [Theory]
    [MemberData(nameof(CorpusFiles))]
    public void A_channel_table_that_is_not_105_channels_is_refused(string path)
    {
        if (path.Length == 0) return;

        byte[] original = File.ReadAllBytes(path);
        IContainerTree tree = _splitter.Open(original);
        if (tree.Extract("_channels.xml") is not { } channels) return;

        string broken = channels.Replace(
            "<u32 n=\"ms_iNumMoveValue\" v=\"105\" />", "<u32 n=\"ms_iNumMoveValue\" v=\"104\" />");
        Assert.NotEqual(channels, broken);

        InvalidDataException error = Assert.Throws<InvalidDataException>(() =>
            _splitter.Apply(original, new Dictionary<string, string> { ["_channels.xml"] = broken }));
        Assert.Contains("105", error.Message);
    }

    private static (string Id, string Xml, uint Clip) FirstFragmentWithAClip(IContainerTree tree)
    {
        foreach (FcbFragmentInfo row in tree.List())
        {
            string xml = tree.Extract(row.Id)!;
            int at = xml.IndexOf("<u32 n=\"m_animNameHash\" v=\"", StringComparison.Ordinal);
            if (at < 0) continue;

            int start = at + "<u32 n=\"m_animNameHash\" v=\"".Length;
            uint clip = uint.Parse(xml[start..xml.IndexOf('"', start)]);
            // Only unique occurrences make a clean single-site edit.
            if (xml.Split($"v=\"{clip}\"").Length == 2)
            {
                return (row.Id, xml, clip);
            }
        }

        throw new InvalidOperationException("no fragment holds a uniquely-valued clip reference");
    }

    /// <summary>
    /// What a state or a section says is its fragment's business; where it sits is the graph's. The
    /// skeleton has to draw that line exactly, since an importer trusts it to decide whether
    /// per-fragment overrides can carry a change at all.
    /// </summary>
    [Theory]
    [MemberData(nameof(CorpusFiles))]
    public void A_skeleton_ignores_what_fragments_say_but_not_where_they_sit(string path)
    {
        if (path.Length == 0) return;

        byte[] original = File.ReadAllBytes(path);
        IContainerTree tree = _splitter.Open(original);
        string shape = tree.Skeleton(_ => true)!;
        Assert.Equal(shape, _splitter.Open(original).Skeleton(_ => true));

        // Every fragment put back verbatim: the graph is rebuilt object by object, and the shape
        // still has to land on the same text.
        byte[] respliced = _splitter.Apply(
            original, tree.List().ToDictionary(row => row.Id, row => tree.Extract(row.Id)!));
        Assert.Equal(shape, _splitter.Open(respliced).Skeleton(_ => true));

        // Two states swapping slots is the change no fragment carries, because no fragment records a
        // position - so this is precisely what the skeleton exists to catch.
        MoveFile graph = MoveCodec.Load(original);
        MoveObject machine = graph.StateMachine!;
        List<int> slots = [.. Enumerable.Range(0, machine.Ops.Count)
            .Where(i => machine.Ops[i].Name == "CMoveBaseState"
                        && machine.Ops[i].Kind == MoveOpKind.PointerNew)];
        (machine.Ops[slots[^1]], machine.Ops[slots[^2]]) = (machine.Ops[slots[^2]], machine.Ops[slots[^1]]);
        Assert.NotEqual(shape, _splitter.Open(MoveCodec.Save(graph)).Skeleton(_ => true));
    }

    /// <summary>
    /// A manager section is a fragment like any other, so registering a new animation package must
    /// not disturb the shape - the trap being that a section's content lives in objects the manager
    /// only points at, which a walk over the graph would otherwise count as scaffolding.
    /// </summary>
    [Theory]
    [MemberData(nameof(CorpusFiles))]
    public void A_section_edit_leaves_the_skeleton_alone(string path)
    {
        if (path.Length == 0) return;

        byte[] original = File.ReadAllBytes(path);
        IContainerTree tree = _splitter.Open(original);
        if (tree.Extract("_packages.xml") is not { } packages) return;

        byte[] after = _splitter.Apply(
            original, new Dictionary<string, string> { ["_packages.xml"] = WithPackageAdded(packages) });
        Assert.Equal(tree.Skeleton(_ => true), _splitter.Open(after).Skeleton(_ => true));
    }

    /// <summary>
    /// A state fragment that neither references outside itself nor keeps a weapon branch, so a clone
    /// of it under a fresh name stands alone: nothing to re-seat, and no branch fragment to bring
    /// along that would still be keyed to the original state.
    /// </summary>
    private static (string Id, string Xml, uint Hash) FirstSelfContainedFragment(IContainerTree tree)
    {
        foreach (FcbFragmentInfo row in tree.List())
        {
            string xml = tree.Extract(row.Id)!;
            if (xml.StartsWith("<MoveState", StringComparison.Ordinal)
                && !xml.Contains("<xref") && !xml.Contains("<branch"))
            {
                return (row.Id, xml, MoveContainerSplitter.UnitOf(row.Id)!.Value);
            }
        }

        throw new InvalidOperationException("every state references outside itself or holds a branch");
    }

    /// <summary>One more package in a `_packages.xml` section: the count bumped, and the three
    /// strings an entry is made of appended after the last one.</summary>
    private static string WithPackageAdded(string packages)
    {
        uint before = uint.Parse(Between(packages, "<u32 n=\"size\" v=\"", "\""));
        string resized = packages.Replace(
            $"<u32 n=\"size\" v=\"{before}\" />", $"<u32 n=\"size\" v=\"{before + 1}\" />");

        return resized.Insert(
            resized.LastIndexOf("</MoveSection>", StringComparison.Ordinal),
            "  <str n=\"Name\" v=\"dlc1_vss_vintorez\" />\n"
            + "  <str n=\"Extension\" v=\"\" />\n"
            + "  <str n=\"ExportWithWorld\" v=\"\" />\n");
    }

    private static string Between(string text, string open, string close)
    {
        int at = text.IndexOf(open, StringComparison.Ordinal) + open.Length;
        return text[at..text.IndexOf(close, at, StringComparison.Ordinal)];
    }
}
