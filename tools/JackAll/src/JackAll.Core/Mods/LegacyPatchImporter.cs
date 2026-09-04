using System.Globalization;
using System.IO.Compression;
using System.IO.Hashing;
using System.Text;
using System.Xml.Linq;
using JackAll.Core.Format;
using JackAll.Core.Format.Fcb;
using JackAll.Core.Format.Move;
using JackAll.Core.Format.Rml;
using JackAll.Core.Naming;

namespace JackAll.Core.Mods;

/// <summary>A container whose change per-fragment overrides could not express, and why.</summary>
public sealed record LegacyImportNote(string ContainerPath, string Reason);

/// <param name="Refused">Containers left out entirely - a whole-file override of one would do more
/// harm than dropping it.</param>
/// <param name="WholeFile">Containers staged whole instead. Not a failure, but it costs the mod its
/// per-fragment merging, so it is reported rather than passed over in silence.</param>
/// <param name="Unreachable">Content the mod shipped that the import did not carry across, because
/// the engine cannot reach it either. Reported for the same reason as the above: the layer is not a
/// byte-for-byte copy of the mod, and where it differs it should say so.</param>
public sealed record LegacyImportResult(
    int TotalEntries,
    int Imported,
    int FragmentsImported,
    int Skipped,
    IReadOnlyList<LegacyImportNote> Refused,
    IReadOnlyList<LegacyImportNote> WholeFile,
    IReadOnlyList<LegacyImportNote> Unreachable)
{
    /// <summary>Value equality all the way down: the synthesized comparison would take the two lists
    /// by reference, so two identical imports would never compare equal.</summary>
    public bool Equals(LegacyImportResult? other)
        => other is not null
           && (TotalEntries, Imported, FragmentsImported, Skipped)
              == (other.TotalEntries, other.Imported, other.FragmentsImported, other.Skipped)
           && Refused.SequenceEqual(other.Refused)
           && WholeFile.SequenceEqual(other.WholeFile)
           && Unreachable.SequenceEqual(other.Unreachable);

    public override int GetHashCode()
        => HashCode.Combine(
            TotalEntries, Imported, FragmentsImported, Skipped,
            Refused.Count, WholeFile.Count, Unreachable.Count);
}

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
///   - A container that splits (see <see cref="IContainerSplitter"/>: one fragment per archetype in
///     an entity library, per placed entity in a worldsector, per resource in a `depload`, per state
///     in a MOVE graph) is compared one fragment at a time, so touching a single weapon or a single
///     animation stages one small override instead of the whole container - the same
///     <c>&lt;container&gt;\&lt;fragment id&gt;</c> shape <see cref="ModPathHashing"/> already
///     recognizes. That granularity only holds when everything *outside* the fragments is untouched
///     and nothing was deleted or moved - changes a per-fragment override cannot express - so those
///     cases fall back to the only coarser unit there is, the whole file. A MOVE graph is the
///     exception: a whole-file override of one is last-wins against every other animation mod, so it
///     is refused and reported instead.
///   - A container that doesn't split is compared by its *decoded* shape rather than raw bytes, since
///     this writer (like most community tools) never reproduces the shipped files'
///     backreference/dedup encoding (see <see cref="FcbDocument"/>'s remarks) - a logically identical
///     container can still differ byte-for-byte for reasons that have nothing to do with the mod. A
///     real change still has to stage real binary (there's no plain-text staged form for a
///     whole-container replacement), so the original legacy bytes are staged unchanged.
///   - The localized string table is its own case: its override unit is a patch document rather than
///     a fragment path, and a whole-file override of it is refused outright.
///   - Everything else is a plain byte-for-byte comparison.
/// </remarks>
public static class LegacyPatchImporter
{
    /// <summary>Staged text is BOM-less, and an import writes one file per changed fragment - tens of
    /// thousands of them for a large mod.</summary>
    private static readonly UTF8Encoding Utf8 = new(encoderShouldEmitUTF8Identifier: false);

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
        List<LegacyImportNote> refused = [], wholeFile = [], unreachable = [];
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

