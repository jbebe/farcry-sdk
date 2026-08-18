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

    /// <summary>Container hash -&gt; the fragments (see <c>FcbFragments</c>) this layer overrides
    /// inside it — a path shaped <c>container.fcb\&lt;fragment id&gt;</c>, itself possibly nested. Each
    /// <see cref="FragmentOverride.EntryHash"/> is a valid <see cref="Read"/> argument, same as any
    /// hash in <see cref="Hashes"/>.</summary>
    IReadOnlyDictionary<uint, IReadOnlyList<FragmentOverride>> FragmentOverrides { get; }

    /// <summary>Files under the reserved top-level <c>plugins\</c> folder, as normalized paths
    /// relative to it. Disjoint from <see cref="Hashes"/>/<see cref="FragmentOverrides"/> — never
    /// compiled into patch.dat; <see cref="PluginSync"/> mirrors them into <c>bin\plugins</c>
    /// instead.</summary>
    IReadOnlyCollection<string> PluginPaths => [];

    /// <summary>Reads one plugin file's bytes; the argument is an element of <see cref="PluginPaths"/>.</summary>
    byte[] ReadPlugin(string pluginPath)
        => throw new KeyNotFoundException($"'{Name}' has no plugin '{pluginPath}'.");
}

/// <summary>One fragment override inside some container, as staged by a single <see cref="IModLayer"/>.</summary>
public readonly record struct FragmentOverride(string FragmentId, uint EntryHash);
