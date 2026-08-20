using System.Buffers.Binary;
using System.Numerics;
using System.Text;

namespace JackAll.Tools.Rtx;

/// <summary>One skeleton node: a tapered tube running <see cref="Length"/> along
/// <see cref="Direction"/> from <see cref="Position"/>.</summary>
public readonly record struct RtxNode(Vector3 Position, Vector3 Direction, float Radius, float Length);

/// <summary>A run of consecutive nodes forming one limb, the trunk included. Both ends inclusive,
/// so the run holds <c>LastNode - FirstNode</c> segments.</summary>
public readonly record struct RtxBranch(int FirstNode, int LastNode);

/// <summary>One leaf card: four corner offsets from <see cref="Position"/>, all of them at the
/// card's own radius.</summary>
public readonly record struct RtxLeafCard(
    Vector3 Position, Vector3 C0, Vector3 C1, Vector3 C2, Vector3 C3);

/// <summary>One detail level of a modelled leaf, in model space.</summary>
public sealed class RtxLeafLod
{
    public required Vector3[] Positions { get; init; }
    public required Vector3[] Normals { get; init; }
    public required Vector2[] Uvs { get; init; }
    public required int[] Indices { get; init; }
}

/// <summary>One modelled leaf, at every detail level the file ships for it, finest first.</summary>
public sealed record RtxHybridLeaf(IReadOnlyList<RtxLeafLod> Lods);

/// <summary>
/// Reads the static geometry out of a Far Cry 2 <c>.rtx</c> RealTree: the branch skeleton, the leaf
/// cards or modelled leaves hanging off it, and the materials the three draw with. Enough to render
/// the species - not the simulation, which is the rest of the file.
/// </summary>
/// <remarks>
/// The file is one dumped memory image. Everything after the header is a single arena whose
/// pointers were live addresses on the machine that saved it, so the offsets are not stored: the
/// engine recomputes them by re-walking the arena in the order it originally packed it, and so does
/// <see cref="Parse"/>. Every step of that walk mirrors <c>RTxcManager::SetSkeletalPointer</c>, and
/// getting one stride wrong desynchronises everything after it - see the format notes in
/// <c>docs/docs/file-formats/rtx.md</c>.
/// </remarks>
public sealed class RtxModel
{
    /// <summary>Version tag every shipped file carries, and the size of the header that follows.</summary>
    private const int HeaderTag = 0x88;

    /// <summary>The arena's own fixed head: a render block and a location block, 0x20 each.</summary>
    private const int ArenaStart = 0x260;

    private const int RenderBlock = 0x220;

    /// <summary>What the file calls itself, e.g. <c>graphics\Vegetation\Desert\Realtrees\HY_Aloes_01</c>.</summary>
    public required string Name { get; init; }

    public required IReadOnlyList<RtxNode> Nodes { get; init; }

    public required IReadOnlyList<RtxBranch> Branches { get; init; }

    /// <summary>The flat cards that stand in for foliage on most species. Empty on the ones that
    /// model their leaves instead - a species carries one kind or the other, never both.</summary>
    public required IReadOnlyList<RtxLeafCard> LeafCards { get; init; }

    /// <summary>Leaves modelled as real meshes, which the jungle plants use in place of cards.</summary>
    public required IReadOnlyList<RtxHybridLeaf> HybridLeaves { get; init; }

    /// <summary>
    /// Material path per slot - 0 bark, 1 leaf cards, 2 modelled leaves - as the file writes it, and
    /// null for a slot this species does not use. The slots are the argument order of
    /// <c>RTxcSkeleton::InitLOD</c>, which sets each one up guarded by the count it draws.
    /// </summary>
    /// <remarks>These name a <c>.mlm</c>, an authoring extension that ships in no archive. The
    /// material that does ship is the <c>.xbm</c> of the same stem.</remarks>
    public required IReadOnlyList<string?> Materials { get; init; }

    public const int SlotBark = 0;
    public const int SlotLeafCards = 1;
    public const int SlotHybridLeaves = 2;
    private const int SlotCount = 4;

