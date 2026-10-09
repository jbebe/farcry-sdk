using System.Text;
using JackAll.Core.Format.Fcb;

namespace JackAll.Tools.Sav;

/// <summary>
/// One parsed Far Cry 2 .sav file's wrapper metadata: world/player name, embedded thumbnail, active
/// DLC ids, and the object count of the persisted-entity `.fcb` blob that makes up the bulk of the
/// file. Deliberately does not decode that `.fcb` blob itself (see <see cref="FcbBlobOffset"/>) — a
/// real save commonly holds tens of thousands of objects in it, far more than a save browser needs to
/// read just to list a file. A caller that wants the full entity tree can seek to
/// <see cref="FcbBlobOffset"/> and hand the rest of the stream to <see cref="Fcb.FcbDocument"/>.
/// </summary>
/// <remarks>
/// Layout: docs/docs/file-formats/savegame.md.
/// </remarks>
public sealed class SaveGameInfo
{
    public required string FilePath { get; init; }
    public required string WorldName { get; init; }
    public required string PlayerName { get; init; }

    /// <summary>The campaign act, 1 to 3.</summary>
    public required uint Act { get; init; }

    /// <summary>Campaign completion, 0 to 100.</summary>
    public required uint CompletionPercent { get; init; }

    /// <summary>The difficulty, 0 (Easy) to 3 (Infamous).</summary>
    public required uint Difficulty { get; init; }

    public string DifficultyName => Tools.Difficulty.NameOf(Difficulty);

    /// <summary>Thumbnail dimensions in pixels.</summary>
    public required int ThumbnailWidth { get; init; }
    public required int ThumbnailHeight { get; init; }

    /// <summary>Raw pixel bytes: BGRA, 4 bytes/pixel, tightly packed rows, top row first.</summary>
    public required byte[] ThumbnailPixels { get; init; }

    public required IReadOnlyList<string> ActiveDlcIds { get; init; }

    /// <summary>
    /// The `objectCount` header field of this save's embedded PersistenceDB `.fcb` dump — a
    /// rough proxy for "how much of the game world this save has permanently recorded state for", not
    /// a precise one. Entities never persisted here still spawn fresh from the game's *current*
    /// entitylibrary.fcb every time it's loaded; entities that ARE counted here keep whatever specific
    /// properties were captured about them at save time regardless of later `.fcb` edits — see
    /// savegame_format.md's "Validated: mod compatibility with existing saves..." section for the
    /// full reasoning (traced directly from `CPersistenceDB::RestoreEntity`).
    /// </summary>
    public required uint PersistedObjectCount { get; init; }

    /// <summary>Byte offset of the embedded `.fcb` blob within the file.</summary>
    public required long FcbBlobOffset { get; init; }
}

/// <summary>Reads the wrapper metadata (everything except the bulk entity-persistence tree) out of a
/// Far Cry 2 `.sav` file, and writes an edited entity tree back into one. See <see cref="SaveGameInfo"/>'s
/// remarks for where the format is derived from.</summary>
public static class SaveGameDocument
{
    private const uint FcbMagic = 0x4643626E; // "FCbn", little-endian — Fcb_MagicConstant()

    // The engine rejects a base header with any other version or type.
    private const uint Version = 10;
    private static readonly uint CampaignGameFileType = FcbClassDefinitions.Crc32Ascii("CCampaignGameFile");

    public static SaveGameInfo Read(string path)
    {
        using FileStream stream = File.OpenRead(path);
        return Read(stream, path);
    }

    /// <summary>
    /// Reads and deserializes the embedded PersistenceDB `.fcb` blob at <see cref="SaveGameInfo.FcbBlobOffset"/>
    /// — a separate, opt-in call from <see cref="Read(string)"/>: a real save's tree commonly holds
    /// tens of thousands of objects, far more than a caller that only wants the wrapper metadata (e.g.
    /// a save browser's list) should pay for.
    /// </summary>
    public static FcbObject ReadFcbRoot(SaveGameInfo info)
    {
        using FileStream stream = File.OpenRead(info.FilePath);
        stream.Seek(info.FcbBlobOffset, SeekOrigin.Begin);

        byte[] blob = new byte[stream.Length - info.FcbBlobOffset];
        int totalRead = 0;
        while (totalRead < blob.Length)
        {
            int read = stream.Read(blob, totalRead, blob.Length - totalRead);
            if (read == 0)
            {
                throw new InvalidDataException($"'{info.FilePath}': truncated while reading the embedded .fcb blob.");
            }
            totalRead += read;
        }

        return FcbDocument.Deserialize(blob);
    }

