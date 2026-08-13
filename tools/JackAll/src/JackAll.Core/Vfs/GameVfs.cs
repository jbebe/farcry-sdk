using System.IO.Hashing;
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
    string? FragmentOverrideSource = null,
    /// <summary>The owning `depload.dat`'s hash, set only on a synthetic dependency-link row (one
    /// entry of a depload's parent/children list, browsable the same way an `.fcb` fragment is - see
    /// <see cref="IsDependencyLink"/>). Deliberately a separate field from <see cref="ContainerHash"/>:
    /// that one is read elsewhere with `.fcb`-fragment-specific meaning baked in (fragment XML
    /// extraction, fragment-override lookup), which a depload link is not.</summary>
    uint? LinkOwnerHash = null,
    /// <summary>The resource CRC32 this dependency link refers to - always set alongside
    /// <see cref="LinkOwnerHash"/>, whether or not it resolves to a known VFS entry (look it up in
    /// <see cref="GameVfs.Files"/> to tell resolved from unresolved).</summary>
    uint? LinkTargetHash = null,
    /// <summary>The dependency's resolved type hash (looked up via the depload's own small deduplicated
    /// type table - see <see cref="Format.DepLoadDocument"/>), set only when this link is a depload
    /// *child* entry - null for a parent entry, which doubles as how a link row's own kind is told
    /// apart. Semantic meaning of the type hash itself isn't yet confirmed.</summary>
    uint? LinkChildTypeHash = null)
{
    /// <summary>True when a mod (or the workspace) supplies this file, whole or in part (a fragment
    /// override counts even without a whole-file replace) — drives Revert and the "only mods" filter.</summary>
    public bool IsModded => SourceKind == SourceKind.Mod || FragmentOverrideSource is not null;

    /// <summary>True for a synthetic row representing one piece of a splitting `.fcb`, rather than a
    /// real archive/mod entry.</summary>
    public bool IsFragment => ContainerHash is not null;

    /// <summary>True for a synthetic row representing one entry (a parent or a child) of a
    /// `depload.dat`'s dependency list, rather than a real archive/mod entry.</summary>
    public bool IsDependencyLink => LinkOwnerHash is not null;

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

    /// <summary>Archive bare name -&gt; every mounted archive with that name (almost always just one)
    /// — computed once at <see cref="Load"/>, alongside <see cref="_archiveIsVolatile"/>. Drives
    /// <see cref="DisplayModuleName"/>, which needs this grouping to pick the right one of several
    /// same-named archives (e.g. dlc1 and dlc_jungle each ship their own "menus.fat") without a full
    /// linear scan of <see cref="_archives"/> for every file.</summary>
    private Dictionary<string, DuniaArchive[]> _archivesByName = [];

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

    /// <summary>
    /// The sniffed-type/`.fcb`-structure cache this instance is reading through and adding to (see
    /// <see cref="GameCache"/>). Exposed rather than saved automatically on <see cref="Dispose"/>,
    /// since not every caller wants that: JackAll.App manages its own cache's lifetime independently
    /// (loaded once at startup, saved on its own schedule) rather than tying it to any one
    /// <see cref="GameVfs"/> instance's lifetime, which a mod toggle can recreate repeatedly in a
    /// single session. A caller that wants the persistence checks <see cref="GameCache.IsDirty"/> and
    /// calls <see cref="GameCache.Save"/> itself once it's done - <c>ModPipeline.SaveCache</c> is the
    /// CLI's version of that.
    /// </summary>
    public GameCache Cache => _cache;

    /// <summary>Entries whose filename nobody has recovered yet — still fully usable.</summary>
    public int UnnamedCount { get; private set; }

    /// <summary>
    /// Whether <paramref name="file"/>'s winning bytes come from an archive that never changes for
    /// the life of the install — i.e. an archive other than the one this tool itself rewrites on
    /// every Build &amp; Apply. This is the same "is it cacheable" test <see cref="MergeFragments"/>
    /// already applies before consulting <see cref="GameCache"/>, exposed because
    /// <see cref="Xrefs.ReferenceIndexer"/> needs exactly the same answer to decide what it may
    /// persist: a mod-supplied or patch-supplied file's references are only valid for this session's
    /// layer stack, so they must never reach the on-disk index.
    /// </summary>
    public bool IsStableSource(VfsFile file)
        => file.SourceKind == SourceKind.Archive
        && !_archiveIsVolatile.GetValueOrDefault(file.SourceName, defaultValue: true);

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
        GameVfs vfs = OpenArchives(install, names, cache, fcbDefinitions, progress);

        // No cache invalidation check here on purpose - the base game's archives never change for
        // the life of an install, so a cache that loaded without error is trusted outright. If the
        // game is reinstalled or patched, the user deletes the cache file themselves.
        vfs.Rebuild([], includeFragments, progress);
        return vfs;
    }

    /// <summary>
    /// Opens every archive without building the merged file index — everything <see cref="Load"/>
    /// does except the final <see cref="Rebuild"/> call. For a caller that only ever needs
    /// <see cref="ReadOriginal"/> (both CLI mod commands: neither browses <see cref="Files"/> or the
    /// fragment-override index, they just diff or splice against the archives' own bytes),
    /// <see cref="Rebuild"/>'s <c>BuildMergedFiles</c> pass is pure waste — a `VfsFile` record
    /// allocated for every entry in every mounted archive, on every single invocation, to populate a
    /// dictionary nobody reads. <see cref="ReadOriginal"/> only touches <see cref="_archives"/>/
    /// <see cref="_vanillaPatchArchive"/>, both fully populated by the time this returns, so it works
    /// identically to a <see cref="Load"/>'d instance — just without the unused index. <paramref
    /// name="cache"/> is still accepted and exposed via <see cref="Cache"/>: it's never touched by
    /// <c>BuildMergedFiles</c>'s type-sniffing here (that pass doesn't run), but <see
    /// cref="ReadOriginalHash"/> still reads and writes through it.
    /// </summary>
    public static GameVfs OpenForOriginalsOnly(
        GameInstall install,
        NameDatabase names,
        GameCache? cache = null,
        IProgress<string>? progress = null)
        => OpenArchives(install, names, cache, fcbDefinitions: null, progress);

    private static GameVfs OpenArchives(
        GameInstall install,
        NameDatabase names,
        GameCache? cache,
        FcbClassDefinitions? fcbDefinitions,
        IProgress<string>? progress)
    {
        var vfs = new GameVfs(names, cache ?? new GameCache(), fcbDefinitions ?? FcbClassDefinitions.Empty);
        vfs._volatileFat = Path.GetFullPath(install.PatchFat);

        foreach (string fat in install.EnumerateArchiveFats())
        {
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

        vfs._archivesByName = vfs._archives
            .GroupBy(a => a.Name)
            .ToDictionary(g => g.Key, g => g.ToArray());

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
                MergeDependencyLinks(files, progress);
            }
            _files = files;
        }
    }

    /// <summary>
    /// A container's decoded vanilla root — the ancestor every fragment override inside it is merged
    /// against (see <see cref="FragmentMerge.Resolve"/>). Callers resolving several fragments of one
    /// container decode once and share the result.
    /// </summary>
    private FcbObject DeserializeOriginal(uint containerHash)
        => FcbDocument.Deserialize(ReadOriginal(containerHash)
            ?? throw new InvalidOperationException($"No archive provides {containerHash:X8}."));

    /// <summary>
    /// The active fragment overrides for a container, or null when it has none. Every consumer —
    /// both <see cref="MergeFragments"/> passes and the read paths — shares this predicate, so they
    /// can never disagree about which containers count as overridden.
    /// </summary>
    private Dictionary<string, List<(IModLayer Layer, uint EntryHash)>>? OverridesFor(uint containerHash)
        => _fragmentOverrides.TryGetValue(containerHash, out var byFragment) && byFragment.Count > 0
            ? byFragment
            : null;

    /// <summary>
    /// A resolved fragment's byte length, for display only — a merge conflict (or vanished vanilla
    /// ancestor) must not take down the whole rebuild over one fragment nobody's reading yet. Falls
    /// back to the highest-priority contributor's raw length; the real error still surfaces the
    /// moment the fragment or its container is actually read, or the patch is built.
    /// </summary>
    private long SizeOfResolvedFragmentSafely(Lazy<FcbObject> vanillaRoot, string fragmentId, List<(IModLayer Layer, uint EntryHash)> layers)
    {
        try
        {
            return Encoding.UTF8.GetByteCount(
                FragmentMerge.Resolve(vanillaRoot.Value, fragmentId, layers, _fcbDefinitions));
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
            MergeDependencyLinks(files, progress);
            _files = files;
        }
    }

    /// <summary>Every archive and enabled mod-layer entry, named/typed and override-resolved — the
    /// part of the merged view that's cheap once the type cache is warm. Sets <see cref="UnnamedCount"/>.</summary>
    private Dictionary<uint, VfsFile> BuildMergedFiles(IProgress<string>? progress)
    {
        PreSniffUncachedTypes(progress);

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

        // Mod layers on top, in order — later wins. One Read serves both the size and, when nothing
        // beneath already settled it, the type sniff.
        foreach (var layer in _layers.Where(l => l.Enabled))
        {
            foreach (uint hash in layer.Hashes)
            {
                files.TryGetValue(hash, out var beneath);

                string? layerPath = layer.PathOf(hash);
                bool named = layerPath is not null || _names.TryResolve(hash, out layerPath!);

                byte[] content = ReadSafely(layer, hash);
                var type = beneath?.Type ?? FileTypeSniffer.Identify(content, layerPath);

                files[hash] = new VfsFile(
                    Hash: hash,
                    Path: named ? layerPath! : SyntheticPath(hash, type),
                    Type: type,
                    Size: content.LongLength,
                    SourceName: layer.Name,
                    SourceKind: SourceKind.Mod,
                    IsOverriding: beneath is not null,
                    NameIsKnown: named);
            }
        }

        return files;
    }

    /// <summary>
    /// The expensive half of <see cref="BuildMergedFiles"/>, done first and off to the side of its
    /// single-threaded, order-dependent fold: every unnamed, cacheable entry whose type isn't already
    /// known gets sniffed — an archive header read each, ~50,000 of them across a real install's
    /// unnamed entries — in parallel, and the results are folded into <see cref="_cache"/> afterward on
    /// one thread. <see cref="GameCache"/>'s writes aren't thread-safe, so the memoization itself can't
    /// happen inside the parallel step - the same split <see cref="MergeFragments"/> already uses for
    /// `.fcb` structure, and for the identical reason.
    ///
    /// <see cref="BuildMergedFiles"/>'s own loop can't simply run in parallel instead: its override
    /// resolution reads whatever the (single, shared, non-thread-safe) <c>files</c> dictionary
    /// currently holds for a hash and depends on archives being visited in a stable order to get
    /// patch.dat's priority right. None of that is touched here - this only ever populates
    /// <see cref="_cache"/>, which <see cref="ResolveType"/> then reads back through in the normal,
    /// sequential pass exactly as before, just always warm.
    ///
    /// A cache hit short-circuits before any of this runs, so once <see cref="GameCache"/> is warm
    /// (a second CLI invocation against the same install, or JackAll.App's second load) this is a
    /// near-instant no-op - the parallel sniff only ever does real work on a genuinely cold cache.
    /// </summary>
    private void PreSniffUncachedTypes(IProgress<string>? progress)
    {
        var needsSniff = new List<(DuniaArchive Archive, FatEntry Entry)>();
        foreach (var archive in _archives)
        {
            if (IsVolatile(archive))
            {
                continue; // never cached (see ResolveType) - rare enough not to bother parallelizing
            }
            foreach (var entry in archive.Entries)
            {
                if (_names.TryResolve(entry.Hash, out _) || _cache.TryGetType(entry.Hash, out _))
                {
                    continue; // settles for free, or already known - nothing to sniff
                }
                needsSniff.Add((archive, entry));
            }
        }

        if (needsSniff.Count == 0)
        {
            return;
        }

        progress?.Report($"Identifying {needsSniff.Count:N0} unnamed file(s)…");
        var sniffed = needsSniff
            .AsParallel()
            .Select(item => (item.Entry.Hash, Type: GameCache.Sniff(item.Archive, item.Entry)))
            .ToArray();

        foreach ((uint hash, FileType type) in sniffed)
        {
            _cache.SetType(hash, type);
        }
    }

    /// <summary>
    /// Adds fragment rows for `.fcb` files that split (see FcbXml.ListFragmentIds) — this is what lets
    /// the tree/file view browse into one with no dedicated UI, since a fragment's path is just the
    /// container's own path plus one more segment (docs/design/fcb-fragment-overlays.md). Mutates
    /// <paramref name="files"/> in place and refreshes <see cref="_fragmentMemo"/> to match.
    /// </summary>
    private void MergeFragments(Dictionary<uint, VfsFile> files, IProgress<string>? progress)
    {
        Dictionary<uint, IReadOnlyList<FcbFragmentInfo>> uncached = DecodeUncachedContainers(files, progress);
        BuildFragmentRows(files, uncached);
        MarkOverriddenContainers(files);
    }

    /// <summary>True when <see cref="_fragmentMemo"/>'s rows for this container were built from the
    /// same winning source it currently has. Shared by the scan and build passes, which must agree on
    /// it exactly (see <see cref="DecodeUncachedContainers"/>).</summary>
    private bool MemoIsCurrent(VfsFile container,
        out (SourceKind Kind, string SourceName, VfsFile[] Fragments) memo)
        => _fragmentMemo.TryGetValue(container.Hash, out memo)
           && memo.Kind == container.SourceKind
           && memo.SourceName == container.SourceName;

    /// <summary>
    /// Scans every `.fcb` container for those whose fragment structure isn't already known (cache
    /// miss, or non-cacheable because they're mod-overridden or live in the volatile patch.dat),
    /// decodes just that normally-tiny list in parallel, and returns the results that may not be
    /// persisted to <see cref="_cache"/>. A container with an active fragment override is queued even
    /// when the memo matches: <see cref="BuildFragmentRows"/> skips its memo shortcut for overridden
    /// containers and expects to find them here — both passes share <see cref="OverridesFor"/> and
    /// <see cref="MemoIsCurrent"/>, so they cannot disagree about which containers those are.
    /// </summary>
    /// <remarks>
    /// The ~46,000-iteration scan deliberately reaches no method containing a try/catch: a loop that
    /// merely *can* call one (DecodeFragments does) measured roughly 1000x slower than one that
    /// can't, even with the call taken 0 times — JIT behaviour around exception handlers, empirically
    /// confirmed. Decoding is confined to the short list and fanned out across cores; DecodeFragments
    /// is pure, and the non-thread-safe <see cref="_cache"/> writes fold back on this one thread.
    /// Progress reports from inside the parallel body because ToArray() blocks until every item is
    /// done — reporting from the fold would freeze the bar through the slow part.
    /// </remarks>
    private Dictionary<uint, IReadOnlyList<FcbFragmentInfo>> DecodeUncachedContainers(
        Dictionary<uint, VfsFile> files, IProgress<string>? progress)
    {
        var needsDecode = new List<(VfsFile Container, bool Cacheable)>();
        foreach (VfsFile c in files.Values)
        {
            if (c.Type.Extension != "fcb") continue;

            if (OverridesFor(c.Hash) is null && MemoIsCurrent(c, out _))
            {
                continue;
            }

            bool cacheable = IsStableSource(c);
            if (!cacheable || !_cache.TryGet(c.Hash, out _))
            {
                needsDecode.Add((c, cacheable));
            }
        }

        const int ReportEvery = 1_000;
        int decodedCount = 0;
        int total = needsDecode.Count;
        var decodedResults = needsDecode
            .AsParallel()
            .Select(item =>
            {
                IReadOnlyList<FcbFragmentInfo> fragments = DecodeFragments(item.Container);
                int done = Interlocked.Increment(ref decodedCount);
                if (done % ReportEvery == 0)
                {
                    progress?.Report($"Indexing .fcb structure… ({done:N0} / {total:N0})");
                }
                return (item.Container, item.Cacheable, Fragments: fragments);
            })
            .ToArray();

        var uncached = new Dictionary<uint, IReadOnlyList<FcbFragmentInfo>>();
        foreach ((VfsFile c, bool cacheable, IReadOnlyList<FcbFragmentInfo> decodedFragments) in decodedResults)
        {
            if (cacheable)
            {
                _cache.Set(c.Hash, decodedFragments);
            }
            else
            {
                uncached[c.Hash] = decodedFragments;
            }
        }
        return uncached;
    }

    /// <summary>
    /// Builds the fragment rows and folds them into <paramref name="files"/>, memoized by container
    /// hash so a container whose winning source hasn't changed reuses its previous rows outright.
    /// </summary>
    /// <remarks>
    /// The memo is read from the previous call's dictionary but built into a fresh one, swapped in
    /// once at the end: writing ~46,000 entries into the long-lived field in place measured ~1.7s
    /// against single-digit milliseconds for build-and-swap. A container with an active override
    /// skips the memo entirely, read and write — the memo key (SourceKind, SourceName) can't see an
    /// override's *content* change, so only skipping keeps an overridden row's size fresh.
    /// </remarks>
    private void BuildFragmentRows(
        Dictionary<uint, VfsFile> files, Dictionary<uint, IReadOnlyList<FcbFragmentInfo>> uncached)
    {
        var fragments = new Dictionary<uint, VfsFile>();
        var newFragmentMemo = new Dictionary<uint, (SourceKind Kind, string SourceName, VfsFile[] Fragments)>();
        foreach (VfsFile container in files.Values)
        {
            if (container.Type.Extension != "fcb")
            {
                continue;
            }

            var byFragment = OverridesFor(container.Hash);

            if (byFragment is null && MemoIsCurrent(container, out var memo))
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

            // The merge ancestor, decoded at most once per container no matter how many of its
            // fragments are overridden.
            var vanillaRoot = new Lazy<FcbObject>(() => DeserializeOriginal(container.Hash));

            var computed = new VfsFile[containerFragments.Count];
            for (int i = 0; i < containerFragments.Count; i++)
            {
                FcbFragmentInfo fragment = containerFragments[i];
                VfsFile vfsFragment = byFragment is not null && byFragment.TryGetValue(fragment.Id, out var contributors)
                    ? FragmentRow(container, fragment.Id,
                        size: SizeOfResolvedFragmentSafely(vanillaRoot, fragment.Id, contributors),
                        sourceName: AttributionOf(contributors),
                        sourceKind: SourceKind.Mod,
                        isOverriding: true)
                    : FragmentRow(container, fragment.Id,
                        size: fragment.Size,
                        sourceName: container.SourceName,
                        sourceKind: container.SourceKind,
                        isOverriding: false);
                computed[i] = vfsFragment;
                fragments[vfsFragment.Hash] = vfsFragment;
            }

            if (byFragment is not null)
            {
                // Every override id with no match above adds a child the vanilla container never had
                // (see FragmentMerge.Resolve's empty-ancestor case) — it still gets its own synthetic
                // row so it's browsable on its own, not just visible once its container is read whole.
                foreach ((string fragmentId, List<(IModLayer Layer, uint EntryHash)> contributors) in byFragment)
                {
                    if (containerFragments.Any(f => string.Equals(f.Id, fragmentId, StringComparison.OrdinalIgnoreCase)))
                    {
                        continue; // already produced above, as an override of an existing child
                    }

                    VfsFile added = FragmentRow(container, fragmentId,
                        size: SizeOfResolvedFragmentSafely(vanillaRoot, fragmentId, contributors),
                        sourceName: AttributionOf(contributors),
                        sourceKind: SourceKind.Mod,
                        isOverriding: false);
                    fragments[added.Hash] = added;
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
    }

    /// <summary>
    /// Patches each overridden container's own row: the built patch really does differ from vanilla
    /// for that hash, so it should read as modded, not just its fragment rows. Runs after
    /// <see cref="BuildFragmentRows"/>, which needs the container's original attribution for the
    /// un-overridden sibling fragments' rows. Only sets <see cref="VfsFile.FragmentOverrideSource"/> —
    /// the row's own bytes still come from wherever they always did.
    /// </summary>
    private void MarkOverriddenContainers(Dictionary<uint, VfsFile> files)
    {
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

    /// <summary>One synthetic row for a piece of a splitting `.fcb`, nested under its container.</summary>
    private static VfsFile FragmentRow(VfsFile container, string fragmentId, long size,
        string sourceName, SourceKind sourceKind, bool isOverriding)
    {
        string path = container.Path + "\\" + fragmentId;
        return new VfsFile(
            Hash: NameHash.Compute(path),
            Path: path,
            Type: new FileType("misc", "xml"),
            Size: size,
            SourceName: sourceName,
            SourceKind: sourceKind,
            IsOverriding: isOverriding,
            NameIsKnown: container.NameIsKnown,
            ContainerHash: container.Hash,
            FragmentId: fragmentId);
    }

    /// <summary>The mod name a row shows for the layers contributing to one fragment.</summary>
    private static string AttributionOf(List<(IModLayer Layer, uint EntryHash)> contributors)
        => contributors.Count == 1 ? contributors[0].Layer.Name : "multiple mods";

    /// <summary>
    /// Adds dependency-link rows for every `depload.dat` — a per-world/per-DLC dependency-preload
    /// index (see docs/docs/file-formats/depload.md), not a container — so its parent/children entries
    /// browse the same way an `.fcb`'s fragments do: one synthetic row per entry, nested under the
    /// depload file's own path (and, for a child, under its parent's own synthetic row - the format's
    /// real two-level shape). Mutates <paramref name="files"/> in place.
    /// </summary>
    /// <remarks>
    /// Unlike <see cref="MergeFragments"/> (~46,000 `.fcb` entries across the game, needs
    /// caching/parallelism to stay fast), there are only a handful of `depload.dat` files total — a
    /// single synchronous pass with no caching is fast enough.
    /// </remarks>
    private void MergeDependencyLinks(Dictionary<uint, VfsFile> files, IProgress<string>? progress)
    {
        List<VfsFile> containers = [.. files.Values
            .Where(f => f.FileName.EndsWith("_depload.dat", StringComparison.OrdinalIgnoreCase))];
        if (containers.Count == 0)
        {
            return;
        }

        var links = new Dictionary<uint, VfsFile>();
        foreach (VfsFile container in containers)
        {
            DepLoadFile depLoad;
            try
            {
                depLoad = DepLoadDocument.Decode(ReadFromSource(container));
            }
            catch
            {
                continue; // unreadable/corrupt - treat as having no links, same as GameCache's "unreadable -> Unknown" precedent
            }

            foreach (DepLoadParent parent in depLoad.Parents)
            {
                VfsFile parentLink = MakeDependencyLinkRow(container, container.Path, files, links, parent.Hash);
                links[parentLink.Hash] = parentLink;

                foreach (DepLoadChild child in parent.Children)
                {
                    VfsFile childLink = MakeDependencyLinkRow(
                        container, parentLink.Path, files, links, child.Hash, child.TypeHash);
                    links[childLink.Hash] = childLink;
                }
            }
        }

        foreach ((uint hash, VfsFile link) in links)
        {
            files[hash] = link;
        }
        progress?.Report($"Indexing depload.dat links… ({links.Count:N0})");
    }

    /// <summary>One parent or child entry of a `depload.dat`, as a browsable row nested under
    /// <paramref name="parentPath"/> - the container's own path for a parent entry, or the parent's own
    /// just-built row path for one of its children.</summary>
    private static VfsFile MakeDependencyLinkRow(
        VfsFile container, string parentPath, Dictionary<uint, VfsFile> files, Dictionary<uint, VfsFile> linksSoFar,
        uint targetHash, uint? childTypeHash = null)
    {
        files.TryGetValue(targetHash, out VfsFile? target);
        string label = target?.Path ?? $"0x{targetHash:X8}";
        string linkPath = parentPath + "\\" + label;
        uint linkHash = NameHash.Compute(linkPath);

        // A single large depload.dat can mint tens of thousands of these synthetic hashes (a real one
        // observed had 25,000+ children) - enough for an actual CRC32 collision against the shared
        // hash space to happen in practice, unlike the much smaller handful of .fcb fragments any one
        // container ever produces. Deterministically disambiguate rather than risk silently clobbering
        // a real file's row (or an earlier link's, in the rare case of a duplicate child within one
        // parent's own slice).
        int suffix = 0;
        while (files.ContainsKey(linkHash) || linksSoFar.ContainsKey(linkHash))
        {
            suffix++;
            linkHash = NameHash.Compute($"{linkPath}#{suffix}");
        }

        return new VfsFile(
            Hash: linkHash,
            Path: linkPath,
            Type: target?.Type ?? FileType.Unknown,
            Size: 0,
            SourceName: container.SourceName,
            SourceKind: container.SourceKind,
            IsOverriding: false,
            NameIsKnown: target is not null,
            LinkOwnerHash: container.Hash,
            LinkTargetHash: targetHash,
            LinkChildTypeHash: childTypeHash);
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
    ///
    /// Deliberately pure - no <see cref="_cache"/> write here, unlike before parallelization. Callers
    /// (currently just <see cref="MergeFragments"/>'s decode fan-out) run many of these concurrently
    /// across cores; the one call site is expected to fold anything that needs writing back into shared
    /// state (like <c>_cache.Set</c>, which isn't thread-safe) on a single thread afterward.
    /// </summary>
    private IReadOnlyList<FcbFragmentInfo> DecodeFragments(VfsFile container)
    {
        try
        {
            (FcbObject root, IReadOnlyList<long> childByteSizes) = FcbDocument.DeserializeWithChildSizes(ReadFromSource(container));
            return FcbXml.ListFragmentsWithSize(root, childByteSizes);
        }
        catch
        {
            return [];
        }
    }

    /// <summary>
    /// Reads the winning copy of a file — including a fragment row, decoded from its container on
    /// demand (fragments carry no stored bytes of their own; see <see cref="VfsFile.IsFragment"/>),
    /// and a container whose fragments are (partly) overridden, assembled from its base bytes plus
    /// whichever fragment overrides currently apply (see <see cref="ReadContainer"/>).
    /// </summary>
    public byte[] Read(uint hash)
    {
        // One snapshot for the whole resolution: a Rebuild on another thread swaps _files, and a
        // row and its container must come from the same generation of the dictionary.
        Dictionary<uint, VfsFile> files = _files;
        if (!files.TryGetValue(hash, out var file))
        {
            throw new KeyNotFoundException($"No file with hash {hash:X8}.");
        }

        if (file.IsDependencyLink)
        {
            // A dependency-link row has no bytes of its own - it's a reference, not content (see
            // docs/docs/file-formats/depload.md) - so this synthesizes a small human-readable summary
            // on demand, purely so the generic Export/Mirror actions have something sensible to do.
            files.TryGetValue(file.LinkOwnerHash!.Value, out VfsFile? owner);
            string ownerLabel = owner?.Path ?? $"0x{file.LinkOwnerHash:X8}";
            uint targetHash = file.LinkTargetHash!.Value;
            string resolved = files.TryGetValue(targetHash, out VfsFile? target)
                ? target.Path
                : "not resolved - no archive/mod entry has this hash";

            var summary = new StringBuilder();
            summary.AppendLine($"Dependency link (from {ownerLabel})");
            summary.AppendLine($"Hash: 0x{targetHash:X8}");
            summary.AppendLine($"Resolved: {resolved}");
            if (file.LinkChildTypeHash is { } typeHash) summary.AppendLine($"Type hash: 0x{typeHash:X8}");
            return new UTF8Encoding(false).GetBytes(summary.ToString());
        }

        if (file.IsFragment)
        {
            // This exact fragment is overridden - no need to touch the container at all, and its
            // sibling fragments' overrides (if any) are irrelevant to it either way.
            if (OverridesFor(file.ContainerHash!.Value) is { } byFragment
                && byFragment.TryGetValue(file.FragmentId!, out List<(IModLayer Layer, uint EntryHash)>? contributors))
            {
                string merged = FragmentMerge.Resolve(
                    DeserializeOriginal(file.ContainerHash!.Value), file.FragmentId!, contributors, _fcbDefinitions);
                return new UTF8Encoding(false).GetBytes(merged);
            }

            FcbObject root = FcbDocument.Deserialize(ReadFromSource(files[file.ContainerHash!.Value]));
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
        if (OverridesFor(container.Hash) is not { } byFragment)
        {
            return baseBytes;
        }

        FcbObject vanillaRoot = DeserializeOriginal(container.Hash);
        Dictionary<string, string> xmlByFragment = byFragment.ToDictionary(
            kv => kv.Key, kv => FragmentMerge.Resolve(vanillaRoot, kv.Key, kv.Value, _fcbDefinitions));
        return FcbAssembler.Apply(baseBytes, xmlByFragment);
    }

    /// <summary>
    /// The name to show a modder for where a file came from. A base-game archive (one sitting
    /// directly in Data_Win32) shows as its bare <see cref="VfsFile.SourceName"/>; a DLC archive
    /// always shows with its parent folder too - "dlc1/entitylibrary", "dlc_jungle/menus" - even when
    /// that bare name happens to be unique, since "which DLC" is exactly what a modder can't tell from
    /// the bare name alone. Mod-sourced files are returned unchanged - this distinction only applies
    /// to archives.
    /// </summary>
    public string DisplayModuleName(VfsFile file)
    {
        if (file.SourceKind != SourceKind.Archive
            || !_archivesByName.TryGetValue(file.SourceName, out DuniaArchive[]? candidates)
            || candidates.Length == 0)
        {
            return file.SourceName;
        }

        DuniaArchive archive;
        if (candidates.Length == 1)
        {
            archive = candidates[0];
        }
        else
        {
            // Several archives share this bare name - a fragment/link row's own hash is synthetic
            // (not a real archive entry), so probe with whichever ancestor hash actually lives in one
            // of their FAT indexes instead.
            uint probeHash = file.ContainerHash ?? file.LinkOwnerHash ?? file.Hash;
            archive = candidates.FirstOrDefault(a => a.Contains(probeHash)) ?? candidates[0];
        }

        return archive.Folder.Equals("base", StringComparison.OrdinalIgnoreCase)
            ? file.SourceName
            : $"{archive.Folder}/{archive.Name}";
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
    /// The content hash of <see cref="ReadOriginal"/>'s answer for <paramref name="hash"/>, without
    /// necessarily decompressing anything to get it: a cache hit returns immediately, and only a miss
    /// falls back to <see cref="ReadOriginal"/> itself (paying for the decompress it already has to do)
    /// to compute and remember one. Null exactly when <see cref="ReadOriginal"/> would return null - no
    /// archive currently provides this hash.
    /// </summary>
    /// <remarks>
    /// This is what lets <see cref="Mods.LegacyPatchImporter.Import"/> tell "genuinely differs from the
    /// base game" apart from "same as the base game" without ever touching the vanilla side's bytes for
    /// the (overwhelmingly common) case where nothing changed - only a real difference needs the actual
    /// bytes <see cref="ReadOriginal"/> provides. The first CLI run against a given install still pays
    /// full decompression cost to populate this, same as <see cref="GameCache"/>'s other sections; every
    /// later run reads it back from <see cref="GameInstall.CacheFile"/> in one gulp.
    /// </remarks>
    public ulong? ReadOriginalHash(uint hash)
    {
        if (_cache.TryGetContentHash(hash, out ulong cached))
        {
            return cached;
        }

        byte[]? original = ReadOriginal(hash);
        if (original is null)
        {
            return null;
        }

        ulong computed = XxHash64.HashToUInt64(original);
        _cache.SetContentHash(hash, computed);
        return computed;
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
    /// its header — the expensive path, and the only one worth caching. The cache is normally
    /// already warm here (see <see cref="PreSniffUncachedTypes"/>); the sniff-and-remember fallback
    /// just keeps this correct on its own.
    /// </summary>
    private FileType ResolveType(DuniaArchive archive, FatEntry entry, string? knownPath, bool cacheable)
    {
        if (knownPath is not null)
        {
            return FileTypeSniffer.Identify(ReadOnlySpan<byte>.Empty, knownPath);
        }
        if (!cacheable)
        {
            return GameCache.Sniff(archive, entry);
        }
        if (_cache.TryGetType(entry.Hash, out FileType known))
        {
            return known;
        }

        FileType sniffed = GameCache.Sniff(archive, entry);
        _cache.SetType(entry.Hash, sniffed);
        return sniffed;
    }

    /// <summary>
    /// Gives a nameless entry somewhere to live and something to be called. Without this an
    /// unnamed file is an unaddressable blob; with it, it's "an .xbt in _unknown\textures" that the
    /// texture handler will happily preview and replace.
    /// </summary>
    private static string SyntheticPath(uint hash, FileType type)
        => Path.Combine("_unknown", type.Category, $"{hash:x8}.{type.Extension}");

    /// <summary>A layer entry's bytes, or empty when the layer's backing file is unreadable — an
    /// unreadable override still gets a row (size 0, sniffed as unknown) rather than a crash.</summary>
    private static byte[] ReadSafely(IModLayer layer, uint hash)
    {
        try
        {
            return layer.Read(hash);
        }
        catch
        {
            return [];
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
