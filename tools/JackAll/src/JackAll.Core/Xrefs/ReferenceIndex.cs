using System.Runtime.InteropServices;
using System.Text;

namespace JackAll.Core.Xrefs;

/// <summary>
/// The built reference graph: every <see cref="RefEdge"/> in the game, queryable in both directions,
/// plus where non-file hashes are defined and what the reference sites are called.
/// </summary>
/// <remarks>
/// Laid out exactly the way <see cref="Vfs.GameCache"/> is, and for the same reason: the whole file
/// is read once into <see cref="_fileBytes"/> and every record array is a
/// <see cref="MemoryMarshal.Cast{TFrom,TTo}(System.ReadOnlySpan{TFrom})"/> view straight over a slice
/// of it, sorted so a lookup is a binary search over that span. Nothing is copied into a parallel
/// managed structure at load. That matters more here than it does for the cache: a real install
/// produces millions of edges, and rebuilding a <c>Dictionary</c> of them on every launch would cost
/// more than the queries ever will.
///
/// Both query directions are served from *one* copy of the edges. Edges are stored sorted by
/// (space, target, source) so <see cref="ReferencesTo"/> is a range scan, and a separate <c>u32</c>
/// permutation array gives the (source, space, target) order <see cref="ReferencesFrom"/> needs -
/// 4 bytes per edge instead of the 16 a second sorted copy would cost.
///
/// **There is no invalidation logic**, deliberately - the same contract <see cref="Vfs.GameCache"/>
/// already establishes. Only entries whose winning source is a non-volatile base archive are
/// persisted; `patch.dat`, mod layers and the workspace are re-extracted every session (a mod is
/// tens to hundreds of files, so that costs nothing). A base archive's bytes never change for the
/// life of an install, so a saved index is either right or the user reinstalled the game, in which
/// case they delete the file - exactly as today. <c>GameCache.TryGetContentHash</c> is deliberately
/// *not* used as a change key: it's populated lazily by the legacy mod importer, so most entries
/// simply have no content hash to compare against.
/// </remarks>
public sealed class ReferenceIndex
{
    private const uint Magic = 0x3158414A; // 'JAX1'
    private const int Version = 1;

    [StructLayout(LayoutKind.Sequential, Pack = 4)]
    private struct EdgeRecord : IKeyedRecord
    {
        public uint SourceFile;
        public uint Target;
        public uint SiteKey;
        public ushort SiteIndex;
        public byte Space;
        public byte Kind;

        // Edges sort by (space, target, ...) - the packed key is that ordering's prefix.
        public readonly ulong Key => BinaryTable.PackKey(Space, Target);
    }

    [StructLayout(LayoutKind.Sequential, Pack = 4)]
    private struct DefRecord : IKeyedRecord
    {
        public uint Id;
        public uint DefiningFile;
        public uint SiteKey;
        public uint Space; // a full word rather than a byte + padding: same 16-byte record either way

        public readonly ulong Key => BinaryTable.PackKey(Space, Id);
    }

    [StructLayout(LayoutKind.Sequential, Pack = 4)]
    private struct NameRecord : IKeyedRecord
    {
        public uint Hash;
        public uint NameOffset;
        public uint NameLength;

        public readonly ulong Key => Hash;
    }

    private byte[] _fileBytes = [];
    private (int Offset, int Count) _edges;
    private (int Offset, int Count) _bySource;   // uint[] permutation into _edges
    private (int Offset, int Count) _definitions;
    private (int Offset, int Count) _names;
    private (int Offset, int Count) _nameBlob;
    private (int Offset, int Count) _indexedFiles; // uint[] of source hashes covered by this index

    /// <summary>An index with nothing in it - what <see cref="Load"/> returns when there's no usable
    /// file, and a perfectly valid thing to query.</summary>
    public static ReferenceIndex Empty { get; } = new();

    public int EdgeCount => _edges.Count;
    public int DefinitionCount => _definitions.Count;
    public int IndexedFileCount => _indexedFiles.Count;

