using JackAll.Core.Mods;
using JackAll.Core.Vfs;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;

namespace JackAll.App;

/// <summary>The Mods-tab half of <see cref="MainViewModel"/>: the layer stack, its selection state,
/// and the merged-view reindex that follows every change to it.</summary>
public sealed partial class MainViewModel
{
    public ObservableCollection<ModRow> Mods { get; } = [];

    // Toggling a mod's checkbox should stick across restarts immediately, same as every other
    // Mods-tab action (add/remove/reorder) - rather than only on window close, where an unclean
    // exit would silently drop it.
    private void ModRow_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ModRow.Enabled))
        {
            SaveConfig();
        }

        // The workspace row's file set changes live as the user stages/unstages files on the Files
        // tab (see ModRow.NotifyFileCountChanged) - if it's the one showing in the details panel right
        // now, that panel would otherwise go stale until the user clicks away and back.
        if (e.PropertyName == nameof(ModRow.FileCount) && sender is ModRow row && ReferenceEquals(row, SelectedMod))
        {
            SelectedModFiles = BuildModFileTree(row.Layer);
        }
    }

    /// <summary>True once there are at least two non-workspace mods, i.e. reordering is possible.</summary>
    public bool HasMultipleMods => Mods.Count(m => !m.IsWorkspace) > 1;

    private ModRow? _selectedMod;
    public ModRow? SelectedMod
    {
        get => _selectedMod;
        set
        {
            if (_selectedMod == value) return;
            _selectedMod = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(HasSelectedMod));
            OnPropertyChanged(nameof(NoSelectedMod));
            SelectedModFiles = value is null ? [] : BuildModFileTree(value.Layer);
        }
    }

    public bool HasSelectedMod => SelectedMod is not null;
    public bool NoSelectedMod => SelectedMod is null;

    private ObservableCollection<ModFileNode> _selectedModFiles = [];
    public ObservableCollection<ModFileNode> SelectedModFiles
    {
        get => _selectedModFiles;
        private set { _selectedModFiles = value; OnPropertyChanged(); }
    }

    /// <summary>
    /// Every file this layer overrides, as a folder tree — plain overrides at their real (or
    /// <c>_hash\</c>-addressed, see <see cref="IModLayer"/>) path, and each splitting-.fcb fragment
    /// override nested under its container, however deep its id runs. Rebuilt fresh on every call:
    /// cheap, since a single mod's file count is orders of magnitude smaller than the whole VFS tree
    /// <see cref="BuildTree"/> maintains incrementally.
    /// </summary>
    private ObservableCollection<ModFileNode> BuildModFileTree(IModLayer layer)
    {
        var root = new ModFileNode("", isFile: false);

        foreach (uint hash in layer.Hashes)
        {
            InsertModFilePath(root, layer.PathOf(hash) ?? $"_hash\\{hash:x8}");
        }

        foreach ((uint containerHash, IReadOnlyList<FragmentOverride> fragments) in layer.FragmentOverrides)
        {
            // layer.PathOf(containerHash) is always null here, never a "this name isn't known"
            // signal: IModLayer.PathOf resolves a *whole-file* override by its own hash (see
            // FolderModLayer's _absolutePaths, keyed by each staged entry's own hash), and a
            // fragment-only override never adds its container's hash there - only the fragment's
            // own hash goes in. The container's real name still has to come from the game's own
            // name database, same as everywhere else that resolves a container's path.
            string containerPath = (_names is not null && _names.TryResolve(containerHash, out string? named))
                ? named
                : $"_hash\\{containerHash:x8}.fcb";
            foreach (FragmentOverride fragment in fragments)
            {
                InsertModFilePath(root, $"{containerPath}\\{fragment.FragmentId}");
            }
        }

        SortModFileNodesRecursively(root);
        return root.Children;
    }

    private static void InsertModFilePath(ModFileNode root, string path)
    {
        ModFileNode current = root;
        string[] segments = path.Split('\\');
        for (int i = 0; i < segments.Length; i++)
        {
            bool isLeaf = i == segments.Length - 1;
            if (!current.ChildIndex.TryGetValue(segments[i], out ModFileNode? next))
            {
                next = new ModFileNode(segments[i], isLeaf);
                current.ChildIndex[segments[i]] = next;
                current.Children.Add(next);
            }
            current = next;
        }
    }

    /// <summary>Folders before files, alphabetical within each group - same convention as the Files
    /// tab's own tree (<see cref="SortRecursively"/>).</summary>
    private static void SortModFileNodesRecursively(ModFileNode node)
    {
        List<ModFileNode> sorted = [.. node.Children
            .OrderBy(c => c.IsFile)
            .ThenBy(c => c.Name, StringComparer.OrdinalIgnoreCase)];

        node.Children.Clear();
        foreach (ModFileNode child in sorted)
        {
            node.Children.Add(child);
            SortModFileNodesRecursively(child);
        }
    }

    private void LoadModsFromConfig()
    {
        Mods.Clear();
        foreach (AppConfig.ModEntry entry in Config.Mods)
        {
            string path = Path.IsPathRooted(entry.Path)
                ? entry.Path
                : Path.Combine(AppContext.BaseDirectory, entry.Path);

            if (!File.Exists(path))
            {
                continue; // a mod the user moved or deleted; dropped rather than crashing on start
            }

            Mods.Add(new ModRow(new ZipModLayer(path) { Enabled = entry.Enabled }, isWorkspace: false));
        }

        if (Workspace is not null)
        {
            Workspace.Enabled = Config.WorkspaceEnabled;
            Mods.Add(new ModRow(Workspace, isWorkspace: true));
        }
    }

    /// <summary>The layer stack, in apply order — workspace last, always.</summary>
    public IReadOnlyList<IModLayer> Layers =>
    [
        .. Mods.Where(m => !m.IsWorkspace).Select(m => m.Layer),
        .. Mods.Where(m => m.IsWorkspace).Select(m => m.Layer),
    ];

    /// <summary>
    /// Recomputes the merged view and rebuilds the tree. Fire-and-forget: the actual rebuild runs on
    /// a background thread, so a mod toggle never blocks the UI thread — even if it lands while the
    /// background `.fcb` indexing pass from <see cref="InitializeAsync"/> is still running. GameVfs's
    /// Rebuild and LoadFragments share one internal lock, so the two calls simply serialize there
    /// instead of racing; whichever finishes last is what the tree ends up showing.
    /// </summary>
    public void Reindex() => _ = ReindexAsync(includeFragments: true);

    /// <summary>
    /// Re-reads every configured mod's zip from disk (see <see cref="LoadModsFromConfig"/>) and
    /// re-scans the workspace folder (via <see cref="Reindex"/>'s own <c>Workspace.Rescan</c> call) -
    /// picks up a mod zip replaced/edited outside the app while it was running, without needing a
    /// restart. Drops any mod whose file has since vanished, same as startup does.
    /// </summary>
    public void RescanMods()
    {
        LoadModsFromConfig();
        Reindex();
        Status = "Mods rescanned from disk.";
    }

    /// <summary>
    /// <paramref name="includeFragments"/> is only ever false for <see cref="InitializeAsync"/>'s
    /// phase-1 call — it still needs the *real* layers applied (a workspace/mod edit staged in a
    /// previous session must show as modded the moment the window opens, not only after the first
    /// edit this session), just without paying for the full `.fcb` fragment decode before first
    /// paint. Every other caller goes through <see cref="Reindex"/>, which always wants the complete
    /// view.
    /// </summary>
    private async Task ReindexAsync(bool includeFragments)
    {
        if (_vfs is null) return;

        Workspace?.Rescan();
        GameVfs vfs = _vfs;
        IReadOnlyList<IModLayer> layers = Layers;
        try
        {
            await Task.Run(() => vfs.Rebuild(layers, includeFragments));
        }
        catch (Exception ex)
        {
            Status = $"Couldn't rebuild the file list: {ex.Message}";
            return;
        }

        // BuildTree() always ends by assigning SelectedFolder, and that setter already calls
        // RefreshFileList() on every set (even a no-op one) - a second call here would just cancel
        // that one via _refreshCts, wasting the scan and guaranteeing a caught-but-thrown
        // OperationCanceledException on every reindex.
        BuildTree();

        // Every ModRow's FileCount/FileCountText is a computed property over its (never-replaced)
        // Layer, so nothing re-reads them without this - see ModRow.NotifyFileCountChanged.
        foreach (ModRow row in Mods)
        {
            row.NotifyFileCountChanged();
        }

        // The layer stack just changed, so the xref overlay describing it is stale. Only the overlay
        // is rebuilt - the base-archive index underneath it is unaffected by any mod toggle.
        await RefreshXrefOverlayAsync();
    }
}
