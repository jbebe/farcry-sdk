using JackAll.App.FileHandlers.Fcb;
using JackAll.Core.Format;
using JackAll.Core.Format.Fcb;
using JackAll.Core.Mods;
using JackAll.Core.Vfs;
using JackAll.Tools.World;

namespace JackAll.App;

/// <summary>The VFS facade half of <see cref="MainViewModel"/>: reads through the merged filesystem,
/// workspace staging and reverts, legacy import, and building patch.dat.</summary>
public sealed partial class MainViewModel
{
    public byte[] Read(VfsFile file) => _vfs!.Read(file.Hash);

    /// <summary>Looks up any VfsFile by key — used by a dependency-link row's "Go to file" jump (see
    /// <see cref="FileHandlers.FileHandlerCatalog"/>'s dependency-link case) to resolve what a link
    /// points to.</summary>
    public VfsFile? FindByHash(ulong key) => _vfs?.Files.GetValueOrDefault(key);

    /// <summary>Every resolved path in the merged filesystem - the map editor filters this down to
    /// one world's sector and terrain files rather than probing synthesized paths against the
    /// hash-only index (CRC32 collisions with unrelated entries are real).</summary>
    public IEnumerable<string> AllKnownPaths
        => _vfs is null ? [] : _vfs.Files.Values.Where(f => f.NameIsKnown).Select(f => f.Path);

    /// <inheritdoc cref="GameVfs.ReadByPath"/>
    public byte[]? ReadByPath(string path) => _vfs?.ReadByPath(path);

    /// <summary>Selects <paramref name="file"/> as if the user had clicked it directly — used by a
    /// dependency-link row's "Go to file" button. Goes through <see cref="SetSelectedFiles"/> (not a
    /// bare <see cref="SelectedFile"/> assignment) so the Files tab's own multi-select state stays
    /// consistent with the new single selection.</summary>
    public void NavigateTo(VfsFile file)
    {
        // Recorded before the move, so Back returns to where the jump started. Only jumps go through
        // here (an xref row, a depload link, an .spk cross-reference) - an ordinary click in the file
        // grid doesn't, which is deliberate: the history is for following references, and mixing
        // scroll-and-click browsing into it would bury the one step the user actually wants back.
        PushNavigationHistory(SelectedFile);
        SetSelectedFiles([file]);
    }

    /// <summary>The vanilla text of a fragment row, ignoring mods/workspace - null when there's
    /// nothing to compare against (a mod-added container). <paramref name="file"/> must be a fragment
    /// (<see cref="VfsFile.IsFragment"/>).</summary>
    public string? ReadOriginalFragment(VfsFile file)
        => _vfs!.ReadOriginalFragment(file.ContainerHash!.Value, file.FragmentId!);

    /// <summary>One container's fragment rows by id, so a caller resolving many ids in the same
    /// container (the Library tab, routing archetypes back to their files) can cache the lookup
    /// instead of rescanning the whole index per click. Keyed via
    /// <see cref="FcbFragments.IdComparer"/>, so an entity row resolves from its bare
    /// <c>&lt;disEntityId&gt;.xml</c> too.</summary>
    public IReadOnlyDictionary<string, VfsFile> FragmentsOf(uint containerHash)
        => _vfs is null
            ? new Dictionary<string, VfsFile>()
            : _vfs.Files.Values
                .Where(f => f.ContainerHash == containerHash && f.FragmentId is not null)
                .ToDictionary(f => f.FragmentId!, FcbFragments.IdComparer);

    /// <summary>One fragment row by container and id — for a caller resolving a single id (the Map
    /// tab's entity jump), which shouldn't pay <see cref="FragmentsOf"/>'s whole-container
    /// dictionary just to read one entry out of it.</summary>
    public VfsFile? FindFragment(uint containerHash, string fragmentId)
        => _vfs?.Files.Values.FirstOrDefault(f =>
            f.ContainerHash == containerHash
            && f.FragmentId is not null
            && FcbFragments.IdComparer.Equals(f.FragmentId, fragmentId));