    private ReadOnlySpan<EdgeRecord> EdgeSpan => BinaryTable.RecordSpan<EdgeRecord>(_fileBytes, _edges);

    private ReadOnlySpan<uint> BySourceSpan => BinaryTable.RecordSpan<uint>(_fileBytes, _bySource);

    private ReadOnlySpan<DefRecord> DefSpan => BinaryTable.RecordSpan<DefRecord>(_fileBytes, _definitions);

    private ReadOnlySpan<NameRecord> NameSpan => BinaryTable.RecordSpan<NameRecord>(_fileBytes, _names);

    private ReadOnlySpan<byte> NameBlobSpan => _fileBytes.AsSpan(_nameBlob.Offset, _nameBlob.Count);

    private ReadOnlySpan<uint> IndexedFileSpan => BinaryTable.RecordSpan<uint>(_fileBytes, _indexedFiles);

    // ------------------------------------------------------------------ queries

    /// <summary>
    /// Every reference *to* <paramref name="target"/> in <paramref name="space"/> - the "who uses
    /// this?" direction, which is the whole reason a global index has to exist at all.
    /// </summary>
    public IReadOnlyList<RefEdge> ReferencesTo(RefSpace space, uint target)
    {
        ReadOnlySpan<EdgeRecord> edges = EdgeSpan;
        ulong key = BinaryTable.PackKey((byte)space, target);
        int start = BinaryTable.LowerBound(edges, key);

        var result = new List<RefEdge>();
        for (int i = start; i < edges.Length; i++)
        {
            EdgeRecord record = edges[i];
            if (record.Key != key)
            {
                break;
            }
            result.Add(ToEdge(record));
        }
        return result;
    }

    /// <summary>Every reference *from* <paramref name="sourceFile"/> - what this file points at.</summary>
    public IReadOnlyList<RefEdge> ReferencesFrom(uint sourceFile)
    {
        ReadOnlySpan<EdgeRecord> edges = EdgeSpan;
        ReadOnlySpan<uint> order = BySourceSpan;
        int start = LowerBoundBySource(edges, order, sourceFile);

        var result = new List<RefEdge>();
        for (int i = start; i < order.Length; i++)
        {
            EdgeRecord record = edges[(int)order[i]];
            if (record.SourceFile != sourceFile)
            {
                break;
            }
            result.Add(ToEdge(record));
        }
        return result;
    }

    /// <summary>Where <paramref name="id"/> lives, for a space whose ids aren't files themselves.
    /// False when nothing in the game defines it - the normal case for an
    /// <see cref="RefSpace.EngineName"/>.</summary>
    public bool TryGetDefinition(RefSpace space, uint id, out RefDefinition definition)
    {
        ReadOnlySpan<DefRecord> defs = DefSpan;
        int found = BinaryTable.Find(defs, BinaryTable.PackKey((byte)space, id));
        if (found < 0)
        {
            definition = default;
            return false;
        }
        DefRecord record = defs[found];
        definition = new RefDefinition((RefSpace)record.Space, record.Id, record.DefiningFile, record.SiteKey);
        return true;
    }

    /// <summary>The readable name of a reference site, or null when only its hash is known (the xref
    /// list renders that as <c>#XXXXXXXX</c>).</summary>
    public string? Name(uint siteKey)
    {
        ReadOnlySpan<NameRecord> names = NameSpan;
        int found = BinaryTable.Find(names, siteKey);
        if (found < 0)
        {
            return null;
        }
        NameRecord record = names[found];
        return Encoding.UTF8.GetString(NameBlobSpan.Slice((int)record.NameOffset, (int)record.NameLength));
    }

