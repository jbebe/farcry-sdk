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

    // v2 adds the content-hash section (see HashRecord) - a v1 file loaded by this version simply
    // fails the version check below and resets to empty, same as any other unparseable cache; nothing
    // here tries to upgrade one in place.
    private const int Version = 2;

    [StructLayout(LayoutKind.Sequential, Pack = 4)]
    private struct TypeRecord
    {
        public uint Hash;
        public ushort TypeId;
        public ushort Padding;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 4)]
    private struct ContainerRecord
    {
        public uint Hash;
        public uint FragmentOffset;
        public uint FragmentCount;
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
    private struct HashRecord
    {
        public uint Hash;
        public uint Padding;
        public ulong ContentHash;
    }

    private static readonly int TypeRecordSize = Marshal.SizeOf<TypeRecord>();
    private static readonly int ContainerRecordSize = Marshal.SizeOf<ContainerRecord>();
    private static readonly int FragmentRecordSize = Marshal.SizeOf<FragmentRecord>();
    private static readonly int HashRecordSize = Marshal.SizeOf<HashRecord>();

    /// <summary>The whole file, read once and kept alive — every span below is a zero-copy window
    /// into this buffer. Empty for a cache that was never loaded from (or saved to) disk yet.</summary>
    private byte[] _fileBytes = [];

    private FileType[] _typeTable = [];
    private (int Offset, int Count) _typeRecords;      // byte offset into _fileBytes, record count
    private (int Offset, int Count) _containers;        // byte offset, record count
    private (int Offset, int Count) _fragmentRecords;   // byte offset, record count
    private (int Offset, int Length) _nameBlob;          // byte offset, byte length
    private (int Offset, int Count) _hashRecords;        // byte offset, record count

    private readonly Dictionary<uint, FileType> _newTypes = [];
    private readonly Dictionary<uint, FcbFragmentInfo[]> _newFragments = [];
    private readonly Dictionary<uint, ulong> _newContentHashes = [];

    /// <summary>True when something was sniffed or decoded afresh this session and the file on disk
    /// is now out of date.</summary>
    public bool IsDirty { get; private set; }

    public int TypeCount => _typeRecords.Count + _newTypes.Count;
    public int FragmentContainerCount => _containers.Count + _newFragments.Count;
    public int ContentHashCount => _hashRecords.Count + _newContentHashes.Count;

    private ReadOnlySpan<TypeRecord> TypeRecordSpan => _typeRecords.Count == 0
        ? default
        : MemoryMarshal.Cast<byte, TypeRecord>(_fileBytes.AsSpan(_typeRecords.Offset, _typeRecords.Count * TypeRecordSize));

    private ReadOnlySpan<ContainerRecord> ContainerSpan => _containers.Count == 0
        ? default
        : MemoryMarshal.Cast<byte, ContainerRecord>(_fileBytes.AsSpan(_containers.Offset, _containers.Count * ContainerRecordSize));

    private ReadOnlySpan<FragmentRecord> FragmentRecordSpan => _fragmentRecords.Count == 0
        ? default
        : MemoryMarshal.Cast<byte, FragmentRecord>(_fileBytes.AsSpan(_fragmentRecords.Offset, _fragmentRecords.Count * FragmentRecordSize));

    private ReadOnlySpan<byte> NameBlobSpan => _nameBlob.Length == 0
        ? default
        : _fileBytes.AsSpan(_nameBlob.Offset, _nameBlob.Length);

    private ReadOnlySpan<HashRecord> HashRecordSpan => _hashRecords.Count == 0
        ? default
        : MemoryMarshal.Cast<byte, HashRecord>(_fileBytes.AsSpan(_hashRecords.Offset, _hashRecords.Count * HashRecordSize));

    // ------------------------------------------------------------------ type lookups

    /// <summary>The entry's type — from the cache, or by reading its header and remembering it.</summary>
    public FileType TypeOf(DuniaArchive archive, FatEntry entry)
    {
        if (TryGetType(entry.Hash, out FileType cached))
        {
            return cached;
        }

        FileType sniffed = Sniff(archive, entry);
        SetType(entry.Hash, sniffed);
        return sniffed;
    }

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
        int lo = 0, hi = records.Length - 1;
        while (lo <= hi)
        {
            int mid = (lo + hi) / 2;
            uint midHash = records[mid].Hash;
            if (midHash == hash)
            {
                type = _typeTable[records[mid].TypeId];
                return true;
            }
            if (midHash < hash) lo = mid + 1; else hi = mid - 1;
        }
        type = default;
        return false;
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
        int lo = 0, hi = containers.Length - 1;
        while (lo <= hi)
        {
            int mid = (lo + hi) / 2;
            uint midHash = containers[mid].Hash;
            if (midHash == hash)
            {
                fragments = Decode(containers[mid]);
                return true;
            }
            if (midHash < hash) lo = mid + 1; else hi = mid - 1;
        }

        fragments = [];
        return false;
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
        int lo = 0, hi = records.Length - 1;
        while (lo <= hi)
        {
            int mid = (lo + hi) / 2;
            uint midHash = records[mid].Hash;
            if (midHash == hash)
            {
                contentHash = records[mid].ContentHash;
                return true;
            }
            if (midHash < hash) lo = mid + 1; else hi = mid - 1;
        }
        contentHash = default;
        return false;
    }

    // ------------------------------------------------------------------ persistence

    /// <summary>
    /// Reads the cache in one gulp. Any problem at all — missing, truncated, wrong version, garbage
    /// — yields an empty cache rather than an error, because every byte of this file is a pure
    /// optimisation and re-deriving it is always correct.
    /// </summary>
    public static GameCache Load(string path)
    {
        if (!File.Exists(path))
        {
            return new GameCache();
        }

        try
        {
            return ParseBytes(File.ReadAllBytes(path));
        }
        catch
        {
            return new GameCache();
        }
    }

    /// <summary>Shared by <see cref="Load"/> and <see cref="Save"/> — a cache freshly written by this
    /// process reads back exactly the way one loaded from disk would.</summary>
    private static GameCache ParseBytes(byte[] bytes)
    {
        using var stream = new MemoryStream(bytes, writable: false);
        using var reader = new BinaryReader(stream);

        if (reader.ReadUInt32() != Magic || reader.ReadInt32() != Version)
        {
            return new GameCache();
        }

        var cache = new GameCache { _fileBytes = bytes };

        // ---- type section ----
        // There are only ~20 distinct (category, extension) pairs in the whole game, so they're
        // interned into a table and each record carries a 2-byte id instead of two strings.
        int typeTableCount = reader.ReadInt32();
        var typeTable = new FileType[typeTableCount];
        for (int i = 0; i < typeTableCount; i++)
        {
            typeTable[i] = new FileType(reader.ReadString(), reader.ReadString());
        }
        cache._typeTable = typeTable;

        int typeRecordCount = reader.ReadInt32();
        cache._typeRecords = ((int)stream.Position, typeRecordCount);
        stream.Position += typeRecordCount * TypeRecordSize;

        // ---- fcb structure section ----
        int containerCount = reader.ReadInt32();
        cache._containers = ((int)stream.Position, containerCount);
        stream.Position += containerCount * ContainerRecordSize;

        int fragmentRecordCount = reader.ReadInt32();
        cache._fragmentRecords = ((int)stream.Position, fragmentRecordCount);
        stream.Position += fragmentRecordCount * FragmentRecordSize;

        int nameBlobLength = reader.ReadInt32();
        cache._nameBlob = ((int)stream.Position, nameBlobLength);
        stream.Position += nameBlobLength;

        // ---- content hash section ----
        int hashRecordCount = reader.ReadInt32();
        cache._hashRecords = ((int)stream.Position, hashRecordCount);
        stream.Position += hashRecordCount * HashRecordSize;

        return cache;
    }

    /// <summary>
    /// Writes the cache via a temp file and an atomic swap, so a crash mid-write can't leave half a
    /// cache behind — which would otherwise be read back as garbage on the next launch. The freshly
    /// written bytes become this instance's new backing store (see <see cref="ParseBytes"/>), so a
    /// second <c>Save</c> later in the same session starts from what's actually on disk rather than
    /// re-decoding it.
    /// </summary>
    public void Save(string path)
    {
        // ---- type section: merge what was already on disk with what got sniffed this session ----
        var allTypes = new Dictionary<uint, FileType>(_typeRecords.Count + _newTypes.Count);
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
        var allFragments = new Dictionary<uint, FcbFragmentInfo[]>(_containers.Count + _newFragments.Count);
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
        var allHashes = new Dictionary<uint, ulong>(_hashRecords.Count + _newContentHashes.Count);
        foreach (HashRecord record in HashRecordSpan)
        {
            allHashes[record.Hash] = record.ContentHash;
        }
        foreach ((uint hash, ulong contentHash) in _newContentHashes)
        {
            allHashes[hash] = contentHash;
        }
        var newHashRecords = new HashRecord[allHashes.Count];
        int h = 0;
        foreach ((uint hash, ulong contentHash) in allHashes.OrderBy(kv => kv.Key))
        {
            newHashRecords[h++] = new HashRecord { Hash = hash, ContentHash = contentHash };
        }

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
                writer.Write(newTypeRecords.Length);
                writer.Write(MemoryMarshal.AsBytes<TypeRecord>(newTypeRecords));

                writer.Write(newContainers.Length);
                writer.Write(MemoryMarshal.AsBytes<ContainerRecord>(newContainers));
                writer.Write(newFragmentRecords.Count);
                writer.Write(MemoryMarshal.AsBytes<FragmentRecord>(CollectionsMarshal.AsSpan(newFragmentRecords)));
                writer.Write((int)nameBlobStream.Length);
                nameBlobStream.Position = 0;
                nameBlobStream.CopyTo(buffer);

                writer.Write(newHashRecords.Length);
                writer.Write(MemoryMarshal.AsBytes<HashRecord>(newHashRecords));
            }
            fileBytes = buffer.ToArray();
        }

        string temp = path + ".writing";
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(temp, fileBytes);
        File.Move(temp, path, overwrite: true);

        GameCache reparsed = ParseBytes(fileBytes);
        _fileBytes = reparsed._fileBytes;
        _typeTable = reparsed._typeTable;
        _typeRecords = reparsed._typeRecords;
        _containers = reparsed._containers;
        _fragmentRecords = reparsed._fragmentRecords;
        _nameBlob = reparsed._nameBlob;
        _hashRecords = reparsed._hashRecords;
        _newTypes.Clear();
        _newFragments.Clear();
        _newContentHashes.Clear();

        IsDirty = false;
    }
}