    /// <summary>What "saving" means for a fragment editor, wherever it was opened from: render the
    /// edited tree and stage it over <paramref name="file"/>.</summary>
    public Func<FcbObject, Task<string?>> StageFragmentEdits(VfsFile file)
        => async root =>
        {
            FcbClassDefinitions definitions = FcbDefinitionsProvider.Value.Value;
            string rendered = await Task.Run(() => FcbXml.ToXml(root, definitions));
            Replace(file, AppText.EncodeUtf8(rendered));
            return null;
        };

    /// <summary>
    /// The same for a whole <c>.fcb</c> container rather than one fragment of one. A fragment override is
    /// stored as the XML that <c>FcbAssembler</c> splices back in, but a container is the real file the
    /// game loads, so this has to stage binary.
    /// </summary>
    /// <remarks>
    /// <see cref="FcbDocument.Serialize"/> always writes the fully expanded form, so a container that
    /// used shared-data backreferences comes back larger than it went in. Tree-equal, not byte-equal.
    /// </remarks>
    public Func<FcbObject, Task<string?>> StageContainerEdits(VfsFile file)
        => async root =>
        {
            byte[] bytes = await Task.Run(() => FcbDocument.Serialize(root));
            Replace(file, bytes);
            return null;
        };

    /// <summary>
    /// The base game's own bytes for <paramref name="file"/>, ignoring every mod/workspace edit - null
    /// when there's nothing to compare against (a mod-added file, or a fragment whose container was
    /// added entirely by a mod). Backs both "Export original…" and the text handler's diff view.
    /// </summary>
    /// <remarks>
    /// A fragment carries no stored bytes of its own (see <see cref="VfsFile.IsFragment"/>), so it
    /// goes through <see cref="ReadOriginalFragment(VfsFile)"/> instead of <see cref="GameVfs.ReadOriginal"/>
    /// and its text is re-encoded to bytes here - both funnel through this one method so callers don't
    /// need to know which kind of row they're holding.
    /// </remarks>
    public byte[]? ReadOriginal(VfsFile file)
        => file switch
        {
            { IsFragment: true } => ReadOriginalFragment(file) is { } xml ? AppText.EncodeUtf8(xml) : null,
            _ => _vfs!.ReadOriginal(file.EngineHash),
        };

    /// <summary>Whether the workspace already carries its own override for this exact file - queried
    /// before Mirror/Mirror original overwrite it (see MainWindow.xaml.cs's Mirror_Click/
    /// MirrorOriginal_Click), so the user gets a chance to back out first. Checks
    /// <see cref="IModLayer.FragmentOverrides"/> for a fragment row and <see cref="IModLayer.Hashes"/>
    /// otherwise, matching how <see cref="Replace"/> itself (via <c>FolderModLayer.Stage</c>) files a
    /// new override under one or the other.</summary>
    public bool IsStagedInWorkspace(VfsFile file) => file switch
    {
        { IsFragment: true } => Workspace!.FragmentOverrides.TryGetValue(file.ContainerHash!.Value, out var fragments)
            && fragments.Any(f => FcbFragments.IdComparer.Equals(f.FragmentId, file.FragmentId)),
        _ => Workspace!.Hashes.Contains(file.EngineHash),
    };

    /// <summary>The relative path a row's override is staged at — null for an unnamed plain file,
    /// which stages under <c>_hash\</c> by its own hash instead (see ModPathHashing.Resolve; an
    /// unnamed *container's* fragments use the same convention one level deeper).</summary>
    private static string? StagePathOf(VfsFile file) => file switch
    {
        { IsFragment: true, NameIsKnown: false } => $"_hash\\{file.ContainerHash:x8}.fcb\\{file.FragmentId}",
        { NameIsKnown: true } => file.Path,
        _ => null,
    };

    /// <summary>The workspace layer's own key for a row's override — never the row's VFS key (a
    /// fragment's is synthetic).</summary>
    private static uint WorkspaceKeyOf(VfsFile file)
        => StagePathOf(file) is { } path ? FolderModLayer.StorageKeyOf(path) : file.EngineHash;

    /// <summary>
    /// Puts a replacement file into the workspace, so it wins over everything below it. For a
    /// fragment row this stages just that one child of a splitting `.fcb` (docs/design/
    /// fcb-fragment-overlays.md, Milestone 2) — <see cref="GameVfs"/> composes it into the container
    /// at read/build time instead of requiring a whole-file replacement for a one-entity edit.
    /// </summary>
    public void Replace(VfsFile file, byte[] content)
    {
        Workspace!.Stage(WorkspaceKeyOf(file), StagePathOf(file), file.Type.Extension, content);
        Reindex();
    }

