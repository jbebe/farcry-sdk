namespace JackAll.Core.Format;

public enum CompressionScheme
{
    None = 0,
    Lzo1x = 1,
    Zlib = 2,
}

/// <summary>One entry in a .fat index: where a file lives inside the sibling .dat.</summary>
public readonly record struct FatEntry(
    uint Hash,
    long Offset,
    int CompressedSize,
    int UncompressedSize,
    CompressionScheme Compression)
{
    /// <summary>
    /// Bytes actually occupied in the .dat. With <see cref="CompressionScheme.None"/> the engine
    /// stores the real length in CompressedSize and leaves UncompressedSize at 0, so this is the
    /// only field that is meaningful in both cases.
    /// </summary>
    public int StoredSize => CompressedSize;

    /// <summary>Size after decompression — what the caller ultimately gets.</summary>
    public int RealSize => Compression == CompressionScheme.None ? CompressedSize : UncompressedSize;
}

/// <summary>
/// Reader/writer for the Dunia "FAT2" version-5 archive index used by Far Cry 2.
///
/// Header (little-endian):
///   0  u32  magic 'FAT2' (0x46415432)
///   4  u32  version (5)
///   8  u32  flags: platform in byte 0 (3 = Windows), compression version in byte 1
///   12 s32  entry count
///   16 ..   entry table, 16 bytes each, sorted ascending by hash (the engine binary-searches it)
///   ..  u32 localization count (0 for FC2)
///
/// Entry, 4 x u32 bit-packed:
///   a: hash (32)
///   b: uncompressedSize (30) &lt;&lt; 2 | compressionScheme (2)
///   c: compressedSize (30) | offset_low2 (2) &lt;&lt; 30
///   d: offset >> 2                                  (34-bit offset overall)
/// </summary>
public sealed class FatArchive
{
    public const uint Magic = 0x46415432; // 'FAT2'
    public const int SupportedVersion = 5;

    public uint Flags { get; private init; }
    public IReadOnlyList<FatEntry> Entries { get; private init; } = [];

    /// <summary>Windows platform (3), compression version 0 — what FC2's own archives carry.</summary>
    public const uint DefaultWindowsFlags = 3;

    public static FatArchive Read(Stream input)
    {
        using var reader = new BinaryReader(input, System.Text.Encoding.ASCII, leaveOpen: true);

        uint magic = reader.ReadUInt32();
        if (magic != Magic)
        {
            throw new InvalidDataException(
                $"Not a FAT2 archive index (magic 0x{magic:X8}, expected 0x{Magic:X8}).");
        }

        uint version = reader.ReadUInt32();
        if (version != SupportedVersion)
        {
            throw new InvalidDataException(
                $"Unsupported .fat version {version}; only Far Cry 2's version {SupportedVersion} is supported.");
        }

        uint flags = reader.ReadUInt32();
        int entryCount = reader.ReadInt32();
        if (entryCount < 0)
        {
            throw new InvalidDataException($"Negative entry count ({entryCount}).");
        }

        var entries = new List<FatEntry>(entryCount);
        for (int i = 0; i < entryCount; i++)
        {
            uint a = reader.ReadUInt32();
            uint b = reader.ReadUInt32();
            uint c = reader.ReadUInt32();
            uint d = reader.ReadUInt32();

            entries.Add(new FatEntry(
                Hash: a,
                Offset: (long)(((c >> 30) & 0x3u) | ((ulong)d << 2)),
                CompressedSize: (int)(c & 0x3FFFFFFFu),
                UncompressedSize: (int)((b >> 2) & 0x3FFFFFFFu),
                Compression: (CompressionScheme)(b & 0x3u)));
        }

        // FC2 ships none of these, but the field is present and must be consumed to stay aligned.
        uint localizationCount = reader.ReadUInt32();
        if (localizationCount != 0)
        {
            throw new NotSupportedException(
                $"Archive declares {localizationCount} localization entries; unseen in FC2 and unhandled.");
        }

        return new FatArchive { Flags = flags, Entries = entries };
    }

    public static FatArchive Read(string path)
    {
        using var stream = File.OpenRead(path);
        return Read(stream);
    }

    public static FatArchive FromEntries(IEnumerable<FatEntry> entries, uint flags = DefaultWindowsFlags)
        => new()
        {
            Flags = flags,
            // The engine binary-searches the table, so hash order is a correctness requirement,
            // not a tidiness one.
            Entries = entries.OrderBy(e => e.Hash).ToList(),
        };

    public void Write(Stream output)
    {
        using var writer = new BinaryWriter(output, System.Text.Encoding.ASCII, leaveOpen: true);

        writer.Write(Magic);
        writer.Write((uint)SupportedVersion);
        writer.Write(Flags);
        writer.Write(Entries.Count);

        uint previousHash = 0;
        bool first = true;
        foreach (var entry in Entries)
        {
            if (!first && entry.Hash < previousHash)
            {
                throw new InvalidOperationException(
                    "Entries must be sorted ascending by hash - the engine binary-searches the table. " +
                    "Build the archive via FromEntries(), which sorts for you.");
            }
            previousHash = entry.Hash;
            first = false;

            Validate(entry);

            uint a = entry.Hash;
            uint b = ((uint)entry.UncompressedSize & 0x3FFFFFFFu) << 2
                     | ((uint)entry.Compression & 0x3u);
            uint c = ((uint)entry.CompressedSize & 0x3FFFFFFFu)
                     | ((uint)(entry.Offset & 0x3) << 30);
            uint d = (uint)(entry.Offset >> 2);

            writer.Write(a);
            writer.Write(b);
            writer.Write(c);
            writer.Write(d);
        }

        writer.Write(0u); // localization count
    }

    public void Write(string path)
    {
        using var stream = File.Create(path);
        Write(stream);
    }

    /// <summary>
    /// The engine's own invariants. Violating these produces an archive that reads back wrong
    /// rather than one that fails loudly, so we check on write.
    /// </summary>
    private static void Validate(FatEntry entry)
    {
        if (entry.Compression == CompressionScheme.None && entry.UncompressedSize != 0)
        {
            throw new InvalidOperationException(
                $"Entry {entry.Hash:X8}: uncompressed entries must carry their length in " +
                "CompressedSize and leave UncompressedSize at 0.");
        }
        if (entry.Compression != CompressionScheme.None && entry.CompressedSize == 0 && entry.UncompressedSize > 0)
        {
            throw new InvalidOperationException(
                $"Entry {entry.Hash:X8}: compressed entry with a zero compressed size.");
        }
        if (entry.Offset < 0 || entry.Offset > (1L << 34) - 1)
        {
            throw new InvalidOperationException(
                $"Entry {entry.Hash:X8}: offset {entry.Offset} does not fit the 34-bit offset field.");
        }
        if ((uint)entry.CompressedSize > 0x3FFFFFFFu || (uint)entry.UncompressedSize > 0x3FFFFFFFu)
        {
            throw new InvalidOperationException($"Entry {entry.Hash:X8}: size does not fit its 30-bit field.");
        }
    }
}
