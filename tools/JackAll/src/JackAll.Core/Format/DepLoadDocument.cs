using System.Runtime.InteropServices;

namespace JackAll.Core.Format;

/// <summary>
/// One dependency of a `depload.dat` parent - a resource CRC32 plus its resolved type hash (looked up
/// via the file's own small deduplicated type table at decode time - see <see cref="DepLoadDocument"/>'s
/// remarks). The type hash's own semantic meaning (e.g. "this dependency is a texture" vs "a mesh") is
/// not yet confirmed - only that it's a per-resource-*type* value shared by many children, not a
/// per-child one, which is why the file bothers deduplicating it at all.
/// </summary>
public readonly record struct DepLoadChild(uint Hash, uint TypeHash);

/// <summary>
/// One `depload.dat` parent entry: a resource CRC32 plus every child (dependency) it pulls in.
/// </summary>
/// <remarks>
/// <paramref name="ChildIndex"/> is where this parent's children sat in the flattened child arrays,
/// kept only to order the blocks on the way back out - <see cref="DepLoadDocument.Encode"/> assigns
/// each parent a fresh one. It has to be carried because block order is not parent order: the
/// parents array is sorted by CRC32 while the child blocks are in an unrelated order, so rebuilding
/// the arrays parent-by-parent would silently reshuffle a file that should have round-tripped.
/// </remarks>
public sealed record DepLoadParent(uint Hash, int ChildIndex, IReadOnlyList<DepLoadChild> Children);

/// <summary>
/// A decoded `depload.dat` - the per-world/per-DLC dependency-preload index the engine walks in
/// `CXGame::LoadDepLoad` before gameplay starts. Not a container of embedded file bytes: every entry
/// is a CRC32 *reference* to another resource that already lives elsewhere in the game's archives, used
/// to prefetch a resource's dependencies (textures, meshes, sub-`.fcb`s, ...) ahead of when the engine's
/// streaming system would otherwise discover the need for them lazily.
/// </summary>
public sealed record DepLoadFile(IReadOnlyList<DepLoadParent> Parents);

/// <summary>
/// Decodes and encodes a `depload.dat`.
/// </summary>
/// <remarks>
/// Reverse-engineered live via GhidraMCP against a fully-symbolized FC2 build (the same "server build"
/// used elsewhere in this project's research - see docs/docs/file-formats/depload.md), tracing
/// `CResourceDataBase::LoadBinaryFile` (0x09c594c0) call-for-call against its own `IFile::Read` calls,
/// then confirmed byte-for-byte against a real shipped `entitylibrary_depload.dat` (433 parents, 1314
/// children, 8-entry type table - every byte accounted for):
/// <code>
/// u32 parentCount N
/// N x { u16 childIndex, u16 childCount, u32 parentHash }   // sorted ascending by parentHash (as u32)
///
/// u32 childHashCount   M_A;  M_A x u32 childHash[]        // one CRC32 per flattened child
/// u32 childTypeIxCount M_B;  M_B x u8  childTypeIndex[]   // M_B == M_A - one index per child, into...
/// u32 typeTableCount   M_C;  M_C x u32 typeHash[]         // ...this small deduplicated type-hash table
/// </code>
/// A parent's children are the slice [childIndex, childIndex+childCount) of the two per-child arrays
/// (hash and type-index) - in every real file M_A == M_B (one entry per flattened child), but M_C is
/// independent and much smaller: real data has 1314 children sharing only 8 distinct type hashes, with
/// every type-index byte observed in [0, 8) - confirming this is a lookup table, not a third per-child
/// field, which an earlier pass at this format (before a real sample was available to check against)
/// had wrongly assumed. Parents are already sorted ascending by CRC32 on disk (the engine binary-searches
/// this array on load, so a real file has to be) - this decoder keeps file order rather than re-sorting.
///
/// Three properties of every one of the 27 shipped files are what let <see cref="Encode"/> rebuild a
/// file from the decoded model alone rather than echoing what it parsed: the child slices are a
/// gapless, non-overlapping cover of the child arrays, the type table is in first-use order with no
/// unused slot, and `childIndex` is *not* monotonic in parent order. The corpus tests pin all three.
/// </remarks>
public static class DepLoadDocument
{
    public static DepLoadFile Decode(byte[] content)
    {
        var cursor = new ByteCursor(content);
        uint parentCount = cursor.ReadU32();

        var rawParents = new (ushort ChildIndex, ushort ChildCount, uint Hash)[parentCount];
        for (int i = 0; i < parentCount; i++)
        {
            ushort childIndex = cursor.ReadU16();
            ushort childCount = cursor.ReadU16();
            uint hash = cursor.ReadU32();
            rawParents[i] = (childIndex, childCount, hash);
        }

        uint[] childHash = ReadU32Array(ref cursor, out uint childHashCount);
        byte[] childTypeIndex = ReadU8Array(ref cursor, out uint childTypeIndexCount);
        uint[] typeTable = ReadU32Array(ref cursor, out _);

        if (childHashCount != childTypeIndexCount)
        {
            throw new InvalidDataException(
                $"depload.dat's per-child arrays disagree on length (hash={childHashCount}, " +
                $"typeIndex={childTypeIndexCount}) - can't align them into per-child records.");
        }

        var parents = new DepLoadParent[parentCount];
        for (int i = 0; i < parentCount; i++)
        {
            (ushort childIndex, ushort childCount, uint hash) = rawParents[i];
            int end = childIndex + childCount;
            if (end > childHashCount)
            {
                throw new InvalidDataException(
                    $"Parent 0x{hash:X8}'s child slice [{childIndex}, {end}) runs past the " +
                    $"{childHashCount}-entry child arrays - corrupt depload.dat.");
            }

            var children = new DepLoadChild[childCount];
            for (int c = 0; c < childCount; c++)
            {
                int idx = childIndex + c;
                byte typeIndex = childTypeIndex[idx];
                if (typeIndex >= typeTable.Length)
                {
                    throw new InvalidDataException(
                        $"Parent 0x{hash:X8}'s child #{c} has type index {typeIndex}, but the type " +
                        $"table only has {typeTable.Length} entries - corrupt depload.dat.");
                }
                children[c] = new DepLoadChild(childHash[idx], typeTable[typeIndex]);
            }
            parents[i] = new DepLoadParent(hash, childIndex, children);
        }

        return new DepLoadFile(parents);
    }