    public static RtxModel Parse(byte[] data)
    {
        var file = new ReadOnlySpan<byte>(data);

        // The first two words size the file's sections; the header the engine reads starts after
        // them, which is what every offset below is relative to.
        int sectionA = ReadInt(file, 0);
        var header = file[8..];
        if (ReadInt(header, 0) != HeaderTag)
        {
            throw new InvalidDataException("Not a .rtx: header tag is not 0x88.");
        }

        int prefixBytes = ReadInt(header, 8);
        int skeletonBytes = ReadInt(header, 0x0c);
        string name = Encoding.Latin1.GetString(header[0x10..0x110]).TrimEnd('\0');

        int skeletonAt = 8 + 0x110 + prefixBytes;
        if (prefixBytes < 0 || skeletonBytes < ArenaStart || skeletonAt + skeletonBytes > data.Length)
        {
            throw new InvalidDataException("Truncated .rtx: the skeleton runs past the end of the file.");
        }

        ReadOnlySpan<byte> skeleton = file.Slice(skeletonAt, skeletonBytes);
        var arena = new Arena(skeletonBytes);

        int nodeCount = ReadInt(skeleton, 0x10);
        int branchCount = ReadInt(skeleton, 0x1c);
        int cardCount = ReadInt(skeleton, 0x28);
        int hybridCount = ReadInt(skeleton, 0x34);

        RtxHybridLeaf[] hybrids = ReadHybridLeaves(skeleton, ref arena, hybridCount);

        arena.SkipArray(ReadInt(skeleton, 0x180), 0x34);
        int branchesAt = arena.TakeArray(branchCount, 0x28);
        arena.SkipArray(branchCount, 0x2c);

        // The per-node record and the geometry beside it are the one pair the walk cannot align
        // past: the record's tail is a variable-length list whose length only it knows.
        int nodesAt = arena.TakeArray(nodeCount, 0x4c);
        int nodeGeometryAt = arena.TakeUnaligned(nodeCount, 0x20);
        int cardsAt = arena.TakeArray(cardCount, 0x5c);
        for (int node = 0; node < nodeCount; node++)
        {
            arena.SkipRaw(ReadShort(skeleton, nodesAt + node * 0x4c + 0x2e) * 2);
        }

        arena.Align();
        arena.SkipArray(nodeCount, 0x48);
        arena.SkipArray(cardCount, 0x18);
        arena.SkipArray(ReadInt(skeleton, RenderBlock + 0x0c), 0x54);
        arena.SkipArray(ReadInt(skeleton, RenderBlock + 8), 4);

        // The two pose arrays are what actually places a node or a card in model space; everything
        // above them is the simulation's own bookkeeping.
        int nodePoseAt = arena.TakeUnaligned(nodeCount, 0x20);
        int cardPoseAt = arena.TakeUnaligned(cardCount, 0x20);
        RequireArenaConsumed(arena.At, skeletonBytes, hybrids);

        return new RtxModel
        {
            Name = name,
            Nodes = ReadNodes(skeleton, nodePoseAt, nodeGeometryAt, nodeCount),
            Branches = ReadBranches(skeleton, branchesAt, branchCount),
            LeafCards = ReadLeafCards(skeleton, cardsAt, cardPoseAt, cardCount),
            HybridLeaves = hybrids,
            Materials = ReadMaterials(file, skeletonAt + skeletonBytes, 8 + sectionA),
        };
    }

    /// <summary>
    /// The only check a file with no chunk tags admits: a walk that got every stride right lands on
    /// the end of the arena, bar the render copy of the finest leaf level the engine leaves there.
    /// One stride wrong and the geometry read above it is silently someone else's bytes.
    /// </summary>
    private static void RequireArenaConsumed(int at, int skeletonBytes, RtxHybridLeaf[] hybrids)
    {
        const int RenderVertexBytes = 60;

        int rendered = hybrids.Sum(leaf => leaf.Lods.Count > 0 ? leaf.Lods[0].Positions.Length : 0);
        int expected = at + Align16(rendered * RenderVertexBytes);
        if (expected != skeletonBytes)
        {
            throw new InvalidDataException(
                $"Unrecognised .rtx layout: the skeleton walk ended at {expected} of {skeletonBytes} bytes.");
        }
    }

    private static RtxNode[] ReadNodes(
        ReadOnlySpan<byte> skeleton, int poseAt, int geometryAt, int count)
    {
        var nodes = new RtxNode[count];
        for (int i = 0; i < count; i++)
        {
            int pose = poseAt + i * 0x20;
            int geometry = geometryAt + i * 0x20;
            nodes[i] = new RtxNode(
                ReadVector3(skeleton, pose),
                ReadVector3(skeleton, pose + 0x10),
                ReadFloat(skeleton, geometry + 8),
                ReadFloat(skeleton, geometry + 0x0c));
        }

        return nodes;
    }

    private static RtxBranch[] ReadBranches(ReadOnlySpan<byte> skeleton, int at, int count)
    {
        var branches = new RtxBranch[count];
        for (int i = 0; i < count; i++)
        {
            int first = ReadInt(skeleton, at + i * 0x28);
            branches[i] = new RtxBranch(first, first + ReadInt(skeleton, at + i * 0x28 + 4));
        }

        return branches;
    }

    private static RtxLeafCard[] ReadLeafCards(
        ReadOnlySpan<byte> skeleton, int cardsAt, int poseAt, int count)
    {
        var cards = new RtxLeafCard[count];
        for (int i = 0; i < count; i++)
        {
            int card = cardsAt + i * 0x5c;
            cards[i] = new RtxLeafCard(
                ReadVector3(skeleton, poseAt + i * 0x20),
                ReadVector3(skeleton, card + 0x24),
                ReadVector3(skeleton, card + 0x30),
                ReadVector3(skeleton, card + 0x3c),
                ReadVector3(skeleton, card + 0x48));
        }

        return cards;
    }

