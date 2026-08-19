using System.Text.RegularExpressions;
using JackAll.Core.Format;
using JackAll.Core.Format.Fcb;

namespace JackAll.Tools.World;

/// <summary>Which binary's library load order to resolve against; they disagree on the base.</summary>
public enum LibraryProfile
{
    /// <summary>Resolves against <c>entitylibrary_full.fcb</c>.</summary>
    Client,

    /// <summary>Resolves against <c>entitylibrary.fcb</c>; the dedicated server never mentions the full one.</summary>
    Server,
}

/// <summary>One library in the chain, at the position the engine loads it.</summary>
/// <param name="IsConfirmed">False for a layer whose load is only established in the dedicated
/// server binary, not yet read in <c>Dunia.dll</c>.</param>
public sealed record ArchetypeLayer(string Path, bool IsConfirmed = true)
{
    /// <summary>This layer's role in the chain, short enough to sit in a badge or a lint line.</summary>
    public string ShortName
    {
        get
        {
            string[] segments = Path.Split('\\', StringSplitOptions.RemoveEmptyEntries);
            string file = segments[^1];
            if (file.Equals("entitylibrary_full.fcb", StringComparison.OrdinalIgnoreCase)) return "full";
            if (file.Equals("entitylibrarypatchoverride.fcb", StringComparison.OrdinalIgnoreCase)) return "patch";
            if (Path.StartsWith(@"worlds\", StringComparison.OrdinalIgnoreCase)) return "base";

            // A DLC library sits in its own folder but under a "generated" subfolder, which would
            // otherwise name every one of them identically.
            for (int i = segments.Length - 2; i >= 0; i--)
            {
                if (!segments[i].Equals("generated", StringComparison.OrdinalIgnoreCase))
                {
                    return segments[i];
                }
            }
            return "dlc";
        }
    }
}

/// <summary>One declaration of an archetype: what declares it, and the node it declares.</summary>
/// <param name="FragmentId">The declaration's own fragment id (see <see cref="FcbFragments"/> — one
/// per archetype), or null when the container doesn't split into fragments.</param>
public sealed record ArchetypeDefinition(
    string Name, ArchetypeLayer Layer, uint ContainerHash, string? FragmentId, FcbObject Node);

/// <summary>
/// Every archetype a world can instantiate, keyed and ordered the way the engine keys and orders
/// them, so a declaration some later library overrides stays visible instead of looking live.
/// </summary>
/// <remarks>
/// Mirrors <c>CEntityLibraryManager</c>: one map from an entity's <c>hidName</c> to its node, fed by
/// <c>ReadFromXML</c> for the base and <c>Override</c> for each library after it, replacing whole
/// nodes rather than merging fields. See docs/docs/engine-internals/entity-instancing.md.
/// </remarks>
/// <remarks>
/// Reading goes through the VFS, so archive priority and a mod's own fragment overrides are already
/// applied before this sees any bytes - game-internal and mod layering end up in one chain.
/// </remarks>
public sealed partial class ArchetypeIndex
{
    private readonly Dictionary<string, List<ArchetypeDefinition>> _byName;

    private ArchetypeIndex(Dictionary<string, List<ArchetypeDefinition>> byName, IReadOnlyList<ArchetypeLayer> layers)
    {
        _byName = byName;
        Layers = layers;
    }

    /// <summary>The libraries this index was built from, in load order.</summary>
    public IReadOnlyList<ArchetypeLayer> Layers { get; }

    /// <summary>Distinct archetype names, whatever declares them.</summary>
    public IReadOnlyCollection<string> Names => _byName.Keys;

    public int Count => _byName.Count;

    /// <summary>Archetypes more than one library declares - the ones where editing the wrong file
    /// changes nothing in game.</summary>
    public IEnumerable<string> Overridden => _byName.Where(p => p.Value.Count > 1).Select(p => p.Key);

    /// <summary>Every declaration of a name, in load order: the last entry is the one that wins.</summary>
    public IReadOnlyList<ArchetypeDefinition> DefinitionsOf(string name)
        => _byName.TryGetValue(name, out List<ArchetypeDefinition>? found) ? found : [];

    public ArchetypeDefinition? Winner(string name)
        => _byName.TryGetValue(name, out List<ArchetypeDefinition>? found) ? found[^1] : null;

    /// <summary>
    /// Where a name's dotted path stops being a namespace and starts being the archetype's own label.
    /// Splitting stops at any prefix that is itself an archetype, because variants like
    /// <c>pickups.Weapons.AS50_new.Multi</c> are independent copies of
    /// <c>pickups.Weapons.AS50_new</c> that inherit nothing from it - so they belong beside it, not
    /// under it, and the base must not become a folder holding its own variants.
    /// </summary>
    public (IReadOnlyList<string> Groups, string Label) SplitForDisplay(string name)
    {
        string[] parts = name.Split('.');
        int groups = parts.Length - 1;
        while (groups > 0 && _byName.ContainsKey(string.Join('.', parts, 0, groups)))
        {
            groups--;
        }
        return (parts[..groups], string.Join('.', parts, groups, parts.Length - groups));
    }

    /// <summary>
    /// Declarations in one fragment that a later library overrides. An edit to any of these is dead:
    /// it changes the file, and the game reads someone else's copy.
    /// </summary>
    public IEnumerable<ArchetypeDefinition> DeadDeclarationsIn(uint containerHash, string? fragmentId)
    {
        foreach (List<ArchetypeDefinition> chain in _byName.Values)
        {
            for (int i = 0; i < chain.Count - 1; i++)
            {
                ArchetypeDefinition shadowed = chain[i];
                if (shadowed.ContainerHash == containerHash
                    && FcbFragments.IdComparer.Equals(shadowed.FragmentId, fragmentId))
                {
                    yield return shadowed;
                }
            }
        }
    }

    [GeneratedRegex(@"^worlds\\(?<world>[^\\]+)\\generated\\entitylibrary(_full)?\.fcb$", RegexOptions.IgnoreCase)]
    private static partial Regex WorldLibraryPattern();

    /// <summary>Worlds that ship an entity library, from a curated path list rather than by probing
    /// synthesized paths against the hash-only archive index - CRC32 collisions are real.</summary>
    public static IReadOnlyList<string> DiscoverWorlds(IEnumerable<string> candidatePaths)
    {
        var worlds = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (string path in candidatePaths)
        {
            if (!path.StartsWith(@"worlds\", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }
            if (WorldLibraryPattern().Match(path) is { Success: true } match)
            {
                worlds.Add(match.Groups["world"].Value.ToLowerInvariant());
            }
        }
        return [.. worlds];
    }

    /// <summary>
    /// Entity libraries outside the <c>worlds\</c> tree and outside the patch - what
    /// <c>CDlcService::GetEntityLibraries</c> would hand the engine. Conservative on purpose: the
    /// in-archive path shape for DLC libraries is not established.
    /// </summary>
    public static IReadOnlyList<string> DiscoverDlcLibraries(IEnumerable<string> candidatePaths)
        => [.. candidatePaths
            .Where(p => Path.GetFileName(p).Equals("entitylibrary.fcb", StringComparison.OrdinalIgnoreCase)
                        && !p.StartsWith(@"worlds\", StringComparison.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(p => p, StringComparer.OrdinalIgnoreCase)];

    /// <summary>
    /// The libraries a world resolves against, in the order <c>CXGame::LoadArchetypes</c> loads them:
    /// one base, then the patch override, then whatever <c>CDlcService::GetEntityLibraries</c> returns.
    /// </summary>
    /// <remarks>
    /// The base is an either/or - the engine branches on a flag and loads one of the two, never both -
    /// so the patch override wins over whichever was chosen. Which base the flag selects is not
    /// decoded, hence <paramref name="profile"/> rather than a guess.
    /// </remarks>
    /// <param name="dlcLibraries">DLC entity libraries the caller found, sorted for determinism; the
    /// engine's own order among them is not established.</param>
    public static IReadOnlyList<ArchetypeLayer> LayerPaths(
        string mapName, LibraryProfile profile = LibraryProfile.Server,
        IEnumerable<string>? dlcLibraries = null)
        => [BaseLayer(mapName, profile), .. SharedLayers(dlcLibraries)];

    /// <summary>Whichever of the two bases <paramref name="profile"/> selects.</summary>
    public static ArchetypeLayer BaseLayer(string mapName, LibraryProfile profile)
        => new(profile == LibraryProfile.Client
            ? $@"worlds\{mapName}\generated\entitylibrary_full.fcb"
            : $@"worlds\{mapName}\generated\entitylibrary.fcb");

    /// <summary>
    /// The layers loading after the base, which every world shares and the profile does not affect.
    /// Because an earlier layer can never shadow a later one, resolving just these answers what
    /// overrides an edit to the patch override or a DLC library, for every world at once.
    /// </summary>
    public static IReadOnlyList<ArchetypeLayer> SharedLayers(IEnumerable<string>? dlcLibraries = null)
        =>
        [
            new(@"generated\entitylibrarypatchoverride.fcb"),
            .. (dlcLibraries ?? [])
                .OrderBy(p => p, StringComparer.OrdinalIgnoreCase)
                .Select(dlc => new ArchetypeLayer(dlc, IsConfirmed: false)),
        ];

    public static ArchetypeIndex Load(
        string mapName, Func<string, byte[]?> readByPath, IProgress<string>? progress = null,
        LibraryProfile profile = LibraryProfile.Server, IEnumerable<string>? dlcLibraries = null)
        => Load(LayerPaths(mapName, profile, dlcLibraries), readByPath, progress);

    public static ArchetypeIndex Load(
        IReadOnlyList<ArchetypeLayer> layers, Func<string, byte[]?> readByPath,
        IProgress<string>? progress = null)
    {
        // CNoCaseStringID, so names differing only in case are the same archetype.
        var byName = new Dictionary<string, List<ArchetypeDefinition>>(StringComparer.OrdinalIgnoreCase);
        foreach (ArchetypeLayer layer in layers)
        {
            progress?.Report($"Reading {layer.Path}");
            if (readByPath(layer.Path) is not { } bytes
                || FcbDocument.TryDeserialize(bytes) is not { } root)
            {
                continue;
            }
            CollectLibrary(root, layer, byName);
        }

        progress?.Report(
            $"Resolved {byName.Count:N0} archetypes, {byName.Count(p => p.Value.Count > 1):N0} overridden");
        return new ArchetypeIndex(byName, layers);
    }

    /// <summary>
    /// Root to group to prototype to its <c>Entity</c> child - the same two levels
    /// <c>BuildArchetypesMap</c> descends, so nothing gets indexed that the engine would never see.
    /// Each prototype is also exactly one fragment, which is what makes a declaration routable back
    /// to the override unit a mod would stage.
    /// </summary>
    private static void CollectLibrary(
        FcbObject root, ArchetypeLayer layer, Dictionary<string, List<ArchetypeDefinition>> byName)
    {
        uint containerHash = NameHash.Compute(layer.Path);
        var fragmentIdByPrototype = new Dictionary<FcbObject, string>(ReferenceEqualityComparer.Instance);
        foreach (FcbFragment fragment in FcbFragments.List(root))
        {
            fragmentIdByPrototype[fragment.Node] = fragment.Id;
        }

        for (int group = 0; group < root.Children.Count; group++)
        {
            foreach (FcbObject prototype in root.Children[group].Children)
            {
                string? fragmentId = fragmentIdByPrototype.GetValueOrDefault(prototype);
                if (prototype.TypeHash != WorldHashes.EntityPrototype)
                {
                    continue;
                }
                foreach (FcbObject entity in prototype.Children)
                {
                    if (entity.TypeHash != WorldHashes.Entity)
                    {
                        continue;
                    }
                    string name = FcbEntityFields.ReadString(entity, WorldHashes.HidName);
                    if (name.Length == 0)
                    {
                        continue;
                    }
                    if (!byName.TryGetValue(name, out List<ArchetypeDefinition>? definitions))
                    {
                        byName[name] = definitions = [];
                    }
                    definitions.Add(new ArchetypeDefinition(name, layer, containerHash, fragmentId, entity));
                }
            }
        }
    }
}