    /// <summary>
    /// Writes <paramref name="info"/>'s wrapper bytes plus <paramref name="root"/> to
    /// <paramref name="destPath"/>, which may be <see cref="SaveGameInfo.FilePath"/> to edit in place or
    /// any other path to leave the source untouched. The bytes before
    /// <see cref="SaveGameInfo.FcbBlobOffset"/> (header, thumbnail, DLC list) are copied through
    /// untouched. Overwrites <paramref name="destPath"/> if it exists - the caller decides whether that
    /// is acceptable.
    /// </summary>
    /// <remarks>
    /// The embedded blob is a plain `.fcb`, so <see cref="FcbDocument.Serialize"/> writes it, header
    /// counts included.
    /// </remarks>
    public static void WriteFcbRoot(SaveGameInfo info, FcbObject root, string destPath)
    {
        byte[] wrapper = ReadWrapperPrefix(info);
        byte[] blob = FcbDocument.Serialize(root);

        string tempPath = destPath + ".tmp";
        using (FileStream output = File.Create(tempPath))
        {
            output.Write(wrapper);
            output.Write(blob);
        }
        File.Move(tempPath, destPath, overwrite: true);
    }

    private static byte[] ReadWrapperPrefix(SaveGameInfo info)
    {
        using FileStream stream = File.OpenRead(info.FilePath);
        byte[] wrapper = new byte[info.FcbBlobOffset];
        int totalRead = 0;
        while (totalRead < wrapper.Length)
        {
            int read = stream.Read(wrapper, totalRead, wrapper.Length - totalRead);
            if (read == 0)
            {
                throw new InvalidDataException($"'{info.FilePath}': truncated while reading the header before the embedded .fcb blob.");
            }
            totalRead += read;
        }
        return wrapper;
    }

    public static SaveGameInfo Read(Stream stream, string path)
    {
        using var reader = new BinaryReader(stream, Encoding.UTF8, leaveOpen: true);

        // Section 1 — CGameFileHeader base: version, type, then the player's position (3 floats).
        uint version = reader.ReadUInt32();
        uint type = reader.ReadUInt32();
        if (version != Version || type != CampaignGameFileType)
        {
            throw new InvalidDataException(
                $"'{path}' is not a campaign save (version {version}, type 0x{type:x8}).");
        }
        ReadExactly(reader, 12, path, "the player position");

        // Section 2 — CCampaignGameFileHeader extension.
        string worldName = ReadLengthPrefixedString(reader, path);
        string playerName = ReadLengthPrefixedString(reader, path);
        uint act = reader.ReadUInt32();
        uint completion = reader.ReadUInt32();
        uint difficulty = reader.ReadUInt32();

        // Section 3 — CScreenShot (thumbnail).
        int width = checked((int)reader.ReadUInt32());
        int height = checked((int)reader.ReadUInt32());
        uint channels = reader.ReadUInt32();
        uint bitsPerChannel = reader.ReadUInt32();
        long pixelByteCount = (long)width * height * channels * bitsPerChannel / 8;
        byte[] pixels = ReadExactly(reader, pixelByteCount, path, "the thumbnail pixel data");

        // Screenshot metadata: u32 key, then a length-prefixed string, per entry.
        uint metadataCount = reader.ReadUInt32();
        for (uint i = 0; i < metadataCount; i++)
        {
            reader.ReadUInt32();
            ReadLengthPrefixedString(reader, path);
        }

        // Section 4 — CCampaignGameFileData: DLC list, the gamer-profile value, then the embedded .fcb blob.
        uint dlcCount = reader.ReadUInt32();
        var dlcIds = new List<string>((int)Math.Min(dlcCount, 64));
        for (uint i = 0; i < dlcCount; i++)
        {
            dlcIds.Add(ReadLengthPrefixedString(reader, path));
        }
        reader.ReadUInt32();

        long fcbOffset = reader.BaseStream.Position;
        uint magic = reader.ReadUInt32();
        if (magic != FcbMagic)
        {
            throw new InvalidDataException(
                $"'{path}': expected an embedded .fcb blob (magic 'FCbn') right after the DLC list, found none.");
        }
        reader.ReadUInt16(); // version — always 2, not checked here; Fcb.FcbDocument validates it if the caller decodes the blob
        reader.ReadUInt16(); // flags
        uint objectCount = reader.ReadUInt32();

        return new SaveGameInfo
        {
            FilePath = path,
            WorldName = worldName,
            PlayerName = playerName,
            Act = act,
            CompletionPercent = completion,
            Difficulty = difficulty,
            ThumbnailWidth = width,
            ThumbnailHeight = height,
            ThumbnailPixels = pixels,
            ActiveDlcIds = dlcIds,
            PersistedObjectCount = objectCount,
            FcbBlobOffset = fcbOffset,
        };
    }

    /// <summary>u32 length prefix + raw bytes, no null terminator — the top-level wrapper's own string
    /// encoding (distinct from the null-terminated strings found inside the embedded `.fcb` blob's
    /// values, which go through <see cref="Fcb.FcbDocument"/> instead).</summary>
    private static string ReadLengthPrefixedString(BinaryReader reader, string path)
    {
        uint length = reader.ReadUInt32();
        byte[] bytes = ReadExactly(reader, length, path, "a length-prefixed string");
        return Encoding.UTF8.GetString(bytes);
    }


    private static byte[] ReadExactly(BinaryReader reader, long count, string path, string what)
    {
        byte[] bytes = reader.ReadBytes(checked((int)count));
        if (bytes.Length != count)
        {
            throw new InvalidDataException($"'{path}': truncated while reading {what}.");
        }
        return bytes;
    }
}
