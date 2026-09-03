using JackAll.Core.Mods;
using JackAll.Core.Naming;
using JackAll.Core.Vfs;
using JackAll.Tools.Reach;
using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;

namespace JackAll.App;

/// <summary>The Files-tab half of <see cref="MainViewModel"/>: the folder tree, the filtered file
/// list, selection state, the details pane's display strings, and folder export.</summary>
public sealed partial class MainViewModel
{
    private readonly Dictionary<string, FolderNode> _folderIndex = new(StringComparer.OrdinalIgnoreCase);
    private FolderNode? _selectedFolder;
    private VfsFile? _selectedFile;
    private IReadOnlyList<VfsFile> _selectedFiles = [];
    private bool _onlyMods;
    private bool _hideUnused;
    private string _filterText = "";

    /// <summary>
    /// Which base-game files no engine code path can open. Loaded once and shared by every query
    /// below; <see cref="ReachList.Empty"/> when the asset is missing, which turns the whole
    /// feature off rather than failing.
    /// </summary>
    private static readonly Lazy<ReachList> Unreachable = new(() => ReachList.Load(AppConfig.ReachFile));
    private CancellationTokenSource? _exportCts;

    /// <summary>Where the selected fragment sits in its container, once the background lookup for it
    /// has landed. Null for every non-fragment row, and while the lookup is still running.</summary>
    private FragmentAncestry? _ancestry;
    private CancellationTokenSource? _ancestryCts;

    /// <summary>How often <see cref="ExportFiles"/> updates <see cref="Status"/> — often enough to
    /// look alive on a big subtree, rarely enough not to spend the export marshalling to the UI thread.</summary>
    private const int ExportReportEvery = 100;

    public ObservableCollection<FolderNode> Roots { get; } = [];
    public ObservableCollection<VfsFile> VisibleFiles { get; } = [];

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
    /// The "Hide unused game files" filter - drops every base-game file the reachability analysis
    /// proved the engine cannot open, and prunes the folders left holding nothing else. A file you
    /// have modded is never hidden, however dead the original was: it is your edit, and losing
    /// sight of it would be worse than the clutter.
    /// </summary>
    public bool HideUnused
    {
        get => _hideUnused;
        set { _hideUnused = value; OnPropertyChanged(); BuildTree(); }
    }

    /// <summary>Whether the shipped verdict list positively says the engine can never open this
    /// file. An <c>unknown</c> row is not unused - that is the case the analysis declined to
    /// decide, and hiding those would be exactly the false negative it refuses to make.</summary>
    public static bool IsUnusedFile(VfsFile file)
        => !file.IsModded && ReachHashOf(file) is { } hash && Unreachable.Value.IsUnused(hash);

