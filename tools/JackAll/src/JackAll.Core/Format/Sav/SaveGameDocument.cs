using System.Text;
using JackAll.Core.Format.Fcb;

namespace JackAll.Core.Format.Sav;

/// <summary>
/// One parsed Far Cry 2 .sav file's wrapper metadata: world/player name, embedded thumbnail, active
/// DLC ids, and the object count of the persisted-entity `.fcb` blob that makes up the bulk of the
/// file. Deliberately does not decode that `.fcb` blob itself (see <see cref="FcbBlobOffset"/>) — a
/// real save commonly holds tens of thousands of objects in it, far more than a save browser needs to
/// read just to list a file. A caller that wants the full entity tree can seek to
/// <see cref="FcbBlobOffset"/> and hand the rest of the stream to <see cref="Fcb.FcbDocument"/>.
/// </summary>
/// <remarks>
/// Field layout confirmed byte-for-byte against a real save file via GhidraMCP, decompiling
/// `FarCry2_server`'s (the Linux dedicated-server binary, see reverse/dunia/overview.md) unstripped
/// `CGameFileHeader`/`CCampaignGameFileHeader`/`CScreenShot`/`CCampaignGameFileData` classes — see
/// reverse/dunia/savegame_format.md for the full derivation, including which fields below are still
/// unconfirmed (flagged in that doc; not re-litigated in code comments here beyond a pointer).
/// </remarks>
public sealed class SaveGameInfo
{
    public required string FilePath { get; init; }
    public required string WorldName { get; init; }
    public required string PlayerName { get; init; }

    /// <summary>Thumbnail dimensions in pixels.</summary>
    public required int ThumbnailWidth { get; init; }
    public required int ThumbnailHeight { get; init; }

    /// <summary>
    /// Raw pixel bytes, 4 bytes/pixel, tightly packed rows, top-to-bottom order not independently
    /// confirmed either — the actual channel order (RGBA vs BGRA) wasn't pinned down (see
    /// savegame_format.md, Section 3); callers currently assume BGRA, the more common convention for
    /// this engine generation's raw pixel dumps.
    /// </summary>
    public required byte[] ThumbnailPixels { get; init; }

    public required IReadOnlyList<string> ActiveDlcIds { get; init; }

    /// <summary>
    /// The `totalObjectCount` header field of this save's embedded PersistenceDB `.fcb` dump — a
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
    /// Writes <paramref name="root"/> back into <paramref name="info"/>'s own `.sav` file, replacing
    /// the embedded `.fcb` blob in place - the wrapper bytes before <see cref="SaveGameInfo.FcbBlobOffset"/>
    /// (header, thumbnail, DLC list) are copied through untouched, since nothing here needs to
    /// understand them, only preserve them. Writes to a temp file first and renames it over the
    /// original (not a safety net for a bad edit - the caller decides that - just so an interrupted
    /// write can't leave a half-written `.sav` on disk).
    /// </summary>
    /// <remarks>
    /// <see cref="FcbDocument.Serialize"/> is a generic, already-production-proven `.fcb` writer (see
    /// its own remarks) - reused as-is here, no savegame-specific binary work needed, since the embedded
    /// blob is a plain, ordinary `.fcb` (see reverse/dunia/savegame_format.md). Its two header count
    /// fields (`totalObjectCount`/`totalValueCount`) won't necessarily match what the original save's
    /// own writer put there - both are read-side "informational only" per <see cref="FcbDocument"/>'s
    /// own remarks (confirmed against the real engine's header reader, not just this reader), so a
    /// mismatch there is expected and harmless, not a sign of corruption.
    /// </remarks>
    public static void WriteFcbRoot(SaveGameInfo info, FcbObject root)
    {
        byte[] wrapper = ReadWrapperPrefix(info);
        byte[] blob = FcbDocument.Serialize(root);

        string tempPath = info.FilePath + ".tmp";
        using (FileStream output = File.Create(tempPath))
        {
            output.Write(wrapper);
            output.Write(blob);
        }
        File.Move(tempPath, info.FilePath, overwrite: true);
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

        // Section 1 — CGameFileHeader base: fixed 20 bytes. Field semantics (plausibly a save-type
        // tag plus the player's world position) aren't confirmed well enough to expose individually —
        // see savegame_format.md Section 1 — so this just skips past them.
        RequireBytes(reader, 20, path, "the base header");

        // Section 2 — CCampaignGameFileHeader extension.
        string worldName = ReadLengthPrefixedString(reader, path);
        string playerName = ReadLengthPrefixedString(reader, path);
        RequireBytes(reader, 12, path, "the header's trailing fields"); // 3 unconfirmed u32s

        // Section 3 — CScreenShot (thumbnail).
        int width = checked((int)reader.ReadUInt32());
        int height = checked((int)reader.ReadUInt32());
        uint channels = reader.ReadUInt32();
        uint bitsPerChannel = reader.ReadUInt32();
        long pixelByteCount = (long)width * height * channels * bitsPerChannel / 8;
        byte[] pixels = ReadExactly(reader, pixelByteCount, path, "the thumbnail pixel data");

        uint metadataCount = reader.ReadUInt32();
        if (metadataCount != 0)
        {
            // ScreenShot::WriteMetaDataInfoToFile's per-entry format was never traced (no real save
            // sample seen had a nonzero count) — rather than guess and silently misparse everything
            // after it, refuse outright instead of returning wrong data.
            throw new NotSupportedException(
                $"'{path}' has {metadataCount} screenshot metadata entries — that per-entry format " +
                "isn't understood yet, so this save can't be parsed past its thumbnail.");
        }

        // Section 4 — CCampaignGameFileData: DLC list, then the embedded .fcb blob.
        uint dlcCount = reader.ReadUInt32();
        var dlcIds = new List<string>((int)Math.Min(dlcCount, 64));
        for (uint i = 0; i < dlcCount; i++)
        {
            dlcIds.Add(ReadLengthPrefixedString(reader, path));
        }
        RequireBytes(reader, 4, path, "the field before the embedded .fcb blob"); // unconfirmed, see savegame_format.md

        long fcbOffset = reader.BaseStream.Position;
        uint magic = reader.ReadUInt32();
        if (magic != FcbMagic)
        {
            throw new InvalidDataException(
                $"'{path}': expected an embedded .fcb blob (magic 'FCbn') right after the DLC list, found none.");
        }
        reader.ReadUInt16(); // version — always 2, not checked here; Fcb.FcbDocument validates it if the caller decodes the blob
        reader.ReadUInt16(); // flags
        uint totalObjectCount = reader.ReadUInt32();

        return new SaveGameInfo
        {
            FilePath = path,
            WorldName = worldName,
            PlayerName = playerName,
            ThumbnailWidth = width,
            ThumbnailHeight = height,
            ThumbnailPixels = pixels,
            ActiveDlcIds = dlcIds,
            PersistedObjectCount = totalObjectCount,
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

    private static void RequireBytes(BinaryReader reader, int count, string path, string what)
        => ReadExactly(reader, count, path, what);

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