    /// <summary>
    /// The whole hash → name table. Carried over wholesale by an incremental rebuild: a name can be
    /// attached to an edge's site *or* to an <see cref="RefSpace.EngineName"/> target (an `.xbg`
    /// material name is the latter), so reconstructing the table by walking only the reused edges'
    /// site keys silently drops the second kind.
    /// </summary>
    public IReadOnlyDictionary<uint, string> AllNames()
    {
        ReadOnlySpan<NameRecord> records = NameSpan;
        ReadOnlySpan<byte> blob = NameBlobSpan;
        var result = new Dictionary<uint, string>(records.Length);
        for (int i = 0; i < records.Length; i++)
        {
            NameRecord record = records[i];
            result[record.Hash] = Encoding.UTF8.GetString(blob.Slice((int)record.NameOffset, (int)record.NameLength));
        }
        return result;
    }

    /// <summary>Every definition in the index, in (space, id) order. Used by the incremental rebuild,
    /// which has to carry definitions over per *defining file* - a lookup this index's own (space, id)
    /// ordering can't answer directly.</summary>
    public IReadOnlyList<RefDefinition> AllDefinitions()
    {
        ReadOnlySpan<DefRecord> defs = DefSpan;
        var result = new RefDefinition[defs.Length];
        for (int i = 0; i < defs.Length; i++)
        {
            DefRecord record = defs[i];
            result[i] = new RefDefinition((RefSpace)record.Space, record.Id, record.DefiningFile, record.SiteKey);
        }
        return result;
    }

    /// <summary>Whether this index already covers <paramref name="fileHash"/> - including a file that
    /// turned out to have no references at all, which is just as worth remembering as a long list.</summary>
    public bool IsIndexed(uint fileHash) => IndexedFileSpan.BinarySearch(fileHash) >= 0;

    private static RefEdge ToEdge(EdgeRecord record) => new(
        record.SourceFile, (RefSpace)record.Space, record.Target, (RefKind)record.Kind,
        record.SiteKey, record.SiteIndex);

    // ------------------------------------------------------------------ ordering

    private static int LowerBoundBySource(ReadOnlySpan<EdgeRecord> edges, ReadOnlySpan<uint> order, uint sourceFile)
    {
        int lo = 0, hi = order.Length;
        while (lo < hi)
        {
            int mid = lo + ((hi - lo) / 2);
            if (edges[(int)order[mid]].SourceFile < sourceFile) lo = mid + 1; else hi = mid;
        }
        return lo;
    }

    private static int FullEdgeOrder(EdgeRecord a, EdgeRecord b)
    {
        if (a.Key != b.Key) return a.Key.CompareTo(b.Key);
        if (a.SourceFile != b.SourceFile) return a.SourceFile.CompareTo(b.SourceFile);
        if (a.SiteKey != b.SiteKey) return a.SiteKey.CompareTo(b.SiteKey);
        return a.SiteIndex.CompareTo(b.SiteIndex);
    }

    // ------------------------------------------------------------------ building

    /// <summary>
    /// Lays out a fresh index from raw extraction output. Goes through the same byte layout
    /// <see cref="Load"/> reads, so an index built in memory and one read from disk behave
    /// identically - there is no second, "in-memory only" code path to keep in sync.
    /// </summary>
    /// <param name="indexedFiles">Every file the build actually visited, including ones with no
    /// references - see <see cref="IsIndexed"/>.</param>
    public static ReferenceIndex Build(
        IEnumerable<RefEdge> edges,
        IEnumerable<RefDefinition> definitions,
        IReadOnlyDictionary<uint, string> names,
        IEnumerable<uint> indexedFiles)
        => ParseBytes(Serialize(edges, definitions, names, indexedFiles));

