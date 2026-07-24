using System.Text;
using JackAll.Core.Format;
using JackAll.Core.Format.Fcb;
using JackAll.Core.Mods;
using JackAll.Core.Naming;

namespace JackAll.Core.Vfs;

/// <summary>Where a file's winning copy came from.</summary>
public enum SourceKind
{
    Archive,
    Mod,
}

/// <summary>One file as the engine would see it, after the override chain is applied.</summary>
public sealed record VfsFile(
    uint Hash,
    string Path,
    FileType Type,
    long Size,
    string SourceName,
    SourceKind SourceKind,
    bool IsOverriding,
    bool NameIsKnown,
    /// <summary>The containing `.fcb`'s hash, when this entry is a synthetic fragment row rather
    /// than a real archive/mod entry — null otherwise.</summary>
    uint? ContainerHash = null,
    /// <summary>This fragment's <c>FcbXml.ListFragmentIds</c> id, alongside <see cref="ContainerHash"/>.</summary>
    string? FragmentId = null,
    /// <summary>Set only on a *container's own* row (never a fragment row) when it has at least one
    /// active fragment override but no whole-file one — the contributing mod's name, or "multiple
    /// mods". Deliberately doesn't touch <see cref="SourceKind"/>/<see cref="SourceName"/>/
    /// <see cref="IsOverriding"/>: those still have to resolve this row's own *whole-file* bytes
    /// (archive or mod) exactly as before fragment overrides existed, since the mod contributing a
    /// fragment override never has a whole-file entry for this hash to read. This field exists purely
    /// so <see cref="IsModded"/> and the UI's attribution text can tell "this file's build output
    /// differs from vanilla because of a fragment edit" apart from "unmodified.".</summary>
    string? FragmentOverrideSource = null)
{
    /// <summary>True when a mod (or the workspace) supplies this file, whole or in part (a fragment
    /// override counts even without a whole-file replace) — drives Revert and the "only mods" filter.</summary>
    public bool IsModded => SourceKind == SourceKind.Mod || FragmentOverrideSource is not null;

    /// <summary>True for a synthetic row representing one piece of a splitting `.fcb`, rather than a
    /// real archive/mod entry.</summary>
    public bool IsFragment => ContainerHash is not null;

    public string Directory => System.IO.Path.GetDirectoryName(Path)?.Replace('/', '\\') ?? string.Empty;
    public string FileName => System.IO.Path.GetFileName(Path);
}

/// <summary>
/// The merged view: every archive plus every enabled mod layer, resolved the way the engine
/// resolves them.
/// </summary>
/// <remarks>
/// One structure answers every question the UI asks — what's in this folder, is it modded, which
/// archive did it come from, can I revert it — because it's the same override chain the engine
/// itself walks. The alternative (a tree per archive, plus bookkeeping to reconcile them) is where
/// mod managers usually accumulate their bugs.
/// </remarks>
public sealed class GameVfs : IDisposable
{
    private readonly List<DuniaArchive> _archives = [];
    private readonly NameDatabase _names;
    private readonly GameCache _cache;
    private readonly FcbClassDefinitions _fcbDefinitions;
    private List<IModLayer> _layers = [];
    private Dictionary<uint, VfsFile> _files = [];

    /// <summary>
    /// Guards every read-modify-write of <see cref="_files"/>/<see cref="_fragmentMemo"/>/
    /// <see cref="_layers"/>. Normally there's only ever one writer (the UI thread, calling
    /// synchronously), but JackAll.App's <c>MainViewModel</c> kicks off the `.fcb` fragment pass
    /// (<see cref="LoadFragments"/>) as a background follow-up to the initial, fragment-free
    /// <see cref="Load"/> so first paint doesn't wait on it — and a mod toggle can legitimately land
    /// while that's still running. Both <see cref="Rebuild"/> and <see cref="LoadFragments"/> take
    /// this lock for their whole body, so one simply waits for the other rather than tearing the
    /// dictionaries. It's only ever taken from a background thread (the app wraps every call in
    /// `Task.Run`), so blocking here never blocks the UI thread.
    /// </summary>
    private readonly object _rebuildLock = new();

    /// <summary>Archive name -&gt; is it volatile — computed once at <see cref="Load"/>, not per
    /// `.fcb` entry (see <see cref="MergeFragments"/>, which consults this up to ~46,000 times per
    /// call): the archive set never changes across a session, so recomputing this from
    /// <see cref="IsVolatile"/> that often would just be repeated path normalization.</summary>
    private Dictionary<string, bool> _archiveIsVolatile = [];

    /// <summary>
    /// Fragment rows already synthesized for a container, keyed by the container's hash — reused
    /// across <see cref="Rebuild"/>/<see cref="LoadFragments"/> calls as long as that container's
    /// winning source hasn't changed. The game has ~46,000 `.fcb` entries; see the fragment-synthesis
    /// pass in <see cref="MergeFragments"/> for why this and <see cref="GameCache"/> together aren't
    /// sufficient on their own to keep that pass fast.
    /// </summary>
    private Dictionary<uint, (SourceKind Kind, string SourceName, VfsFile[] Fragments)> _fragmentMemo = [];

