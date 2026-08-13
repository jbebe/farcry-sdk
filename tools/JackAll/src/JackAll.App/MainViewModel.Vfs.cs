using JackAll.Core.Format;
using JackAll.Core.Mods;
using JackAll.Core.Vfs;

namespace JackAll.App;

/// <summary>The VFS facade half of <see cref="MainViewModel"/>: reads through the merged filesystem,
/// workspace staging and reverts, legacy import, and building patch.dat.</summary>
public sealed partial class MainViewModel
{
    public byte[] Read(VfsFile file) => _vfs!.Read(file.Hash);

    /// <summary>Looks up any VfsFile by hash — used by a dependency-link row's "Go to file" jump (see
    /// <see cref="FileHandlers.FileHandlerCatalog"/>'s dependency-link case) to resolve what a link
    /// points to.</summary>
    public VfsFile? FindByHash(uint hash) => _vfs?.Files.GetValueOrDefault(hash);

    /// <summary>The merged filesystem's copy of <paramref name="path"/>, or null when no layer
    /// provides it (or it can't be read). For callers that know a game-relative path rather than a
    /// file they already have in hand - the .mgb editor resolving a material's texture, and the
    /// oasis string table finding its own source file.</summary>
    public byte[]? ReadByPath(string path)
    {
        try
        {
            return FindByHash(NameHash.Compute(path)) is { } file ? Read(file) : null;
        }
        catch
        {
            return null;
        }
    }

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
        => file.IsFragment
            ? (ReadOriginalFragment(file) is { } xml ? AppText.EncodeUtf8(xml) : null)
            : _vfs!.ReadOriginal(file.Hash);

    /// <summary>Whether the workspace already carries its own override for this exact file - queried
    /// before Mirror/Mirror original overwrite it (see MainWindow.xaml.cs's Mirror_Click/
    /// MirrorOriginal_Click), so the user gets a chance to back out first. Checks
    /// <see cref="IModLayer.FragmentOverrides"/> for a fragment row and <see cref="IModLayer.Hashes"/>
    /// otherwise, matching how <see cref="Replace"/> itself (via <c>FolderModLayer.Stage</c>) files a
    /// new override under one or the other.</summary>
    public bool IsStagedInWorkspace(VfsFile file) => file.IsFragment
        ? Workspace!.FragmentOverrides.TryGetValue(file.ContainerHash!.Value, out var fragments)
          && fragments.Any(f => f.EntryHash == file.Hash)
        : Workspace!.Hashes.Contains(file.Hash);

    /// <summary>
    /// Puts a replacement file into the workspace, so it wins over everything below it. For a
    /// fragment row this stages just that one child of a splitting `.fcb` (docs/design/
    /// fcb-fragment-overlays.md, Milestone 2) — <see cref="GameVfs"/> composes it into the container
    /// at read/build time instead of requiring a whole-file replacement for a one-entity edit.
    /// </summary>
    public void Replace(VfsFile file, byte[] content)
    {
        // A named container's fragment path (container's real path + its NN_Name.xml id) hashes back
        // to the right container on the next scan. An *unnamed* container's own path is only ever the
        // synthetic display placeholder (GameVfs.SyntheticPath) - it doesn't hash back to anything -
        // so its fragments have to go through the same _hash\ convention a plain unnamed file uses,
        // just one level deeper: _hash\<container hash>.fcb\<fragment id> (see ModPathHashing.Resolve).
        string? knownPath = file switch
        {
            { IsFragment: true, NameIsKnown: true } => file.Path,
            { IsFragment: true, NameIsKnown: false } => $"_hash\\{file.ContainerHash:x8}.fcb\\{file.FragmentId}",
            { NameIsKnown: true } => file.Path,
            _ => null,
        };

        Workspace!.Stage(file.Hash, knownPath, file.Type.Extension, content);
        Reindex();
    }

    /// <summary>
    /// Drops the workspace's copy. Only ever removes *your* edit — a mod zip's override is removed
    /// by disabling the mod, and a base-game file can't be deleted at all.
    /// </summary>
    public bool Revert(VfsFile file)
    {
        bool removed = Workspace!.Unstage(file.Hash);
        if (removed) Reindex();
        return removed;
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