    /// <summary>
    /// Drops the workspace's copy. Only ever removes *your* edit — a mod zip's override is removed
    /// by disabling the mod, and a base-game file can't be deleted at all.
    /// </summary>
    public bool Revert(VfsFile file)
    {
        bool removed = Workspace!.Unstage(WorkspaceKeyOf(file));
        if (removed) Reindex();
        return removed;
    }

    /// <summary>
    /// Archetype edits the enabled layers stage that a later entity library declares again - the file
    /// changes and the game reads the other copy. Resolved through the merged filesystem, so a layer
    /// that adds an archetype to a later library counts toward the answer.
    /// </summary>
    public async Task<IReadOnlyList<DeadEdit>> LintArchetypes(LibraryProfile profile = LibraryProfile.Client)
    {
        if (_vfs is null)
        {
            return [];
        }

        List<StagedFragment> staged = [.. ArchetypeLint.StagedFragmentsOf(Layers)];
        var progress = new Progress<string>(s => Status = s);
        List<string> paths = [.. AllKnownPaths];
        return await Task.Run(() => ArchetypeLint.Run(staged, paths, ReadByPath, profile, progress));
    }

    /// <summary>
    /// Converts a legacy mod (a zip carrying a full replacement patch.dat/patch.fat, the old
    /// build_patch.bat-style workflow) into the workspace's own format, keeping only what it actually
    /// changed relative to the true vanilla game - see <see cref="LegacyPatchImporter"/> for how that
    /// diff works. Runs on a background thread; <see cref="Reindex"/> picks up the newly staged files
    /// once it's done, same as any other workspace edit.
    /// </summary>
    public async Task<LegacyImportResult> ImportLegacyMod(string zipPath)
    {
        if (_vfs is not { } vfs || Workspace is not { } workspace || _names is not { } names)
        {
            throw new InvalidOperationException("No game install is loaded.");
        }

        var progress = new Progress<string>(s => Status = s);
        LegacyImportResult result = await Task.Run(() =>
            LegacyPatchImporter.Import(
                zipPath, workspace, names, vfs.Definitions, vfs.ReadOriginal, vfs.ReadOriginalHash, progress));
        Reindex();
        return result;
    }

    /// <summary>
    /// Compiles the enabled layers into patch.dat/patch.fat, on a background thread. Wraps
    /// <see cref="PatchBuilder.Build"/> rather than letting <c>MainWindow</c> call it directly so it
    /// can supply <see cref="GameVfs.ReadOriginal"/> without exposing <see cref="_vfs"/> itself —
    /// needed as the base a fragment override (see docs/design/fcb-fragment-overlays.md) splices onto
    /// when its container has no whole-file override of its own. On success, also re-opens <c>_vfs</c>'s
    /// mounted patch.fat/.dat — the one archive this build just replaced — since holding onto the old
    /// handle isn't just stale, it throws on the next read that happens to land on it (see the remarks
    /// on <see cref="GameVfs.ReloadPatchArchive"/>).
    /// </summary>
    public async Task<BuildResult> BuildPatch()
    {
        if (Install is not { } install || _vfs is not { } vfs)
        {
            throw new InvalidOperationException("No game install is loaded.");
        }
        IReadOnlyList<IModLayer> layers = Layers;
        BuildResult result = await Task.Run(() => PatchBuilder.Build(install, layers, vfs.ReadOriginal, vfs.Definitions));
        await Task.Run(vfs.ReloadPatchArchive);
        return result;
    }

    /// <summary>
    /// Re-opens the mounted patch.fat/.dat after anything else replaces them on disk outside
    /// <see cref="BuildPatch"/> — currently just <c>GameInstall.RestoreVanilla</c>
    /// (<c>MainWindow.RestoreVanilla_Click</c>). Same reasoning as <see cref="BuildPatch"/>'s own
    /// reload: <see cref="_vfs"/> is a session-long instance, and a stale archive handle risks more
    /// than stale answers (see <see cref="GameVfs.ReloadPatchArchive"/>). A no-op if nothing is loaded.
    /// </summary>
    public void ReloadPatchArchive() => _vfs?.ReloadPatchArchive();
}