    /// <summary>
    /// Container hash -&gt; fragment id -&gt; every enabled layer overriding it, in priority order (later
    /// in the list = higher priority, same order <see cref="_layers"/> is walked everywhere else) —
    /// rebuilt every <see cref="Rebuild"/> from <see cref="_layers"/>' <see cref="IModLayer.FragmentOverrides"/>.
    /// Milestone 3 (docs/design/fcb-fragment-overlays.md): every contributing layer is folded through
    /// <see cref="Diff3"/> against the vanilla ancestor instead of only the last one winning outright.
    /// Drives both <see cref="ReadContainer"/> (splicing overrides into a container's bytes) and
    /// <see cref="MergeFragments"/> (showing overridden fragment rows as modded).
    /// </summary>
    private Dictionary<uint, Dictionary<string, List<(IModLayer Layer, uint EntryHash)>>> _fragmentOverrides = [];

    /// <summary>
    /// The one archive we write, and therefore the one archive whose types can't be cached. It is
    /// also the smallest by three orders of magnitude, so sniffing it fresh every launch is free.
    /// </summary>
    private string _volatileFat = string.Empty;

    /// <summary>
    /// The immutable `.vanilla` backup of <c>install.PatchFat</c>/<c>.Dat</c> (see
    /// <see cref="GameInstall.EnsureVanillaBackup"/>), mounted separately from <see cref="_archives"/>
    /// so it never affects the merged view - only <see cref="ReadOriginal"/> consults it. Null until
    /// the first ever deploy creates the backup, in which case the live patch archive mounted in
    /// <see cref="_archives"/> is itself still genuinely vanilla (nothing has written to it yet).
    /// </summary>
    private DuniaArchive? _vanillaPatchArchive;

    public IReadOnlyList<DuniaArchive> Archives => _archives;
    public IReadOnlyDictionary<uint, VfsFile> Files => _files;

    /// <summary>The class config this instance decodes `.fcb` fragments with — <c>PatchBuilder.Build</c>
    /// needs the same one to extract a fragment's vanilla ancestor text the same way <see cref="Read"/>
    /// does (see docs/design/fcb-fragment-overlays.md Milestone 3).</summary>
    public FcbClassDefinitions Definitions => _fcbDefinitions;

    /// <summary>Entries whose filename nobody has recovered yet — still fully usable.</summary>
    public int UnnamedCount { get; private set; }

    private GameVfs(NameDatabase names, GameCache cache, FcbClassDefinitions fcbDefinitions)
    {
        _names = names;
        _cache = cache;
        _fcbDefinitions = fcbDefinitions;
    }

