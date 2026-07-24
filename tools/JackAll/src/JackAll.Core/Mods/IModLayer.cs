using System.Collections.Concurrent;
using System.Globalization;
using System.IO.Compression;
using JackAll.Core.Format;

namespace JackAll.Core.Mods;

/// <summary>
/// A set of file overrides, keyed by the same CRC32 the engine uses. A zip on disk and the user's
/// workspace folder are the same thing to everything downstream — which is why the workspace needs
/// no special-casing anywhere except being pinned last in the order.
/// </summary>
public interface IModLayer
{
    string Name { get; }
    bool Enabled { get; set; }

    /// <summary>Every hash this layer overrides as a standalone archive entry. Disjoint from
    /// <see cref="FragmentOverrides"/> — a fragment path never appears here, since replacing one
    /// child of a splitting `.fcb` isn't a standalone override; <c>GameVfs</c>/<c>PatchBuilder</c>
    /// compose it into its container instead.</summary>
    IReadOnlyCollection<uint> Hashes { get; }

    byte[] Read(uint hash);

    /// <summary>The relative path, when this layer knows it (a <c>_hash\</c> entry doesn't).</summary>
    string? PathOf(uint hash);

    /// <summary>Container hash -&gt; the fragments (see <c>FcbXml.ListFragmentIds</c>) this layer
    /// overrides inside it — a path shaped <c>container.fcb\NN_Name.xml</c>. Each
    /// <see cref="FragmentOverride.EntryHash"/> is a valid <see cref="Read"/> argument, same as any
    /// hash in <see cref="Hashes"/>.</summary>
    IReadOnlyDictionary<uint, IReadOnlyList<FragmentOverride>> FragmentOverrides { get; }
}

/// <summary>One fragment override inside some container, as staged by a single <see cref="IModLayer"/>.</summary>
public readonly record struct FragmentOverride(string FragmentId, uint EntryHash);

/// <summary>
/// Maps files inside a mod to engine hashes.
/// </summary>
/// <remarks>
/// Two conventions, both needed:
///
///   worlds\world1\generated\x.fcb   → hashed as that relative path (the normal case)
///   _hash\4A724578.xbt              → used as the literal hash 0x4A724578
///
/// The second exists because the community's filename dictionary is incomplete. Without it, any
/// archive entry whose name nobody has recovered would be permanently unmoddable — you could see
/// the file but never override it, since you couldn't produce a path that hashes to it.
///
/// A third shape, layered on top of the first: a path with a segment (other than the last) ending in
/// <c>.fcb</c> overrides one fragment of a splitting `.fcb` (see <c>FcbXml.ListFragmentIds</c>'s
/// <c>NN_Name.xml</c> naming) rather than being a standalone archive entry — see
/// <see cref="ModPathTarget.ContainerHash"/>. The same nameless-entry problem the <c>_hash\</c>
/// convention solves for a plain file applies one level deeper here too: an *unnamed* container's own
/// display path (<c>GameVfs.SyntheticPath</c>, e.g. <c>_unknown\data\1a2b3c4d.fcb</c>) is a display
/// convenience that deliberately doesn't hash back to the real archive hash, unlike a named
/// container's own recovered path — so overriding a fragment inside one needs the container's hash
/// spelled out directly: <c>_hash\1a2b3c4d.fcb\03_Foo.xml</c>.
/// </remarks>
internal static class ModPathHashing
{
    public const string HashFolder = "_hash";

