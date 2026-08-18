using JackAll.Core.Format.Fcb;
using JackAll.Tools.World;
using JackAll.Tools.Xbm;

namespace JackAll.Tests;

/// <summary>
/// The entity-to-mesh resolution the map's model layer rests on: the field hashes, both node shapes
/// the data ships in (flat on the component in worldsector files, nested in per-slot "object"
/// children in entity libraries), and the load pipeline's failure behaviour.
/// </summary>
public class WorldModelsTests
{
    private const string SectorFixture = "Fixtures/WorldSector/worldsector56.data.fcb";
    private const string LibraryFixture = "Fixtures/Fcb/worlds_entitylibrary.fcb";
    private const string MeshFixture = "Fixtures/Xbg/andrehyppolite.xbg";
    private const string LibraryPath = @"worlds\world1\generated\entitylibrary.fcb";

    private static bool FixturesPresent
        => File.Exists(SectorFixture) && File.Exists(LibraryFixture) && File.Exists(MeshFixture);

    [Fact]
    [Trait("Category", "RequiresFixture")]
    public void The_fixture_files_were_actually_found()
        => Assert.True(
            FixturesPresent,
            $"{SectorFixture} / {LibraryFixture} / {MeshFixture} missing, so every fixture-backed "
            + "test in this class silently no-opped.");

    /// <summary>The hashes the resolution rests on.</summary>
    [Fact]
    public void The_component_and_field_name_hashes_match_the_engines()
    {
        Assert.Equal(0xBF9B3A5Cu, FcbClassDefinitions.Crc32Ascii("text_objModel"));
        Assert.Equal(0x035982C6u, FcbClassDefinitions.Crc32Ascii("CGraphicComponent"));
        Assert.Equal(0xA8ADABECu, FcbClassDefinitions.Crc32Ascii("object"));
        Assert.Equal(0xBF9B3A5Cu, WorldHashes.TextObjModel);
        Assert.Equal(0x035982C6u, WorldHashes.CGraphicComponent);
        Assert.Equal(0xA8ADABECu, WorldHashes.GraphicObject);
    }

    /// <summary>Worldsector shape: the slot fields sit flat on the CGraphicComponent.</summary>
    [Fact]
    [Trait("Category", "RequiresFixture")]
    public void Sector_entities_resolve_their_flat_mesh_paths()
    {
        if (!FixturesPresent) return;

        List<string> paths = [.. SectorEntities().Select(e => WorldModels.MeshPath(e)).OfType<string>()];

        Assert.Equal(10, paths.Count);
        Assert.All(paths, p => Assert.EndsWith(".xbg", p));
        Assert.All(paths, p => Assert.Equal(p.ToLowerInvariant(), p));
        Assert.All(paths, p => Assert.DoesNotContain('/', p));
    }

    /// <summary>Entity-library shape: the slot fields sit in a nested "object" child.</summary>
    [Fact]
    [Trait("Category", "RequiresFixture")]
    public void Library_archetypes_resolve_their_nested_mesh_paths()
    {
        if (!FixturesPresent) return;

        ArchetypeIndex index = ArchetypeIndex.Load([new ArchetypeLayer(LibraryPath)], ReadLibrary);
        List<string> paths = [.. index.Names
            .Select(n => WorldModels.MeshPath(index.Winner(n)!.Node))
            .OfType<string>()];

        Assert.Equal(333, paths.Count);
        Assert.Contains(@"graphics\objects\mapcompass\compass.xbg", paths);
    }

    [Fact]
    [Trait("Category", "RequiresFixture")]
    public void A_reader_that_misses_everything_fails_every_path_without_throwing()
    {
        if (!FixturesPresent) return;

        WorldModelSet set = WorldModels.Load(BuildEntities(), EmptyIndex(), _ => null);

        Assert.Empty(set.Models);
        Assert.Empty(set.ModelIndexByEntity);
        Assert.True(set.FailedPathCount > 0);
    }

    [Fact]
    [Trait("Category", "RequiresFixture")]
    public void A_reader_that_serves_a_real_mesh_maps_every_resolving_entity()
    {
        if (!FixturesPresent) return;

        byte[] mesh = File.ReadAllBytes(MeshFixture);
        List<WorldEntity> entities = BuildEntities();
        WorldModelSet set = WorldModels.Load(
            entities, EmptyIndex(),
            path => path.EndsWith(".xbm", StringComparison.OrdinalIgnoreCase) ? null : mesh);

        int resolvable = entities.Count(e => e.Position is not null && WorldModels.MeshPath(e.Node) is not null);
        Assert.Equal(resolvable, set.ModelIndexByEntity.Count);
        Assert.Equal(0, set.FailedPathCount);
        Assert.All(set.ModelIndexByEntity.Values, i => Assert.InRange(i, 0, set.Models.Count - 1));
    }

