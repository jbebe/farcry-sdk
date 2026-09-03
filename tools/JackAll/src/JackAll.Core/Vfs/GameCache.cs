using System.Runtime.InteropServices;
using System.Text;
using JackAll.Core.Format;
using JackAll.Core.Format.Fcb;
using JackAll.Core.Naming;

namespace JackAll.Core.Vfs;

/// <summary>
/// The one on-disk cache file: sniffed archive-entry types, decoded `.fcb` fragment structure, and
/// each entry's own content hash.
/// </summary>
/// <remarks>
/// Three answers, one file, because they share the same lifecycle end to end: all three are pure,
/// re-derivable facts about bytes that never change for the life of an install (see the class remarks
/// that used to live on <c>ArchiveTypeCache</c>/<c>FcbStructureCache</c> — a quarter of the game's
/// entries have no recovered filename, so identifying them means ~50,000 random header reads; a `.fcb`
/// that splits needs a full decode to know its pieces; and telling a legacy mod's repacked entry apart
/// from the base game's own means decompressing the vanilla side just to compare, unless that
/// comparison can be made against a hash instead — see <see cref="TryGetContentHash"/>), all three are
/// trusted outright with no invalidation logic if they load without error, and all three are the
/// user's to delete (one file, not three) if the game is reinstalled or patched underneath us.
/// **patch.dat is deliberately not cached** in any section — it's the one archive the tool itself
/// rewrites on every Build &amp; Apply, so sniffing/decoding/hashing it fresh every launch (~216
/// entries) is what lets everything else be cached with no invalidation machinery at all.
///
/// All three sections are laid out so the on-disk bytes double as the runtime lookup structure: the
/// whole file is read once into <see cref="_fileBytes"/> and kept alive, and every record array is a
/// <see cref="MemoryMarshal.Cast{TFrom,TTo}(System.ReadOnlySpan{TFrom})"/> view straight over a slice
/// of it — never copied into a parallel managed structure. Records are sorted by hash, so a lookup is
/// a binary search directly over that span. There is deliberately no step that copies these records
/// into a <c>Dictionary</c> at load: for the ~50,000-record type section, building one would mean
/// rehashing every entry into a different bookkeeping structure on every single launch, for data this
/// class already holds in the exact shape a lookup needs. A small in-memory overlay
/// (<see cref="_newTypes"/>/<see cref="_newFragments"/>/<see cref="_newContentHashes"/>) holds only
/// what got sniffed/decoded/hashed *this* session — the rare, dirty path — and is folded back into the
/// byte-backed arrays only when <see cref="Save"/> runs.
///
/// The `.fcb` section can't use one flat record type the way the type section does — a container's
/// fragment list is variable-length — so it goes one level deeper with the same idea: a sorted
/// <c>ContainerRecord[]</c> (hash -&gt; a range into a flat <c>FragmentRecord[]</c>), which itself
/// points into one shared UTF8 name blob rather than each fragment's id being its own heap string read
/// via <c>BinaryReader.ReadString</c>. A container's fragments are decoded into
/// <see cref="FcbFragmentInfo"/> objects only when actually asked for (<see cref="TryGet"/>), not for
/// the whole file up front.
///
/// The content-hash section is a flat <c>HashRecord[]</c>, same shape as the type section - one fixed
/// 16-byte record per entry, sorted by hash, no variable-length data to point into.
/// </remarks>
public sealed class GameCache
{
    private const uint Magic = 0x3143414A; // 'JAC1'

    // The version invalidates every fragment list recorded before a container learned to split: v3
    // for deep fragment ids (see FcbFragments), v4 for `oasisstrings.rml` and MOVE graphs, which a
    // v3 file would keep answering "doesn't split" for. An old file loaded by this version simply
    // fails the version check below and resets to empty, same as any other unparseable cache;
    // nothing here tries to upgrade one in place.
    private const int Version = 4;

    [StructLayout(LayoutKind.Sequential, Pack = 4)]
    private struct TypeRecord : IKeyedRecord
    {
        public uint Hash;
        public ushort TypeId;
        public ushort Padding;