    /// <summary>Resolves a relative path to what it overrides. Null for paths that are not overrides
    /// at all (readme files and the like).</summary>
    public static ModPathTarget? Resolve(string relativePath)
    {
        string normalized = NameHash.Normalize(relativePath);
        if (normalized.Length == 0)
        {
            return null;
        }

        string[] segments = normalized.Split('\\');

        if (segments[0] == HashFolder)
        {
            return ResolveHashAddressed(normalized, segments);
        }

        // A named container's fragment: some segment before the last ends in .fcb.
        for (int i = 0; i < segments.Length - 1; i++)
        {
            if (!segments[i].EndsWith(".fcb", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            string containerPath = string.Join('\\', segments[..(i + 1)]);
            string fragmentId = string.Join('\\', segments[(i + 1)..]);
            return new ModPathTarget(
                NameHash.Compute(normalized), NameHash.Compute(containerPath), fragmentId);
        }

        return new ModPathTarget(NameHash.Compute(normalized), null, null);
    }

    /// <summary>
    /// <c>_hash\&lt;hex&gt;[.ext]</c> (a plain unnamed override), or <c>_hash\&lt;hex&gt;.fcb\NN_Name.xml</c>
    /// (a fragment override inside an unnamed container — the hex is the *container's* hash, read
    /// straight off this segment rather than computed from any path, since none exists for it).
    /// </summary>
    private static ModPathTarget? ResolveHashAddressed(string normalized, string[] segments)
    {
        if (segments.Length < 2)
        {
            return null;
        }

        string leaf = segments[1];
        // Everything before the first dot is the hash: "4a724578.xbt" and a bare "4a724578" both
        // work, so a user can drop in a file with or without an extension.
        int dot = leaf.IndexOf('.');
        string hexPart = dot < 0 ? leaf : leaf[..dot];
        if (!uint.TryParse(hexPart, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out uint hash))
        {
            return null;
        }

        if (segments.Length == 2)
        {
            return new ModPathTarget(hash, null, null);
        }

        string extension = dot < 0 ? "" : leaf[dot..];
        if (!extension.Equals(".fcb", StringComparison.OrdinalIgnoreCase))
        {
            return null; // extra segments after anything but a _hash\<hex>.fcb leaf mean nothing
        }

        string fragmentId = string.Join('\\', segments[2..]);
        return new ModPathTarget(NameHash.Compute(normalized), hash, fragmentId);
    }

    /// <summary>
    /// Indexes one resolved target into a layer's <c>Hashes</c>/<c>FragmentOverrides</c> bookkeeping —
    /// shared so <see cref="ZipModLayer"/>'s constructor and <see cref="FolderModLayer"/>'s
    /// <c>Rescan</c>/<c>Stage</c> classify identically. Idempotent: re-adding the same fragment
    /// replaces its prior entry rather than duplicating it, so a folder layer's <c>Stage</c> (which
    /// must update this immediately, not just on the next <c>Rescan</c> — callers build a patch right
    /// after staging with no rescan in between) can call this unconditionally.
    /// </summary>
    public static void Add(
        ModPathTarget target, HashSet<uint> hashes, Dictionary<uint, List<FragmentOverride>> fragmentOverrides)
    {
        if (target.ContainerHash is not { } containerHash)
        {
            hashes.Add(target.EntryHash);
            return;
        }

        if (!fragmentOverrides.TryGetValue(containerHash, out List<FragmentOverride>? fragments))
        {
            fragments = [];
            fragmentOverrides[containerHash] = fragments;
        }
        fragments.RemoveAll(f => f.EntryHash == target.EntryHash);
        fragments.Add(new FragmentOverride(target.FragmentId!, target.EntryHash));
    }

    /// <summary>Snapshots the mutable per-container fragment lists into the immutable shape
    /// <see cref="IModLayer.FragmentOverrides"/> exposes.</summary>
    public static IReadOnlyDictionary<uint, IReadOnlyList<FragmentOverride>> Freeze(
        Dictionary<uint, List<FragmentOverride>> fragmentOverrides)
        => fragmentOverrides.ToDictionary(kv => kv.Key, kv => (IReadOnlyList<FragmentOverride>)kv.Value);
}

/// <summary>What one relative path inside a mod resolves to — see <see cref="ModPathHashing.Resolve"/>.</summary>
internal readonly record struct ModPathTarget(uint EntryHash, uint? ContainerHash, string? FragmentId);

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
    private readonly Dictionary<uint, List<FragmentOverride>> _fragmentOverrides = [];
    private readonly ConcurrentDictionary<uint, byte[]> _readCache = new();

    public string Name { get; }
    public bool Enabled { get; set; } = true;
    public string ZipPath { get; }
    public IReadOnlyCollection<uint> Hashes => _hashes;
    public IReadOnlyDictionary<uint, IReadOnlyList<FragmentOverride>> FragmentOverrides { get; }

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

            ModPathTarget? target = ModPathHashing.Resolve(entry.FullName);
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

    public string? PathOf(uint hash)
    {
        string? name = _entryNames.GetValueOrDefault(hash);
        if (name is null) return null;
        string normalized = NameHash.Normalize(name);
        return normalized.StartsWith(ModPathHashing.HashFolder + "\\", StringComparison.Ordinal)
            ? null
            : normalized;
    }
}

/// <summary>
/// A mod backed by a folder. This is what the workspace is: the tool stages every edit here as a
/// plain file, so the staging area is inspectable, diffable and zippable by hand.
/// </summary>
public sealed class FolderModLayer : IModLayer
{
    private readonly Dictionary<uint, string> _absolutePaths = [];
    private readonly HashSet<uint> _hashes = [];
    private readonly Dictionary<uint, List<FragmentOverride>> _fragmentOverrides = [];

