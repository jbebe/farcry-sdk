using System.Text.RegularExpressions;
using JackAll.Core.Format;
using JackAll.Core.Format.Fcb;
using JackAll.Core.Mods;
using JackAll.Core.Vfs;

namespace JackAll.Tools.World;

/// <summary>
/// Turns a mod layer into the form a mod ships. Every new world the layer creates is built from its
/// fragments into whole files - each sector's worldsector, the entity library cut from tmpla's full
/// set, the sector flags and the navmesh - and everything else in the layer is copied as it is. A
/// layer that only adds to another mod's world gets the navmesh of the sectors it changed, and keeps
/// its fragments, which merge onto that mod's files at install.
/// </summary>
public static partial class WorldPack
{
    public const string FullLibrary = @"worlds\tmpla\generated\entitylibrary_full.fcb";

    [GeneratedRegex(@"^worlds\\(?<world>[^\\]+)\\generated\\\k<world>\.game\.xml$", RegexOptions.IgnoreCase)]
    private static partial Regex DescriptorPattern();

    [GeneratedRegex(@"^levels\\(?<level>[^\\]+)\\generated\\worldsectors\\worldsector(?<id>\d+)\.data\.fcb$", RegexOptions.IgnoreCase)]
    private static partial Regex WorldSectorPattern();

    /// <summary>
    /// Game path -&gt; bytes of <paramref name="mod"/> packed, read through <paramref name="vfs"/>, which
    /// must already hold the layers below it and <paramref name="mod"/> itself, on top.
    /// </summary>
    public static SortedDictionary<string, byte[]> Pack(
        IModLayer mod, GameVfs vfs, FcbClassDefinitions defs, IProgress<string>? progress = null)
    {
        Dictionary<string, TerrainMap> maps = TerrainMap.Discover(vfs.Files.Values.Where(f => f.NameIsKnown).Select(f => f.Path))
            .ToDictionary(m => m.Name, StringComparer.OrdinalIgnoreCase);
        var packed = new SortedDictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);

        bool IsNew(string world) => NewWorld.IsNew(world, vfs.ReadOriginal, vfs.ReadByPath);

        var created = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (uint hash in mod.Hashes)
        {
            if (mod.PathOf(hash) is not { } path)
            {
                progress?.Report($"Skipped {hash:X8}: staged by hash, so it has no path to copy it to");
                continue;
            }
            packed[path] = mod.Read(hash);
            if (DescriptorPattern().Match(path) is { Success: true } descriptor && IsNew(descriptor.Groups["world"].Value))
            {
                created.Add(descriptor.Groups["world"].Value);
            }
        }

        var built = new HashSet<uint>();
        foreach (string world in created)
        {
            TerrainMap map = maps.GetValueOrDefault(world)
                ?? throw new InvalidOperationException($"{world} has no terrain, so it cannot be packed.");
            foreach ((string path, byte[] bytes) in BuildWorld(world, map, vfs.ReadByPath, defs, progress))
            {
                packed[path] = bytes;
                built.Add(NameHash.Compute(path));
            }
        }

        var extended = new Dictionary<string, HashSet<int>>(StringComparer.OrdinalIgnoreCase);
        foreach ((uint container, IReadOnlyList<FragmentOverride> fragments) in mod.FragmentOverrides)
        {
            if (built.Contains(container))
            {
                continue;
            }
            foreach (FragmentOverride fragment in fragments)
            {
                if (mod.PathOf(fragment.EntryHash) is { } path)
                {
                    packed[path] = mod.Read(fragment.EntryHash);
                }
            }
            if (FragmentMerge.ContainerPathOf([[.. fragments.Select(f => (mod, f.EntryHash))]]) is { } containerPath
                && WorldSectorPattern().Match(containerPath) is { Success: true } sector
                && IsNew(sector.Groups["level"].Value))
            {
                string level = sector.Groups["level"].Value;
                (extended.TryGetValue(level, out HashSet<int>? ids) ? ids : extended[level] = []).Add(int.Parse(sector.Groups["id"].Value));
            }
        }

