using JackAll.Tools.World;

namespace JackAll.Tests;

/// <summary>The Map tab's check against untouched retail worlds, which the game loads fine: whatever
/// it reports there is noise it would bury a real problem in.</summary>
[Trait("Category", "RequiresFixture")]
public class WorldLintCorpusTests
{
    private static readonly string ArchiveRoot = Path.Combine(Fc2Corpus.Root, "worlds", "worlds");

    [Theory]
    [InlineData("world1")]
    [InlineData("world2")]
    public void An_untouched_retail_world_reports_nothing(string name)
    {
        if (!Directory.Exists(Path.Combine(ArchiveRoot, "levels"))) return;

        List<string> paths = [.. Directory.EnumerateFiles(ArchiveRoot, "*", SearchOption.AllDirectories)
            .Select(p => Path.GetRelativePath(ArchiveRoot, p))];
        byte[]? Read(string path) => File.Exists(Path.Combine(ArchiveRoot, path)) ? File.ReadAllBytes(Path.Combine(ArchiveRoot, path)) : null;
        TerrainMap map = TerrainMap.Discover(paths).Single(m => m.Name == name);
        Fc2World world = WorldLoader.Load(map, Read);
        var session = new WorldEditSession(world, map.SectorsPerSide);
        HashSet<string> known = new(paths, StringComparer.OrdinalIgnoreCase);
        Dictionary<int, string> sdat = map.Sectors.ToDictionary(s => s.SectorId, s => s.Path);

        IReadOnlyList<WorldFinding> findings = WorldLint.Run(
            session, ArchetypeIndex.Load(name, Read),
            sector => sdat.TryGetValue(sector, out string? path) && known.Contains(WorldNavMesh.PathOf(path, sector)),
            _ => new HashSet<string>());

        Assert.True(findings.Count == 0, $"{world.Entities.Count} entities - " + string.Join("; ", findings
            .GroupBy(f => (f.Severity, f.Kind)).Select(g => $"{g.Key.Severity} {g.Key.Kind}: {g.Count()}")));
    }
}
