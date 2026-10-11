using System.IO;
using JackAll.Core;
using JackAll.Core.Mods;
using JackAll.Core.Vfs;

namespace SoundBench.Game;

/// <summary>The game as installed, with mod layer folders on top, resolved the way the game finds files.</summary>
public sealed class GameFiles : IDisposable
{
    private readonly GameVfs _vfs;
    private readonly FolderModLayer[] _layers;

    private GameFiles(GameInstall install, GameVfs vfs, FolderModLayer[] layers)
    {
        Install = install;
        _vfs = vfs;
        _layers = layers;
    }

    public GameInstall Install { get; }

    public static GameFiles Open(string gamePath, IReadOnlyList<string> layerFolders, IProgress<string> progress)
    {
        GameInstall install = GameInstall.TryOpen(gamePath, out string error) ?? throw new InvalidOperationException(error);
        GameVfs vfs = GameVfs.Load(
            install, BundledAssets.LoadNames(), GameCache.Load(install.CacheFile), progress: progress, includeFragments: false);
        FolderModLayer[] layers = [.. layerFolders.Select(folder => new FolderModLayer(folder, Path.GetFileName(Path.GetDirectoryName(folder)) ?? folder))];
        vfs.Rebuild(layers, includeFragments: false, progress);
        return new GameFiles(install, vfs, layers);
    }

    /// <summary>Re-reads the layer folders, for banks rebuilt since they were opened.</summary>
    public void Reload()
    {
        foreach (FolderModLayer layer in _layers)
        {
            layer.Rescan();
        }
        _vfs.Rebuild(_layers, includeFragments: false);
    }

    public byte[]? Read(string path) => _vfs.ReadByPath(path);

    public byte[]? Read(uint hash) => _vfs.Files.TryGetValue(hash, out VfsFile? file) ? _vfs.Read(file.Hash) : null;

    /// <summary>A file's name and what provides it: a layer, or "game" for the installed archives.</summary>
    public string Describe(uint hash)
        => _vfs.Files.TryGetValue(hash, out VfsFile? file)
            ? $"{file.FileName} ({(file.SourceKind == SourceKind.Mod ? file.SourceName : "game")})"
            : $"{hash:x8}";

    public IEnumerable<string> KnownPaths => _vfs.Files.Values.Where(f => f.NameIsKnown).Select(f => f.Path);

    public void Dispose() => _vfs.Dispose();
}
