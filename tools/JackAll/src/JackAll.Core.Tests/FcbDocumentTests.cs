using System.Buffers.Binary;
using System.Text;
using JackAll.Core.Format.Fcb;

namespace JackAll.Core.Tests;

/// <summary>
/// Runs against real, shipped .fcb files (extracted directly from the game's own archives:
/// patch/worlds/dlc1/dlc_jungle entitylibrary trees, plus the patch-override tree) rather than a
/// synthetic fixture, for the same reason as <see cref="XbtTextureTests"/> - the only authority on
/// what the engine actually writes is what it actually shipped. Every parsing rule asserted here was
/// independently confirmed against the real engine parser in Dunia.dll (Fcb_ParseObject @
/// 0x10234d60, Fcb_ReadHeader @ 0x10235080) via GhidraMCP - see reverse/dunia/fcb_format.md.
/// </summary>
public class FcbDocumentTests
{
    private const string FixturesDir = "Fixtures/Fcb";

    /// <summary>Root type hash shared by every entitylibrary tree in these fixtures (CRC32("EntityLibrary")).</summary>
    private const uint EntityLibraryTypeHash = 0xBCDD10B4;

    public static TheoryData<string> SampleFiles()
    {
        var data = new TheoryData<string>();
        if (!Directory.Exists(FixturesDir))
        {
            data.Add(string.Empty); // keeps xUnit from erroring on an empty theory
            return data;
        }
        foreach (string file in Directory.EnumerateFiles(FixturesDir, "*.fcb"))
        {
            data.Add(file);
        }
        return data;
    }

    [Fact]
    [Trait("Category", "RequiresFixture")]
    public void The_fixture_files_were_actually_found()
        => Assert.True(
            Directory.Exists(FixturesDir) && Directory.EnumerateFiles(FixturesDir, "*.fcb").Any(),
            $"{FixturesDir} had no .fcb samples, so every sample-backed test in this class silently no-opped.");

    [Theory]
    [MemberData(nameof(SampleFiles))]
    [Trait("Category", "RequiresFixture")]
    public void Deserializing_a_real_shipped_fcb_produces_the_expected_root_shape(string path)
    {
        if (string.IsNullOrEmpty(path)) return;

        FcbObject root = FcbDocument.Deserialize(File.ReadAllBytes(path));

        Assert.Equal(EntityLibraryTypeHash, root.TypeHash);
        Assert.True(root.Children.Count > 0);
    }

    /// <summary>
    /// The strongest correctness signal available without a spec: the header's totalObjectCount
    /// field, written by whatever compiler produced the real shipped file, counts distinct
    /// (backreference-deduplicated) objects - if this class's object-level backreference handling
    /// were wrong in any way, walking the parsed tree by reference identity would not land on the
    /// same number the file itself claims.
    /// </summary>
    [Theory]
    [MemberData(nameof(SampleFiles))]
    [Trait("Category", "RequiresFixture")]
    public void The_headers_object_count_matches_the_number_of_distinct_parsed_objects(string path)
    {
        if (string.IsNullOrEmpty(path)) return;

        byte[] data = File.ReadAllBytes(path);
        uint headerObjectCount = BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(8, 4));

        FcbObject root = FcbDocument.Deserialize(data);

        var visited = new HashSet<FcbObject>(ReferenceEqualityComparer.Instance);
        int uniqueObjectCount = CountUniqueObjects(root, visited);