    /// <summary>The hash the verdict list can answer for: a fragment has no engine hash of its own
    /// and is only ever as reachable as the container it lives inside.</summary>
    private static uint? ReachHashOf(VfsFile file)
        => file.ContainerHash ?? (file.IsSynthetic ? null : file.EngineHash);

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
        set
        {
            _selectedFolder = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(HasSelectedFolder));
            OnPropertyChanged(nameof(NoSelectedFolder));
            RefreshFileList();
        }
    }

    /// <summary>Whether the details pane has a folder to offer "Export folder…" for. Only consulted
    /// while nothing is selected in the file grid (see <see cref="NoSelection"/>), which is exactly
    /// the state browsing to a folder leaves you in.</summary>
    public bool HasSelectedFolder => SelectedFolder is not null;
    public bool NoSelectedFolder => SelectedFolder is null;

    public VfsFile? SelectedFile
    {
        get => _selectedFile;
        set
        {
            _selectedFile = value;
            RefreshAncestry(value);
            OnPropertyChanged();
            OnPropertyChanged(nameof(HasSelection));
            OnPropertyChanged(nameof(NoSelection));
            OnPropertyChanged(nameof(CanRevert));
            OnPropertyChanged(nameof(CanRevertFromWorkspace));
            OnPropertyChanged(nameof(SizeText));
            OnPropertyChanged(nameof(OriginText));
            OnPropertyChanged(nameof(HashText));
            OnPropertyChanged(nameof(PathText));
            OnPropertyChanged(nameof(ModOrigin));
            OnPropertyChanged(nameof(NamingNote));
            OnPropertyChanged(nameof(HasNamingNote));
            OnPropertyChanged(nameof(ReachNote));
            OnPropertyChanged(nameof(HasReachNote));
            OnPropertyChanged(nameof(HasOriginal));
            OnPropertyChanged(nameof(SelectionIsModel));
        }
    }

    /// <summary>All rows currently selected in the Files tab's grid, kept in sync from code-behind.</summary>
    public IReadOnlyList<VfsFile> SelectedFiles => _selectedFiles;

    public int SelectedCount => _selectedFiles.Count;
    public bool HasSelection => SelectedCount == 1;
    public bool NoSelection => SelectedCount == 0;
    public bool IsMultiSelection => SelectedCount > 1;
    public bool CanRevert => SelectedFile?.IsModded == true;

    /// <summary>Whether the Revert button can actually act on this row - the underlying
    /// <see cref="Workspace"/>.Unstage only ever removes the workspace's own whole-file override (see
    /// <see cref="Revert"/>), so this is narrower than <see cref="CanRevert"/> in two ways: a row whose
    /// only "modded" signal is a fragment inside it (<see cref="VfsFile.FragmentOverrideSource"/>) has
    /// nothing of its own to unstage, and a row overridden by some other mod zip isn't the workspace's
    /// to remove - see Revert_Click's own messages for what each of those cases tells the user instead.</summary>
    public bool CanRevertFromWorkspace => SelectedFile is { FragmentOverrideSource: null, SourceName: "workspace" };

    /// <summary>Whether "Export original…" has anything worth exporting separately from "Export" -
    /// requires <see cref="CanRevert"/> (an unmodded file's original is identical to what "Export"
    /// already produces, so the second button would just be a duplicate) and an actual base-game
    /// version to fall back to: a mod-added file (or, for a fragment, one whose container itself was
    /// added entirely by a mod) can be modded with nothing to compare against.</summary>
    public bool HasOriginal => CanRevert && SelectedFile is { } f && ReadOriginal(f) is not null;

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
        var f => $"archive: {ModuleNameFor(f)}",
    };

    /// <summary>The archive name to show for <paramref name="file"/> - disambiguated with its parent
    /// folder when another mounted archive shares the same bare name (see
    /// <see cref="GameVfs.DisplayModuleName"/>), without exposing <see cref="_vfs"/> itself.</summary>
    public string ModuleNameFor(VfsFile file) => _vfs?.DisplayModuleName(file) ?? file.SourceName;

    /// <summary>Which mod supplied this file, and whether that meant overriding the base game.</summary>
    public string ModOrigin => SelectedFile switch
    {
        { FragmentOverrideSource: { } source } => $"Mod: {source}  (overrides one or more fragments inside this file)",
        { IsModded: true } f => f.IsOverriding ? $"Mod: {f.SourceName}  (overrides the base game file)" : $"Mod: {f.SourceName}",
        _ => string.Empty,
    };

    /// <summary>Empty for a synthetic row — its key is tool-internal, not an engine hash anyone can
    /// address the file by.</summary>
    public string HashText => SelectedFile is { IsSynthetic: false } f ? $"{f.EngineHash:X8}" : string.Empty;
    public string PathText => SelectedFile?.Path ?? string.Empty;

    public bool HasNamingNote => SelectedFile is { NameIsKnown: false };

    public string NamingNote => SelectedFile is { NameIsKnown: false }
        ? "This file's real name is unknown - it's addressed by hash. Edits still work."
        : string.Empty;

    public bool HasAncestry => AncestryText.Length > 0;

    /// <summary>Which mission layer or library group the selected fragment lives in. A fragment id
    /// carries none of this, so without it there is no way to see from the file list that an entity
    /// even has a structural parent.</summary>
    public string AncestryText => _ancestry is { } a ? $"Lives under {a.Display}." : string.Empty;

    public bool HasLayerMismatchNote => LayerMismatchNote.Length > 0;

    public string LayerMismatchNote => MismatchNoteFor(_ancestry);

    /// <summary>The same note for a file that isn't the selected one - what the editor tab shows,
    /// where the details pane isn't on screen to carry it. Opening an editor decodes the container
    /// anyway, so this answers directly rather than through the background lookup.</summary>
    public string? LayerMismatchNoteFor(VfsFile file)
        => MismatchNoteFor(_vfs?.AncestryOf(file)) is { Length: > 0 } note ? note : null;

    /// <summary>The silent-wrong case: the entity's own mission component names one layer while the
    /// sector nests it under another. The nesting is what the game spawns from.</summary>
    private static string MismatchNoteFor(FragmentAncestry? ancestry)
        => ancestry is { IsLayerMismatch: true, ParentName: var parent }
            ? $"This entity's mission component names a different layer than the \"{parent}\" it sits "
              + "under. The game spawns it from where it sits, so the component alone changes nothing - "
              + "and a fragment override cannot move it between layers."
            : string.Empty;

    /// <summary>
    /// Looks up <paramref name="file"/>'s ancestry off the UI thread, because answering needs the
    /// whole container decoded and an entity library is 6 MB. Cancelling the previous lookup is what
    /// keeps arrowing down the file list from queuing one decode per row passed through.
    /// </summary>
    private void RefreshAncestry(VfsFile? file)
    {
        _ancestryCts?.Cancel();
        _ancestryCts?.Dispose();
        // Cleared, not just disposed: the early return below leaves this field live, and cancelling a
        // disposed source throws.
        _ancestryCts = null;
        SetAncestry(null);

        if (_vfs is not { } vfs || file is not { IsFragment: true })
        {
            return;
        }

        var cts = new CancellationTokenSource();
        _ancestryCts = cts;
        _ = LookUpAncestryAsync(vfs, file, cts.Token);
    }

    /// <summary>Clearing it matters as much as setting it: moving to a row with no ancestry has to
    /// take the previous row's line off the pane rather than leave it standing.</summary>
    private void SetAncestry(FragmentAncestry? ancestry)
    {
        _ancestry = ancestry;
        OnPropertyChanged(nameof(AncestryText));
        OnPropertyChanged(nameof(HasAncestry));
        OnPropertyChanged(nameof(LayerMismatchNote));
        OnPropertyChanged(nameof(HasLayerMismatchNote));
    }

    private async Task LookUpAncestryAsync(GameVfs vfs, VfsFile file, CancellationToken token)
    {
        FragmentAncestry? found;
        try
        {
            found = await Task.Run(() => vfs.AncestryOf(file), token);
        }
        catch (Exception ex) when (ex is OperationCanceledException or InvalidDataException
                                      or InvalidOperationException or KeyNotFoundException)
        {
            return;
        }

        if (!token.IsCancellationRequested)
        {
            SetAncestry(found);
        }
    }

    public bool HasReachNote => ReachNote.Length > 0;

    public string ReachNote => SelectedFile is { } file ? ReachNoteFor(file) : string.Empty;

    /// <summary>Why this file is dead, phrased for someone about to spend an evening on it. Empty
    /// for everything the engine can reach, which is almost every file.</summary>
    public static string ReachNoteFor(VfsFile file)
    {
        if (ReachHashOf(file) is not { } hash
            || !Unreachable.Value.TryGet(hash, out ReachListEntry entry)
            || entry.Verdict != ReachVerdict.Unused)
        {
            return string.Empty;
        }

        string why = ReachReasons.Explain(entry.Reason);
        string subject = file.IsFragment ? "The file this lives inside is never read" : "The game never reads this file";
        return file.IsModded
            ? $"{subject}: {why} Your edit is staged, but the game will not load it."
            : $"{subject}: {why} Editing it will have no effect in game.";
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
            if (!IsUnusedFile(file))
            {
                // Stops at the first ancestor already marked: unlike mods this is true of nearly
                // every file, so walking the full chain each time would be ~200,000 redundant walks.
                for (FolderNode? n = node; n is { ContainsUsedFiles: false }; n = ParentOf(_folderIndex, n))
                {
                    n.ContainsUsedFiles = true;
                }
            }
        }

        SortRecursively(root);
        if (OnlyMods || HideUnused)
        {
            Prune(root, n => (!OnlyMods || n.ContainsMods) && (!HideUnused || n.ContainsUsedFiles));
        }

        Roots.Clear();
        foreach (FolderNode child in root.Children)
        {
            Roots.Add(child);
        }

        SelectedFolder = previous is not null
                          && _folderIndex.TryGetValue(previous, out FolderNode? restored)
                          && (!OnlyMods || restored.ContainsMods)
                          && (!HideUnused || restored.ContainsUsedFiles)
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

    /// <summary>
    /// Every file "Export folder…" would write for <paramref name="folder"/>: everything at or below
    /// it in the tree.
    /// </summary>
    /// <remarks>
    /// Honours the same view switch the file list itself does — <see cref="OnlyMods"/> (which
    /// already pruned the tree you clicked in, so exporting vanilla files out of a mods-only view
    /// would contradict it) — but deliberately not
    /// <see cref="FilterText"/>: this action is "everything down this path", not "everything down
    /// this path that also happens to match what I last typed in the search box".
    /// </remarks>
    public IReadOnlyList<VfsFile> FilesUnder(FolderNode folder)
    {
        if (_vfs is null) return [];

        string root = folder.FullPath;
        string prefix = PathPrefixOf(root);

        return _vfs.Files.Values
            .Where(f => IsUnder(f, root, prefix)
                        && (!OnlyMods || f.IsModded)
                        && (!HideUnused || !IsUnusedFile(f)))
            .OrderBy(f => f.Path, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <summary>What every path at or below <paramref name="root"/> starts with — empty for the
    /// (unselectable) tree root, so it matches the whole VFS rather than nothing.</summary>
    private static string PathPrefixOf(string root) => root.Length == 0 ? string.Empty : root + "\\";

    private static bool IsUnder(VfsFile file, string root, string prefix)
    {
        // A synthetic row — an .fcb fragment, a depload.dat link — lives *inside* its container's own
        // path, which on disk is a file, not a directory. Sweeping one up from an ancestor folder
        // would mean writing worlds\…\foo.fcb as both a file and a folder in the same export, so
        // they're only in scope when the folder asked for is the one they sit in directly (i.e. you
        // pointed at the container itself, where there's nothing else to export anyway).
        if (file.IsFragment)
        {
            return string.Equals(file.Directory, root, StringComparison.OrdinalIgnoreCase);
        }

        return root.Length == 0
               || string.Equals(file.Directory, root, StringComparison.OrdinalIgnoreCase)
               || file.Directory.StartsWith(prefix, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Writes <paramref name="files"/> under <paramref name="destination"/>, recreating the folder
    /// structure they have below <paramref name="folder"/>. Runs on a background thread and can be
    /// stopped mid-run with <see cref="CancelExport"/>; a file that can't be read is counted and
    /// skipped rather than abandoning the rest of the subtree.
    /// </summary>
    /// <remarks>
    /// <paramref name="files"/> is what <see cref="FilesUnder"/> returned rather than something this
    /// recomputes, so what lands on disk is exactly the count and size the caller put in front of the
    /// user before they agreed to it.
    /// </remarks>
    public async Task<FolderExportResult> ExportFolderAsync(
        FolderNode folder, IReadOnlyList<VfsFile> files, string destination)
    {
        string prefix = PathPrefixOf(folder.FullPath);
        var progress = new Progress<string>(s => Status = s);

        var cts = new CancellationTokenSource();
        _exportCts = cts;
        OnPropertyChanged(nameof(IsExporting));
        try
        {
            return await Task.Run(() => ExportFiles(files, prefix, destination, progress, cts.Token));
        }
        finally
        {
            _exportCts = null;
            cts.Dispose();
            OnPropertyChanged(nameof(IsExporting));
        }
    }

    private FolderExportResult ExportFiles(
        IReadOnlyList<VfsFile> files, string prefix, string destination,
        IProgress<string> progress, CancellationToken token)
    {
        int written = 0, failed = 0;
        string? firstError = null;

        // One CreateDirectory per file would be tens of thousands of syscalls for a subtree that only
        // has a few hundred distinct folders in it.
        var created = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        for (int i = 0; i < files.Count; i++)
        {
            if (token.IsCancellationRequested)
            {
                return new FolderExportResult(written, failed, firstError, Cancelled: true);
            }

            VfsFile file = files[i];
            try
            {
                string target = Path.Combine(destination, OutputPath.Relative(file.Path[prefix.Length..]));
                string directory = Path.GetDirectoryName(target)!;
                if (created.Add(directory))
                {
                    Directory.CreateDirectory(directory);
                }
                File.WriteAllBytes(target, Read(file));
                written++;
            }
            catch (Exception ex)
            {
                failed++;
                firstError ??= $"{file.Path}: {ex.Message}";
            }

            if ((i + 1) % ExportReportEvery == 0)
            {
                progress.Report($"Exporting… ({i + 1:N0} / {files.Count:N0})");
            }
        }

        return new FolderExportResult(written, failed, firstError, Cancelled: false);
    }

    /// <summary>Stops the running <see cref="ExportFolderAsync"/> after the file it's on — a subtree
    /// can be gigabytes, and there's no undoing bytes already written, only stopping more of them.</summary>
    public void CancelExport() => _exportCts?.Cancel();

    /// <summary>Whether a folder export is running, so the status bar can offer to cancel it.</summary>
    public bool IsExporting => _exportCts is not null;

    /// <summary>Drops every branch <paramref name="keep"/> rejects, for the view filters that prune
    /// the directory tree as well as the file list.</summary>
    private static void Prune(FolderNode node, Func<FolderNode, bool> keep)
    {
        var kept = node.Children.Where(keep).ToList();
        node.Children.Clear();
        foreach (FolderNode child in kept)
        {
            Prune(child, keep);
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
        (string[] includes, string[] excludes, string? extFilter, string? archFilter, uint? hashFilter) = ParseFilter(_filterText);
        string? folderPath = SelectedFolder?.FullPath;
        bool onlyMods = OnlyMods;
        bool hideUnused = HideUnused;

        List<VfsFile>? matches;
        try
        {
            // The scan itself (a substring match over every file, when a filter is active) is real
            // CPU work over a large collection - running it on a background thread is what actually
            // keeps the UI thread free while it happens, debounce or not.
            matches = await Task.Run(() =>
            {
                IEnumerable<VfsFile> files;
                if (includes.Length > 0 || excludes.Length > 0 || extFilter is not null || archFilter is not null || hashFilter is not null)
                {
                    files = vfs.Files.Values.Where(f =>
                    {
                        var normalizedPath = NormalizeSlashes(f.Path);
                        // Filter for exclusion first, skip file early
                        if (excludes.Length > 0 && excludes.Any(x => normalizedPath.Contains(x, StringComparison.OrdinalIgnoreCase)))
                            return false;

                        if (hashFilter is { } hash && f.Hash != hash)
                            return false;

                        if (archFilter is not null && !vfs.DisplayModuleName(f).Contains(archFilter, StringComparison.OrdinalIgnoreCase))
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

                if (hideUnused)
                {
                    files = files.Where(f => !IsUnusedFile(f));
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
    /// Pulls the special <c>ext:xbt</c>/<c>arch:dlc1</c>/<c>hash:1a2b3c4d</c>-shaped tokens out of the
    /// filter text, leaving whatever's left as the ordinary path substring needle. Whitespace-delimited
    /// and freely combinable, e.g. <c>"ext:xbt cliff"</c>: only .xbt files whose path also contains
    /// "cliff". <c>arch:</c> matches against <see cref="GameVfs.DisplayModuleName"/> (so both the bare
    /// archive name and, for a colliding one, its disambiguated "folder/name" form work); <c>hash:</c>
    /// takes a hex CRC32 (with or without a leading "0x") and matches <see cref="VfsFile.Hash"/> exactly
    /// - an unparsable hash: value is dropped rather than falling back to a literal text match, since a
    /// mistyped hash is never a meaningful path substring.
    /// </summary>
    private static (string[] Includes, string[] Excludes, string? Extension, string? Archive, uint? Hash) ParseFilter(string filterText)
    {
        string? extension = null;
        string? archive = null;
        uint? hash = null;
        var includes = new List<string>();
        var excludes = new List<string>();

        foreach (string token in filterText.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            if (token.StartsWith("ext:", StringComparison.OrdinalIgnoreCase))
                extension = token[4..].TrimStart('.');
            else if (token.StartsWith("arch:", StringComparison.OrdinalIgnoreCase))
                archive = token[5..];
            else if (token.StartsWith("hash:", StringComparison.OrdinalIgnoreCase))
            {
                string hex = token[5..];
                if (hex.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
                    hex = hex[2..];
                if (uint.TryParse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out uint parsed))
                    hash = parsed;
            }
            else if (token.StartsWith("-", StringComparison.OrdinalIgnoreCase) && token.Length > 1)
                excludes.Add(token[1..]);
            else
                includes.Add(token);
        }

        return (
            includes.Select(NormalizeSlashes).ToArray(),
            excludes.Select(NormalizeSlashes).ToArray(),
            extension is { Length: > 0 } ? extension : null,
            archive is { Length: > 0 } ? archive : null,
            hash
        );
    }
}