    /// <summary>Foreign or corrupt bytes behind a referenced material path must cost the range its
    /// texture, not the load.</summary>
    [Fact]
    [Trait("Category", "RequiresFixture")]
    public void Foreign_bytes_behind_a_material_path_do_not_disturb_the_load()
    {
        if (!FixturesPresent) return;

        byte[] mesh = File.ReadAllBytes(MeshFixture);
        WorldModelSet set = WorldModels.Load(
            BuildEntities(), EmptyIndex(),
            path => path.EndsWith(".xbm", StringComparison.OrdinalIgnoreCase) ? [0, 1, 2, 3, 255] : mesh);

        Assert.NotEmpty(set.Models);
        Assert.NotEmpty(set.Models.SelectMany(m => m.MaterialRanges));
        Assert.All(set.Models.SelectMany(m => m.MaterialRanges), r => Assert.Null(r.DiffuseTexturePath));
    }

    /// <summary>The albedo slot differs per material template: DiffuseTexture1 outranks Skin,
    /// Skin outranks Fabric.</summary>
    [Fact]
    public void The_diffuse_slot_priority_follows_the_material_templates()
    {
        static XbmMaterial Material(params (string Key, string Value)[] textures) => new()
        {
            Name = "m",
            Template = "t",
            Textures = [.. textures.Select(t => new XbmProperty { Key = t.Key, Value = t.Value })],
            Properties = [],
        };

        Assert.Equal(@"a\d.xbt", WorldModels.DiffuseTextureOf(
            Material(("FabricTexture", @"a\f.xbt"), ("DiffuseTexture1", @"a\d.xbt"), ("SkinTexture", @"a\s.xbt"))));
        Assert.Equal(@"a\s.xbt", WorldModels.DiffuseTextureOf(
            Material(("NormalTexture1", @"a\n.xbt"), ("SkinTexture", @"a\s.xbt"))));
        Assert.Equal(@"a\f.xbt", WorldModels.DiffuseTextureOf(
            Material(("MaskTexture1", @"a\m.xbt"), ("FabricTexture", @"a\f.xbt"))));
        Assert.Equal(@"a\d2.xbt", WorldModels.DiffuseTextureOf(Material(("DiffuseTexture2", @"a\d2.xbt"))));
        Assert.Null(WorldModels.DiffuseTextureOf(Material(("NormalTexture1", @"a\n.xbt"))));
    }

    private static byte[]? ReadLibrary(string path)
        => path.Equals(LibraryPath, StringComparison.OrdinalIgnoreCase) ? File.ReadAllBytes(LibraryFixture) : null;

    private static ArchetypeIndex EmptyIndex() => ArchetypeIndex.Load([new ArchetypeLayer("missing.fcb")], _ => null);

    private static IEnumerable<FcbObject> SectorEntities()
        => FcbDocument.Deserialize(File.ReadAllBytes(SectorFixture)).Children
            .Where(layer => layer.TypeHash == WorldHashes.MissionLayer)
            .SelectMany(layer => layer.Children)
            .Where(node => node.TypeHash == WorldHashes.Entity);

    private static List<WorldEntity> BuildEntities()
    {
        var doc = new WorldSectorDocument
        {
            SourcePath = SectorFixture,
            SectorId = 56,
            PristineRoot = FcbDocument.Deserialize(File.ReadAllBytes(SectorFixture)),
        };
        return [.. doc.PristineRoot.Children
            .Where(layer => layer.TypeHash == WorldHashes.MissionLayer)
            .SelectMany(layer => layer.Children)
            .Where(node => node.TypeHash == WorldHashes.Entity)
            .Select(node => new WorldEntity
            {
                Node = node,
                HomeSector = doc,
                LayerPathId = "main",
                Position = FcbEntityFields.ReadVector3(node, WorldHashes.HidPos)
                    ?? FcbEntityFields.ReadVector3(node, WorldHashes.HidPosPrecise),
            })];
    }
}