    private static byte[] Serialize(
        IEnumerable<RefEdge> edges,
        IEnumerable<RefDefinition> definitions,
        IReadOnlyDictionary<uint, string> names,
        IEnumerable<uint> indexedFiles)
    {
        EdgeRecord[] edgeRecords = [.. edges.Select(e => new EdgeRecord
        {
            SourceFile = e.SourceFile,
            Target = e.Target,
            SiteKey = e.SiteKey,
            SiteIndex = e.SiteIndex,
            Space = (byte)e.TargetSpace,
            Kind = (byte)e.Kind,
        })];
        Array.Sort(edgeRecords, FullEdgeOrder);

        // The by-source view is a permutation, not a second copy: sorting indices keeps the extra
        // cost at 4 bytes per edge, which matters at the millions-of-edges scale a real install hits.
        var order = new uint[edgeRecords.Length];
        for (uint i = 0; i < order.Length; i++)
        {
            order[i] = i;
        }
        Array.Sort(order, (x, y) =>
        {
            EdgeRecord a = edgeRecords[x], b = edgeRecords[y];
            if (a.SourceFile != b.SourceFile) return a.SourceFile.CompareTo(b.SourceFile);
            return FullEdgeOrder(a, b);
        });

        DefRecord[] defRecords = [.. definitions
            .Select(d => new DefRecord
            {
                Id = d.Id,
                DefiningFile = d.DefiningFile,
                SiteKey = d.SiteKey,
                Space = (byte)d.Space,
            })
            .DistinctBy(d => (d.Space, d.Id))
            .OrderBy(d => d.Space).ThenBy(d => d.Id)];

        var nameRecords = new List<NameRecord>(names.Count);
        using var nameBlob = new MemoryStream();
        foreach ((uint hash, string name) in names.OrderBy(kv => kv.Key))
        {
            byte[] nameBytes = Encoding.UTF8.GetBytes(name);
            nameRecords.Add(new NameRecord
            {
                Hash = hash,
                NameOffset = (uint)nameBlob.Length,
                NameLength = (uint)nameBytes.Length,
            });
            nameBlob.Write(nameBytes);
        }

        uint[] files = [.. indexedFiles.Distinct().Order()];

        using var buffer = new MemoryStream();
        using (var writer = new BinaryWriter(buffer, Encoding.UTF8, leaveOpen: true))
        {
            writer.Write(Magic);
            writer.Write(Version);

            BinaryTable.WriteSection(writer, edgeRecords);
            BinaryTable.WriteSection(writer, order);
            BinaryTable.WriteSection(writer, defRecords);
            BinaryTable.WriteSection(writer, CollectionsMarshal.AsSpan(nameRecords));
            BinaryTable.WriteSection(writer, nameBlob.GetBuffer().AsSpan(0, (int)nameBlob.Length));
            BinaryTable.WriteSection(writer, files);
        }
        return buffer.ToArray();
    }

    // ------------------------------------------------------------------ persistence

    /// <summary>
    /// Reads an index in one gulp. Any problem at all - missing, truncated, wrong version, garbage -
    /// yields <see cref="Empty"/> rather than an error: every byte here is re-derivable, so falling
    /// back to "no references known yet" is always correct and never loses user data.
    /// </summary>
    public static ReferenceIndex Load(string path)
    {
        if (!File.Exists(path))
        {
            return Empty;
        }

        try
        {
            return ParseBytes(File.ReadAllBytes(path));
        }
        catch
        {
            return Empty;
        }
    }

    private static ReferenceIndex ParseBytes(byte[] bytes)
    {
        using var stream = new MemoryStream(bytes, writable: false);
        using var reader = new BinaryReader(stream);

        if (reader.ReadUInt32() != Magic || reader.ReadInt32() != Version)
        {
            return Empty;
        }

        var index = new ReferenceIndex { _fileBytes = bytes };

        index._edges = BinaryTable.ReadSection<EdgeRecord>(reader);
        index._bySource = BinaryTable.ReadSection<uint>(reader);
        index._definitions = BinaryTable.ReadSection<DefRecord>(reader);
        index._names = BinaryTable.ReadSection<NameRecord>(reader);
        index._nameBlob = BinaryTable.ReadSection<byte>(reader);
        index._indexedFiles = BinaryTable.ReadSection<uint>(reader);

        return index;
    }

    /// <summary>Writes this index atomically (see <see cref="AtomicFile"/>).</summary>
    public void Save(string path) => AtomicFile.Write(path, _fileBytes);
}