            // The string table is checked first because it is the one container whose override unit
            // is not a fragment path, so the shared path below could never express it.
            if (named && ContainerFormats.IsStringTable(Path.GetFileName(path)))
            {
                if (TryImportStringTable(legacyBytes, vanillaBytes, path,
                        workspace, ref fragmentsImported, ref skipped))
                {
                    continue;
                }
            }
            else if (TryImportContainer(entry.Hash, legacyBytes, vanillaBytes, named ? path : null, type,
                         workspace, names, fcbDefinitions, refused, wholeFile, unreachable, ref fragmentsImported,
                         ref skipped, progress))
            {
                continue;
            }

            workspace.Stage(entry.Hash, named ? path : null, type.Extension, legacyBytes);
            imported++;
        }

        return new LegacyImportResult(
            legacy.Entries.Count, imported, fragmentsImported, skipped, refused, wholeFile, unreachable);
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
    /// Imports one splitting container as the fragments that differ from vanilla. Returns false,
    /// staging nothing, when the entry names no container this can address, or when the change is one
    /// fragments cannot express and the format still accepts a whole-file override - in both cases
    /// the caller's plain whole-file path takes over, and the second case is recorded in
    /// <paramref name="wholeFile"/> on the way past. A MOVE graph is the exception: it is refused
    /// and reported rather than coarsened, per the class remarks.
    /// </summary>
    private static bool TryImportContainer(
        uint hash, byte[] legacyBytes, byte[]? vanillaBytes, string? knownPath, FileType type,
        FolderModLayer workspace, NameDatabase names, FcbClassDefinitions defs,
        List<LegacyImportNote> refused, List<LegacyImportNote> wholeFile,
        List<LegacyImportNote> unreachable, ref int fragmentsImported,
        ref int skipped, IProgress<string>? progress)
    {
        if (ContainerPathFor(knownPath, type, hash) is not { } containerPath)
        {
            return false;
        }

        IContainerSplitter splitter = ContainerFormats.For(containerPath, defs, names);
        string? refusal = StageFragments(
            splitter, legacyBytes, vanillaBytes, containerPath, hash, workspace,
            unreachable, ref fragmentsImported, ref skipped);
        if (refusal is null)
        {
            return true;
        }

        if (!ContainerFormats.IsMoveGraph(Path.GetFileName(containerPath)))
        {
            wholeFile.Add(new LegacyImportNote(containerPath, refusal));
            progress?.Report($"Staged {containerPath} whole: {refusal}.");
            return false;
        }

        refused.Add(new LegacyImportNote(containerPath, refusal));
        progress?.Report($"Left out {containerPath}: {refusal}.");
        return true;
    }

    /// <summary>
    /// The path a container's fragments are staged under, or null when this entry names no container.
    /// An entry the hashlist couldn't name is only ever a `.fcb`: recognition reads the path, and a
    /// hash-addressed override has none to read.
    /// </summary>
    private static string? ContainerPathFor(string? knownPath, FileType type, uint hash)
    {
        if (knownPath is not null)
        {
            return ContainerFormats.IsContainerSegment(Path.GetFileName(knownPath)) ? knownPath : null;
        }

        return type.Extension.Equals("fcb", StringComparison.OrdinalIgnoreCase)
            ? $"_hash\\{hash:x8}.fcb"
            : null;
    }

    /// <summary>
    /// Diffs a container one fragment at a time and stages only the changed (or added) ones,
    /// returning null once it has. Anything else is why this granularity can't faithfully represent
    /// the change, in words a modder can act on.
    /// </summary>
    /// <remarks>
    /// A container that splits into nothing still resolves here: with no fragments to elide, its
    /// skeleton is the whole decoded container, so comparing the two answers "did anything really
    /// change?" for a format whose writer doesn't reproduce the shipped dedup encoding.
    /// </remarks>
    private static string? StageFragments(
        IContainerSplitter splitter, byte[] legacyBytes, byte[]? vanillaBytes, string containerPath,
        uint hash, FolderModLayer workspace, List<LegacyImportNote> unreachable,
        ref int fragmentsImported, ref int skipped)
    {
        if (vanillaBytes is null)
        {
            return "the base game has no copy of it to compare against";
        }

        IContainerTree legacy, vanilla;
        try
        {
            legacy = splitter.Open(legacyBytes);
            vanilla = splitter.Open(vanillaBytes);
        }
        catch (Exception ex) when (ex is InvalidDataException or EndOfStreamException or MoveFormatException)
        {
            return $"it did not decode ({ex.Message})";
        }

        IReadOnlyList<FcbFragmentInfo> rows = legacy.List();
        var legacyIds = new HashSet<string>(rows.Select(r => r.Id), FcbFragments.IdComparer);
        IReadOnlyList<FcbFragmentInfo> vanillaRows = vanilla.List();
        var vanillaIds = new HashSet<string>(vanillaRows.Select(r => r.Id), FcbFragments.IdComparer);
        if (legacyIds.Count != rows.Count || vanillaIds.Count != vanillaRows.Count)
        {
            return "two of its fragments share one id, so an override could not say which it meant";
        }

        if (legacy.Skeleton(vanillaIds.Contains) is not { } legacyShape
            || vanilla.Skeleton(vanillaIds.Contains) is not { } vanillaShape)
        {
            return "this format is not compared by shape";
        }

        // A shape difference is a reason to verify, not to refuse. Entities re-filed or removed need a
        // structural override to carry them; content merely added - a library group and the archetypes
        // in it - the fragments express on their own. Either way the reassembly check below is what
        // decides, since it compares what the staged set actually rebuilds.
        bool shapeDiffers = legacyShape != vanillaShape;
        (string Id, string Xml)? structural = shapeDiffers ? legacy.StructuralOverride(vanilla) : null;

        // Nothing is written until every fragment has been read, so a refusal found part way through
        // leaves the caller free to stage the whole file instead of on top of half a fragment set.
        List<(string Id, string Xml)> changed = [];
        int unchanged = 0;
        foreach (FcbFragmentInfo row in rows)
        {
            // A listed fragment that will not extract means the splitter disagrees with itself, and
            // going on would drop whatever the mod put there without saying so.
            if (legacy.Extract(row.Id) is not { } xml)
            {
                return $"it lists a fragment, {row.Id}, that it will not then hand over";
            }

            if (vanilla.Extract(row.Id) is { } vanillaXml)
            {
                if (vanillaXml == xml)
                {
                    unchanged++;
                    continue;
                }

                // Parsed once and handed to both walks: the comparison decides whether this is a real
                // edit, and the restore then strips the editor's rounding off everything else it
                // carries, which would otherwise overwrite vanilla's own values on build.
                if (TryParse(vanillaXml) is { } original && TryParse(xml) is { } modded)
                {
                    if (SameElement(original, modded))
                    {
                        unchanged++;
                        continue;
                    }

                    if (RestoreNoisyFloats(original, modded))
                    {
                        xml = splitter.Canonicalize(row.Id, modded.ToString());
                    }
                }
            }

            changed.Add((row.Id, xml));
        }

        if (structural is { } extra)
        {
            changed.Add(extra);
        }

        // Everything above is a diff of the two containers; this is the check that the diff actually
        // rebuilds the mod's container. Without it a sector that was also reordered, or changed in
        // some way outside its fragments, would import as a quietly wrong one.
        if (shapeDiffers && Reassemble(splitter, vanillaBytes, changed, vanillaIds) != legacyShape)
        {
            return DescribeMovedFragments(legacy, vanilla, vanillaIds) is { } what
                ? $"{what}, and something else about it changed too"
                : vanillaIds.IsSubsetOf(legacyIds)
                    ? "something outside its fragments changed, or a fragment moved"
                    : "it drops fragments, which an override cannot remove";
        }

        if (rows.Count == 0)
        {
            skipped++;
            return null;
        }

        foreach ((string id, string xml) in changed)
        {
            workspace.Stage(hash, $"{containerPath}\\{id}", "xml", Utf8.GetBytes(xml));
        }

        // Declarations the mod carries that a later one of the same id supersedes. The engine reaches
        // only the last, so the layer is right without them - but it is no longer a byte-for-byte copy
        // of what the mod shipped, and that is worth saying out loud.
        int superseded = legacy.Unreachable().Count - vanilla.Unreachable().Count;
        if (superseded > 0)
        {
            unreachable.Add(new LegacyImportNote(
                containerPath,
                $"{superseded} declaration(s) a later one supersedes, which the engine never reads"));
        }

        fragmentsImported += changed.Count;
        skipped += unchanged;
        return null;
    }

    /// <summary>How far two floats may sit apart, in units in the last place, and still count as the
    /// same value (see docs/modding/vortex.md).</summary>
    public const int FloatNoiseUlps = 8;

    /// <summary>Whether two fragments say the same thing, floats compared within
    /// <see cref="FloatNoiseUlps"/>.</summary>
    public static bool SameWithinFloatNoise(string vanillaXml, string legacyXml)
        => vanillaXml == legacyXml
           || (TryParse(vanillaXml) is { } a && TryParse(legacyXml) is { } b && SameElement(a, b));

    /// <summary>The legacy fragment with every float that differs from vanilla only by rounding put
    /// back to vanilla's own value, or null when there was no such float.</summary>
    public static string? WithoutFloatNoise(string vanillaXml, string legacyXml)
        => TryParse(vanillaXml) is { } a && TryParse(legacyXml) is { } b && RestoreNoisyFloats(a, b)
            ? b.ToString()
            : null;

    private static XElement? TryParse(string xml)
    {
        try
        {
            return XElement.Parse(xml);
        }
        catch (System.Xml.XmlException)
        {
            return null;
        }
    }

    /// <summary>Whether anything was put back.</summary>
    private static bool RestoreNoisyFloats(XElement vanilla, XElement legacy)
    {
        if (vanilla.Name != legacy.Name)
        {
            return false;
        }

        if (!vanilla.HasElements && !legacy.HasElements)
        {
            bool noisy = SameAttributes(vanilla, legacy)
                && vanilla.Value != legacy.Value
                && SameValue(vanilla.Value, legacy.Value);
            if (noisy)
            {
                legacy.Value = vanilla.Value;
            }

            return noisy;
        }

        bool restored = false;
        foreach ((XElement a, XElement b) in Pair(vanilla, legacy))
        {
            restored |= RestoreNoisyFloats(a, b);
        }

        return restored;
    }

    /// <summary>Vanilla and legacy children side by side, matched by the key each carries so a
    /// component the mod added cannot hide the untouched values beside it. Keyless children - a
    /// vector's x/y/z - are matched by position.</summary>
    private static IEnumerable<(XElement Vanilla, XElement Legacy)> Pair(XElement vanilla, XElement legacy)
    {
        if (KeyedChildren(vanilla) is { } byKey)
        {
            foreach (XElement child in legacy.Elements())
            {
                // Removing as it matches keeps one vanilla child from restoring two legacy ones.
                if (KeyOf(child) is { } key && byKey.Remove((child.Name, key), out XElement? twin))
                {
                    yield return (twin, child);
                }
            }
            yield break;
        }

        if (vanilla.Elements().Count() == legacy.Elements().Count())
        {
            foreach ((XElement a, XElement b) in vanilla.Elements().Zip(legacy.Elements()))
            {
                yield return (a, b);
            }
        }
    }

    /// <summary>The children indexed by the key each carries, or null when one has none or two share
    /// one - the cases where only position can line them up.</summary>
    private static Dictionary<(XName, string), XElement>? KeyedChildren(XElement parent)
    {
        var byKey = new Dictionary<(XName, string), XElement>();
        foreach (XElement child in parent.Elements())
        {
            if (KeyOf(child) is not { } key || !byKey.TryAdd((child.Name, key), child))
            {
                return null;
            }
        }

        return byKey;
    }

    private static string? KeyOf(XElement element)
        => (string?)element.Attribute("name")
           ?? (string?)element.Attribute("hash")
           ?? (string?)element.Attribute("type");

    private static bool SameAttributes(XElement a, XElement b)
        => a.Attributes().Select(x => (x.Name, x.Value))
            .SequenceEqual(b.Attributes().Select(x => (x.Name, x.Value)));

    private static bool SameElement(XElement a, XElement b)
    {
        if (a.Name != b.Name || !SameAttributes(a, b))
        {
            return false;
        }

        List<XElement> mine = [.. a.Elements()];
        List<XElement> theirs = [.. b.Elements()];
        if (mine.Count != theirs.Count)
        {
            return false;
        }

        if (mine.Count == 0)
        {
            return SameValue(a.Value, b.Value);
        }

        for (int i = 0; i < mine.Count; i++)
        {
            if (!SameElement(mine[i], theirs[i]))
            {
                return false;
            }
        }

        return true;
    }

    // A whole number is compared exactly: a large id can round to its neighbour's float.
    private static bool SameValue(string a, string b)
        => a == b
           || ((LooksFractional(a) || LooksFractional(b))
               && float.TryParse(a, NumberStyles.Float, CultureInfo.InvariantCulture, out float x)
               && float.TryParse(b, NumberStyles.Float, CultureInfo.InvariantCulture, out float y)
               && WithinNoise(x, y));

    private static bool LooksFractional(string text) => text.AsSpan().IndexOfAny(".eE") >= 0;

    private static bool WithinNoise(float a, float b)
    {
        if (!float.IsFinite(a) || !float.IsFinite(b))
        {
            return false;
        }

        int vanillaBits = BitConverter.SingleToInt32Bits(a);
        int legacyBits = BitConverter.SingleToInt32Bits(b);

        // Opposite signs are a real change, and the raw bit distance across zero is meaningless.
        return (vanillaBits < 0) == (legacyBits < 0)
            && Math.Abs((long)vanillaBits - legacyBits) <= FloatNoiseUlps;
    }

    /// <summary>The shape the staged set actually rebuilds, for comparing against the shape the mod
    /// shipped.</summary>
    private static string? Reassemble(
        IContainerSplitter splitter, byte[] vanillaBytes, List<(string Id, string Xml)> staged,
        HashSet<string> vanillaIds)
    {
        try
        {
            Dictionary<string, string> byId = staged.ToDictionary(
                c => c.Id, c => c.Xml, FcbFragments.IdComparer);
            return splitter.Open(splitter.Apply(vanillaBytes, byId)).Skeleton(vanillaIds.Contains);
        }
        catch (Exception ex) when (ex is InvalidDataException or EndOfStreamException or MoveFormatException)
        {
            return null;
        }
    }

    /// <summary>
    /// Why two containers of the same fragments still differ in shape, when the answer is that
    /// fragments changed parent - a worldsector mod moving entities into a mission layer of its own,
    /// which is most of what the fragment model cannot currently express. Null when nothing moved and
    /// the difference is something else.
    /// </summary>
    private static string? DescribeMovedFragments(
        IContainerTree legacy, IContainerTree vanilla, IEnumerable<string> commonIds)
    {
        int moved = 0;
        string grouping = "";
        var newParents = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (string id in commonIds)
        {
            if (legacy.AncestryOf(id) is not { } after || vanilla.AncestryOf(id) is not { } before
                || after.ParentName.Equals(before.ParentName, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            moved++;
            grouping = after.Grouping;
            newParents.Add(after.ParentName);
        }

        if (moved == 0)
        {
            return null;
        }

        string what = moved == 1 ? "1 entity was" : $"{moved} entities were";
        return $"{what} moved into {grouping}(s) {string.Join(", ", newParents.Order())}, "
            + "which a per-fragment override cannot do";
    }

    /// <summary>
    /// Imports a legacy whole-file string table as the sections that differ from vanilla, which is
    /// the only shape a layer may express one in. Returns false, staging nothing, when either side
    /// isn't a readable table.
    /// </summary>
    /// <remarks>
    /// A legacy mod always shipped the whole 946 KB file, so this is where nearly every localization
    /// mod in existence gets turned into a diff. Sections the legacy table drops entirely are left
    /// at vanilla: a fragment deletes nothing, and keeping a string the mod meant to remove is far
    /// less damaging than removing every string it never meant to touch.
    /// </remarks>
    private static bool TryImportStringTable(
        byte[] legacyBytes, byte[]? vanillaBytes, string containerPath,
        FolderModLayer workspace, ref int fragmentsImported, ref int skipped)
    {
        if (vanillaBytes is null
            || !RmlDocument.TryDeserialize(legacyBytes, out XElement? legacyRoot)
            || !RmlDocument.TryDeserialize(vanillaBytes, out XElement? vanillaRoot))
        {
            return false;
        }

        IReadOnlyList<OasisStringEdit> changed = OasisStringsPatch.Changed(
            StringTableContainerSplitter.Strings(legacyRoot),
            StringTableContainerSplitter.Strings(vanillaRoot));
        skipped += StringTableContainerSplitter.Strings(legacyRoot).Count() - changed.Count;

        if (changed.Count > 0)
        {
            string patchPath = OasisStringsPatch.PatchPathOf(containerPath);
            workspace.Stage(
                NameHash.Compute(patchPath), patchPath, "xml",
                Utf8.GetBytes(OasisStringsPatch.Render(changed)));
            fragmentsImported += changed.Count;
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
