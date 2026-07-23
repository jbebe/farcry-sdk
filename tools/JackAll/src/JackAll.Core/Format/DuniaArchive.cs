namespace JackAll.Core.Format;

/// <summary>
/// A mounted .fat/.dat pair, opened read-only. Entry data is read on demand — the .dat files run to
/// gigabytes and are never loaded whole.
/// </summary>
public sealed class DuniaArchive : IDisposable
{
    private readonly FileStream _data;
    private readonly FatArchive _index;
    private readonly Dictionary<uint, FatEntry> _byHash;
    private readonly Lock _readLock = new();

    /// <summary>Archive name as the user sees it, e.g. "worlds" or "patch".</summary>
    public string Name { get; }
    public string FatPath { get; }

    public IReadOnlyList<FatEntry> Entries => _index.Entries;

    private DuniaArchive(string name, string fatPath, FatArchive index, FileStream data)
    {
        Name = name;
        FatPath = fatPath;
        _index = index;
        _data = data;

        // Duplicate hashes within one archive would mean the engine's binary search is ambiguous;
        // it doesn't happen in shipped data, but last-wins keeps us from throwing on a weird file.
        _byHash = new Dictionary<uint, FatEntry>(index.Entries.Count);
        foreach (var entry in index.Entries)
        {
            _byHash[entry.Hash] = entry;
        }
    }

    /// <summary>Opens the .fat at <paramref name="fatPath"/> and its sibling .dat, found by swapping
    /// the extension - the normal case for every archive except a `.vanilla` backup pair, whose
    /// double extension (<c>patch.fat.vanilla</c>/<c>patch.dat.vanilla</c>) that swap can't handle;
    /// use <see cref="Open(string, string)"/> for those instead.</summary>
    public static DuniaArchive Open(string fatPath)
    {
        string datPath = Path.ChangeExtension(fatPath, ".dat");
        if (!File.Exists(datPath))
        {
            throw new FileNotFoundException(
                $"'{Path.GetFileName(fatPath)}' has no sibling .dat - the index is useless without it.",
                datPath);
        }

        return Open(fatPath, datPath);
    }

    /// <summary>Opens an explicit .fat/.dat pair, bypassing <see cref="Open(string)"/>'s
    /// extension-swap convention.</summary>
    public static DuniaArchive Open(string fatPath, string datPath)
    {
        var index = FatArchive.Read(fatPath);
        // Read-only, shared, and deletable out from under us: the game may well be running while the
        // user browses, and PatchBuilder.Build replaces patch.dat/.fat in place while this same file
        // can legitimately still be mounted here (JackAll.App keeps its GameVfs open for the whole
        // session) - without FileShare.Delete, Windows refuses that replace with an access-denied
        // error as long as this handle stays open.
        //
        // FileShare.Delete alone is *not* enough to keep using this handle afterward, though - it
        // only avoids the access-denied error on the replace itself. Confirmed empirically: once
        // another file has been moved into this handle's path, further reads through this handle
        // throw EndOfStreamException (Length still reports the old size, but the bytes are gone) -
        // unlike a plain delete-with-no-replacement, where the old handle keeps reading fine. Whoever
        // mounted this archive and might outlive a PatchBuilder.Build against the same install.PatchFat
        // MUST re-open it afterward - see GameVfs.ReloadPatchArchive, which JackAll.App's
        // MainViewModel.BuildPatch calls right after a successful build for exactly this reason.
        var data = new FileStream(
            datPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, bufferSize: 64 * 1024);

        return new DuniaArchive(
            Path.GetFileNameWithoutExtension(fatPath), fatPath, index, data);
    }

    public bool TryGetEntry(uint hash, out FatEntry entry) => _byHash.TryGetValue(hash, out entry);

    public bool Contains(uint hash) => _byHash.ContainsKey(hash);

    /// <summary>Reads and decompresses one entry.</summary>
    public byte[] Read(FatEntry entry)
    {
        byte[] stored = ReadStored(entry);

        return entry.Compression switch
        {
            CompressionScheme.None => stored,
            CompressionScheme.Lzo1x => Lzo1x.Decompress(stored, entry.UncompressedSize),
            CompressionScheme.Zlib => DecompressZlib(stored, entry.UncompressedSize),
            _ => throw new NotSupportedException($"Unknown compression scheme {entry.Compression}."),
        };
    }

    public byte[] Read(uint hash)
        => TryGetEntry(hash, out var entry)
            ? Read(entry)
            : throw new KeyNotFoundException($"Archive '{Name}' has no entry {hash:X8}.");

    /// <summary>
    /// The entry's bytes exactly as they sit in the .dat, compression untouched. This is what lets
    /// the builder copy vanilla entries through without ever needing a compressor.
    /// </summary>
    public byte[] ReadStored(FatEntry entry)
    {
        var buffer = new byte[entry.StoredSize];
        lock (_readLock)
        {
            _data.Seek(entry.Offset, SeekOrigin.Begin);
            _data.ReadExactly(buffer);
        }
        return buffer;
    }

    /// <summary>Reads just the first bytes of an entry, for magic-number type sniffing.</summary>
    public byte[] ReadHeader(FatEntry entry, int count)
    {
        // Compressed entries have to be fully decompressed to see their header; they're small
        // enough individually that this is fine, and callers cache the result.
        if (entry.Compression != CompressionScheme.None)
        {
            byte[] full = Read(entry);
            return full.Length <= count ? full : full[..count];
        }

        int toRead = Math.Min(count, entry.StoredSize);
        var buffer = new byte[toRead];
        lock (_readLock)
        {
            _data.Seek(entry.Offset, SeekOrigin.Begin);
            _data.ReadExactly(buffer);
        }
        return buffer;
    }

    private static byte[] DecompressZlib(byte[] stored, int expectedSize)
    {
        using var source = new MemoryStream(stored);
        using var zlib = new System.IO.Compression.ZLibStream(
            source, System.IO.Compression.CompressionMode.Decompress);
        var output = new byte[expectedSize];
        zlib.ReadExactly(output);
        return output;
    }

    public void Dispose() => _data.Dispose();
}
