using JackAll.App.FileHandlers.Fcb;
using JackAll.Core.Mods;
using JackAll.Core.Naming;
using JackAll.Core.Vfs;
using JackAll.Core;
using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;

namespace JackAll.App;

/// <summary>The window's view model. This file holds the shared state and startup orchestration; each
/// tab's own state and behavior lives in a feature partial (Mods/Saves/Files/Vfs/Xrefs).</summary>
public sealed partial class MainViewModel : INotifyPropertyChanged
{
    private GameVfs? _vfs;
    private GameCache _cache = new();
    private NameDatabase? _names;
    private string _status = "Starting…";
    private bool _busy;

    public AppConfig Config { get; private set; } = AppConfig.Load();
    public GameInstall? Install { get; private set; }
    public FolderModLayer? Workspace { get; private set; }

    /// <summary>
    /// Archives (relative to <c>Data_Win32</c>) whose hash didn't match <see cref="VanillaHashes"/>,
    /// as of the last <see cref="CheckVanillaHashesAsync"/> call — empty when everything checked out.
    /// Left for code-behind to notice and show as a dialog: <see cref="MainViewModel"/> otherwise has
    /// no WPF dependency.
    /// </summary>
    public IReadOnlyList<string> ArchiveHashMismatches { get; private set; } = [];

    /// <summary>
    /// Hashes <paramref name="install"/>'s base archives against the known-good reference set. Meant
    /// to run once, right when the user picks this game folder (<c>MainWindow.PromptForGameFolder</c>)
    /// — not on every launch via <see cref="InitializeAsync"/> like it used to: the archives' contents
    /// can't change out from under an already-configured install between one launch and the next
    /// (nothing here writes to them), so re-hashing potentially gigabytes of data on every startup was
    /// pure waste once the folder had already been validated.
    /// </summary>
    public async Task CheckVanillaHashesAsync(GameInstall install)
    {
        ArchiveHashMismatches = await Task.Run(() =>
            VanillaHashesProvider.Value.Value
                .FindMismatches(install.DataDir, install.EnumerateBaseArchiveRelativePaths()));
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

        // Phase 3, once the tree the user is actually looking at is complete. See BuildXrefsAsync's
        // remarks for why this follows the fragment pass rather than running beside it.
        await BuildXrefsAsync();
    }

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
