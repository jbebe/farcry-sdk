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

    /// <summary>Every listed row extracts, and nothing else does.</summary>
    [Theory]
    [MemberData(nameof(CorpusFiles))]
    public void Only_top_level_states_are_listed_as_fragments(string path)
    {
        if (path.Length == 0) return;

        byte[] original = File.ReadAllBytes(path);
        MoveStateIndex index = MoveStateIndex.Build(MoveCodec.Load(original));
        IContainerTree tree = _splitter.Open(original);

        Assert.Equal(index.TopLevelStates.Count(), tree.List().Count);
        Assert.All(tree.List(), row => Assert.NotNull(tree.Extract(row.Id)));

        foreach (MoveObject nested in index.Slots.Where(index.IsNested))
        {
            uint hash = MoveStateIndex.NameHashOf(nested)!.Value;
            Assert.Null(tree.Extract(MoveContainerSplitter.IdOf(hash)));
        }
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
            original, new Dictionary<string, string> { [MoveContainerSplitter.IdOf(fresh)] = clone })));

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
        => Assert.Equal(3882209901u, MoveContainerSplitter.StateOf(id));

    [Theory]
    [MemberData(nameof(CorpusFiles))]
    public void A_fragment_reads_by_its_label_and_binds_by_its_number(string path)
    {
        if (path.Length == 0) return;

        IContainerTree tree = _splitter.Open(File.ReadAllBytes(path));
        string bare = tree.List()[0].Id;
        uint hash = MoveContainerSplitter.StateOf(bare)!.Value;

        Assert.Equal(tree.Extract(bare), tree.Extract(MoveContainerSplitter.IdOf(hash, "Pawn_Aim")));
        Assert.True(FcbFragments.IdComparer.Equals(bare, $"Pawn_Aim.{hash}.xml"));
    }

    [Theory]
    [MemberData(nameof(CorpusFiles))]
    public void Canonicalizing_normalises_formatting_before_a_merge_sees_it(string path)
    {
        if (path.Length == 0) return;

        IContainerTree tree = _splitter.Open(File.ReadAllBytes(path));
        string xml = tree.Extract(tree.List()[0].Id)!;

        string reflowed = xml.Replace("  <", "      <").Replace("\r\n", "\n");
        Assert.Equal(xml, _splitter.Canonicalize(reflowed));
        Assert.Equal(xml, _splitter.Canonicalize(xml));
    }

    [Fact]
    public void An_unreadable_fragment_names_the_problem()
        => Assert.ThrowsAny<Exception>(() => _splitter.Canonicalize("not xml at all"));

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

    /// <summary>A fragment with no reference leaving it, so a clone of it needs no re-seating.</summary>
    private static (string Id, string Xml, uint Hash) FirstSelfContainedFragment(IContainerTree tree)
    {
        foreach (FcbFragmentInfo row in tree.List())
        {
            string xml = tree.Extract(row.Id)!;
            if (!xml.Contains("<xref"))
            {
                return (row.Id, xml, MoveContainerSplitter.StateOf(row.Id)!.Value);
            }
        }

        throw new InvalidOperationException("every fragment references outside itself");
    }

    private static string Between(string text, string open, string close)
    {
        int at = text.IndexOf(open, StringComparison.Ordinal) + open.Length;
        return text[at..text.IndexOf(close, at, StringComparison.Ordinal)];
    }
}