    /// <summary>
    /// The modelled leaves, which sit at the very front of the arena: a table of one pointer each,
    /// then a record apiece holding that leaf's detail levels back to back.
    /// </summary>
    private static RtxHybridLeaf[] ReadHybridLeaves(
        ReadOnlySpan<byte> skeleton, ref Arena arena, int count)
    {
        if (count <= 0)
        {
            return [];
        }

        arena.SkipArray(count, 4);
        var leaves = new RtxHybridLeaf[count];
        for (int leaf = 0; leaf < count; leaf++)
        {
            int record = arena.At;
            arena.SkipRaw(0x150);
            int lodCount = Math.Max(ReadInt(skeleton, record + 0x120), 0);
            int table = record + 0x150;
            arena.SkipRaw(lodCount * 0x10);

            var lods = new RtxLeafLod[lodCount];
            for (int lod = 0; lod < lods.Length; lod++)
            {
                int entry = table + lod * 0x10;
                int vertexCount = ReadInt(skeleton, entry);
                int indexCount = ReadInt(skeleton, entry + 8);
                int verticesAt = arena.TakeArray(vertexCount, 0x94);
                int indicesAt = arena.TakeArray(indexCount, 2);
                lods[lod] = ReadLeafLod(skeleton, verticesAt, vertexCount, indicesAt, indexCount);
            }

            leaves[leaf] = new RtxHybridLeaf(lods);
        }

        return leaves;
    }

    /// <summary>The rest of each 0x94-byte vertex is the leaf simulation's: which vertices it is
    /// pinned to, and how far from each it rests.</summary>
    private static RtxLeafLod ReadLeafLod(
        ReadOnlySpan<byte> skeleton, int verticesAt, int vertexCount, int indicesAt, int indexCount)
    {
        var positions = new Vector3[vertexCount];
        var normals = new Vector3[vertexCount];
        var uvs = new Vector2[vertexCount];
        for (int i = 0; i < vertexCount; i++)
        {
            int vertex = verticesAt + i * 0x94;
            positions[i] = ReadVector3(skeleton, vertex);
            normals[i] = ReadVector3(skeleton, vertex + 0x0c);
            uvs[i] = new Vector2(ReadFloat(skeleton, vertex + 0x48), ReadFloat(skeleton, vertex + 0x4c));
        }

        var indices = new int[indexCount - indexCount % 3];
        for (int i = 0; i < indices.Length; i++)
        {
            int index = BinaryPrimitives.ReadUInt16LittleEndian(skeleton[(indicesAt + i * 2)..]);
            indices[i] = index < vertexCount ? index : 0;
        }

        return new RtxLeafLod { Positions = positions, Normals = normals, Uvs = uvs, Indices = indices };
    }

    /// <summary>The one part of the file that is not a memory image: a count, then a slot, a length
    /// and an unterminated path per entry.</summary>
    private static string?[] ReadMaterials(ReadOnlySpan<byte> file, int at, int end)
    {
        var materials = new string?[SlotCount];
        if (at + 4 > end)
        {
            return materials;
        }

        int count = ReadInt(file, at);
        int cursor = at + 4;
        for (int i = 0; i < count && cursor + 8 <= end; i++)
        {
            int slot = ReadInt(file, cursor);
            int length = ReadInt(file, cursor + 4);
            if (length < 0 || cursor + 8 + length > end)
            {
                break;
            }

            if ((uint)slot < SlotCount)
            {
                materials[slot] = Encoding.Latin1.GetString(file.Slice(cursor + 8, length));
            }

            cursor += 8 + length;
        }

        return materials;
    }

    /// <summary>
    /// The bump allocator the file was packed with: every array is placed at the cursor and the
    /// cursor rounded up to 16, bar a couple the engine leaves tight against the next.
    /// </summary>
    private struct Arena(int limit)
    {
        private readonly int _limit = limit;

        public int At { get; private set; } = ArenaStart;

        /// <summary>Where the array lands, with the cursor left past its padded end.</summary>
        public int TakeArray(int count, int stride)
        {
            int at = At;
            SkipArray(count, stride);
            return at;
        }

        public int TakeUnaligned(int count, int stride)
        {
            int at = At;
            SkipRaw(count * stride);
            return at;
        }

        public void SkipArray(int count, int stride)
        {
            if (count > 0)
            {
                SkipRaw(Align16(count * stride));
            }
        }

        public void SkipRaw(int bytes)
        {
            if (bytes < 0 || At + bytes > _limit)
            {
                throw new InvalidDataException("Malformed .rtx: an array runs past the end of the skeleton.");
            }

            At += bytes;
        }

        public void Align() => At = Align16(At);
    }

    /// <summary>Every array in the arena starts on a 16-byte boundary.</summary>
    private static int Align16(int value) => (value + 15) & ~15;

    private static int ReadInt(ReadOnlySpan<byte> data, int at)
        => BinaryPrimitives.ReadInt32LittleEndian(data[at..]);

    private static short ReadShort(ReadOnlySpan<byte> data, int at)
        => BinaryPrimitives.ReadInt16LittleEndian(data[at..]);

    private static float ReadFloat(ReadOnlySpan<byte> data, int at)
        => BinaryPrimitives.ReadSingleLittleEndian(data[at..]);

    private static Vector3 ReadVector3(ReadOnlySpan<byte> data, int at)
        => new(ReadFloat(data, at), ReadFloat(data, at + 4), ReadFloat(data, at + 8));
}
