using JackAll.Core.Mods;
using JackAll.Tools.Sav;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Windows.Media.Imaging;
using System.Windows.Media;
using System.Windows;

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

/// <summary>What one <see cref="MainViewModel.ExportFolderAsync"/> run actually did.
/// <paramref name="FirstError"/> is only the first failure's message: a subtree can be tens of
/// thousands of files, and a per-file dialog (or a wall of them at the end) is unreadable — the count
/// says how bad it was, the message says what kind of bad.</summary>
public sealed record FolderExportResult(int Written, int Failed, string? FirstError, bool Cancelled);

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
    /// of which container it's inside) plus plugin files — <see cref="IModLayer.Hashes"/> alone would
    /// undercount (or show zero) a layer that only stages `.fcb` fragments or an FCSE plugin, since
    /// those are tracked separately in <see cref="IModLayer.FragmentOverrides"/>/
    /// <see cref="IModLayer.PluginPaths"/>, not <c>Hashes</c>.</summary>
    public int FileCount => Layer.Hashes.Count + Layer.FragmentOverrides.Values.Sum(f => f.Count)
        + Layer.PluginPaths.Count;
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