        Assert.Equal((int)headerObjectCount, uniqueObjectCount);
    }

    [Theory]
    [MemberData(nameof(SampleFiles))]
    [Trait("Category", "RequiresFixture")]
    public void Deserializing_then_serializing_a_real_shipped_fcb_reproduces_the_same_tree(string path)
    {
        if (string.IsNullOrEmpty(path)) return;

        FcbObject original = FcbDocument.Deserialize(File.ReadAllBytes(path));
        byte[] rebuilt = FcbDocument.Serialize(original);
        FcbObject reparsed = FcbDocument.Deserialize(rebuilt);

        // Not byte-identical: real shipped files lean heavily on the backreference/shared-bytes
        // dedup tricks (see FcbDocument's remarks), and this writer - like Gibbed's own - always
        // emits the fully-expanded form. The tree shape and every value's bytes must still match.
        AssertSameShape(original, reparsed);
    }

    [Fact]
    public void A_simple_hand_built_tree_round_trips_through_the_public_API()
    {
        var root = new FcbObject { TypeHash = 0xCAFEBABE };
        root.Values.Add(0x00000001, [0x01, 0x02, 0x03]);
        root.Values.Add(0x00000002, Encoding.UTF8.GetBytes("hello\0"));
        var child = new FcbObject { TypeHash = 0xDEADBEEF };
        child.Values.Add(0x00000003, [0xFF]);
        root.Children.Add(child);

        byte[] serialized = FcbDocument.Serialize(root);
        FcbObject reparsed = FcbDocument.Deserialize(serialized);

        AssertSameShape(root, reparsed);
    }

    [Fact]
    public void An_object_level_backreference_returns_the_same_shared_subtree()
    {
        // Root has two children; the second is encoded as a backreference (marker 0xFE + pointer
        // index) to the first, exactly as real shipped .fcb dedups repeated subtrees.
        using var body = new MemoryStream();
        body.WriteByte(0x02);              // root childCount = 2
        WriteU32(body, 0xAAAAAAAA);         // root TypeHash
        body.WriteByte(0x00);              // root valueCount = 0

        body.WriteByte(0x00);              // child A: childCount = 0
        WriteU32(body, 0x11111111);         // child A TypeHash
        body.WriteByte(0x00);              // child A valueCount = 0

        body.WriteByte(0xFE);              // child B: backreference marker
        WriteU32(body, 1);                  // pointer index 1 = child A (index 0 is the root itself)

        byte[] fcb = WrapWithHeader(body.ToArray());

        FcbObject root = FcbDocument.Deserialize(fcb);

        Assert.Equal(2, root.Children.Count);
        Assert.Same(root.Children[0], root.Children[1]);
        Assert.Equal(0x11111111u, root.Children[0].TypeHash);

        // The writer always expands - re-serializing loses the sharing but not the meaning: both
        // children still decode to the same content, just as two independent copies.
        FcbObject reparsed = FcbDocument.Deserialize(FcbDocument.Serialize(root));
        Assert.Equal(0x11111111u, reparsed.Children[0].TypeHash);
        Assert.Equal(0x11111111u, reparsed.Children[1].TypeHash);
        Assert.NotSame(reparsed.Children[0], reparsed.Children[1]);
    }

    [Fact]
    public void DeserializeWithChildSizes_matches_the_fully_expanded_size_when_nothing_is_deduped()
    {
        // A tree built purely through FcbDocument.Serialize is guaranteed backreference-free (the
        // writer always emits the fully-expanded form - see class remarks), so the root's own children
        // should occupy exactly their fully-expanded size on disk, no smaller.
        var root = new FcbObject { TypeHash = 0xCAFEBABE };
        var childA = new FcbObject { TypeHash = 0x11111111 };
        childA.Values.Add(0x00000001, [0x01, 0x02, 0x03]);
        var childB = new FcbObject { TypeHash = 0x22222222 };
        childB.Values.Add(0x00000002, Encoding.UTF8.GetBytes("hello\0"));
        var grandchild = new FcbObject { TypeHash = 0x33333333 };
        grandchild.Values.Add(0x00000003, [0xFF]);
        childB.Children.Add(grandchild);
        root.Children.Add(childA);
        root.Children.Add(childB);

        (FcbObject reparsed, IReadOnlyList<long> childByteSizes) =
            FcbDocument.DeserializeWithChildSizes(FcbDocument.Serialize(root));

        Assert.Equal(2, childByteSizes.Count);
        Assert.Equal(TestSupport.FullyExpandedFcbSize(reparsed.Children[0]), childByteSizes[0]);
        Assert.Equal(TestSupport.FullyExpandedFcbSize(reparsed.Children[1]), childByteSizes[1]);
    }

    [Fact]
    public void DeserializeWithChildSizes_reports_just_the_backreference_marker_size_for_a_shared_child()
    {
        // Same shape as An_object_level_backreference_returns_the_same_shared_subtree: root's second
        // child is a backreference to the first, not its own copy of the bytes.
        using var body = new MemoryStream();
        body.WriteByte(0x02);              // root childCount = 2
        WriteU32(body, 0xAAAAAAAA);         // root TypeHash
        body.WriteByte(0x00);              // root valueCount = 0

        body.WriteByte(0x00);              // child A: childCount = 0
        WriteU32(body, 0x11111111);         // child A TypeHash
        body.WriteByte(0x00);              // child A valueCount = 0
        // Child A's literal encoding above is 1 + 4 + 1 = 6 bytes.

        body.WriteByte(0xFE);              // child B: backreference marker
        WriteU32(body, 1);                  // pointer index 1 = child A (index 0 is the root itself)
        // Child B's on-disk encoding is just the marker + index = 5 bytes, regardless of how big
        // whatever it points at is.

        byte[] fcb = WrapWithHeader(body.ToArray());

        (FcbObject root, IReadOnlyList<long> childByteSizes) = FcbDocument.DeserializeWithChildSizes(fcb);

        Assert.Equal(2, childByteSizes.Count);
        Assert.Equal(6, childByteSizes[0]);
        Assert.Equal(5, childByteSizes[1]);
        Assert.Same(root.Children[0], root.Children[1]); // still the same shared object either way
    }

    [Fact]
    public void A_value_level_backward_offset_reuses_an_earlier_values_bytes()
    {
        // Root has two values; the second's size field is a backward byte offset to the first's,
        // meaning "my bytes are the same as that earlier value's" - real shipped .fcb uses this to
        // avoid repeating identical byte blobs.
        using var body = new MemoryStream();
        body.WriteByte(0x00);              // root childCount = 0
        WriteU32(body, 0xBBBBBBBB);         // root TypeHash
        body.WriteByte(0x02);              // root valueCount = 2

        WriteU32(body, 1);                  // value 1 nameHash
        long p1 = body.Position;
        body.WriteByte(0x04);              // value 1 size = 4 (direct)
        byte[] shared = [0xDE, 0xAD, 0xBE, 0xEF];
        body.Write(shared);

        WriteU32(body, 2);                  // value 2 nameHash
        long p2 = body.Position;
        body.WriteByte(0xFE);              // value 2 size field: backreference marker
        WriteU32(body, (uint)(p2 - p1));    // backward byte offset to value 1's size field

        byte[] fcb = WrapWithHeader(body.ToArray());

        FcbObject root = FcbDocument.Deserialize(fcb);

        Assert.Equal(shared, root.Values[1]);
        Assert.Equal(shared, root.Values[2]);
    }

    [Fact]
    public void An_objects_own_value_count_never_means_backreference_even_with_marker_0xFE()
    {
        // Confirmed directly in Fcb_ParseObject: an object's own value-count field decodes 0xFE and
        // 0xFF identically (both just "read the next 4 bytes as the literal count") - unlike the
        // object-level child-list position, there's no backreference branch here at all. A real
        // compiler probably never emits 0xFE for this rather than the equivalent 0xFF, but the
        // engine accepts it, so this class must too rather than throwing.
        using var body = new MemoryStream();
        body.WriteByte(0x00);              // root childCount = 0
        WriteU32(body, 0xCCCCCCCC);         // root TypeHash
        body.WriteByte(0xFE);              // root valueCount, encoded via the 0xFE marker byte...
        WriteU32(body, 1);                  // ...meaning "1", not "backreference to pointer index 1"

        WriteU32(body, 0x00000042);         // the one value's nameHash
        body.WriteByte(0x02);              // size = 2 (direct)
        body.Write((byte[])[0x12, 0x34]);

        byte[] fcb = WrapWithHeader(body.ToArray());

        FcbObject root = FcbDocument.Deserialize(fcb);

        Assert.Single(root.Values);
        Assert.Equal((byte[])[0x12, 0x34], root.Values[0x00000042]);
    }

    [Fact]
    public void Deserialize_rejects_a_file_without_the_FCbn_signature()
        => Assert.Throws<InvalidDataException>(() => FcbDocument.Deserialize("not an fcb file!!"u8.ToArray()));

    [Fact]
    public void Deserialize_rejects_an_unsupported_version()
    {
        using var output = new MemoryStream();
        WriteU32(output, 0x4643626Eu);
        WriteU16(output, 99); // not version 2
        WriteU16(output, 0);
        WriteU32(output, 0);
        WriteU32(output, 0);

        Assert.Throws<InvalidDataException>(() => FcbDocument.Deserialize(output.ToArray()));
    }

    [Fact]
    public void Deserialize_rejects_flags_bit_0_since_the_string_hashed_TypeHash_path_is_unimplemented()
    {
        using var output = new MemoryStream();
        WriteU32(output, 0x4643626Eu);
        WriteU16(output, 2);
        WriteU16(output, 1); // flags bit 0 set
        WriteU32(output, 0);
        WriteU32(output, 0);

        Assert.Throws<InvalidDataException>(() => FcbDocument.Deserialize(output.ToArray()));
    }

    private static int CountUniqueObjects(FcbObject node, HashSet<FcbObject> visited)
    {
        if (!visited.Add(node)) return 0; // already counted once - a backreferenced/shared subtree

        int count = 1;
        foreach (FcbObject child in node.Children)
        {
            count += CountUniqueObjects(child, visited);
        }
        return count;
    }

    private static void AssertSameShape(FcbObject expected, FcbObject actual)
    {
        Assert.Equal(expected.TypeHash, actual.TypeHash);
        Assert.Equal(expected.Values.Keys.OrderBy(k => k), actual.Values.Keys.OrderBy(k => k));
        foreach (uint key in expected.Values.Keys)
        {
            Assert.Equal(expected.Values[key], actual.Values[key]);
        }

        Assert.Equal(expected.Children.Count, actual.Children.Count);
        for (int i = 0; i < expected.Children.Count; i++)
        {
            AssertSameShape(expected.Children[i], actual.Children[i]);
        }
    }

    private static byte[] WrapWithHeader(byte[] rootBytes)
    {
        using var output = new MemoryStream();
        WriteU32(output, 0x4643626Eu); // "FCbn"
        WriteU16(output, 2);
        WriteU16(output, 0);
        WriteU32(output, 0); // totalObjectCount - not consulted on read
        WriteU32(output, 0); // totalValueCount  - not consulted on read
        output.Write(rootBytes);
        return output.ToArray();
    }

    private static void WriteU32(Stream s, uint v)
    {
        Span<byte> b = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32LittleEndian(b, v);
        s.Write(b);
    }

    private static void WriteU16(Stream s, ushort v)
    {
        Span<byte> b = stackalloc byte[2];
        BinaryPrimitives.WriteUInt16LittleEndian(b, v);
        s.Write(b);
    }
}
