using System.IO;
using JackAll.Core;
using JackAll.Core.Mods;
using JackAll.Core.Naming;
using JackAll.Core.Vfs;

namespace SoundBench.Game;

/// <summary>The game as installed, with mod layer folders on top, resolved the way the game finds files.</summary>
public sealed class GameFiles : IDisposable
{
    private readonly GameVfs _vfs;
    private readonly FolderModLayer[] _layers;

    /// <summary>A file was saved into a layer since the files were last built.</summary>
    private bool _staged;

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
        Task<NameDatabase> names = Task.Run(BundledAssets.LoadNames);
        GameCache cache = GameCache.Load(install.CacheFile);
        GameVfs vfs = GameVfs.OpenForOriginalsOnly(install, names.Result, cache, progress);
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
        _staged = false;
        _vfs.Rebuild(_layers, includeFragments: false);
    }

    /// <summary>Writes a file into the top layer that provides it, else the top layer; the next read sees it.</summary>
    /// <returns>The layer's folder, or null with no layer or no such file.</returns>
    public string? Save(uint hash, byte[] content)
    {
        FolderModLayer? layer = _layers.LastOrDefault(l => l.Hashes.Contains(hash)) ?? _layers.LastOrDefault();
        if (layer is null || !_vfs.Files.TryGetValue(hash, out VfsFile? file))
        {
            return null;
        }
        layer.Stage(hash, file.NameIsKnown ? file.Path : null, file.Type.Extension, content);
        _staged = true;
        return layer.RootPath;
    }

    public byte[]? Read(string path)
    {
        Serve();
        return _vfs.ReadByPath(path);
    }

    public byte[]? Read(uint hash)
    {
        Serve();
        return _vfs.Files.TryGetValue(hash, out VfsFile? file) ? _vfs.Read(file.Hash) : null;
    }

    /// <summary>Has the files resolve to what was saved since they were last built.</summary>
    private void Serve()
    {
        if (_staged)
        {
            _staged = false;
            _vfs.Rebuild(_layers, includeFragments: false);
        }
    }

    /// <summary>A file's name and what provides it: a layer, or the archive it comes from.</summary>
    public string Describe(uint hash)
        => _vfs.Files.TryGetValue(hash, out VfsFile? file) ? $"{file.FileName} ({_vfs.DisplayModuleName(file)})" : $"{hash:x8}";

    public IEnumerable<string> KnownPaths => _vfs.Files.Values.Where(f => f.NameIsKnown).Select(f => f.Path);

    public void Dispose() => _vfs.Dispose();
}
