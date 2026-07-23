using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using JackAll.App.FileHandlers.Fcb;
using JackAll.App.SaveGames;
using JackAll.Core;
using JackAll.Core.Format.Fcb;
using JackAll.Core.Format.Sav;
using JackAll.Core.Mods;
using JackAll.Core.Naming;
using JackAll.Core.SaveGames;
using JackAll.Core.Vfs;

namespace JackAll.App;

/// <summary>A folder in the merged tree. Children are built once, on demand.</summary>
public sealed class FolderNode(string name, string fullPath)
{
    public string Name { get; } = name;
    public string FullPath { get; } = fullPath;
    public ObservableCollection<FolderNode> Children { get; } = [];
    public bool HasFiles { get; set; }
    public bool IsEmpty => Children.Count == 0 && !HasFiles;
    public bool ContainsMods { get; set; }

    /// <summary>
    /// Two-way bound to the TreeViewItem's own IsExpanded (see the implicit TreeViewItem style in
    /// MainWindow.xaml) — carried over by <see cref="MainViewModel.BuildTree"/> when it rebuilds the
    /// tree from scratch (every FolderNode is a brand-new instance each time, so without this every
    /// edit would silently collapse the whole tree back to nothing expanded). A plain mutable
    /// property, not INotifyPropertyChanged-backed, is enough: it's only ever set before this node is
    /// added to an ObservableCollection WPF is watching, exactly like <see cref="HasFiles"/>/
    /// <see cref="ContainsMods"/> already are.
    /// </summary>
    public bool IsExpanded { get; set; }

    public override string ToString() => Name;
}

/// <summary>
/// One node in the Mods tab's per-mod file tree (see <see cref="MainViewModel.SelectedModFiles"/>) —
/// either a folder or a leaf override/fragment entry. Rebuilt from scratch on every selection, unlike
/// <see cref="FolderNode"/>'s whole-VFS tree: a single mod's file count is small enough that there's
/// no expansion state worth preserving across rebuilds.
/// </summary>
public sealed class ModFileNode(string name, bool isFile)
{
    public string Name { get; } = name;
    public bool IsFile { get; } = isFile;
    public ObservableCollection<ModFileNode> Children { get; } = [];

    /// <summary>Only used while building the tree, to find an existing child by name in O(1) instead
    /// of scanning <see cref="Children"/>.</summary>
    internal Dictionary<string, ModFileNode> ChildIndex { get; } = new(StringComparer.OrdinalIgnoreCase);

    public override string ToString() => Name;
}

/// <summary>A mod row in the Mods tab — a zip, or the pinned workspace.</summary>
public sealed class ModRow(IModLayer layer, bool isWorkspace) : INotifyPropertyChanged
{
    public IModLayer Layer { get; } = layer;
    public bool IsWorkspace { get; } = isWorkspace;

    public string Name => IsWorkspace ? "workspace  (your edits - always applied last)" : Layer.Name;

    /// <summary>Whole-file overrides plus fragment overrides (each fragment counts as one, regardless
    /// of which container it's inside) — <see cref="IModLayer.Hashes"/> alone would undercount (or
    /// show zero) a layer that only stages `.fcb` fragments, since those are tracked separately in
    /// <see cref="IModLayer.FragmentOverrides"/>, not <c>Hashes</c>.</summary>
    public int FileCount => Layer.Hashes.Count + Layer.FragmentOverrides.Values.Sum(f => f.Count);
    public string FileCountText => FileCount == 1 ? "1 file" : $"{FileCount:N0} files";

    /// <summary>
    /// This <see cref="ModRow"/> instance is never replaced once created (see
    /// <see cref="MainViewModel.LoadModsFromConfig"/>) - only its underlying <see cref="Layer"/>'s
    /// content changes, in place, whenever something stages or reverts a file (e.g. <c>Stage</c>
    /// mutating the workspace's own dictionaries). <see cref="FileCount"/>/<see cref="FileCountText"/>
    /// are plain computed properties, so nothing re-reads them unless told to — call this after
    /// anything that could have changed <see cref="Layer"/>'s <see cref="IModLayer.Hashes"/>/
    /// <see cref="IModLayer.FragmentOverrides"/> (<see cref="MainViewModel.ReindexAsync"/> does, on
    /// every row, after every rebuild).
    /// </summary>
    public void NotifyFileCountChanged()
    {
        OnPropertyChanged(nameof(FileCount));
        OnPropertyChanged(nameof(FileCountText));
    }