        public readonly ulong Key => Hash;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 4)]
    private struct ContainerRecord : IKeyedRecord
    {
        public uint Hash;
        public uint FragmentOffset;
        public uint FragmentCount;

        public readonly ulong Key => Hash;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 4)]
    private struct FragmentRecord
    {
        public uint NameOffset;
        public uint NameLength;
        public long Size;
    }

    /// <summary>An entry hash paired with the xxHash64 of that entry's own *decompressed* content -
    /// see <see cref="TryGetContentHash"/>.</summary>
    [StructLayout(LayoutKind.Sequential, Pack = 8)]
    private struct HashRecord : IKeyedRecord
    {
        public uint Hash;
        public uint Padding;
        public ulong ContentHash;

        public readonly ulong Key => Hash;
    }

    /// <summary>The whole file, read once and kept alive — every span below is a zero-copy window
    /// into this buffer. Empty for a cache that was never loaded from (or saved to) disk yet.</summary>
    private byte[] _fileBytes = [];

    private FileType[] _typeTable = [];
    private (int Offset, int Count) _typeRecords;
    private (int Offset, int Count) _containers;
    private (int Offset, int Count) _fragmentRecords;
    private (int Offset, int Count) _nameBlob;
    private (int Offset, int Count) _hashRecords;

    private readonly Dictionary<uint, FileType> _newTypes = [];
    private readonly Dictionary<uint, FcbFragmentInfo[]> _newFragments = [];
    private readonly Dictionary<uint, ulong> _newContentHashes = [];

    /// <summary>True when something was sniffed or decoded afresh this session and the file on disk
    /// is now out of date.</summary>
    public bool IsDirty { get; private set; }

    public int TypeCount => _typeRecords.Count + _newTypes.Count;
    public int FragmentContainerCount => _containers.Count + _newFragments.Count;
    public int ContentHashCount => _hashRecords.Count + _newContentHashes.Count;

    private ReadOnlySpan<TypeRecord> TypeRecordSpan => BinaryTable.RecordSpan<TypeRecord>(_fileBytes, _typeRecords);

    private ReadOnlySpan<ContainerRecord> ContainerSpan => BinaryTable.RecordSpan<ContainerRecord>(_fileBytes, _containers);

    private ReadOnlySpan<FragmentRecord> FragmentRecordSpan => BinaryTable.RecordSpan<FragmentRecord>(_fileBytes, _fragmentRecords);

    private ReadOnlySpan<byte> NameBlobSpan => _fileBytes.AsSpan(_nameBlob.Offset, _nameBlob.Count);

    private ReadOnlySpan<HashRecord> HashRecordSpan => BinaryTable.RecordSpan<HashRecord>(_fileBytes, _hashRecords);

    // ------------------------------------------------------------------ type lookups

    /// <summary>The query half of a split sniff: whether <paramref name="hash"/>'s type is already
    /// known, without sniffing it if not. Lets a caller (<see cref="Vfs.GameVfs.BuildMergedFiles"/>)
    /// find exactly which entries still need the expensive part before doing any of it, the same way
    /// <see cref="TryGet"/> already does for `.fcb` structure.</summary>
    public bool TryGetType(uint hash, out FileType type)
        => TryGetCachedType(hash, out type) || _newTypes.TryGetValue(hash, out type);

    /// <summary>The write half of a split sniff: records a type sniffed elsewhere (typically off-thread,
    /// in parallel) — mirrors <see cref="Set"/> on the fragment side. Not thread-safe, same as every
    /// other mutator here; the caller is responsible for only ever calling it from one thread at a
    /// time (see <see cref="Vfs.GameVfs.BuildMergedFiles"/>'s parallel-sniff/serial-fold split).</summary>
    public void SetType(uint hash, FileType type)
    {
        _newTypes[hash] = type;
        IsDirty = true;
    }

    private bool TryGetCachedType(uint hash, out FileType type)
    {
        ReadOnlySpan<TypeRecord> records = TypeRecordSpan;
        int found = BinaryTable.Find(records, hash);
        if (found < 0)
        {
            type = default;
            return false;
        }
        type = _typeTable[records[found].TypeId];
        return true;
    }

    /// <summary>Reads an entry's header without consulting or touching the cache.</summary>
    public static FileType Sniff(DuniaArchive archive, FatEntry entry)
    {
        try
        {
            return FileTypeSniffer.IdentifyByContent(archive.ReadHeader(entry, FileTypeSniffer.HeaderBytes));
        }
        catch
        {
            // An unreadable entry is still an entry — call it unknown rather than failing the load.
            return FileType.Unknown;
        }
    }

    // ------------------------------------------------------------------ fcb structure lookups

    public bool TryGet(uint hash, out IReadOnlyList<FcbFragmentInfo> fragments)
    {
        if (_newFragments.TryGetValue(hash, out FcbFragmentInfo[]? added))
        {
            fragments = added;
            return true;
        }

        ReadOnlySpan<ContainerRecord> containers = ContainerSpan;
        int found = BinaryTable.Find(containers, hash);
        if (found < 0)
        {
            fragments = [];
            return false;
        }
        fragments = Decode(containers[found]);
        return true;
    }

    /// <summary>Records the answer for a hash — including an empty list, since "doesn't split" is
    /// just as worth remembering as a fragment list is.</summary>
    public void Set(uint hash, IReadOnlyList<FcbFragmentInfo> fragments)
    {
        _newFragments[hash] = [.. fragments];
        IsDirty = true;
    }

    private FcbFragmentInfo[] Decode(ContainerRecord container)
    {
        ReadOnlySpan<FragmentRecord> records = FragmentRecordSpan;
        ReadOnlySpan<byte> nameBlob = NameBlobSpan;
        var result = new FcbFragmentInfo[container.FragmentCount];
        for (int i = 0; i < result.Length; i++)
        {
            FragmentRecord record = records[(int)container.FragmentOffset + i];
            string name = Encoding.UTF8.GetString(nameBlob.Slice((int)record.NameOffset, (int)record.NameLength));
            result[i] = new FcbFragmentInfo(name, record.Size);
        }
        return result;
    }

    // ------------------------------------------------------------------ content hash lookups

    /// <summary>
    /// The xxHash64 of an entry's own decompressed content, keyed by the same entry hash every other
    /// section uses. Content hashes never mix across archives - it's the caller's job to decide which
    /// entry (typically <see cref="Vfs.GameVfs.ReadOriginal"/>'s answer for a given hash) this is a
    /// hash *of*; this class only ever remembers what it's told.
    /// </summary>
    public bool TryGetContentHash(uint hash, out ulong contentHash)
        => TryGetCachedContentHash(hash, out contentHash) || _newContentHashes.TryGetValue(hash, out contentHash);

    /// <summary>Records a hash computed elsewhere. Not thread-safe, same as every other mutator here -
    /// see <see cref="SetType"/>'s remarks for the parallel-compute/serial-fold split this expects.</summary>
    public void SetContentHash(uint hash, ulong contentHash)
    {
        _newContentHashes[hash] = contentHash;
        IsDirty = true;
    }

    private bool TryGetCachedContentHash(uint hash, out ulong contentHash)
    {
        ReadOnlySpan<HashRecord> records = HashRecordSpan;
        int found = BinaryTable.Find(records, hash);
        if (found < 0)
        {
            contentHash = default;
            return false;
        }
        contentHash = records[found].ContentHash;
        return true;
    }

    // ------------------------------------------------------------------ persistence

    /// <summary>
    /// Reads the cache in one gulp. Any problem at all — missing, truncated, wrong version, garbage
    /// — yields an empty cache rather than an error, because every byte of this file is a pure
    /// optimisation and re-deriving it is always correct.
    /// </summary>
    public static GameCache Load(string path)
    {
        var cache = new GameCache();
        if (!File.Exists(path))
        {
            return cache;
        }

        try
        {
            cache.LoadFrom(File.ReadAllBytes(path));
            return cache;
        }
        catch
        {
            return new GameCache();
        }
    }

    /// <summary>Shared by <see cref="Load"/> and <see cref="Save"/> — a cache freshly written by this
    /// process reads back exactly the way one loaded from disk would. Wrong magic/version leaves the
    /// instance empty, same as any other unparseable file.</summary>
    private void LoadFrom(byte[] bytes)
    {
        using var stream = new MemoryStream(bytes, writable: false);
        using var reader = new BinaryReader(stream);

        if (reader.ReadUInt32() != Magic || reader.ReadInt32() != Version)
        {
            return;
        }

        _fileBytes = bytes;

        // There are only ~20 distinct (category, extension) pairs in the whole game, so they're
        // interned into a table and each record carries a 2-byte id instead of two strings.
        int typeTableCount = reader.ReadInt32();
        var typeTable = new FileType[typeTableCount];
        for (int i = 0; i < typeTableCount; i++)
        {
            typeTable[i] = new FileType(reader.ReadString(), reader.ReadString());
        }
        _typeTable = typeTable;

        _typeRecords = BinaryTable.ReadSection<TypeRecord>(reader);
        _containers = BinaryTable.ReadSection<ContainerRecord>(reader);
        _fragmentRecords = BinaryTable.ReadSection<FragmentRecord>(reader);
        _nameBlob = BinaryTable.ReadSection<byte>(reader);
        _hashRecords = BinaryTable.ReadSection<HashRecord>(reader);
    }

    /// <summary>
    /// Writes the cache atomically (see <see cref="AtomicFile"/>). The freshly written bytes become
    /// this instance's new backing store (see <see cref="LoadFrom"/>), so a second <c>Save</c>
    /// later in the same session starts from what's actually on disk rather than re-decoding it.
    /// </summary>
    public void Save(string path)
    {
        // ---- type section: merge what was already on disk with what got sniffed this session ----
        var allTypes = new Dictionary<uint, FileType>(TypeCount);
        foreach (TypeRecord record in TypeRecordSpan)
        {
            allTypes[record.Hash] = _typeTable[record.TypeId];
        }
        foreach ((uint hash, FileType type) in _newTypes)
        {
            allTypes[hash] = type;
        }

        var typeIds = new Dictionary<FileType, ushort>();
        var newTypeRecords = new TypeRecord[allTypes.Count];
        int t = 0;
        foreach ((uint hash, FileType type) in allTypes.OrderBy(kv => kv.Key))
        {
            if (!typeIds.TryGetValue(type, out ushort id))
            {
                id = (ushort)typeIds.Count;
                typeIds[type] = id;
            }
            newTypeRecords[t++] = new TypeRecord { Hash = hash, TypeId = id };
        }
        FileType[] newTypeTable = [.. typeIds.OrderBy(kv => kv.Value).Select(kv => kv.Key)];

        // ---- fcb section: same merge, decoding whatever was already on disk back into fragment lists ----
        var allFragments = new Dictionary<uint, FcbFragmentInfo[]>(FragmentContainerCount);
        foreach (ContainerRecord container in ContainerSpan)
        {
            allFragments[container.Hash] = Decode(container);
        }
        foreach ((uint hash, FcbFragmentInfo[] fragments) in _newFragments)
        {
            allFragments[hash] = fragments;
        }

        var newContainers = new ContainerRecord[allFragments.Count];
        var newFragmentRecords = new List<FragmentRecord>();
        using var nameBlobStream = new MemoryStream();
        int c = 0;
        foreach ((uint hash, FcbFragmentInfo[] fragments) in allFragments.OrderBy(kv => kv.Key))
        {
            uint offset = (uint)newFragmentRecords.Count;
            foreach (FcbFragmentInfo fragment in fragments)
            {
                byte[] nameBytes = Encoding.UTF8.GetBytes(fragment.Id);
                newFragmentRecords.Add(new FragmentRecord
                {
                    NameOffset = (uint)nameBlobStream.Length,
                    NameLength = (uint)nameBytes.Length,
                    Size = fragment.Size,
                });
                nameBlobStream.Write(nameBytes);
            }
            newContainers[c++] = new ContainerRecord
            {
                Hash = hash,
                FragmentOffset = offset,
                FragmentCount = (uint)fragments.Length,
            };
        }

        // ---- content hash section: same merge, flat records like the type section ----
        var allHashes = new Dictionary<uint, ulong>(ContentHashCount);
        foreach (HashRecord record in HashRecordSpan)
        {
            allHashes[record.Hash] = record.ContentHash;
        }
        foreach ((uint hash, ulong contentHash) in _newContentHashes)
        {
            allHashes[hash] = contentHash;
        }
        HashRecord[] newHashRecords = [.. allHashes
            .OrderBy(kv => kv.Key)
            .Select(kv => new HashRecord { Hash = kv.Key, ContentHash = kv.Value })];

        byte[] fileBytes;
        using (var buffer = new MemoryStream())
        {
            using (var writer = new BinaryWriter(buffer, Encoding.UTF8, leaveOpen: true))
            {
                writer.Write(Magic);
                writer.Write(Version);

                writer.Write(newTypeTable.Length);
                foreach (FileType type in newTypeTable)
                {
                    writer.Write(type.Category);
                    writer.Write(type.Extension);
                }
                BinaryTable.WriteSection(writer, newTypeRecords);

                BinaryTable.WriteSection(writer, newContainers);
                BinaryTable.WriteSection(writer, CollectionsMarshal.AsSpan(newFragmentRecords));
                BinaryTable.WriteSection(writer, nameBlobStream.GetBuffer().AsSpan(0, (int)nameBlobStream.Length));

                BinaryTable.WriteSection(writer, newHashRecords);
            }
            fileBytes = buffer.ToArray();
        }

        AtomicFile.Write(path, fileBytes);

        LoadFrom(fileBytes);
        _newTypes.Clear();
        _newFragments.Clear();
        _newContentHashes.Clear();

        IsDirty = false;
    }
}
