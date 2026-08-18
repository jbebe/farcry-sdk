using JackAll.Core.Format;

namespace JackAll.Core.Mods;

/// <summary>
/// A mod backed by a folder. This is what the workspace is: the tool stages every edit here as a
/// plain file, so the staging area is inspectable, diffable and zippable by hand.
/// </summary>
public sealed class FolderModLayer : IModLayer
{
    private readonly Dictionary<uint, string> _absolutePaths = [];
    private readonly HashSet<uint> _hashes = [];
    private readonly Dictionary<uint, FragmentMap> _fragmentOverrides = [];
    private readonly Dictionary<string, string> _pluginFiles = new(StringComparer.Ordinal);

    public string Name { get; }
    public bool Enabled { get; set; } = true;
    public string RootPath { get; }
    public IReadOnlyCollection<uint> Hashes => _hashes;
    public IReadOnlyDictionary<uint, IReadOnlyList<FragmentOverride>> FragmentOverrides { get; private set; } =
        ModPathHashing.Freeze([]);
    public IReadOnlyCollection<string> PluginPaths => _pluginFiles.Keys;

    public FolderModLayer(string rootPath, string name)
    {
        RootPath = rootPath;
        Name = name;
        Rescan();
    }

    /// <summary>Re-reads the folder. Cheap, and called after every staged edit.</summary>
    public void Rescan()
    {
        _absolutePaths.Clear();
        _hashes.Clear();
        _fragmentOverrides.Clear();
        _pluginFiles.Clear();

        if (Directory.Exists(RootPath))
        {
            foreach (string file in Directory.EnumerateFiles(RootPath, "*", SearchOption.AllDirectories))
            {
                LayerPath classified = ModPathHashing.Classify(Path.GetRelativePath(RootPath, file));
                if (classified.PluginPath is { } pluginPath)
                {
                    _pluginFiles[pluginPath] = file;
                    continue;
                }
                if (classified.Target is not { } target)
                {
                    continue;
                }

                _absolutePaths[target.EntryHash] = file;
                ModPathHashing.Add(target, _hashes, _fragmentOverrides);
            }
        }

        FragmentOverrides = ModPathHashing.Freeze(_fragmentOverrides);
    }

    public byte[] Read(uint hash)
        => _absolutePaths.TryGetValue(hash, out string? path)
            ? File.ReadAllBytes(path)
            : throw new KeyNotFoundException($"'{Name}' does not override {hash:X8}.");

    public byte[] ReadPlugin(string pluginPath)
        => _pluginFiles.TryGetValue(pluginPath, out string? path)
            ? File.ReadAllBytes(path)
            : throw new KeyNotFoundException($"'{Name}' has no plugin '{pluginPath}'.");

    public string? PathOf(uint hash)
    {
        if (!_absolutePaths.TryGetValue(hash, out string? absolute)) return null;
        string relative = ModPathHashing.ContentPathOf(Path.GetRelativePath(RootPath, absolute));
        return relative.StartsWith(ModPathHashing.HashFolder + "\\", StringComparison.Ordinal)
            ? null
            : relative;
    }

    /// <summary>
    /// Writes an override into the folder, under the reserved <c>mods\</c> wrapper — a game path
    /// can then never collide with the <c>plugins\</c> payload folder, and the folder zips up as-is
    /// into a valid mod archive. <paramref name="knownPath"/> is the file's real relative path when
    /// we know it; when we don't, the file is staged under <c>mods\_hash\</c> so it still reaches
    /// the engine. Updates <see cref="Hashes"/>/<see cref="FragmentOverrides"/> immediately (not
    /// just on the next <see cref="Rescan"/>) — <c>PatchBuilder.Build</c> can run right after a
    /// stage with no rescan in between.
    /// </summary>
    /// <remarks>
    /// <paramref name="hash"/> only ever picks the <c>_hash\</c> fallback filename when
    /// <paramref name="knownPath"/> is null — the actual storage key is always
    /// <c>ModPathHashing.Resolve(relative).EntryHash</c>, matching <see cref="Rescan"/>. Those agree
    /// for every case except one: a fragment override addressed via <c>_hash\&lt;container hash&gt;
    /// .fcb\&lt;fragment id&gt;</c> (<paramref name="hash"/> being the fragment's own display hash,
    /// not a hash of that staged path at all) — using <paramref name="hash"/> directly there would
    /// store the content under a key <see cref="FragmentOverride.EntryHash"/> never points at.
    /// </remarks>
    public void Stage(uint hash, string? knownPath, string extension, byte[] content)
    {
        string relative = knownPath is not null
            ? NameHash.Normalize(knownPath)
            : Path.Combine(ModPathHashing.HashFolder, $"{hash:x8}.{extension.TrimStart('.')}");

        string destination = Path.Combine(RootPath, ModPathHashing.ModsFolder, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        File.WriteAllBytes(destination, content);

        ModPathTarget target = ModPathHashing.Resolve(relative)
            ?? throw new InvalidOperationException($"'{relative}' round-tripped to an unstageable path.");

        // A previous stage of this override under another spelling (_hash\-addressed vs named) left
        // its file at a different path; leaving both would hand Rescan two files for one hash.
        if (_absolutePaths.TryGetValue(target.EntryHash, out string? previous)
            && !previous.Equals(destination, StringComparison.OrdinalIgnoreCase)
            && File.Exists(previous))
        {
            File.Delete(previous);
            PruneEmptyDirectories(Path.GetDirectoryName(previous), RootPath);
        }

        _absolutePaths[target.EntryHash] = destination;
        ModPathHashing.Add(target, _hashes, _fragmentOverrides);
        FragmentOverrides = ModPathHashing.Freeze(_fragmentOverrides);
    }

    /// <summary>Removes an override, reverting that file to whatever the layers below provide.</summary>
    public bool Unstage(uint hash)
    {
        if (!_absolutePaths.TryGetValue(hash, out string? path))
        {
            return false;
        }

        // Classified before the delete, since it needs the relative path to know whether this was a
        // fragment override and which container it belonged to.
        ModPathTarget? target = ModPathHashing.Classify(Path.GetRelativePath(RootPath, path)).Target;

        File.Delete(path);
        _absolutePaths.Remove(hash);
        _hashes.Remove(hash);
        if (target?.ContainerHash is { } containerHash
            && _fragmentOverrides.TryGetValue(containerHash, out FragmentMap? fragments))
        {
            fragments.Remove(target.Value.FragmentId!);
            if (fragments.Count == 0)
            {
                _fragmentOverrides.Remove(containerHash);
            }
            FragmentOverrides = ModPathHashing.Freeze(_fragmentOverrides);
        }

        // Leave no empty scaffolding behind, or the workspace slowly fills with dead folders.
        PruneEmptyDirectories(Path.GetDirectoryName(path), RootPath);
        return true;
    }

    /// <summary>Deletes now-empty folders from <paramref name="dir"/> up to (never including)
    /// <paramref name="stopAt"/>.</summary>
    internal static void PruneEmptyDirectories(string? dir, string stopAt)
    {
        while (dir is not null
               && dir.StartsWith(stopAt, StringComparison.OrdinalIgnoreCase)
               && !dir.Equals(stopAt, StringComparison.OrdinalIgnoreCase)
               && Directory.Exists(dir)
               && !Directory.EnumerateFileSystemEntries(dir).Any())
        {
            Directory.Delete(dir);
            dir = Path.GetDirectoryName(dir);
        }
    }
}
