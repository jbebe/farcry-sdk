using System.Collections.Concurrent;
using JackAll.Core.Format;
using JackAll.Core.Format.Fcb;

namespace JackAll.Core.Mods;

public sealed record BuildResult(
    int TotalEntries,
    int VanillaEntries,
    int OverriddenEntries,
    int AddedEntries,
    long OutputBytes,
    IReadOnlyList<FragmentConflict> Conflicts,
    PluginSyncResult Plugins);

/// <summary>
/// Compiles the vanilla patch archive plus the enabled mod layers into a new patch.dat/patch.fat,
/// then mirrors the layers' <c>plugins\</c> payloads into <c>bin\plugins</c> (see
/// <see cref="PluginSync"/>).
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
    /// <see cref="IContainerSplitter.Apply"/> splices onto when there is no whole-file override, and as the vanilla
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
    /// <param name="resolveFragmentConflictsWithLoadOrder">
    /// Forwarded to every <see cref="FragmentMerge.Resolve"/> call - see that parameter's remarks.
    /// False (the default) matches every caller before this option existed: a genuine fragment
    /// collision throws rather than silently picking a side. <c>jackall-cli mod build</c> is the one
    /// caller that passes true, since a mod-manager-driven build has no interactive way to ask a user
    /// to hand-fix a conflict on the spot.
    /// </param>
    public static BuildResult Build(
        GameInstall install,
        IReadOnlyList<IModLayer> layers,
        Func<uint, byte[]?>? readArchiveOriginal = null,
        FcbClassDefinitions? fcbDefinitions = null,
        bool resolveFragmentConflictsWithLoadOrder = false)
    {
        var conflicts = new ConcurrentQueue<FragmentConflict>();

        install.EnsureVanillaBackup();

        Dictionary<uint, byte[]> replacements = ComputeReplacements(
            layers.Where(l => l.Enabled).ToList(),
            readArchiveOriginal,
            fcbDefinitions ?? FcbClassDefinitions.Empty,
            resolveFragmentConflictsWithLoadOrder ? conflicts : null);

        BuildResult result = WriteArchive(install, replacements, [.. conflicts]);

        // After the archive swap, so a plugin-sync failure never leaves a half-written patch. The
        // pair's atomicity doesn't extend to bin\plugins - the manifest lets the next build
        // reconcile it on its own.
        return result with { Plugins = PluginSync.Apply(install, layers) };
    }

    /// <summary>
    /// Every hash that gets a fully-computed replacement entry in the patch: a plain whole-file
    /// override, or a container assembled from its base bytes plus one or more fragment overlays.
    /// Computed once, up front, so the vanilla-copy loop and the final write pass agree on exactly
    /// which hashes are "replaced" without redoing any of this work.
    /// </summary>
    /// <remarks>
    /// Every hash's read/decode/resolve/encode is independent, so each stage runs as a parallel pass
    /// folded into `replacements` sequentially afterward — this is where nearly all of a
    /// fragment-heavy build's time goes (an entity library's fragment overrides alone can run into
    /// tens of megabytes of edited XML).
    /// </remarks>
    private static Dictionary<uint, byte[]> ComputeReplacements(
        List<IModLayer> enabled,
        Func<uint, byte[]?>? readArchiveOriginal,
        FcbClassDefinitions defs,
        ConcurrentQueue<FragmentConflict>? conflicts)
    {
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

        var replacements = new Dictionary<uint, byte[]>();
        foreach ((uint hash, byte[] bytes) in wholeFileOverrides
            .AsParallel()
            .Select(kv => (kv.Key, Bytes: kv.Value.Read(kv.Key)))
            .ToArray())
        {
            replacements[hash] = bytes;
        }

        // Fragment overlays, one level deeper than whole files: every contributor folds through
        // Diff3 against the vanilla ancestor (see FragmentMerge, shared with GameVfs).
        var fragmentOverrides = FragmentMerge.BuildOverrideIndex(enabled);
        var containersWithFragments = fragmentOverrides.Where(kv => kv.Value.Count > 0).ToList();
        if (containersWithFragments.Count == 0)
        {
            return replacements;
        }

        // The vanilla ancestor every contributing layer's edit is merged against - needed even when
        // a whole-file override also exists for this container, since the merge ancestor is always
        // "what Revert would restore." Decoded once per container, shared by every fragment in it.
        var vanillaByContainer = containersWithFragments
            .AsParallel()
            .Select(kv =>
            {
                byte[] vanillaBytes = readArchiveOriginal?.Invoke(kv.Key)
                    ?? throw new InvalidOperationException(
                        $"A fragment override targets {kv.Key:X8}, but no archive currently provides " +
                        "its vanilla ancestor.");
                string containerPath = RecoveredContainerPath(kv.Key, kv.Value.Values);
                IContainerSplitter splitter = ContainerFormats.For(containerPath, defs);
                return (ContainerHash: kv.Key, VanillaBytes: vanillaBytes, Splitter: splitter,
                    Tree: splitter.Open(vanillaBytes), Display: containerPath);
            })
            .ToDictionary(x => x.ContainerHash);

        // Every (container, fragment) pair, flattened into one parallel pass rather than nesting a
        // parallel loop inside another: a mod that concentrates most of its edits into a single huge
        // container would otherwise leave every core but one idle.
        var resolvedByContainer = containersWithFragments
            .SelectMany(kv => kv.Value.Select(f => (ContainerHash: kv.Key, FragmentId: f.Key, Contributors: f.Value)))
            .AsParallel()
            .Select(item =>
            {
                var container = vanillaByContainer[item.ContainerHash];
                return (item.ContainerHash, item.FragmentId, Xml: FragmentMerge.Resolve(
                    container.Splitter, container.Tree, item.FragmentId, item.Contributors,
                    conflicts, container.Display));
            })
            .GroupBy(x => x.ContainerHash)
            .ToDictionary(g => g.Key, g => g.ToDictionary(x => x.FragmentId, x => x.Xml));

        // Splicing back in decodes and re-encodes the whole container, so the
        // containers run concurrently for this last step too.
        foreach ((uint containerHash, byte[] bytes) in resolvedByContainer
            .AsParallel()
            .Select(kv =>
            {
                byte[] baseBytes = replacements.TryGetValue(kv.Key, out byte[]? wholeFileBytes)
                    ? wholeFileBytes
                    : vanillaByContainer[kv.Key].VanillaBytes;
                var container = vanillaByContainer[kv.Key];
                FragmentMerge.ReportContradictions(container.Splitter, kv.Value, conflicts, container.Display);
                return (kv.Key, Bytes: container.Splitter.Apply(baseBytes, kv.Value));
            })
            .ToArray())
        {
            replacements[containerHash] = bytes;
        }

        return replacements;
    }

    /// <summary>
    /// The container's own path, read off any contributor's staged fragment path. This is what
    /// <see cref="ContainerFormats.For"/> picks the splitter from as well as what a conflict report
    /// names, so a wrong answer here decodes the container as the wrong format. A fragment-only layer
    /// never carries the container's own hash, so a hash-addressed one has no recovered name to fall
    /// back on and gets a synthesized `.fcb` one.
    /// </summary>
    private static string RecoveredContainerPath(
        uint containerHash, IEnumerable<List<(IModLayer Layer, uint EntryHash)>> contributorsByFragment)
    {
        foreach ((IModLayer layer, uint entryHash) in contributorsByFragment.SelectMany(c => c))
        {
            if (layer.PathOf(entryHash) is { } staged && ModPathHashing.ContainerPathOf(staged) is { } path)
            {
                return path;
            }
        }

        return $"_hash\\{containerHash:x8}.fcb";
    }

    /// <summary>
    /// Streams the vanilla backup plus <paramref name="replacements"/> into a fresh
    /// patch.dat/patch.fat pair, written to temp files and swapped in only once complete.
    /// </summary>
    private static BuildResult WriteArchive(
        GameInstall install, Dictionary<uint, byte[]> replacements, IReadOnlyList<FragmentConflict> conflicts)
    {
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
            OutputBytes: new FileInfo(install.PatchDat).Length,
            Conflicts: conflicts,
            Plugins: PluginSyncResult.Empty);
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
