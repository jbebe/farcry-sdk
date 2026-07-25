using JackAll.Core.Format;

namespace JackAll.Core.Tests;

/// <summary>
/// The layout was reverse-engineered live via GhidraMCP, tracing `CResourceDataBase::LoadBinaryFile`
/// (0x09c594c0) call-for-call against its own `IFile::Read` calls, then confirmed byte-for-byte against
/// a real shipped `entitylibrary_depload.dat` (see the `RequiresFixture` test below and
/// docs/docs/file-formats/depload.md) - including catching a first-pass mistake (assuming the type
/// table was a third per-child array, not a small deduplicated lookup table) that only showed up once a
/// real file was available to check against, not from the disassembly alone.
/// </summary>
public class DepLoadDocumentTests
{
    private const string FixturesDir = "Fixtures/DepLoad";

    /// <summary>
    /// Hand-builds raw bytes matching the confirmed layout, writing each section's own independent
    /// count prefix - matching the real per-CryVector framing confirmed in the disassembly.
    /// </summary>
    private static byte[] Build(uint[] parentHashes, (ushort Index, ushort Count)[] slices,
        uint[] childHash, byte[] childTypeIndex, uint[] typeTable)
    {
        using var stream = new MemoryStream();
        using var w = new BinaryWriter(stream);

        w.Write((uint)parentHashes.Length);
        for (int i = 0; i < parentHashes.Length; i++)
        {
            w.Write(slices[i].Index);
            w.Write(slices[i].Count);
            w.Write(parentHashes[i]);
        }

        w.Write((uint)childHash.Length);
        foreach (uint h in childHash) w.Write(h);

        w.Write((uint)childTypeIndex.Length);
        foreach (byte t in childTypeIndex) w.Write(t);

        w.Write((uint)typeTable.Length);
        foreach (uint t in typeTable) w.Write(t);

        return stream.ToArray();
    }

    [Fact]
    public void Decode_reads_parents_and_slices_their_children_correctly()
    {
        byte[] file = Build(
            parentHashes: [0x10, 0x20],
            slices: [(0, 2), (2, 1)],
            childHash: [0xAA, 0xBB, 0xCC],
            childTypeIndex: [1, 0, 1],
            typeTable: [0x100, 0x200]);

        DepLoadFile decoded = DepLoadDocument.Decode(file);

        Assert.Equal(2, decoded.Parents.Count);

        DepLoadParent first = decoded.Parents[0];
        Assert.Equal(0x10u, first.Hash);
        Assert.Equal(2, first.Children.Count);
        Assert.Equal(new DepLoadChild(0xAA, 0x200), first.Children[0]); // type index 1 -> typeTable[1]
        Assert.Equal(new DepLoadChild(0xBB, 0x100), first.Children[1]); // type index 0 -> typeTable[0]

        DepLoadParent second = decoded.Parents[1];
        Assert.Equal(0x20u, second.Hash);
        Assert.Single(second.Children);
        Assert.Equal(new DepLoadChild(0xCC, 0x200), second.Children[0]);
    }

    [Fact]
    public void Decode_handles_a_parent_with_no_children()
    {
        byte[] file = Build(
            parentHashes: [0x10],
            slices: [(0, 0)],
            childHash: [],
            childTypeIndex: [],
            typeTable: []);

        DepLoadFile decoded = DepLoadDocument.Decode(file);

        Assert.Single(decoded.Parents);
        Assert.Empty(decoded.Parents[0].Children);
    }

    [Fact]
    public void Decode_lets_many_children_share_a_small_type_table()
    {
        // Mirrors the real shipped file's shape: far more children than distinct types.
        byte[] file = Build(
            parentHashes: [0x10],
            slices: [(0, 4)],
            childHash: [1, 2, 3, 4],
            childTypeIndex: [0, 0, 1, 0],
            typeTable: [0xAAAA, 0xBBBB]);

        DepLoadFile decoded = DepLoadDocument.Decode(file);

        IReadOnlyList<DepLoadChild> children = decoded.Parents[0].Children;
        Assert.Equal(0xAAAAu, children[0].TypeHash);
        Assert.Equal(0xAAAAu, children[1].TypeHash);
        Assert.Equal(0xBBBBu, children[2].TypeHash);
        Assert.Equal(0xAAAAu, children[3].TypeHash);
    }

    [Fact]
    public void Decode_rejects_mismatched_per_child_array_lengths()
    {
        byte[] file = Build(
            parentHashes: [0x10],
            slices: [(0, 1)],
            childHash: [0xAA],
            childTypeIndex: [0, 0], // one entry too many - must not silently misalign
            typeTable: [0x100]);

        Assert.Throws<InvalidDataException>(() => DepLoadDocument.Decode(file));
    }

    [Fact]
    public void Decode_rejects_a_type_index_past_the_type_table()
    {
        byte[] file = Build(
            parentHashes: [0x10],
            slices: [(0, 1)],
            childHash: [0xAA],
            childTypeIndex: [5], // type table only has 1 entry
            typeTable: [0x100]);

        Assert.Throws<InvalidDataException>(() => DepLoadDocument.Decode(file));
    }

    [Fact]
    public void Decode_rejects_a_child_slice_that_runs_past_the_array()
    {
        byte[] file = Build(
            parentHashes: [0x10],
            slices: [(0, 5)], // only 1 child actually exists
            childHash: [0xAA],
            childTypeIndex: [0],
            typeTable: [0x100]);

        Assert.Throws<InvalidDataException>(() => DepLoadDocument.Decode(file));
    }

    [Fact]
    public void Decode_rejects_a_truncated_file()
    {
        Assert.Throws<InvalidDataException>(() => DepLoadDocument.Decode(new byte[] { 5, 0, 0, 0 }));
    }

    public static TheoryData<string> SampleFiles()
    {
        var data = new TheoryData<string>();
        if (!Directory.Exists(FixturesDir))
        {
            data.Add(string.Empty); // keeps xUnit from erroring on an empty theory
            return data;
        }
        foreach (string file in Directory.EnumerateFiles(FixturesDir, "*.dat"))
        {
            data.Add(file);
        }
        return data;
    }

    [Theory]
    [MemberData(nameof(SampleFiles))]
    [Trait("Category", "RequiresFixture")]
    public void Decoding_a_real_shipped_depload_dat_succeeds(string path)
    {
        if (string.IsNullOrEmpty(path)) return;

        DepLoadFile decoded = DepLoadDocument.Decode(File.ReadAllBytes(path));

        Assert.NotEmpty(decoded.Parents);
        Assert.Contains(decoded.Parents, p => p.Children.Count > 0);
    }
}
