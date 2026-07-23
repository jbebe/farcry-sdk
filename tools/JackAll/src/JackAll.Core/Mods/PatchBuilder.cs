using JackAll.Core.Format;
using JackAll.Core.Format.Fcb;

namespace JackAll.Core.Mods;

public sealed record BuildResult(
    int TotalEntries,
    int VanillaEntries,
    int OverriddenEntries,
    int AddedEntries,
    long OutputBytes);

/// <summary>
/// Compiles the vanilla patch archive plus the enabled mod layers into a new patch.dat/patch.fat.
/// </summary>
/// <remarks>
/// The output is a pure function of (vanilla backup, enabled layers, order). Nothing is read from
/// the patch currently on disk, so building twice produces the same bytes and disabling a mod
/// genuinely removes it.
///
/// Two properties make this safe enough to run on a whim:
///
///   - Vanilla entries are copied across as raw stored bytes, compression untouched. We never
///     decompress and recompress the game's own data, so we can't corrupt it, and we need no LZO
///     compressor at all.
///   - New and overridden entries are written uncompressed. That's legal (the shipped archives are
///     full of uncompressed entries) and costs some disk space, which is irrelevant at patch.dat's
///     scale.
///
/// The build writes to temp files and swaps them in at the end, so an error or a crash mid-build
/// leaves the game's existing patch intact rather than half-written.
/// </remarks>
public static class PatchBuilder
{
    /// <summary>
    /// <paramref name="readArchiveOriginal"/> resolves a hash to the archives' own current bytes for
    /// it (ignoring mods) — needed for a container with a fragment override, both as the base
    /// <see cref="FcbAssembler"/> splices onto when there's no whole-file override, and as the vanilla
    /// ancestor every contributing layer's edit is merged against (docs/design/
    /// fcb-fragment-overlays.md Milestone 3) — since nearly every real `.fcb` lives in an archive
    /// other than <c>patch.dat</c> and this method otherwise only ever touches the vanilla patch
    /// archive. <c>GameVfs.ReadOriginal</c> is exactly this; callers with a live
    /// <see cref="JackAll.Core.Vfs.GameVfs"/> pass that. Null (the default) is fine as long as no
    /// layer actually stages a fragment override, which every existing caller before fragment
    /// overlays existed relied on implicitly. <paramref name="fcbDefinitions"/> mirrors
    /// <c>GameVfs.Load</c>'s own default-to-<see cref="FcbClassDefinitions.Empty"/> precedent; callers
    /// with a live <see cref="JackAll.Core.Vfs.GameVfs"/> pass its own <c>Definitions</c> so a
    /// fragment's ancestor text decodes the same way <c>GameVfs.Read</c> would show it.
    /// </summary>
    public static BuildResult Build(
        GameInstall install,
        IReadOnlyList<IModLayer> layers,
        Func<uint, byte[]?>? readArchiveOriginal = null,
        FcbClassDefinitions? fcbDefinitions = null)
    {
        FcbClassDefinitions defs = fcbDefinitions ?? FcbClassDefinitions.Empty;

        install.EnsureVanillaBackup();

        var enabled = layers.Where(l => l.Enabled).ToList();

        // Later layers win, so walking forward and overwriting gives exactly the documented
        // "last one wins, no conflict resolution" semantics.
        var wholeFileOverrides = new Dictionary<uint, IModLayer>();
        foreach (var layer in enabled)
        {
            foreach (uint hash in layer.Hashes)
            {
                wholeFileOverrides[hash] = layer;
            }
        }

        // Container hash -> fragment id -> every contributing layer in priority order - one level
        // deeper than wholeFileOverrides. Milestone 3 (docs/design/fcb-fragment-overlays.md): every
        // contributor is folded through Diff3 against the vanilla ancestor, not just the last one -
        // see FragmentMerge for why this index-building and the fold below live there, shared with
        // GameVfs, instead of being duplicated here.
        var fragmentOverrides = FragmentMerge.BuildOverrideIndex(enabled);

        // Every hash that gets a fully-computed replacement entry in the patch: a plain whole-file
        // override, or a container assembled from its base bytes plus one or more fragment overlays.
        // Computed once, up front, so both the vanilla-copy loop below and the final write pass agree
        // on exactly which hashes are "replaced" without redoing any of this work.
        var replacements = new Dictionary<uint, byte[]>();
        foreach ((uint hash, IModLayer layer) in wholeFileOverrides)
        {
            replacements[hash] = layer.Read(hash);
        }
        foreach ((uint containerHash, Dictionary<string, List<(IModLayer Layer, uint EntryHash)>> byFragment)
            in fragmentOverrides)
        {
            if (byFragment.Count == 0) continue;

            // The vanilla ancestor every contributing layer's edit is merged against (Milestone 3) -
            // needed even when a whole-file override also exists for this container, since the merge
            // ancestor is always "what Revert would restore," not whatever the whole-file override
            // replaced it with.
            byte[] vanillaBytes = readArchiveOriginal?.Invoke(containerHash)
                ?? throw new InvalidOperationException(
                    $"A fragment override targets {containerHash:X8}, but no archive currently provides " +
                    "its vanilla ancestor.");
            FcbObject vanillaRoot = FcbDocument.Deserialize(vanillaBytes);

            byte[] baseBytes = replacements.TryGetValue(containerHash, out byte[]? wholeFileBytes)
                ? wholeFileBytes
                : vanillaBytes;

            Dictionary<string, string> xmlByFragment = byFragment.ToDictionary(
                kv => kv.Key, kv => FragmentMerge.Resolve(vanillaRoot, kv.Key, kv.Value, defs));
            replacements[containerHash] = FcbAssembler.Apply(baseBytes, xmlByFragment);
        }

        var vanillaIndex = FatArchive.Read(install.VanillaPatchFat);
        using var vanillaData = File.OpenRead(install.VanillaPatchDat);

        string tempDat = install.PatchDat + ".building";
        string tempFat = install.PatchFat + ".building";

        var entries = new List<FatEntry>(vanillaIndex.Entries.Count + replacements.Count);
        int overridden = 0;

        try
        {
            using (var output = File.Create(tempDat))
            {
                // Offset order, not the index's hash order. The shipped .dat packs its entries
                // contiguously in an order of its own, so copying them in that same order makes a
                // no-mod build reproduce the original file byte for byte — which is what lets the
                // tests assert exact equality instead of merely "it still loads".
                foreach (var vanilla in vanillaIndex.Entries.OrderBy(e => e.Offset))
                {
                    if (replacements.ContainsKey(vanilla.Hash))
                    {
                        overridden++;
                        continue; // an enabled mod replaces this one; written below
                    }

                    // Straight byte copy of the stored (still-compressed) payload.
                    var stored = new byte[vanilla.StoredSize];
                    vanillaData.Seek(vanilla.Offset, SeekOrigin.Begin);
                    vanillaData.ReadExactly(stored);

                    long offset = output.Position;
                    output.Write(stored);
                    entries.Add(vanilla with { Offset = offset });
                }

                foreach ((uint hash, byte[] content) in replacements)
                {
                    long offset = output.Position;
                    output.Write(content);

                    entries.Add(new FatEntry(
                        Hash: hash,
                        Offset: offset,
                        CompressedSize: content.Length,
                        UncompressedSize: 0, // engine invariant for uncompressed entries
                        Compression: CompressionScheme.None));
                }
            }

            FatArchive.FromEntries(entries, vanillaIndex.Flags).Write(tempFat);

            // Only now, with both files fully written, replace the live pair.
            ReplaceFile(tempDat, install.PatchDat);
            ReplaceFile(tempFat, install.PatchFat);
        }
        catch
        {
            SafeDelete(tempDat);
            SafeDelete(tempFat);
            throw;
        }

        int added = replacements.Count - overridden;
        return new BuildResult(
            TotalEntries: entries.Count,
            VanillaEntries: vanillaIndex.Entries.Count - overridden,
            OverriddenEntries: overridden,
            AddedEntries: added,
            OutputBytes: new FileInfo(install.PatchDat).Length);
    }