        foreach ((string world, HashSet<int> sectorIds) in extended)
        {
            TerrainMap map = maps[world];
            progress?.Report($"{world}: navmesh for the {sectorIds.Count} sectors this layer changes");
            (Dictionary<int, NvmSector> sectors, _) = NavMeshGenerator.BuildMap(map, world, vfs.ReadByPath, progress);
            foreach ((string sdat, int id) in map.Sectors.Where(s => sectorIds.Contains(s.SectorId)))
            {
                packed[WorldNavMesh.PathOf(sdat, id)] = NvmSectorFile.Encode(sectors[id]);
            }
        }
        return packed;
    }

    /// <summary>A new world's built files: every sector's worldsector, its entity library, its
    /// sectorsdep with every sector flagged, and its navmesh.</summary>
    public static SortedDictionary<string, byte[]> BuildWorld(
        string world, TerrainMap map, Func<string, byte[]?> read, FcbClassDefinitions defs, IProgress<string>? progress = null)
    {
        var files = new SortedDictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
        var namers = new List<FcbObject>();

        // Every sector gets a file, an empty one as a bare main layer, so a mod of this world always
        // has one to fragment and never has to replace sectorsdep, which has no ids to merge by.
        foreach ((string sdat, int id) in map.Sectors)
        {
            string path = WorldLoader.WorldSectorPath(sdat, id);
            FcbObject sector = read(path) is { } bytes ? FcbDocument.Deserialize(bytes) : ContainerSkeletons.WorldSector(id);
            sector.Values[WorldHashes.WorldSectorX] = BitConverter.GetBytes((uint)(id % NewWorld.TemplateSide));
            sector.Values[WorldHashes.WorldSectorY] = BitConverter.GetBytes((uint)(id / NewWorld.TemplateSide));
            files[path] = FcbDocument.Serialize(sector);
            namers.Add(sector);
        }
        foreach (string kind in new[] { "mapsdata", "managers", "omnis" })
        {
            if (read($@"worlds\{world}\generated\{world}.{kind}.fcb") is { } bytes && FcbDocument.TryDeserialize(bytes) is { } root)
            {
                namers.Add(root);
            }
        }

        string libraryPath = ArchetypeIndex.BaseLayer(world).Path;
        FcbObject? authored = read(libraryPath) is { } authoredBytes ? FcbDocument.Deserialize(authoredBytes) : null;
        if (authored is not null)
        {
            namers.Add(authored);
        }
        FcbObject full = FcbDocument.Deserialize(read(FullLibrary)
            ?? throw new InvalidOperationException($"{FullLibrary} is missing; a new world's library is cut from it."));
        FcbObject cut = LibraryCut.Cut(full, namers);
        byte[] library = FcbDocument.Serialize(cut);
        if (authored is not null)
        {
            library = FcbAssembler.Apply(library, FcbFragments.List(authored).ToDictionary(
                f => f.Id, f => FcbXml.ToXml(f.Node, defs), FcbFragments.IdComparer));
        }
        files[libraryPath] = library;
        progress?.Report($"{world}: {cut.Children.Sum(g => g.Children.Count):N0} archetypes kept of {full.Children.Sum(g => g.Children.Count):N0}");

        string depPath = $@"worlds\{world}\generated\{world}.sectorsdep.fcb";
        FcbObject sectorsDep = FcbDocument.Deserialize(read(depPath) ?? throw new InvalidOperationException($"{depPath} is missing."));
        IReadOnlySet<int> ids = map.Sectors.Select(s => s.SectorId).ToHashSet();
        SectorsDep.Mark(sectorsDep, SectorsDep.HasMainSectorData, ids);
        SectorsDep.Mark(sectorsDep, SectorsDep.HasNavMesh, ids);
        files[depPath] = FcbDocument.Serialize(sectorsDep);

        (Dictionary<int, NvmSector> navMesh, int obstacles) = NavMeshGenerator.BuildMap(
            map, world, path => files.TryGetValue(path, out byte[]? builtBytes) ? builtBytes : read(path), progress);
        foreach ((string sdat, int id) in map.Sectors)
        {
            files[WorldNavMesh.PathOf(sdat, id)] = NvmSectorFile.Encode(navMesh[id]);
        }
        progress?.Report($"{world}: {map.Sectors.Count} sectors, {obstacles} obstacles cut out of the navmesh");
        return files;
    }
}