    public bool Enabled
    {
        get => Layer.Enabled;
        set
        {
            if (Layer.Enabled == value) return;
            Layer.Enabled = value;
            OnPropertyChanged();
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

/// <summary>A row in the Saves tab — one parsed .sav file, plus its thumbnail decoded to a displayable
/// bitmap (kept out of JackAll.Core, same reasoning as the DDS→bitmap conversion for .xbt textures in
/// <c>XbtFileHandler</c>: Core stays free of a WPF dependency, so pixel decoding for display lives here).</summary>
public sealed class SaveRow(SaveGameInfo info)
{
    public SaveGameInfo Info { get; } = info;
    public string FileName => Path.GetFileName(Info.FilePath);
    public string WorldName => Info.WorldName;
    public string PlayerName => Info.PlayerName;
    public DateTime LastWriteTimeLocal { get; } = File.GetLastWriteTime(info.FilePath);
    public string LastWriteTimeText => LastWriteTimeLocal.ToString("g");
    public string PersistedObjectCountText => $"{Info.PersistedObjectCount:N0} persisted entities";
    public string DlcText => Info.ActiveDlcIds.Count > 0 ? string.Join(", ", Info.ActiveDlcIds) : "none";

    /// <summary>
    /// The "deploy to this save too" checkbox — visual only for now, plain get/set with no backing
    /// logic. Nothing reads this yet: it exists so the intended shape of a future "also patch the
    /// selected saves' own persisted entities, not just patch.dat" feature is visible in the UI before
    /// that feature itself is built.
    /// </summary>
    public bool IsSelectedForDeploy { get; set; }

    /// <summary>
    /// Null if the thumbnail couldn't be decoded — shown as "no preview" rather than failing the
    /// whole row, since the thumbnail is the one part of the format
    /// (reverse/dunia/savegame_format.md, Section 3) whose exact pixel layout is a best guess, not
    /// fully confirmed.
    /// </summary>
    public BitmapSource? Thumbnail { get; } = TryDecodeThumbnail(info);
    public bool HasThumbnail => Thumbnail is not null;

    private static BitmapSource? TryDecodeThumbnail(SaveGameInfo info)
    {
        try
        {
            // Channel order (BGRA vs RGBA) was never independently confirmed — BGRA is the guess
            // savegame_format.md settles on; a swapped-looking preview is the visible symptom if
            // that guess is wrong, not a crash.
            var bitmap = new WriteableBitmap(info.ThumbnailWidth, info.ThumbnailHeight, 96, 96, PixelFormats.Bgra32, null);
            bitmap.WritePixels(
                new Int32Rect(0, 0, info.ThumbnailWidth, info.ThumbnailHeight),
                info.ThumbnailPixels, info.ThumbnailWidth * 4, 0);
            bitmap.Freeze();
            return bitmap;
        }
        catch
        {
            return null;
        }
    }
}

public sealed class MainViewModel : INotifyPropertyChanged
{
    private GameVfs? _vfs;
    private Lazy<FcbStringCorpus>? _entityLibraryCorpus;
    private GameCache _cache = new();
    private NameDatabase? _names;
    private readonly Dictionary<string, FolderNode> _folderIndex = new(StringComparer.OrdinalIgnoreCase);
    private FolderNode? _selectedFolder;
    private VfsFile? _selectedFile;
    private IReadOnlyList<VfsFile> _selectedFiles = [];
    private bool _onlyMods;
    private string _filterText = "";
    private string _status = "Starting…";
    private bool _busy;

    public AppConfig Config { get; private set; } = AppConfig.Load();
    public GameInstall? Install { get; private set; }
    public FolderModLayer? Workspace { get; private set; }

    /// <summary>
    /// Archives (relative to <c>Data_Win32</c>) whose hash didn't match <see cref="VanillaHashes"/>
    /// on this session's <see cref="InitializeAsync"/> — empty when everything checked out. Left for
    /// code-behind to notice and show as a dialog: <see cref="MainViewModel"/> otherwise has no WPF
    /// dependency, and a warning that matters this much shouldn't ride on <see cref="Status"/>, which
    /// this same method immediately overwrites a few lines further down.
    /// </summary>
    public IReadOnlyList<string> ArchiveHashMismatches { get; private set; } = [];

    public ObservableCollection<ModRow> Mods { get; } = [];
    public ObservableCollection<FolderNode> Roots { get; } = [];
    public ObservableCollection<VfsFile> VisibleFiles { get; } = [];
    public ObservableCollection<SaveRow> Saves { get; } = [];

    private string _savesStatus = "Looking for saves…";
    public string SavesStatus
    {
        get => _savesStatus;
        private set { _savesStatus = value; OnPropertyChanged(); }
    }

    private SaveRow? _selectedSave;
    public SaveRow? SelectedSave
    {
        get => _selectedSave;
        set
        {
            if (_selectedSave == value) return;
            _selectedSave = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(HasSelectedSave));
            OnPropertyChanged(nameof(NoSelectedSave));
            SelectedSaveDetails = value is null ? null : new SaveDetailsViewModel(value, GetEntityLibraryCorpus);
        }
    }

    public bool HasSelectedSave => SelectedSave is not null;
    public bool NoSelectedSave => SelectedSave is null;

    private SaveDetailsViewModel? _selectedSaveDetails;
    public SaveDetailsViewModel? SelectedSaveDetails
    {
        get => _selectedSaveDetails;
        private set { _selectedSaveDetails = value; OnPropertyChanged(); }
    }

    public MainViewModel()
    {
        // Reordering only means something with two or more movable mods - drives the Mods grid's
        // per-row up/down buttons (see MainWindow.xaml), which would otherwise be redundant clutter
        // on a single-mod list.
        Mods.CollectionChanged += (_, e) =>
        {
            OnPropertyChanged(nameof(HasMultipleMods));

            if (e.OldItems is not null)
            {
                foreach (ModRow row in e.OldItems)
                {
                    row.PropertyChanged -= ModRow_PropertyChanged;
                }
            }
            if (e.NewItems is not null)
            {
                foreach (ModRow row in e.NewItems)
                {
                    row.PropertyChanged += ModRow_PropertyChanged;
                }
            }
        };
    }

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
    /// override nested one level under its container. Rebuilt fresh on every call: cheap, since a
    /// single mod's file count is orders of magnitude smaller than the whole VFS tree
    /// <see cref="BuildTree"/> maintains incrementally.
    /// </summary>
    private static ObservableCollection<ModFileNode> BuildModFileTree(IModLayer layer)
    {
        var root = new ModFileNode("", isFile: false);

        foreach (uint hash in layer.Hashes)
        {
            InsertModFilePath(root, layer.PathOf(hash) ?? $"_hash\\{hash:x8}");
        }

        foreach ((uint containerHash, IReadOnlyList<FragmentOverride> fragments) in layer.FragmentOverrides)
        {
            string containerPath = layer.PathOf(containerHash) ?? $"_hash\\{containerHash:x8}.fcb";
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

    public string Status
    {
        get => _status;
        set { _status = value; OnPropertyChanged(); }
    }

    public bool Busy
    {
        get => _busy;
        set { _busy = value; OnPropertyChanged(); }
    }

    /// <summary>
    /// The "Show only mod files" filter - literally "did a mod layer win this hash". Rebuilding the
    /// tree (rather than just the file list) is what makes this filter the directory list too, by
    /// pruning away branches that carry no mod content.
    /// </summary>
    public bool OnlyMods
    {
        get => _onlyMods;
        set { _onlyMods = value; OnPropertyChanged(); BuildTree(); }
    }

    /// <summary>
    /// A partial-match search over every file's full path — while it's non-empty, the file list
    /// shows every match across the whole tree instead of just the selected folder (the folder tree
    /// stays put for navigation, it just stops constraining the list). '/' and '\' are treated as
    /// equivalent since paths mix both conventions. An <c>ext:xbt</c>-shaped token filters by file
    /// type instead (see <see cref="ParseFilter"/>) and can combine with plain text, e.g.
    /// <c>"ext:xbt cliff"</c>.
    /// </summary>
    public string FilterText
    {
        get => _filterText;
        set { _filterText = value; OnPropertyChanged(); RefreshFileList(debounce: true); }
    }

    public FolderNode? SelectedFolder
    {
        get => _selectedFolder;
        set { _selectedFolder = value; OnPropertyChanged(); RefreshFileList(); }
    }

    public VfsFile? SelectedFile
    {
        get => _selectedFile;
        set
        {
            _selectedFile = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(HasSelection));
            OnPropertyChanged(nameof(NoSelection));
            OnPropertyChanged(nameof(CanRevert));
            OnPropertyChanged(nameof(SizeText));
            OnPropertyChanged(nameof(OriginText));
            OnPropertyChanged(nameof(HashText));
            OnPropertyChanged(nameof(PathText));
            OnPropertyChanged(nameof(ModOrigin));
            OnPropertyChanged(nameof(NamingNote));
            OnPropertyChanged(nameof(HasNamingNote));
            OnPropertyChanged(nameof(HasOriginal));
        }
    }

    /// <summary>All rows currently selected in the Files tab's grid, kept in sync from code-behind.</summary>
    public IReadOnlyList<VfsFile> SelectedFiles => _selectedFiles;

    public int SelectedCount => _selectedFiles.Count;
    public bool HasSelection => SelectedCount == 1;
    public bool NoSelection => SelectedCount == 0;
    public bool IsMultiSelection => SelectedCount > 1;
    public bool CanRevert => SelectedFile?.IsModded == true;

    /// <summary>Whether "Export original…" has anything to export - a mod-added file (or, for a
    /// fragment, one whose container itself was added entirely by a mod) has no base game version to
    /// compare against or fall back to.</summary>
    public bool HasOriginal => SelectedFile is { } f && ReadOriginal(f) is not null;

    public string MultiSelectCountText => $"{SelectedCount:N0} files selected";
    public string MultiSelectSizeText => FormatSize(_selectedFiles.Sum(f => f.Size));

    /// <summary>Called from code-behind whenever the Files tab's grid selection changes.</summary>
    public void SetSelectedFiles(IReadOnlyList<VfsFile> files)
    {
        _selectedFiles = files;
        SelectedFile = files.Count == 1 ? files[0] : null;
        OnPropertyChanged(nameof(SelectedFiles));
        OnPropertyChanged(nameof(SelectedCount));
        OnPropertyChanged(nameof(IsMultiSelection));
        OnPropertyChanged(nameof(MultiSelectCountText));
        OnPropertyChanged(nameof(MultiSelectSizeText));
    }

    public string SizeText => SelectedFile is { } f ? FormatSize(f.Size) : string.Empty;

    public string OriginText => SelectedFile switch
    {
        null => string.Empty,
        { IsModded: true } => "mod",
        var f => $"archive: {f.SourceName}",
    };

    /// <summary>Which mod supplied this file, and whether that meant overriding the base game.</summary>
    public string ModOrigin => SelectedFile switch
    {
        { FragmentOverrideSource: { } source } => $"Mod: {source}  (overrides one or more fragments inside this file)",
        { IsModded: true } f => f.IsOverriding ? $"Mod: {f.SourceName}  (overrides the base game file)" : $"Mod: {f.SourceName}",
        _ => string.Empty,
    };

    public string HashText => SelectedFile is { } f ? $"{f.Hash:X8}" : string.Empty;
    public string PathText => SelectedFile?.Path ?? string.Empty;

    public bool HasNamingNote => SelectedFile is { NameIsKnown: false };

    public string NamingNote => SelectedFile is { NameIsKnown: false }
        ? "This file's real name is unknown - it's addressed by hash. Edits still work."
        : string.Empty;

    /// <summary>
    /// Two phases, so the window doesn't sit blank while the game's ~214,000 archive entries and
    /// ~46,000 `.fcb` containers get indexed. Phase 1 opens the archives, resolves every entry's name
    /// and type (fast once <see cref="_cache"/> is warm — a cold first run is the one case that still
    /// pays for real header reads, see <see cref="GameCache"/>), layers the configured mods on top,
    /// and shows the result: a browsable filesystem plus a populated Mods tab. Phase 2
    /// (<see cref="LoadFragmentsAsync"/>) runs in the background right after, decoding which `.fcb`
    /// files split into pieces — the one pass that's genuinely expensive on *every* launch, cache or
    /// not (see <see cref="GameVfs.LoadFragments"/>) — and folds those rows in once it's done.
    /// </summary>
    public async Task InitializeAsync()
    {
        Busy = true;
        try
        {
            var install = GameInstall.TryOpen(Config.GamePath, out _);
            if (install is null)
            {
                Status = "Pick your Far Cry 2 folder to get started.";
                return;
            }

            Install = install;
            Directory.CreateDirectory(AppConfig.WorkspaceDir);

            ArchiveHashMismatches = await Task.Run(() =>
                VanillaHashes.Load(AppConfig.VanillaHashesFile)
                    .FindMismatches(install.DataDir, install.EnumerateBaseArchiveRelativePaths()));

            var progress = new Progress<string>(s => Status = s);
            var names = await Task.Run(() => NameDatabase.Load(AppConfig.NamesFile));
            _names = names;
            _cache = await Task.Run(() => GameCache.Load(AppConfig.CacheFile));

            GameVfs vfs = await Task.Run(() => GameVfs.Load(
                install, names, _cache, FcbDefinitionsProvider.Value.Value, progress, includeFragments: false));
            _vfs = vfs;

            Workspace = new FolderModLayer(AppConfig.WorkspaceDir, "workspace");

            LoadModsFromConfig();

            // Applies the real layers (workspace + configured mods) before first paint - anything
            // already staged from a previous session has to show as modded the moment the window
            // opens, not only after the user's first edit this session actually calls Reindex().
            // includeFragments stays false here so this doesn't also pay for the full .fcb decode
            // pass (that's still LoadFragmentsAsync's job, right below).
            await ReindexAsync(includeFragments: false);

            Status = $"{vfs.Files.Count:N0} files across {vfs.Archives.Count} archives"
                   + $"  •  {vfs.UnnamedCount:N0} with unknown names"
                   + $"  •  {names.Count:N0} names known - indexing .fcb structure…";
        }
        finally
        {
            Busy = false;
        }

        _ = LoadFragmentsAsync();
    }

    /// <summary>
    /// The deferred half of <see cref="InitializeAsync"/>: decodes `.fcb` fragment structure for
    /// everything currently in the merged view and folds the resulting rows in, entirely on a
    /// background thread. <see cref="GameVfs.LoadFragments"/> takes its own lock (shared with
    /// <see cref="GameVfs.Rebuild"/>), so this is safe to run alongside a user-triggered
    /// <see cref="Reindex"/> that lands mid-flight — whichever finishes last simply wins, and neither
    /// call ever touches <see cref="_vfs"/>'s dictionaries from two threads at once.
    /// </summary>
    private async Task LoadFragmentsAsync()
    {
        if (_vfs is null) return;

        GameVfs vfs = _vfs;
        var progress = new Progress<string>(s => Status = s);
        Busy = true;
        try
        {
            await Task.Run(() => vfs.LoadFragments(progress));

            // First launch (or after a game update) this is where the header reads and `.fcb` decodes
            // happened; writing them down means no launch ever pays for them again.
            if (_cache.IsDirty)
            {
                await Task.Run(() => _cache.Save(AppConfig.CacheFile));
            }
        }
        catch (Exception ex)
        {
            Status = $"Couldn't finish indexing .fcb structure: {ex.Message}";
            return;
        }
        finally
        {
            Busy = false;
        }

        BuildTree();
        Status = $"{vfs.Files.Count:N0} files across {vfs.Archives.Count} archives"
               + $"  •  {vfs.UnnamedCount:N0} with unknown names";
    }

    /// <summary>
    /// The reverse hash -&gt; string dictionary <see cref="SaveGames.SaveGameReferenceResolver"/> resolves
    /// a selected save's remaining hashes against - built once per session, on first use, and cached
    /// (see <see cref="_entityLibraryCorpus"/>): harvesting is a real cost (tens of thousands of
    /// strings across several multi-MB files), and the entitylibrary set essentially never changes
    /// mid-session. Empty (not null) before <see cref="_vfs"/> is loaded, so a save opened before the
    /// game install finishes indexing just shows no extra resolutions yet rather than failing.
    /// </summary>
    private FcbStringCorpus GetEntityLibraryCorpus()
    {
        if (_vfs is not { } vfs)
        {
            return new FcbStringCorpus();
        }

        _entityLibraryCorpus ??= new Lazy<FcbStringCorpus>(() => BuildEntityLibraryCorpus(vfs));
        return _entityLibraryCorpus.Value;
    }

    /// <summary>Harvests every non-fragment <c>.fcb</c> whose filename contains "entitylibrary" out of
    /// the merged view - base game, every DLC, and any patch/mod override, exactly as the engine would
    /// currently see them. See <see cref="SaveGames.SaveGameReferenceResolver"/> for why this specific
    /// file set is the target.</summary>
    private static FcbStringCorpus BuildEntityLibraryCorpus(GameVfs vfs)
    {
        var corpus = new FcbStringCorpus();
        foreach (VfsFile file in vfs.Files.Values)
        {
            if (file.IsFragment || file.Type.Extension != "fcb"
                || !file.FileName.Contains("entitylibrary", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            try
            {
                FcbObject root = FcbDocument.Deserialize(vfs.Read(file.Hash));
                corpus.AddTree(root, vfs.Definitions);
            }
            catch (InvalidDataException)
            {
                // Corrupt/unreadable entry - skip it rather than losing the whole corpus over one file.
            }
        }
        return corpus;
    }

    /// <summary>
    /// Populates the Saves tab from <see cref="SaveGameLocator.SavedGamesFolder"/>. Independent of
    /// <see cref="Install"/>/<see cref="_vfs"/> — a save is read from the user's Documents folder, not
    /// the game install — so this can run in parallel with <see cref="InitializeAsync"/> rather than
    /// waiting on it. A .sav that fails to parse is skipped rather than failing the whole tab: the
    /// format (reverse/dunia/savegame_format.md) was derived from one real save, and this is the
    /// bulk-exposure test of whether it generalizes across a player's other saves too.
    /// </summary>
    public async Task LoadSavesAsync()
    {
        List<SaveRow> rows;
        int failed = 0;
        try
        {
            (rows, failed) = await Task.Run(() =>
            {
                var loaded = new List<SaveRow>();
                int failedCount = 0;
                foreach (string path in SaveGameLocator.EnumerateSaveFiles())
                {
                    try
                    {
                        loaded.Add(new SaveRow(SaveGameDocument.Read(path)));
                    }
                    catch
                    {
                        failedCount++; // corrupt file, or a save shaped differently than the one this format was derived from
                    }
                }
                loaded.Sort((a, b) => b.LastWriteTimeLocal.CompareTo(a.LastWriteTimeLocal)); // newest first
                return (loaded, failedCount);
            });
        }
        catch (Exception ex)
        {
            SavesStatus = $"Couldn't read the saves folder: {ex.Message}";
            return;
        }

        Saves.Clear();
        foreach (SaveRow row in rows)
        {
            Saves.Add(row);
        }

        SavesStatus = Saves.Count == 0
            ? $"No saves found in {SaveGameLocator.SavedGamesFolder}"
            : $"{Saves.Count:N0} save(s) found"
              + (failed > 0 ? $"  •  {failed} couldn't be read" : string.Empty);
    }

    /// <summary>Re-reads one save's own metadata off disk and swaps its <see cref="SaveRow"/> in
    /// <see cref="Saves"/> for a fresh one - called after writing edits back into that save's file (see
    /// <c>MainWindow.xaml.cs</c>'s <c>OpenSaveXmlEditorTab</c>), so the sidebar's persisted-object-count/
    /// thumbnail/etc. don't keep showing stale pre-edit values. Narrower than re-running
    /// <see cref="LoadSavesAsync"/> (which would reset the whole list and selection for an unrelated
    /// reason) - re-selects the refreshed row only if it was already selected, which also re-triggers
    /// <see cref="SelectedSaveDetails"/>'s own reload.</summary>
    public void RefreshSaveRow(string filePath)
    {
        int index = -1;
        for (int i = 0; i < Saves.Count; i++)
        {
            if (string.Equals(Saves[i].Info.FilePath, filePath, StringComparison.OrdinalIgnoreCase))
            {
                index = i;
                break;
            }
        }
        if (index < 0) return;

        var refreshed = new SaveRow(SaveGameDocument.Read(filePath));
        bool wasSelected = ReferenceEquals(SelectedSave, Saves[index]);
        Saves[index] = refreshed;
        if (wasSelected)
        {
            SelectedSave = refreshed;
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
    }

    private void BuildTree()
    {
        if (_vfs is null) return;

        string? previous = SelectedFolder?.FullPath;

        // Every FolderNode below is a brand-new instance - captured before _folderIndex is cleared,
        // so the tree doesn't silently collapse back to nothing expanded on every edit (see
        // FolderNode.IsExpanded).
        var previouslyExpanded = new HashSet<string>(
            _folderIndex.Values.Where(n => n.IsExpanded).Select(n => n.FullPath),
            StringComparer.OrdinalIgnoreCase);

        var root = new FolderNode("", "");
        _folderIndex.Clear();
        _folderIndex[""] = root;

        foreach (VfsFile file in _vfs.Files.Values)
        {
            FolderNode node = EnsureFolder(_folderIndex, root, file.Directory, previouslyExpanded);
            node.HasFiles = true;
            if (file.IsModded)
            {
                // Light up the whole path to a modded file, so you can find your edits by
                // descending the tree instead of remembering where you put them.
                for (FolderNode? n = node; n is not null; n = ParentOf(_folderIndex, n))
                {
                    n.ContainsMods = true;
                }
            }
        }

        SortRecursively(root);
        if (OnlyMods)
        {
            PruneToModsOnly(root);
        }

        Roots.Clear();
        foreach (FolderNode child in root.Children)
        {
            Roots.Add(child);
        }

        SelectedFolder = previous is not null
                          && _folderIndex.TryGetValue(previous, out FolderNode? restored)
                          && (!OnlyMods || restored.ContainsMods)
            ? restored
            : Roots.FirstOrDefault();
    }

    /// <summary>The folder node for an archive-relative directory path, if the tree currently has one.</summary>
    public FolderNode? FindFolder(string directory) => _folderIndex.GetValueOrDefault(directory);

    /// <summary>
    /// The chain of folders from a top-level root down to (and including) <paramref name="node"/> —
    /// what code-behind needs to expand/reveal a folder in the tree view that isn't already showing.
    /// </summary>
    public IReadOnlyList<FolderNode> GetAncestorChain(FolderNode node)
    {
        var chain = new List<FolderNode>();
        for (FolderNode? current = node; current is not null; current = ParentOf(_folderIndex, current))
        {
            chain.Insert(0, current);
        }
        return chain;
    }

    /// <summary>Drops every branch that carries no mod content, for the "Show only mod files" filter.</summary>
    private static void PruneToModsOnly(FolderNode node)
    {
        var kept = node.Children.Where(c => c.ContainsMods).ToList();
        node.Children.Clear();
        foreach (FolderNode child in kept)
        {
            PruneToModsOnly(child);
            node.Children.Add(child);
        }
    }

    private static FolderNode? ParentOf(Dictionary<string, FolderNode> index, FolderNode node)
    {
        string? parent = Path.GetDirectoryName(node.FullPath);
        return string.IsNullOrEmpty(parent) ? null : index.GetValueOrDefault(parent);
    }

    private static FolderNode EnsureFolder(
        Dictionary<string, FolderNode> index, FolderNode root, string directory, HashSet<string> previouslyExpanded)
    {
        if (string.IsNullOrEmpty(directory))
        {
            return root;
        }
        if (index.TryGetValue(directory, out FolderNode? existing))
        {
            return existing;
        }

        string parentPath = Path.GetDirectoryName(directory) ?? string.Empty;
        FolderNode parent = EnsureFolder(index, root, parentPath, previouslyExpanded);

        var node = new FolderNode(Path.GetFileName(directory), directory)
        {
            IsExpanded = previouslyExpanded.Contains(directory),
        };
        parent.Children.Add(node);
        index[directory] = node;
        return node;
    }

    private static void SortRecursively(FolderNode node)
    {
        var sorted = node.Children.OrderBy(c => c.Name, StringComparer.OrdinalIgnoreCase).ToList();
        node.Children.Clear();
        foreach (FolderNode child in sorted)
        {
            SortRecursively(child);
            node.Children.Add(child);
        }
    }

    private CancellationTokenSource? _refreshCts;
    private const int FilterDebounceMilliseconds = 250;

    /// <summary>
    /// Kicks off (re)computing the file list without blocking the caller. <paramref name="debounce"/>
    /// is for the filter textbox specifically — every keystroke calls this, and without a short delay
    /// each one would start scanning the ~150,000-file merged view before the previous scan even
    /// finished. Cancelling the previous run (rather than letting stale ones finish and overwrite a
    /// newer result) is what makes it safe to fire on every keystroke at all.
    /// </summary>
    private void RefreshFileList(bool debounce = false)
    {
        _refreshCts?.Cancel();
        _refreshCts?.Dispose();
        var cts = new CancellationTokenSource();
        _refreshCts = cts;
        _ = RefreshFileListAsync(debounce, cts.Token);
    }

    private async Task RefreshFileListAsync(bool debounce, CancellationToken token)
    {
        if (debounce)
        {
            try
            {
                await Task.Delay(FilterDebounceMilliseconds, token);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }

        if (_vfs is null)
        {
            VisibleFiles.Clear();
            return;
        }

        GameVfs vfs = _vfs;
        (string[] includes, string[] excludes, string? extFilter) = ParseFilter(_filterText);
        string? folderPath = SelectedFolder?.FullPath;
        bool onlyMods = OnlyMods;

        List<VfsFile>? matches;
        try
        {
            // The scan itself (a substring match over every file, when a filter is active) is real
            // CPU work over a large collection - running it on a background thread is what actually
            // keeps the UI thread free while it happens, debounce or not.
            matches = await Task.Run(() =>
            {
                IEnumerable<VfsFile> files;
                if (includes.Length > 0 || excludes.Length > 0 || extFilter is not null)
                {
                    files = vfs.Files.Values.Where(f =>
                    {
                        var normalizedPath = NormalizeSlashes(f.Path);
                        // Filter for exclusion first, skip file early
                        if (excludes.Length > 0 && excludes.Any(x => normalizedPath.Contains(x, StringComparison.OrdinalIgnoreCase)))
                            return false;
                        
                        // Include and extension comes after that
                        var extMatch = extFilter is null || string.Equals(f.Type.Extension, extFilter, StringComparison.OrdinalIgnoreCase);
                        var includesMatch = includes.Length == 0 || includes.All(x => normalizedPath.Contains(x, StringComparison.OrdinalIgnoreCase));
                        
                        return extMatch && includesMatch;
                    });
                }
                else if (folderPath is not null)
                {
                    files = vfs.Files.Values
                        .Where(f => string.Equals(f.Directory, folderPath, StringComparison.OrdinalIgnoreCase));
                }
                else
                {
                    return null;
                }

                if (onlyMods)
                {
                    files = files.Where(f => f.IsModded);
                }

                token.ThrowIfCancellationRequested();
                return files.OrderBy(f => f.FileName, StringComparer.OrdinalIgnoreCase).ToList();
            }, token);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        if (token.IsCancellationRequested) return;

        VisibleFiles.Clear();
        if (matches is not null)
        {
            foreach (VfsFile file in matches)
            {
                VisibleFiles.Add(file);
            }
        }
    }

    private static string NormalizeSlashes(string path) => path.Replace('/', '\\');

    /// <summary>
    /// Pulls an <c>ext:xbt</c>-shaped token (matched against <see cref="VfsFile.Type"/>'s already
    /// lowercased, dot-less extension - see <c>FileTypeSniffer.Identify</c>) out of the filter text,
    /// leaving whatever's left as the ordinary path substring needle. Whitespace-delimited, so
    /// <c>"ext:xbt cliff"</c> combines both: only .xbt files whose path also contains "cliff".
    /// </summary>
    private static (string[] Includes, string[] Excludes, string? Extension) ParseFilter(string filterText)
    {
        string? extension = null;
        var includes = new List<string>();
        var excludes = new List<string>();

        foreach (string token in filterText.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            if (token.StartsWith("ext:", StringComparison.OrdinalIgnoreCase))
                extension = token[4..].TrimStart('.');
            else if (token.StartsWith("-", StringComparison.OrdinalIgnoreCase) && token.Length > 1)
                excludes.Add(token[1..]);
            else
                includes.Add(token);
        }

        return (
            includes.Select(NormalizeSlashes).ToArray(), 
            excludes.Select(NormalizeSlashes).ToArray(), 
            extension is { Length: > 0 } ? extension : null
        );
    }

    public byte[] Read(VfsFile file) => _vfs!.Read(file.Hash);

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
            ? (ReadOriginalFragment(file) is { } xml ? new UTF8Encoding(false).GetBytes(xml) : null)
            : _vfs!.ReadOriginal(file.Hash);

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
            LegacyPatchImporter.Import(zipPath, workspace, names, vfs.Definitions, vfs.ReadOriginal, progress));
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

    public void SaveConfig()
    {
        Config.GamePath = Install?.RootPath ?? Config.GamePath;
        Config.Mods.Clear();
        foreach (ModRow row in Mods.Where(m => !m.IsWorkspace))
        {
            Config.Mods.Add(new AppConfig.ModEntry(((ZipModLayer)row.Layer).ZipPath, row.Enabled));
        }
        Config.WorkspaceEnabled = Mods.FirstOrDefault(m => m.IsWorkspace)?.Enabled ?? Config.WorkspaceEnabled;
        Config.Save();
    }

    public static string FormatSize(long bytes) => bytes switch
    {
        < 1024 => $"{bytes} B",
        < 1024 * 1024 => $"{bytes / 1024.0:0.#} KB",
        _ => $"{bytes / (1024.0 * 1024):0.##} MB",
    };

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
