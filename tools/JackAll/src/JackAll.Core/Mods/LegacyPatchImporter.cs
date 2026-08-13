using System.IO.Compression;
using System.IO.Hashing;
using System.Text;
using JackAll.Core.Format;
using JackAll.Core.Format.Fcb;
using JackAll.Core.Naming;

namespace JackAll.Core.Mods;

public sealed record LegacyImportResult(int TotalEntries, int Imported, int FragmentsImported, int Skipped);

/// <summary>
/// Converts a legacy community mod - one built the old way as a full replacement patch.dat/patch.fat
/// meant to be dropped straight into Data_Win32 - into <see cref="FolderModLayer"/>'s own staging
/// format.
/// </summary>
/// <remarks>
/// Almost everything a legacy patch.dat carries is the base game's own untouched bytes: the old
/// build_patch.bat-style workflow repacks the *whole* archive, not just what the mod actually changed.
/// Staging all of it verbatim would bury a mod's real edits in ~200,000 entries of pure noise, so every
/// entry is diffed against the true vanilla original (<paramref name="readOriginal"/> in
/// <see cref="Import"/> - typically <see cref="Vfs.GameVfs.ReadOriginal"/>, ignoring every currently
/// active mod/workspace edit) and only genuine differences are staged:
///
///   - An entity-library-shaped `.fcb` (see <see cref="FcbXml.ToXml"/>) is split and compared one
///     group at a time, so touching a single entity stages one small fragment override instead of the
///     whole multi-hundred-KB container - the same <c>&lt;container&gt;.fcb\NN_Name.xml</c> shape
///     <see cref="ModPathHashing"/> already recognizes as a fragment override (matching what
///     JackAll.App's own structured fragment editor stages).
///   - Any other `.fcb` is compared by its *decoded* shape (<see cref="FcbXml.RenderObject"/>) rather
///     than raw bytes, since this writer (like most community tools) never reproduces the shipped
///     files' backreference/dedup encoding (see <see cref="FcbDocument"/>'s remarks) - a logically
///     identical container can still differ byte-for-byte for reasons that have nothing to do with the
///     mod. A real change still has to stage real `.fcb` bytes (there's no plain-text staged form for
///     a whole-container replacement - see JackAll.App's own Import button, which round-trips through
///     <see cref="FcbDocument.Serialize"/> for exactly this reason), so the original legacy bytes are
///     staged unchanged, not a re-render.
///   - Everything else is a plain byte-for-byte comparison.
/// </remarks>
public static class LegacyPatchImporter
{
    /// <summary>
    /// Extracts the patch.fat/patch.dat pair from <paramref name="zipPath"/>, diffs every entry against
    /// <paramref name="readOriginal"/>, and stages whatever differs into <paramref name="workspace"/>.
    /// Throws <see cref="InvalidDataException"/> if the zip doesn't contain a patch.fat/patch.dat pair
    /// at all - the signal that this isn't a legacy full-patch mod (an ordinary community mod zip, a
    /// plain tree of relative game paths, belongs in <c>ZipModLayer</c>/"Add mod zip…" instead).
    /// </summary>
    public static LegacyImportResult Import(
        string zipPath,
        FolderModLayer workspace,
        NameDatabase names,
        FcbClassDefinitions fcbDefinitions,
        Func<uint, byte[]?> readOriginal,
        Func<uint, ulong?> readOriginalHash,
        IProgress<string>? progress = null)
    {
        string tempDir = Path.Combine(Path.GetTempPath(), "jackall-legacy-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        try
        {
            string tempFat = Path.Combine(tempDir, "patch.fat");
            string tempDat = Path.Combine(tempDir, "patch.dat");
            using (var zip = ZipFile.OpenRead(zipPath))
            {
                (ZipArchiveEntry fatEntry, ZipArchiveEntry datEntry) = FindPatchEntries(zip);
                fatEntry.ExtractToFile(tempFat, overwrite: true);
                datEntry.ExtractToFile(tempDat, overwrite: true);
            }

            return Import(tempFat, tempDat, workspace, names, fcbDefinitions, readOriginal, readOriginalHash, progress);
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    /// <summary>The same import against a folder: finds the patch.fat/patch.dat pair anywhere under
    /// <paramref name="directory"/>, throwing when there is none — the signal that this isn't a
    /// legacy full-patch mod at all.</summary>
    public static LegacyImportResult ImportFromDirectory(
        string directory,
        FolderModLayer workspace,
        NameDatabase names,
        FcbClassDefinitions fcbDefinitions,
        Func<uint, byte[]?> readOriginal,
        Func<uint, ulong?> readOriginalHash,
        IProgress<string>? progress = null)
    {
        (string Fat, string Dat) pair = FindPatchPair(directory)
            ?? throw new InvalidOperationException(
                $"No patch.fat/patch.dat pair under '{directory}' - this isn't a legacy full-patch mod. "
                + "An ordinary community mod (a tree of relative game paths) is used as a layer directly.");

        return Import(pair.Fat, pair.Dat, workspace, names, fcbDefinitions, readOriginal, readOriginalHash, progress);
    }

    /// <summary>
    /// The same import, against a patch.fat/patch.dat pair already sitting on disk rather than one
    /// still inside a zip.
    /// </summary>
    /// <remarks>
    /// This is the real body; the zip overload is a thin unpack-then-call wrapper. It exists as its
    /// own entry point because not every caller starts from a zip: a mod manager driving JackAll
    /// (the Vortex extension calls this through <c>jackall-cli mod import-legacy</c>) is handed an
    /// archive someone else has already extracted, and re-zipping it just to hand it back would be
    /// pure ceremony.
    /// </remarks>
    public static LegacyImportResult Import(
        string fatPath,
        string datPath,
        FolderModLayer workspace,
        NameDatabase names,
        FcbClassDefinitions fcbDefinitions,
        Func<uint, byte[]?> readOriginal,
        Func<uint, ulong?> readOriginalHash,
        IProgress<string>? progress = null)
    {
        using DuniaArchive legacy = DuniaArchive.Open(fatPath, datPath);

        int imported = 0, fragmentsImported = 0, skipped = 0, processed = 0;
        foreach (FatEntry entry in legacy.Entries)
        {
            processed++;
            if (processed % 2_000 == 0)
            {
                progress?.Report($"Comparing against the base game… ({processed:N0} / {legacy.Entries.Count:N0})");
            }

            byte[] legacyBytes = legacy.Read(entry);
            ulong legacyHash = XxHash64.HashToUInt64(legacyBytes);
            ulong? vanillaHash = readOriginalHash(entry.Hash);

            // A hash match is exactly as conclusive as the byte match it replaces (see
            // GameCache.TryGetContentHash's remarks on trusting a 64-bit content hash outright), .fcb
            // included - decoding both sides to compare can only confirm what this already knows for
            // free. Crucially, unlike a byte compare, this never has to decompress the vanilla side to
            // reach that conclusion: readOriginalHash answers from GameCache's persisted table when
            // warm, so this is what makes a second mod install against the same game cheaper than the
            // first, not just this one. This is *not* the same shortcut the class remarks warn against:
            // that's about a byte *mismatch* not implying a real change (a legacy tool can re-encode
            // `.fcb` content losing the original's dedup, without the modder having touched it), which
            // this doesn't touch at all - a mismatch still falls through to the full decoded comparison
            // below, exactly as before, now paying for the vanilla decompress only when it's actually
            // needed.
            if (vanillaHash == legacyHash)
            {
                skipped++;
                continue;
            }

            byte[]? vanillaBytes = vanillaHash is not null ? readOriginal(entry.Hash) : null;

            bool named = names.TryResolve(entry.Hash, out string path);

            FileType type = named
                ? FileTypeSniffer.Identify(ReadOnlySpan<byte>.Empty, path)
                : FileTypeSniffer.IdentifyByContent(
                    legacyBytes.AsSpan(0, Math.Min(legacyBytes.Length, FileTypeSniffer.HeaderBytes)));

            if (type.Extension.Equals("fcb", StringComparison.OrdinalIgnoreCase)
                && TryImportFcb(entry.Hash, legacyBytes, vanillaBytes, named ? path : null,
                    workspace, fcbDefinitions, ref imported, ref fragmentsImported, ref skipped))
            {
                continue;
            }

            workspace.Stage(entry.Hash, named ? path : null, type.Extension, legacyBytes);
            imported++;
        }

        return new LegacyImportResult(legacy.Entries.Count, imported, fragmentsImported, skipped);
    }

    /// <summary>
    /// Finds the patch.fat/patch.dat pair inside an already-extracted mod folder, the directory
    /// counterpart of <see cref="FindPatchEntries"/>. Returns null when there isn't one — the signal
    /// that this is an ordinary path-tree mod rather than a legacy full patch.
    /// </summary>
    public static (string Fat, string Dat)? FindPatchPair(string directory)
    {
        if (!Directory.Exists(directory))
        {
            return null;
        }

        foreach (string fat in Directory.EnumerateFiles(directory, "patch.fat", SearchOption.AllDirectories))
        {
            string dat = Path.Combine(Path.GetDirectoryName(fat)!, "patch.dat");
            if (File.Exists(dat))
            {
                return (fat, dat);
            }
        }
        return null;
    }

    /// <summary>
    /// Handles one entry once it's already known to be named/sniffed as `.fcb` - decodes it and either
    /// splits it into per-group fragment overrides or stages/skips it whole, per the class remarks.
    /// Returns false, staging nothing, when the content isn't actually a well-formed .fcb despite its
    /// extension (not every entry named or sniffed "fcb" necessarily is one - e.g. a "BASE"-magic
    /// container sniffs to the unrelated .wlu.fcb extension) - the caller falls back to the plain
    /// whole-file byte comparison every other entry goes through.
    /// </summary>
    private static bool TryImportFcb(
        uint hash, byte[] legacyBytes, byte[]? vanillaBytes, string? containerPath,
        FolderModLayer workspace, FcbClassDefinitions defs,
        ref int imported, ref int fragmentsImported, ref int skipped)
    {
        FcbObject legacyRoot;
        try
        {
            legacyRoot = FcbDocument.Deserialize(legacyBytes);
        }
        catch (Exception ex) when (ex is InvalidDataException or EndOfStreamException)
        {
            return false;
        }

        FcbObject? vanillaRoot = null;
        if (vanillaBytes is not null)
        {
            try
            {
                vanillaRoot = FcbDocument.Deserialize(vanillaBytes);
            }
            catch (Exception ex) when (ex is InvalidDataException or EndOfStreamException)
            {
                // No decodable vanilla ancestor - every comparison below just treats this as new.
            }
        }

        FcbXmlExport export = FcbXml.ToXml(legacyRoot, defs);
        if (export.ExternalFiles.Count == 0)
        {
            bool unchanged = vanillaRoot is not null
                && FcbXml.RenderObject(legacyRoot, defs) == FcbXml.RenderObject(vanillaRoot, defs);
            if (unchanged)
            {
                skipped++;
            }
            else
            {
                workspace.Stage(hash, containerPath, "fcb", legacyBytes);
                imported++;
            }
            return true;
        }

        string containerRelative = containerPath ?? $"_hash\\{hash:x8}.fcb";
        foreach ((string fragmentId, string xml) in export.ExternalFiles)
        {
            string? vanillaXml = vanillaRoot is not null ? FcbXml.ExtractFragment(vanillaRoot, fragmentId, defs) : null;
            if (vanillaXml == xml)
            {
                skipped++;
                continue;
            }

            workspace.Stage(hash, $"{containerRelative}\\{fragmentId}", "xml", new UTF8Encoding(false).GetBytes(xml));
            fragmentsImported++;
        }

        return true;
    }

    /// <summary>
    /// The patch.fat/patch.dat pair's entry names inside <paramref name="zipPath"/>, or null when
    /// there isn't one — the non-throwing counterpart of <see cref="FindPatchEntries"/>, for callers
    /// asking "which kind of mod is this?" rather than committing to an import.
    /// </summary>
    public static (string Fat, string Dat)? FindPatchPairInZip(string zipPath)
    {
        using var zip = ZipFile.OpenRead(zipPath);
        (ZipArchiveEntry? fat, ZipArchiveEntry? dat) = LocatePatchEntries(zip);
        return fat is not null && dat is not null ? (fat.FullName, dat.FullName) : null;
    }

    private static (ZipArchiveEntry Fat, ZipArchiveEntry Dat) FindPatchEntries(ZipArchive zip)
    {
        (ZipArchiveEntry? fat, ZipArchiveEntry? dat) = LocatePatchEntries(zip);
        if (fat is null)
        {
            throw new InvalidDataException(
                "No patch.fat found in this zip - this doesn't look like a legacy full-patch mod. " +
                "Use \"Add mod zip…\" instead for an ordinary community mod (a tree of relative game paths).");
        }
        if (dat is null)
        {
            throw new InvalidDataException($"Found '{fat.FullName}' in the zip but no matching patch.dat alongside it.");
        }

        return (fat, dat);
    }

    /// <summary>The pair if both are there, whichever half was found if not — the caller decides
    /// whether a half-match is an error or just a "no".</summary>
    private static (ZipArchiveEntry? Fat, ZipArchiveEntry? Dat) LocatePatchEntries(ZipArchive zip)
    {
        ZipArchiveEntry? fat = zip.Entries.FirstOrDefault(
            e => string.Equals(Path.GetFileName(e.FullName), "patch.fat", StringComparison.OrdinalIgnoreCase));
        if (fat is null)
        {
            return (null, null);
        }

        string? dir = Path.GetDirectoryName(fat.FullName);
        ZipArchiveEntry? dat = zip.Entries.FirstOrDefault(e =>
            string.Equals(Path.GetFileName(e.FullName), "patch.dat", StringComparison.OrdinalIgnoreCase)
            && string.Equals(Path.GetDirectoryName(e.FullName), dir, StringComparison.OrdinalIgnoreCase));

        return (fat, dat);
    }
}