    /// <summary>
    /// Opens every archive and builds the merged view. <paramref name="includeFragments"/> defaults to
    /// true so this method alone is a complete, ready-to-query VFS for callers (tests included) that
    /// don't care about first-paint latency. JackAll.App's <c>MainViewModel</c> is the one caller that
    /// passes false, so the folder tree and file list can show up before the `.fcb` fragment pass runs
    /// — see <see cref="LoadFragments"/>.
    /// </summary>
    public static GameVfs Load(
        GameInstall install,
        NameDatabase names,
        GameCache? cache = null,
        FcbClassDefinitions? fcbDefinitions = null,
        IProgress<string>? progress = null,
        bool includeFragments = true)
    {
        var vfs = new GameVfs(names, cache ?? new GameCache(), fcbDefinitions ?? FcbClassDefinitions.Empty);
        vfs._volatileFat = Path.GetFullPath(install.PatchFat);

        foreach (string fat in install.EnumerateArchiveFats())
        {
            // The .vanilla backup is not a mountable archive, and mounting the live patch.dat is
            // right: it's what the game currently reads.
            if (fat.EndsWith(GameInstall.VanillaSuffix, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            progress?.Report($"Reading {Path.GetFileName(fat)}…");
            try
            {
                vfs._archives.Add(DuniaArchive.Open(fat));
            }
            catch (Exception ex)
            {
                progress?.Report($"Skipped {Path.GetFileName(fat)}: {ex.Message}");
            }
        }

        if (install.HasVanillaBackup)
        {
            try
            {
                vfs._vanillaPatchArchive = DuniaArchive.Open(install.VanillaPatchFat, install.VanillaPatchDat);
            }
            catch (Exception ex)
            {
                progress?.Report($"Skipped vanilla patch backup: {ex.Message}");
            }
        }

        // GroupBy, not ToDictionary: archive names aren't guaranteed unique (e.g. DLC folders can
        // duplicate a base-game archive's name) - every other by-name lookup in this class already
        // tolerates that ambiguity via `.First(a => a.Name == ...)`, so this matches that leniency
        // instead of throwing on a duplicate key.
        vfs._archiveIsVolatile = vfs._archives
            .GroupBy(a => a.Name)
            .ToDictionary(g => g.Key, g => g.Any(vfs.IsVolatile));

        // No cache invalidation check here on purpose - the base game's archives never change for
        // the life of an install, so a cache that loaded without error is trusted outright. If the
        // game is reinstalled or patched, the user deletes the cache file themselves.
        vfs.Rebuild([], includeFragments, progress);
        return vfs;
    }

    private bool IsVolatile(DuniaArchive archive)
        => string.Equals(
            Path.GetFullPath(archive.FatPath), _volatileFat, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Re-opens the one archive <c>PatchBuilder.Build</c> just replaced (<c>install.PatchFat</c>/
    /// <c>.Dat</c>) and refreshes the merged view from it. Call this after every successful build —
    /// the old <see cref="DuniaArchive"/>'s file handle doesn't just show stale content afterward, it
    /// actively throws <see cref="EndOfStreamException"/> on any further read (see the remarks on
    /// <see cref="DuniaArchive.Open"/>), so holding onto it is not merely wrong, it's a live crash
    /// waiting for the next read that happens to land on that archive — which
    /// <see cref="ReadOriginal"/> can, since it's the one archive <see cref="GameVfs"/> otherwise never
    /// needs to touch again after the initial <see cref="Load"/>.
    /// </summary>
    public void ReloadPatchArchive()
    {
        // The swap and the merge it feeds both need _rebuildLock - held for the whole method (not just
        // released and re-acquired inside Rebuild) so a concurrent Reindex()/LoadFragments() can never
        // observe the archive list mid-swap or read from the about-to-be-disposed stale handle. Safe to
        // nest: _rebuildLock is a plain object, so the classic `lock` statement's Monitor-based
        // reentrancy lets Rebuild's own `lock` below re-enter on this same thread.
        lock (_rebuildLock)
        {
            int index = _archives.FindIndex(
                a => string.Equals(Path.GetFullPath(a.FatPath), _volatileFat, StringComparison.OrdinalIgnoreCase));
            if (index < 0)
            {
                return; // no patch.fat was ever mounted (shouldn't happen - it's required for a valid install)
            }

            DuniaArchive stale = _archives[index];
            _archives[index] = DuniaArchive.Open(stale.FatPath);
            stale.Dispose();

            // A build also calls install.EnsureVanillaBackup(), so the very first deploy of this
            // session is exactly when the backup can go from not-existing to existing - mount it now
            // rather than waiting for the next app launch, or ReadOriginal would keep treating the
            // live patch archive (now this build's own output) as vanilla for the rest of the session.
            if (_vanillaPatchArchive is null)
            {
                string vanillaFat = _volatileFat + GameInstall.VanillaSuffix;
                string vanillaDat = Path.ChangeExtension(_volatileFat, ".dat") + GameInstall.VanillaSuffix;
                if (File.Exists(vanillaFat) && File.Exists(vanillaDat))
                {
                    _vanillaPatchArchive = DuniaArchive.Open(vanillaFat, vanillaDat);
                }
            }

            Rebuild(_layers);
        }
    }

    /// <summary>
    /// Recomputes the merged view. Call after the mod list or the workspace changes.
    /// <paramref name="includeFragments"/> is only ever false for the very first build (see
    /// <see cref="Load"/>) — every other caller wants the complete view, `.fcb` browsing included.
    /// </summary>
    public void Rebuild(IReadOnlyList<IModLayer> layers, bool includeFragments = true, IProgress<string>? progress = null)
    {
        lock (_rebuildLock)
        {
            _layers = layers.ToList();
            _fragmentOverrides = FragmentMerge.BuildOverrideIndex(_layers.Where(l => l.Enabled));
            Dictionary<uint, VfsFile> files = BuildMergedFiles(progress);
            if (includeFragments)
            {
                MergeFragments(files, progress);
            }
            _files = files;
        }
    }

    /// <summary>
    /// The final XML for one fragment, folding every enabled layer touching it (in priority order)
    /// via <see cref="FragmentMerge.Resolve"/> — docs/design/fcb-fragment-overlays.md Milestone 3.
    /// This is the one part <see cref="PatchBuilder"/> can't share verbatim: getting from a container
    /// hash to its decoded vanilla root is specific to how each side reaches the archives.
    /// </summary>
    private string ResolveFragment(uint containerHash, string fragmentId, List<(IModLayer Layer, uint EntryHash)> layers)
    {
        FcbObject vanillaRoot = FcbDocument.Deserialize(ReadOriginal(containerHash)
            ?? throw new InvalidOperationException($"No archive provides {containerHash:X8}."));
        return FragmentMerge.Resolve(vanillaRoot, fragmentId, layers, _fcbDefinitions);
    }

    /// <summary>
    /// <see cref="ResolveFragment"/>'s length, for display only — never lets a merge conflict (or a
    /// vanished vanilla ancestor) escape <see cref="MergeFragments"/> and take down the whole rebuild
    /// over one fragment nobody's actively reading yet. Falls back to the highest-priority
    /// contributor's own raw length (what Milestone 2 would have shown) so the row still renders
    /// something reasonable; the real error still surfaces loudly the moment something actually reads
    /// this fragment or its container (<see cref="Read"/>/<see cref="ReadContainer"/>), or builds the
    /// patch — never silently, just not here.
    /// </summary>
    private long SizeOfResolvedFragmentSafely(uint containerHash, string fragmentId, List<(IModLayer Layer, uint EntryHash)> layers)
    {
        try
        {
            return Encoding.UTF8.GetByteCount(ResolveFragment(containerHash, fragmentId, layers));
        }
        catch (Exception ex) when (ex is InvalidDataException or InvalidOperationException)
        {
            return layers[^1].Layer.Read(layers[^1].EntryHash).LongLength;
        }
    }

    /// <summary>
    /// Adds `.fcb` fragment rows to whatever <see cref="Files"/> already holds, without repeating the
    /// archive/mod merge pass that built it. This is the deferred half of a fragment-free
    /// <see cref="Load"/>/<see cref="Rebuild"/> — JackAll.App calls it once, in the background, right
    /// after the fragment-free view is already on screen, so browsing into a split `.fcb` lights up a
    /// moment later instead of holding up first paint.
    /// </summary>
    public void LoadFragments(IProgress<string>? progress = null)
    {
        lock (_rebuildLock)
        {
            var files = new Dictionary<uint, VfsFile>(_files);
            MergeFragments(files, progress);
            _files = files;
        }
    }

    /// <summary>Every archive and enabled mod-layer entry, named/typed and override-resolved — the
    /// part of the merged view that's cheap once the type cache is warm. Sets <see cref="UnnamedCount"/>.</summary>
    private Dictionary<uint, VfsFile> BuildMergedFiles(IProgress<string>? progress)
    {
        var files = new Dictionary<uint, VfsFile>();
        int unnamed = 0;

        int totalEntries = _archives.Sum(a => a.Entries.Count);
        int processed = 0;
        const int ReportEvery = 5_000;

        foreach (var archive in _archives)
        {
            bool cacheable = !IsVolatile(archive);

            foreach (var entry in archive.Entries)
            {
                // patch.dat is the engine's highest-priority archive, so when two archives carry
                // the same hash it wins — matching the search order the engine actually uses.
                bool overriding = files.TryGetValue(entry.Hash, out var existing);
                if (overriding && !IsHigherPriority(archive.Name, existing!.SourceName))
                {
                    processed++;
                    continue;
                }

                bool named = _names.TryResolve(entry.Hash, out string path);
                if (!named)
                {
                    path = string.Empty;
                }

                var type = ResolveType(archive, entry, named ? path : null, cacheable);
                if (!named)
                {
                    path = SyntheticPath(entry.Hash, type);
                    unnamed++;
                }

                files[entry.Hash] = new VfsFile(
                    Hash: entry.Hash,
                    Path: path,
                    Type: type,
                    Size: entry.RealSize,
                    SourceName: archive.Name,
                    SourceKind: SourceKind.Archive,
                    IsOverriding: overriding,
                    NameIsKnown: named);

                processed++;
                if (processed % ReportEvery == 0)
                {
                    progress?.Report($"Indexing files… ({processed:N0} / {totalEntries:N0})");
                }
            }
        }

        UnnamedCount = unnamed;

        // Mod layers on top, in order — later wins.
        foreach (var layer in _layers.Where(l => l.Enabled))
        {
            foreach (uint hash in layer.Hashes)
            {
                files.TryGetValue(hash, out var beneath);

                string? layerPath = layer.PathOf(hash);
                bool named = layerPath is not null || _names.TryResolve(hash, out layerPath!);

                var type = beneath?.Type
                           ?? FileTypeSniffer.Identify(ReadHeaderSafely(layer, hash), layerPath);

                files[hash] = new VfsFile(
                    Hash: hash,
                    Path: named ? layerPath! : SyntheticPath(hash, type),
                    Type: type,
                    Size: SizeSafely(layer, hash),
                    SourceName: layer.Name,
                    SourceKind: SourceKind.Mod,
                    IsOverriding: beneath is not null,
                    NameIsKnown: named);
            }
        }

        return files;
    }

    /// <summary>
    /// Adds fragment rows for `.fcb` files that split (see FcbXml.ListFragmentIds) — this is what lets
    /// the tree/file view browse into one with no dedicated UI, since a fragment's path is just the
    /// container's own path plus one more segment (docs/design/fcb-fragment-overlays.md). Mutates
    /// <paramref name="files"/> in place and refreshes <see cref="_fragmentMemo"/> to match.
    /// </summary>
    /// <remarks>
    /// Split into three passes rather than one combined loop, for reasons empirically confirmed
    /// against real game data (~46,000 `.fcb` entries), not just style:
    ///
    ///   1. Scan for containers needing a decode (cache miss, or non-cacheable because they're
    ///      currently mod-overridden or in the volatile, never-cached patch.dat). Also skips
    ///      containers the in-memory memo already has an up-to-date entry for *and that have no
    ///      active fragment override* — so a patch.dat entry that never changes doesn't redecode on
    ///      every single edit, but a non-cacheable container whose fragment override just appeared
    ///      still gets queued here even though the memo matches, since pass 3 (which computes the
    ///      exact same `hasOverrides` and must agree with this pass on it) will be forced past its
    ///      own memo shortcut for it and needs `uncached` to actually have an entry.
    ///   2. Decode just that (normally empty or tiny) list. A loop that can *conditionally* call a
    ///      method containing a try/catch (DecodeFragments does, for FcbDocument.Deserialize)
    ///      measured roughly 1000x slower than an identical loop that never references that method
    ///      at all — even when the call is actually taken 0 times. Keeping pass 1's ~46,000-iteration
    ///      scan free of any such call, and confining the call to this short list, is what avoids
    ///      that cost, whatever its exact cause (JIT/tiering behaviour around methods with
    ///      exception handlers, most likely, but the fix here is empirical rather than proven).
    ///   3. Build the actual VfsFile fragment rows, memoized by container hash so a container whose
    ///      winning source hasn't changed since the last call reuses its previous rows outright.
    ///      The memo is read from the *previous* call's dictionary but built into a brand new one
    ///      (swapped into _fragmentMemo only once, at the end) rather than written into the existing
    ///      field in place — writing ~46,000 entries one at a time into that long-lived field (which
    ///      survives across every call) measured ~1.7s, against single-digit milliseconds for the
    ///      same work building a fresh dictionary and swapping it in. A container with an active
    ///      fragment override (see <see cref="_fragmentOverrides"/>) skips this memo entirely, both
    ///      read and write: the memo's cache key is the container's own (SourceKind, SourceName),
    ///      which doesn't change when a fragment override's *content* changes (e.g. re-staging the
    ///      same workspace fragment with different bytes) — only skipping it guarantees an
    ///      overridden row's Size/attribution can't go stale in that case.
    ///
    /// A fourth, final step patches the *container's own* row (not its fragments) when it has an
    /// active fragment override but no whole-file one — the built patch really does differ from
    /// vanilla for that hash, so its row should read as modded too, not just its fragment rows.
    /// Deliberately last: it must run after pass 3 builds the *un*-overridden sibling fragments'
    /// rows, which need the container's original archive/mod attribution, not this patched one.
    /// </remarks>
    private void MergeFragments(Dictionary<uint, VfsFile> files, IProgress<string>? progress)
    {
        var needsDecode = new List<(VfsFile Container, bool Cacheable)>();
        foreach (VfsFile c in files.Values)
        {
            if (c.Type.Extension != "fcb") continue;

            // Must mirror pass 3's own memo-skip condition below exactly (including the hasOverrides
            // check), or the two passes disagree about which containers pass 3 can expect to find in
            // `uncached` — if this pass skips one via a stale-but-still-source-matching memo entry
            // while pass 3 is forced past its own memo shortcut (because hasOverrides just went
            // false->true for it), pass 3's `uncached[container.Hash]` throws KeyNotFoundException. A
            // container that's never cacheable (lives in the volatile patch.dat, most commonly) hits
            // this every time: its structure only ever lived in the memo, never in `_cache`.
            bool hasOverrides = _fragmentOverrides.TryGetValue(c.Hash, out var overridesForThis)
                && overridesForThis.Count > 0;

            if (!hasOverrides
                && _fragmentMemo.TryGetValue(c.Hash, out var existingMemo)
                && existingMemo.Kind == c.SourceKind
                && existingMemo.SourceName == c.SourceName)
            {
                continue;
            }

            bool cacheable = c.SourceKind == SourceKind.Archive
                && !_archiveIsVolatile.GetValueOrDefault(c.SourceName, defaultValue: true);
            if (!cacheable || !_cache.TryGet(c.Hash, out _))
            {
                needsDecode.Add((c, cacheable));
            }
        }

        var uncached = new Dictionary<uint, IReadOnlyList<FcbFragmentInfo>>();
        int decoded = 0;
        const int ReportEvery = 1_000;
        foreach ((VfsFile c, bool cacheable) in needsDecode)
        {
            IReadOnlyList<FcbFragmentInfo> decodedFragments = DecodeFragments(c, cacheable);
            if (!cacheable)
            {
                uncached[c.Hash] = decodedFragments;
            }

            decoded++;
            if (decoded % ReportEvery == 0)
            {
                progress?.Report($"Indexing .fcb structure… ({decoded:N0} / {needsDecode.Count:N0})");
            }
        }

        var fragments = new Dictionary<uint, VfsFile>();
        var newFragmentMemo = new Dictionary<uint, (SourceKind Kind, string SourceName, VfsFile[] Fragments)>();
        foreach (VfsFile container in files.Values)
        {
            if (container.Type.Extension != "fcb")
            {
                continue;
            }

            bool hasOverrides = _fragmentOverrides.TryGetValue(container.Hash,
                out Dictionary<string, List<(IModLayer Layer, uint EntryHash)>>? byFragment) && byFragment.Count > 0;

            if (!hasOverrides
                && _fragmentMemo.TryGetValue(container.Hash, out var memo)
                && memo.Kind == container.SourceKind
                && memo.SourceName == container.SourceName)
            {
                foreach (VfsFile fragment in memo.Fragments)
                {
                    fragments[fragment.Hash] = fragment;
                }
                newFragmentMemo[container.Hash] = memo;
                continue;
            }

            if (!_cache.TryGet(container.Hash, out IReadOnlyList<FcbFragmentInfo> containerFragments))
            {
                containerFragments = uncached[container.Hash];
            }

            var computed = new VfsFile[containerFragments.Count];
            for (int i = 0; i < containerFragments.Count; i++)
            {
                FcbFragmentInfo fragment = containerFragments[i];
                string fragmentPath = container.Path + "\\" + fragment.Id;
                uint fragmentHash = NameHash.Compute(fragmentPath);

                VfsFile vfsFragment = hasOverrides && byFragment!.TryGetValue(fragment.Id, out var contributors)
                    ? new VfsFile(
                        Hash: fragmentHash,
                        Path: fragmentPath,
                        Type: new FileType("misc", "xml"),
                        Size: SizeOfResolvedFragmentSafely(container.Hash, fragment.Id, contributors),
                        SourceName: contributors.Count == 1 ? contributors[0].Layer.Name : "multiple mods",
                        SourceKind: SourceKind.Mod,
                        IsOverriding: true,
                        NameIsKnown: container.NameIsKnown,
                        ContainerHash: container.Hash,
                        FragmentId: fragment.Id)
                    : new VfsFile(
                        Hash: fragmentHash,
                        Path: fragmentPath,
                        Type: new FileType("misc", "xml"),
                        Size: fragment.Size,
                        SourceName: container.SourceName,
                        SourceKind: container.SourceKind,
                        IsOverriding: false,
                        NameIsKnown: container.NameIsKnown,
                        ContainerHash: container.Hash,
                        FragmentId: fragment.Id);
                computed[i] = vfsFragment;
                fragments[fragmentHash] = vfsFragment;
            }

            if (hasOverrides)
            {
                // Every override id with no match above: not overriding an existing child, but adding
                // one the vanilla container never had (see FragmentMerge.Resolve's empty-ancestor
                // case). Still gets its own synthetic row - same as any other fragment - so it's
                // browsable/readable on its own, not just visible once its container is read whole.
                // Never memoized: a container with overrides never takes the memo-shortcut branch
                // above in the first place, so there's nothing to keep in sync here.
                foreach ((string fragmentId, List<(IModLayer Layer, uint EntryHash)> contributors) in byFragment!)
                {
                    if (containerFragments.Any(f => string.Equals(f.Id, fragmentId, StringComparison.OrdinalIgnoreCase)))
                    {
                        continue; // already produced above, as an override of an existing child
                    }

                    string fragmentPath = container.Path + "\\" + fragmentId;
                    uint fragmentHash = NameHash.Compute(fragmentPath);
                    fragments[fragmentHash] = new VfsFile(
                        Hash: fragmentHash,
                        Path: fragmentPath,
                        Type: new FileType("misc", "xml"),
                        Size: SizeOfResolvedFragmentSafely(container.Hash, fragmentId, contributors),
                        SourceName: contributors.Count == 1 ? contributors[0].Layer.Name : "multiple mods",
                        SourceKind: SourceKind.Mod,
                        IsOverriding: false,
                        NameIsKnown: container.NameIsKnown,
                        ContainerHash: container.Hash,
                        FragmentId: fragmentId);
                }
            }
            else
            {
                newFragmentMemo[container.Hash] = (container.SourceKind, container.SourceName, computed);
            }
        }
        _fragmentMemo = newFragmentMemo;
        foreach ((uint hash, VfsFile fragment) in fragments)
        {
            files[hash] = fragment;
        }

        // Last: the container's own row, so pass 3 above still saw its original archive/mod
        // attribution when building the un-overridden sibling fragments' rows (see remarks). Only
        // sets FragmentOverrideSource - SourceKind/SourceName/IsOverriding are left alone, since this
        // row's own bytes still come from wherever they always did (see VfsFile.FragmentOverrideSource).
        foreach ((uint containerHash, Dictionary<string, List<(IModLayer Layer, uint EntryHash)>> byFragment)
            in _fragmentOverrides)
        {
            if (byFragment.Count == 0
                || !files.TryGetValue(containerHash, out VfsFile? container)
                || container.SourceKind == SourceKind.Mod)
            {
                continue; // already has a whole-file override - that attribution wins outright
            }

            string[] contributors = [.. byFragment.Values.SelectMany(list => list).Select(w => w.Layer.Name).Distinct()];
            files[containerHash] = container with
            {
                FragmentOverrideSource = contributors.Length == 1 ? contributors[0] : "multiple mods",
            };
        }
    }

    /// <summary>
    /// The rare path: an `.fcb` whose fragments weren't already resolved by <see cref="_cache"/> or
    /// <see cref="_fragmentMemo"/> — either a genuine cache miss (first time this game install has
    /// been seen) or a `.fcb` currently overridden by a mod (including living in the volatile
    /// `patch.dat`), whose structure isn't a fixed fact about the game and so is never written to the
    /// on-disk <see cref="_cache"/>. It's still worth the full
    /// <see cref="FcbXml.ListFragmentsWithSize"/> pass (accurate sizes, not just
    /// <see cref="FcbXml.ListFragmentIds"/>) even for a non-cacheable entry: the in-memory
    /// <see cref="_fragmentMemo"/> already keeps this from being redone on every call — it's correctly
    /// invalidated only when that hash's winning source actually changes (kind or name), which is
    /// exactly when a mod starts or stops overriding it — so this only ever runs once per real change,
    /// not once per edit. An unreadable/corrupt entry is treated as "doesn't split", matching
    /// <see cref="GameCache.Sniff"/>'s "unreadable -&gt; Unknown" precedent.
    /// </summary>
    private IReadOnlyList<FcbFragmentInfo> DecodeFragments(VfsFile container, bool cacheable)
    {
        IReadOnlyList<FcbFragmentInfo> fragments;
        try
        {
            (FcbObject root, IReadOnlyList<long> childByteSizes) = FcbDocument.DeserializeWithChildSizes(ReadFromSource(container));
            fragments = FcbXml.ListFragmentsWithSize(root, childByteSizes);
        }
        catch
        {
            fragments = [];
        }

        if (cacheable)
        {
            _cache.Set(container.Hash, fragments);
        }
        return fragments;
    }

    /// <summary>
    /// Reads the winning copy of a file — including a fragment row, decoded from its container on
    /// demand (fragments carry no stored bytes of their own; see <see cref="VfsFile.IsFragment"/>),
    /// and a container whose fragments are (partly) overridden, assembled from its base bytes plus
    /// whichever fragment overrides currently apply (see <see cref="ReadContainer"/>).
    /// </summary>
    public byte[] Read(uint hash)
    {
        if (!_files.TryGetValue(hash, out var file))
        {
            throw new KeyNotFoundException($"No file with hash {hash:X8}.");
        }

        if (file.IsFragment)
        {
            // This exact fragment is overridden - no need to touch the container at all, and its
            // sibling fragments' overrides (if any) are irrelevant to it either way.
            if (_fragmentOverrides.TryGetValue(file.ContainerHash!.Value, out var byFragment)
                && byFragment.TryGetValue(file.FragmentId!, out List<(IModLayer Layer, uint EntryHash)>? contributors))
            {
                string merged = ResolveFragment(file.ContainerHash!.Value, file.FragmentId!, contributors);
                return new UTF8Encoding(false).GetBytes(merged);
            }

            FcbObject root = FcbDocument.Deserialize(ReadFromSource(_files[file.ContainerHash!.Value]));
            string xml = FcbXml.ExtractFragment(root, file.FragmentId!, _fcbDefinitions)
                ?? throw new InvalidDataException(
                    $"'{file.FragmentId}' no longer matches any group in '{file.Directory}' - it may have changed shape.");
            return new UTF8Encoding(false).GetBytes(xml);
        }

        return file.Type.Extension == "fcb" ? ReadContainer(file) : ReadFromSource(file);
    }

    /// <summary>
    /// A container's base bytes (its own whole-file winning source, exactly as
    /// <see cref="ReadFromSource"/> always resolved it) with any active fragment overrides spliced in
    /// via <see cref="FcbAssembler"/>. Unchanged, with no decode/encode cost, when nothing overrides
    /// any of this container's fragments — the common case.
    /// </summary>
    private byte[] ReadContainer(VfsFile container)
    {
        byte[] baseBytes = ReadFromSource(container);
        if (!_fragmentOverrides.TryGetValue(container.Hash, out var byFragment) || byFragment.Count == 0)
        {
            return baseBytes;
        }

        Dictionary<string, string> xmlByFragment = byFragment.ToDictionary(
            kv => kv.Key, kv => ResolveFragment(container.Hash, kv.Key, kv.Value));
        return FcbAssembler.Apply(baseBytes, xmlByFragment);
    }

    private byte[] ReadFromSource(VfsFile file)
    {
        if (file.SourceKind == SourceKind.Mod)
        {
            var layer = _layers.First(l => l.Name == file.SourceName);
            return layer.Read(file.Hash);
        }

        var archive = _archives.First(a => a.Name == file.SourceName);
        return archive.Read(file.Hash);
    }

    /// <summary>
    /// Reads the copy the archives provide, ignoring mods — i.e. what Revert would restore.
    /// Null when the file exists only because a mod added it.
    /// </summary>
    /// <remarks>
    /// Almost everything lives in an archive JackAll never writes to, so the live-mounted copy in
    /// <see cref="_archives"/> is always genuinely vanilla for it. The one exception is anything whose
    /// only archive-provided home is <c>patch.dat</c> itself (rare, but real — e.g. the game's own
    /// <c>entitylibrarypatchoverride.fcb</c>): once a single deploy has happened, the live patch.dat
    /// mounted there is JackAll's *own* previous build output, not vanilla, and a hash added by that
    /// build wouldn't be "original" at all. <see cref="_vanillaPatchArchive"/> — the immutable
    /// pre-first-deploy backup — is checked first and, when it exists, the live patch archive is
    /// excluded from the fallback search entirely, so this can't silently drift onto JackAll's own
    /// output the way it used to.
    /// </remarks>
    public byte[]? ReadOriginal(uint hash)
    {
        if (_vanillaPatchArchive?.Contains(hash) == true)
        {
            return _vanillaPatchArchive.Read(hash);
        }

        // Once the vanilla backup exists, the live patch archive is JackAll's own build output, not
        // an original source - excluded here so a hash it only has because of a previous deploy
        // correctly falls through to null, same as any other mod-added file.
        var winner = _archives
            .Where(a => a.Contains(hash) && (_vanillaPatchArchive is null || !IsVolatile(a)))
            .OrderByDescending(a => PriorityOf(a.Name))
            .FirstOrDefault();

        return winner?.Read(hash);
    }

    /// <summary>
    /// The vanilla text of one fragment - what <see cref="ReadOriginal"/> is to a whole file, but for
    /// one piece of a splitting `.fcb`. Deliberately re-decodes the container's own <see cref="ReadOriginal"/>
    /// bytes rather than trusting anything already staged/merged, so this returns the true vanilla
    /// shape even when the fragment is *currently* overridden by a mod or the workspace - a caller
    /// diffing "what did I actually change" (JackAll.App's fragment editor) needs exactly that,
    /// otherwise reopening an already-edited fragment would have nothing real to compare against.
    /// Null when the container itself has no archive-provided original (a mod-added file, most
    /// commonly) or <paramref name="fragmentId"/> no longer matches any group in the vanilla shape.
    /// </summary>
    public string? ReadOriginalFragment(uint containerHash, string fragmentId)
    {
        byte[]? originalContainer = ReadOriginal(containerHash);
        if (originalContainer is null)
        {
            return null;
        }

        FcbObject root = FcbDocument.Deserialize(originalContainer);
        return FcbXml.ExtractFragment(root, fragmentId, _fcbDefinitions);
    }

    /// <summary>patch beats everything else; otherwise mount order doesn't matter (no collisions).</summary>
    private static int PriorityOf(string archiveName)
        => archiveName.Equals("patch", StringComparison.OrdinalIgnoreCase) ? 1 : 0;

    private static bool IsHigherPriority(string candidate, string incumbent)
        => PriorityOf(candidate) > PriorityOf(incumbent);

    /// <summary>
    /// A known filename settles the type for free. Only a nameless entry has to be identified from
    /// its header — the expensive path, and the only one worth caching.
    /// </summary>
    private FileType ResolveType(DuniaArchive archive, FatEntry entry, string? knownPath, bool cacheable)
    {
        if (knownPath is not null)
        {
            return FileTypeSniffer.Identify(ReadOnlySpan<byte>.Empty, knownPath);
        }

        return cacheable
            ? _cache.TypeOf(archive, entry)
            : GameCache.Sniff(archive, entry);
    }

    /// <summary>
    /// Gives a nameless entry somewhere to live and something to be called. Without this an
    /// unnamed file is an unaddressable blob; with it, it's "an .xbt in _unknown\textures" that the
    /// texture handler will happily preview and replace.
    /// </summary>
    private static string SyntheticPath(uint hash, FileType type)
        => Path.Combine("_unknown", type.Category, $"{hash:x8}.{type.Extension}");

    private static byte[] ReadHeaderSafely(IModLayer layer, uint hash)
    {
        try
        {
            byte[] content = layer.Read(hash);
            return content.Length <= FileTypeSniffer.HeaderBytes
                ? content
                : content[..FileTypeSniffer.HeaderBytes];
        }
        catch
        {
            return [];
        }
    }

    private static long SizeSafely(IModLayer layer, uint hash)
    {
        try
        {
            return layer.Read(hash).LongLength;
        }
        catch
        {
            return 0;
        }
    }

    public void Dispose()
    {
        foreach (var archive in _archives)
        {
            archive.Dispose();
        }
        _vanillaPatchArchive?.Dispose();
    }
}
