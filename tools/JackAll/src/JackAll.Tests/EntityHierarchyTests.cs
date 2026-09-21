using JackAll.Core.Format.Fcb;
using JackAll.Tools.World;

namespace JackAll.Tests;

/// <summary>The Map tab's hierarchy over a retail sector: layer first, then archetype path or name
/// family, with every entity filed exactly once.</summary>
public class EntityHierarchyTests
{
    private const string FixturePath = "Fixtures/WorldSector/worldsector56.data.fcb";
    private const string SectorPath = @"levels\mp_14_woodlands\generated\worldsectors\worldsector56.data.fcb";

    private static readonly ArchetypeIndex NoLibrary =
        ArchetypeIndex.Load([new ArchetypeLayer("missing.fcb")], _ => null);

    private static List<WorldEntity> Entities()
    {
        byte[] bytes = File.ReadAllBytes(FixturePath);
        var map = new TerrainMap
        {
            Name = "mp_14_woodlands",
            SectorsPerSide = 10,
            Sectors = [(@"levels\mp_14_woodlands\generated\sdat\sd56.sdat", 56)],
        };
        return [.. WorldLoader.Load(map, p => p.Equals(SectorPath, StringComparison.OrdinalIgnoreCase) ? bytes : null).Entities];
    }

    private static IEnumerable<WorldEntity> All(HierarchyGroup group)
        => group.Entities.Concat(group.Groups.SelectMany(All));

    [Fact]
    [Trait("Category", "RequiresFixture")]
    public void Layers_come_first_with_main_at_the_top()
    {
        if (!File.Exists(FixturePath)) return;

        List<WorldEntity> entities = Entities();
        IReadOnlyList<HierarchyGroup> layers = EntityHierarchy.Build(entities, NoLibrary);

        Assert.Equal(MissionLayers.MainName, layers[0].LayerPathId);
        Assert.Equal(entities.Select(e => e.LayerPathId).Distinct().Count(), layers.Count);
        foreach (HierarchyGroup layer in layers)
        {
            Assert.All(All(layer), e => Assert.Equal(layer.LayerPathId, e.LayerPathId));
        }
    }

    [Fact]
    [Trait("Category", "RequiresFixture")]
    public void Every_entity_is_filed_exactly_once()
    {
        if (!File.Exists(FixturePath)) return;

        List<WorldEntity> entities = Entities();
        List<WorldEntity> filed = [.. EntityHierarchy.Build(entities, NoLibrary).SelectMany(All)];

        Assert.Equal(entities.Count, filed.Count);
        Assert.Equal(entities.ToHashSet(), filed.ToHashSet());
    }

    /// <summary>A bound entity files under its archetype's dotted path, split the way the Library tab does.</summary>
    [Fact]
    public void An_archetype_bound_entity_files_under_its_dotted_path()
    {
        var entity = new WorldEntity
        {
            Node = new FcbObject(),
            HomeSector = new WorldSectorDocument { SourcePath = SectorPath, SectorId = 56, PristineRoot = new FcbObject() },
            LayerPathId = MissionLayers.MainName,
            Name = "Crate_7",
            ArchetypeName = "OA_Props.Props.Crate01",
        };

        HierarchyGroup layer = Assert.Single(EntityHierarchy.Build([entity], NoLibrary));
        HierarchyGroup section = Assert.Single(layer.Groups);
        Assert.Equal(EntityHierarchy.FromArchetype, section.Label);
        Assert.Equal(["OA_Props", "Props", "Crate01"], Path(section, entity));
    }

    /// <summary>A big name family splits into numbered buckets, in numeric order.</summary>
    [Fact]
    public void A_large_family_splits_into_buckets_in_numeric_order()
    {
        var sector = new WorldSectorDocument { SourcePath = SectorPath, SectorId = 56, PristineRoot = new FcbObject() };
        List<WorldEntity> many = [.. Enumerable.Range(0, 12_000).Select(i => new WorldEntity
        {
            Node = new FcbObject(), HomeSector = sector, LayerPathId = MissionLayers.MainName, Name = $"StaticObject_{i}",
        })];

        HierarchyGroup family = EntityHierarchy.Build(many, NoLibrary)[0].Groups.Single().Groups.Single();
        Assert.Equal("StaticObject", family.Label);
        Assert.Equal(Enumerable.Range(0, 12).Select(k => $"{k * 1000:N0}+"), family.Groups.Select(g => g.Label));
    }

    private static List<string> Path(HierarchyGroup group, WorldEntity entity)
    {
        foreach (HierarchyGroup sub in group.Groups)
        {
            if (sub.Entities.Contains(entity))
            {
                return [sub.Label];
            }
            if (All(sub).Contains(entity))
            {
                return [sub.Label, .. Path(sub, entity)];
            }
        }
        return [];
    }
}