    public string Name { get; }
    public bool Enabled { get; set; } = true;
    public string RootPath { get; }
    public IReadOnlyCollection<uint> Hashes => _hashes;
    public IReadOnlyDictionary<uint, IReadOnlyList<FragmentOverride>> FragmentOverrides { get; private set; } =
        ModPathHashing.Freeze([]);

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

        if (Directory.Exists(RootPath))
        {
            foreach (string file in Directory.EnumerateFiles(RootPath, "*", SearchOption.AllDirectories))
            {
                string relative = Path.GetRelativePath(RootPath, file);
                ModPathTarget? target = ModPathHashing.Resolve(relative);
                if (target is null)
                {
                    continue;
                }

                _absolutePaths[target.Value.EntryHash] = file;
                ModPathHashing.Add(target.Value, _hashes, _fragmentOverrides);
            }
        }

        FragmentOverrides = ModPathHashing.Freeze(_fragmentOverrides);
    }

    public byte[] Read(uint hash)
        => _absolutePaths.TryGetValue(hash, out string? path)
            ? File.ReadAllBytes(path)
            : throw new KeyNotFoundException($"'{Name}' does not override {hash:X8}.");

    public string? PathOf(uint hash)
    {
        if (!_absolutePaths.TryGetValue(hash, out string? absolute)) return null;
        string relative = NameHash.Normalize(Path.GetRelativePath(RootPath, absolute));
        return relative.StartsWith(ModPathHashing.HashFolder + "\\", StringComparison.Ordinal)
            ? null
            : relative;
    }

    /// <summary>
    /// Writes an override into the folder. <paramref name="knownPath"/> is the file's real relative
    /// path when we know it; when we don't, the file is staged under <c>_hash\</c> so it still
    /// reaches the engine. Updates <see cref="Hashes"/>/<see cref="FragmentOverrides"/> immediately
    /// (not just on the next <see cref="Rescan"/>) — <c>PatchBuilder.Build</c> can run right after a
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

        string destination = Path.Combine(RootPath, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        File.WriteAllBytes(destination, content);

        ModPathTarget target = ModPathHashing.Resolve(relative)
            ?? throw new InvalidOperationException($"'{relative}' round-tripped to an unstageable path.");

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

        // Resolved before the delete, since it needs the relative path to know whether this was a
        // fragment override and which container it belonged to.
        ModPathTarget? target = ModPathHashing.Resolve(Path.GetRelativePath(RootPath, path));

        File.Delete(path);
        _absolutePaths.Remove(hash);
        _hashes.Remove(hash);
        if (target?.ContainerHash is { } containerHash
            && _fragmentOverrides.TryGetValue(containerHash, out List<FragmentOverride>? fragments))
        {
            fragments.RemoveAll(f => f.EntryHash == hash);
            if (fragments.Count == 0)
            {
                _fragmentOverrides.Remove(containerHash);
            }
            FragmentOverrides = ModPathHashing.Freeze(_fragmentOverrides);
        }

        // Leave no empty scaffolding behind, or the workspace slowly fills with dead folders.
        string? dir = Path.GetDirectoryName(path);
        while (dir is not null
               && dir.StartsWith(RootPath, StringComparison.OrdinalIgnoreCase)
               && !dir.Equals(RootPath, StringComparison.OrdinalIgnoreCase)
               && Directory.Exists(dir)
               && !Directory.EnumerateFileSystemEntries(dir).Any())
        {
            Directory.Delete(dir);
            dir = Path.GetDirectoryName(dir);
        }
        return true;
    }
}