    /// <summary>
    /// Serializes a `depload.dat`, re-deriving the parents' sort order, the child slices and the type
    /// table from the model - so an edited file is laid out correctly rather than needing its indices
    /// hand-maintained.
    /// </summary>
    public static byte[] Encode(DepLoadFile file)
    {
        int count = file.Parents.Count;

        // uint sorts unsigned, which is what the engine's binary search over this array expects;
        // comparing these hashes signed is the documented way to make a file that loads but misbehaves.
        int[] byHash = [.. Enumerable.Range(0, count).OrderBy(i => file.Parents[i].Hash)];
        int[] byBlock = [.. Enumerable.Range(0, count).OrderBy(i => file.Parents[i].ChildIndex)];

        int childCount = file.Parents.Sum(p => p.Children.Count);
        if (childCount > ushort.MaxValue)
        {
            throw new InvalidDataException(
                $"This depload.dat holds {childCount} children, but childIndex is a u16 - a file " +
                $"cannot hold more than {ushort.MaxValue} in total.");
        }

        var childIndex = new int[count];
        var typeIndexOf = new Dictionary<uint, byte>();
        var typeTable = new List<uint>();
        var childHash = new uint[childCount];
        var childTypeIndex = new byte[childCount];
        int at = 0;
        foreach (int i in byBlock)
        {
            DepLoadParent parent = file.Parents[i];
            if (parent.Children.Count > ushort.MaxValue)
            {
                throw new InvalidDataException(
                    $"Parent 0x{parent.Hash:X8} has {parent.Children.Count} children, but childCount " +
                    $"is a u16 - depload.dat cannot address more than {ushort.MaxValue} per parent.");
            }

            childIndex[i] = at;
            foreach (DepLoadChild child in parent.Children)
            {
                if (!typeIndexOf.TryGetValue(child.TypeHash, out byte typeIndex))
                {
                    if (typeTable.Count > byte.MaxValue)
                    {
                        throw new InvalidDataException(
                            $"This depload.dat needs more than {byte.MaxValue + 1} distinct type hashes, " +
                            "but childTypeIndex is a u8.");
                    }
                    typeIndex = (byte)typeTable.Count;
                    typeIndexOf[child.TypeHash] = typeIndex;
                    typeTable.Add(child.TypeHash);
                }

                childHash[at] = child.Hash;
                childTypeIndex[at] = typeIndex;
                at++;
            }
        }

        var writer = new ByteWriter();
        writer.WriteU32((uint)count);
        foreach (int i in byHash)
        {
            DepLoadParent parent = file.Parents[i];
            writer.WriteU16((ushort)childIndex[i]);
            writer.WriteU16((ushort)parent.Children.Count);
            writer.WriteU32(parent.Hash);
        }

        writer.WriteU32((uint)childCount);
        writer.WriteU32Array(childHash);
        writer.WriteU32((uint)childCount);
        writer.WriteRaw(childTypeIndex);
        writer.WriteU32((uint)typeTable.Count);
        writer.WriteU32Array(CollectionsMarshal.AsSpan(typeTable));

        return writer.ToArray();
    }

    private static uint[] ReadU32Array(ref ByteCursor cursor, out uint count)
    {
        count = cursor.ReadU32();
        var result = new uint[count];
        for (int i = 0; i < count; i++)
        {
            result[i] = cursor.ReadU32();
        }
        return result;
    }

    private static byte[] ReadU8Array(ref ByteCursor cursor, out uint count)
    {
        count = cursor.ReadU32();
        return cursor.ReadBytes((int)count);
    }
}