    /// <summary>
    /// Swaps <paramref name="tempPath"/> into <paramref name="destPath"/>'s place. Deliberately
    /// <c>File.Delete</c> then a plain (non-overwrite) <c>File.Move</c>, not <c>File.Move(overwrite:
    /// true)</c>: on Windows the latter fails with <see cref="UnauthorizedAccessException"/> whenever
    /// anything — including this same process's own <c>GameVfs</c>, which stays open for the app's
    /// whole session and may well have <paramref name="destPath"/> mounted via a <c>DuniaArchive</c> —
    /// still has the destination open for reading, even with <see cref="FileShare.Delete"/> set on
    /// that handle. A plain delete-then-rename has no such restriction, and the effect is the same:
    /// an existing open reader keeps reading the old (now-unlinked but still valid) file contents,
    /// and every fresh open after this call sees <paramref name="tempPath"/>'s.
    /// </summary>
    private static void ReplaceFile(string tempPath, string destPath)
    {
        if (File.Exists(destPath))
        {
            File.Delete(destPath);
        }
        File.Move(tempPath, destPath);
    }

    private static void SafeDelete(string path)
    {
        try
        {
            if (File.Exists(path)) File.Delete(path);
        }
        catch
        {
            // Best effort — a leftover .building file is noise, not damage, and reporting the
            // original failure matters more than reporting a failure to clean up after it.
        }
    }
}
