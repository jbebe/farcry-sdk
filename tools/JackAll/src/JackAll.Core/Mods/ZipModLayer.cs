using System.Collections.Concurrent;
using System.IO.Compression;

namespace JackAll.Core.Mods;

/// <summary>A mod distributed as a zip — the format community mods already ship in.</summary>
/// <remarks>
/// <see cref="Read"/> caches every entry's decompressed bytes in <see cref="_readCache"/>, keyed by
/// hash — unlike a base-game archive (tens of thousands of entries, must stay read-on-demand), a mod
/// zip is small by nature, so keeping every entry it can ever be asked for in memory for the layer's
/// lifetime costs nothing. This also fully replaces the previous per-call
/// <c>ZipFile.OpenRead</c>/re-scan of the whole central directory — a hash is read from the zip at
/// most once. Safe without any lock: a cache-race on the same hash from two threads just costs a
/// redundant (independently safe, since each opens its own <c>ZipArchive</c>) re-read, never
/// corruption; <see cref="_entryNames"/> never changes after construction, so there's no staleness to
/// guard against either — <see cref="JackAll.App.MainViewModel.RescanMods"/> always builds a brand new
/// <see cref="ZipModLayer"/> rather than mutating this one.
/// </remarks>
public sealed class ZipModLayer : IModLayer
{
    private readonly Dictionary<uint, string> _entryNames = [];
    private readonly HashSet<uint> _hashes = [];
    private readonly Dictionary<uint, FragmentMap> _fragmentOverrides = [];
    private readonly Dictionary<string, string> _pluginEntryNames = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<uint, byte[]> _readCache = new();

    public string Name { get; }
    public bool Enabled { get; set; } = true;
    public string ZipPath { get; }
    public IReadOnlyCollection<uint> Hashes => _hashes;
    public IReadOnlyDictionary<uint, IReadOnlyList<FragmentOverride>> FragmentOverrides { get; }
    public IReadOnlyCollection<string> PluginPaths => _pluginEntryNames.Keys;

    public ZipModLayer(string zipPath)
    {
        ZipPath = zipPath;
        Name = Path.GetFileNameWithoutExtension(zipPath);

        using var zip = ZipFile.OpenRead(zipPath);
        foreach (var entry in zip.Entries)
        {
            if (entry.FullName.EndsWith('/') || entry.Length == 0 && entry.Name.Length == 0)
            {
                continue; // directory
            }

            if (ModPathHashing.PluginPathOf(entry.FullName) is { } pluginPath)
            {
                _pluginEntryNames[pluginPath] = entry.FullName;
                continue;
            }

            ModPathTarget? target = ModPathHashing.Resolve(ModPathHashing.ContentPathOf(entry.FullName));
            if (target is null)
            {
                continue; // not a valid override (e.g. a readme) - silently skipped
            }

            _entryNames[target.Value.EntryHash] = entry.FullName;
            ModPathHashing.Add(target.Value, _hashes, _fragmentOverrides);
        }

        FragmentOverrides = ModPathHashing.Freeze(_fragmentOverrides);
    }

    public byte[] Read(uint hash)
    {
        if (!_entryNames.TryGetValue(hash, out string? entryName))
        {
            throw new KeyNotFoundException($"Mod '{Name}' does not override {hash:X8}.");
        }

        // No defensive copy: the returned array is the cache entry itself, shared across every caller
        // that reads this hash again. Safe only because nothing downstream ever writes back into a
        // Read() result (FcbAssembler.Apply and friends decode into a new FcbObject tree and encode a
        // fresh array rather than editing in place) - if that ever stops being true, this cache silently
        // corrupts for every later reader of the same hash.
        return _readCache.GetOrAdd(hash, _ => ReadFromZip(entryName));
    }

    private byte[] ReadFromZip(string entryName)
    {
        using var zip = ZipFile.OpenRead(ZipPath);
        var entry = zip.GetEntry(entryName)
            ?? throw new InvalidDataException($"'{entryName}' vanished from '{Name}' since it was indexed.");

        using var stream = entry.Open();
        using var buffer = new MemoryStream();
        stream.CopyTo(buffer);
        return buffer.ToArray();
    }

    public byte[] ReadPlugin(string pluginPath)
        => _pluginEntryNames.TryGetValue(pluginPath, out string? entryName)
            ? ReadFromZip(entryName)
            : throw new KeyNotFoundException($"'{Name}' has no plugin '{pluginPath}'.");

    public string? PathOf(uint hash)
    {
        string? name = _entryNames.GetValueOrDefault(hash);
        if (name is null) return null;
        string normalized = ModPathHashing.ContentPathOf(name);
        return normalized.StartsWith(ModPathHashing.HashFolder + "\\", StringComparison.Ordinal)
            ? null
            : normalized;
    }
}
