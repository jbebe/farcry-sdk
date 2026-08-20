using System.Numerics;
using JackAll.Core.Format.Fcb;
using JackAll.Tools.World;
using JackAll.Tools.Xbg;
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

        List<string> paths = [.. SectorEntities().SelectMany(WorldModels.MeshPaths)];

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
        List<string> paths = [.. index.Names.SelectMany(n => WorldModels.MeshPaths(index.Winner(n)!.Node))];

        Assert.Equal(333, paths.Count);
        Assert.Contains(@"graphics\objects\mapcompass\compass.xbg", paths);
    }

    /// <summary>A component's several graphics slots name parts inside one mesh file rather than
    /// separate files - hidMeshName is what differs between them - so an archetype with several
    /// slots still resolves to the single .xbg its component draws.</summary>
    [Fact]
    [Trait("Category", "RequiresFixture")]
    public void Several_slots_on_one_component_resolve_to_a_single_mesh()
    {
        if (!FixturesPresent) return;

        ArchetypeIndex index = ArchetypeIndex.Load([new ArchetypeLayer(LibraryPath)], ReadLibrary);
        List<FcbObject> multiSlot = [.. index.Names
            .Select(n => index.Winner(n)!.Node)
            .Where(node => FcbEntityFields.FindComponent(node, WorldHashes.CGraphicComponent) is { } component
                && component.Children.Count(c => c.TypeHash == WorldHashes.GraphicObject) > 1)];

        Assert.NotEmpty(multiSlot);
        Assert.All(multiSlot, node => Assert.Single(WorldModels.MeshPaths(node)));
    }

    [Fact]
    [Trait("Category", "RequiresFixture")]
    public void A_reader_that_misses_everything_fails_every_path_without_throwing()
    {
        if (!FixturesPresent) return;

        WorldModelSet set = WorldModels.Load(BuildEntities(), EmptyIndex(), _ => null);

        Assert.Empty(set.Models);
        Assert.Empty(set.ModelIndicesByEntity);
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

        int resolvable = entities.Count(e => e.Position is not null && WorldModels.MeshPaths(e.Node).Count > 0);
        Assert.Equal(resolvable, set.ModelIndicesByEntity.Count);
        Assert.Equal(0, set.FailedPathCount);
        Assert.All(set.ModelIndicesByEntity.Values,
            indices => Assert.All(indices, i => Assert.InRange(i, 0, set.Models.Count - 1)));
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
        Assert.Equal(@"a\d.xbt", WorldModels.DiffuseTextureOf(
            Material(textures: [("FabricTexture", @"a\f.xbt"), ("DiffuseTexture1", @"a\d.xbt"), ("SkinTexture", @"a\s.xbt")])));
        Assert.Equal(@"a\s.xbt", WorldModels.DiffuseTextureOf(
            Material(textures: [("NormalTexture1", @"a\n.xbt"), ("SkinTexture", @"a\s.xbt")])));
        Assert.Equal(@"a\f.xbt", WorldModels.DiffuseTextureOf(
            Material(textures: [("MaskTexture1", @"a\m.xbt"), ("FabricTexture", @"a\f.xbt")])));
        Assert.Equal(@"a\d2.xbt", WorldModels.DiffuseTextureOf(Material(textures: [("DiffuseTexture2", @"a\d2.xbt")])));
        Assert.Null(WorldModels.DiffuseTextureOf(Material(textures: [("NormalTexture1", @"a\n.xbt")])));
    }

    private static XbmMaterial Material(
        (string Key, string Value)[]? textures = null, (string Key, string Value)[]? properties = null) => new()
    {
        Name = "m",
        Template = "t",
        Textures = [.. (textures ?? []).Select(t => new XbmProperty { Key = t.Key, Value = t.Value })],
        Properties = [.. (properties ?? []).Select(p => new XbmProperty { Key = p.Key, Value = p.Value })],
    };

    /// <summary>A material only reads its alpha as coverage when it says so; on the rest that
    /// channel holds a gloss or spec mask.</summary>
    [Fact]
    public void Alpha_mode_follows_the_materials_own_flags()
    {
        Assert.Equal(MaterialAlpha.Opaque, WorldModels.AlphaOf(Material()));
        Assert.Equal(MaterialAlpha.Opaque, WorldModels.AlphaOf(
            Material(properties: [("AlphaTestEnabled", "0"), ("AlphaBlendEnabled", "0")])));
        Assert.Equal(MaterialAlpha.Mask, WorldModels.AlphaOf(Material(properties: [("AlphaTestEnabled", "1")])));
        Assert.Equal(MaterialAlpha.Blend, WorldModels.AlphaOf(Material(properties: [("AlphaBlendEnabled", "1")])));
        Assert.Equal(MaterialAlpha.Blend, WorldModels.AlphaOf(
            Material(properties: [("AlphaTestEnabled", "1"), ("AlphaBlendEnabled", "1")])));
    }

    /// <summary>The tints the engine's diffuse blend needs, including the HDR ones a clamp would
    /// darken and the single-tint materials that must fill both ends of the blend.</summary>
    [Fact]
    public void Diffuse_tints_survive_parsing_unclamped()
    {
        Assert.Equal(
            (new Vector3(0.11f, 0.11f, 0.11f), new Vector3(0.282f, 0.282f, 0.282f)),
            WorldModels.TintsOf(Material(properties:
                [("DiffuseColorBase", "0.11, 0.11, 0.11"), ("DiffuseColor1", "0.282, 0.282, 0.282")])));

        // 295 retail materials author a tint past 1; capping them renders the surface too dark.
        Assert.Equal(
            new Vector3(1.647f, 1.914f, 2f),
            WorldModels.TintsOf(Material(properties: [("DiffuseColor1", "1.647, 1.914, 2")])).Tint);

        // Either tint alone fills both ends, so the blend is flat rather than fading to white.
        Assert.Equal(
            (new Vector3(0.5f, 0.5f, 0.5f), new Vector3(0.5f, 0.5f, 0.5f)),
            WorldModels.TintsOf(Material(properties: [("DiffuseColorBase", "0.5, 0.5, 0.5")])));

        Assert.Equal((Vector3.One, Vector3.One), WorldModels.TintsOf(Material()));
    }

    /// <summary>
    /// Everything the Generic shader reads off a material, in one pass: both diffuse layers, the
    /// mask that gates them, all three tints and all three UV multipliers. The values are the swamp
    /// boat's hull material, whose layer 1 is an 8x8 grey swatch - the case that first showed the
    /// second layer had to exist.
    /// </summary>
    [Fact]
    public void A_material_carries_both_layers_its_mask_and_every_tiling()
    {
        MaterialSurface surface = WorldModels.SurfaceOf(Material(
            textures:
            [
                ("DiffuseTexture1", @"graphics\_textures\diffuse\icone\grey.xbt"),
                ("DiffuseTexture2", @"graphics\_textures\diffuse\ground\dirt_03_d.xbt"),
                ("MaskTexture1", @"graphics\_textures\mask\rust_01_m.xbt"),
            ],
            properties:
            [
                ("DiffuseColorBase", "0.322, 0.306, 0.306"),
                ("DiffuseColor1", "0.439, 0.439, 0.439"),
                ("DiffuseColor2", "0.369, 0.322, 0.282"),
                ("DiffuseTiling1", "15, 15"),
                ("DiffuseTiling2", "4, 2"),
                ("MaskTiling1", "1, 1"),
            ]));

        Assert.Equal(@"graphics\_textures\diffuse\icone\grey.xbt", surface.DiffuseTexturePath);
        Assert.Equal(@"graphics\_textures\diffuse\ground\dirt_03_d.xbt", surface.SecondDiffusePath);
        Assert.Equal(@"graphics\_textures\mask\rust_01_m.xbt", surface.MaskPath);

        Assert.Equal(new Vector3(0.322f, 0.306f, 0.306f), surface.TintBase);
        Assert.Equal(new Vector3(0.439f, 0.439f, 0.439f), surface.Tint);
        Assert.Equal(new Vector3(0.369f, 0.322f, 0.282f), surface.SecondTint);

        Assert.Equal(new Vector2(15f, 15f), surface.DiffuseTiling);
        Assert.Equal(new Vector2(4f, 2f), surface.SecondDiffuseTiling);
        Assert.Equal(new Vector2(1f, 1f), surface.MaskTiling);
    }

    /// <summary>A material naming no second layer, no mask and no tiling leaves those switched off
    /// rather than defaulted to something that would scale or darken the surface.</summary>
    [Fact]
    public void What_a_material_does_not_name_stays_neutral()
    {
        MaterialSurface surface = WorldModels.SurfaceOf(Material(
            textures: [("DiffuseTexture1", @"graphics\_textures\diffuse\wood\woodplank_03_d.xbt")]));

        Assert.Null(surface.SecondDiffusePath);
        Assert.Null(surface.MaskPath);
        Assert.Equal(Vector3.One, surface.SecondTint);
        Assert.Equal(Vector2.One, surface.DiffuseTiling);
        Assert.Equal(Vector2.One, surface.SecondDiffuseTiling);
        Assert.Equal(Vector2.One, surface.MaskTiling);
    }

    /// <summary>A zero tiling would collapse the whole texture into one texel, so it reads as
    /// "unset" rather than being passed through.</summary>
    [Fact]
    public void A_zero_tiling_is_ignored()
    {
        MaterialSurface surface = WorldModels.SurfaceOf(Material(
            textures: [("DiffuseTexture1", "a.xbt")],
            properties: [("DiffuseTiling1", "0, 0")]));

        Assert.Equal(Vector2.One, surface.DiffuseTiling);
    }

    /// <summary>The same over real .xbm bytes, because the layers only reach the shader if the
    /// parser surfaces every slot under the key the lookup expects.</summary>
    [Fact]
    [Trait("Category", "RequiresFixture")]
    public void Real_layered_materials_resolve_through_the_parser()
    {
        const string Fixture = @".\Fixtures\XbmSwatch\swaps.xbm";
        if (!File.Exists(Fixture)) return;

        MaterialSurface surface = WorldModels.SurfaceOf(XbmMaterial.Parse(File.ReadAllBytes(Fixture)));

        Assert.EndsWith("grey.xbt", surface.DiffuseTexturePath, StringComparison.OrdinalIgnoreCase);
        Assert.EndsWith("clay02_d.xbt", surface.SecondDiffusePath, StringComparison.OrdinalIgnoreCase);
        Assert.NotNull(surface.MaskPath);

        // The other fixture points both layers at the same swatch, which is a real and legitimate
        // shape: the mask still decides the tint even when there is nothing to blend to.
        const string Flat = @".\Fixtures\XbmSwatch\flat.xbm";
        if (!File.Exists(Flat)) return;

        MaterialSurface flat = WorldModels.SurfaceOf(XbmMaterial.Parse(File.ReadAllBytes(Flat)));
        Assert.EndsWith("grey.xbt", flat.DiffuseTexturePath, StringComparison.OrdinalIgnoreCase);
        Assert.EndsWith("grey.xbt", flat.SecondDiffusePath, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>End-to-end over real materials, because the flags only reach
    /// <see cref="WorldModels.AlphaOf"/> if the .xbm parser surfaces them as plain integers. The
    /// opaque case is the one that matters most: most of the retail set is opaque, and reading its
    /// alpha as coverage erases the surface.</summary>
    [Fact]
    [Trait("Category", "RequiresFixture")]
    public void Real_materials_classify_through_the_parser()
    {
        foreach (string path in Directory.EnumerateFiles(@".\Fixtures\Xbm", "*.xbm"))
        {
            Assert.Equal(MaterialAlpha.Opaque, WorldModels.AlphaOf(XbmMaterial.Parse(File.ReadAllBytes(path))));
        }

        AssertFixtureAlpha(@".\Fixtures\XbmAlpha\blended.xbm", MaterialAlpha.Blend);
        AssertFixtureAlpha(@".\Fixtures\XbmAlpha\masked.xbm", MaterialAlpha.Mask);
    }

    private static void AssertFixtureAlpha(string path, MaterialAlpha expected)
    {
        if (!File.Exists(path)) return;

        Assert.Equal(expected, WorldModels.AlphaOf(XbmMaterial.Parse(File.ReadAllBytes(path))));
    }

    private static byte[]? ReadLibrary(string path)
        => path.Equals(LibraryPath, StringComparison.OrdinalIgnoreCase) ? File.ReadAllBytes(LibraryFixture) : null;

    /// <summary>A handful of outfits over one mesh is cheap, so each entity keeps its own.</summary>
    [Fact]
    [Trait("Category", "RequiresFixture")]
    public void A_few_outfits_over_one_mesh_each_keep_their_own_geometry()
    {
        if (!File.Exists(BoatFixture)) return;

        string[] parts = BoatParts();
        WorldModelSet set = LoadOutfits(Outfits(parts, 3));

        Assert.Equal(3, set.Models.Count);
    }

    /// <summary>
    /// Past the cap they all collapse onto the most common one. Without this a wardrobe file bakes
    /// once per outfit worn anywhere in the world - 682 of them for the mercenaries of world 1,
    /// turning a 2 MB mesh into 137 MB.
    /// </summary>
    [Fact]
    [Trait("Category", "RequiresFixture")]
    public void Too_many_outfits_over_one_mesh_collapse_onto_the_most_common()
    {
        if (!File.Exists(BoatFixture)) return;

        string[] parts = BoatParts();
        List<string> outfits = Outfits(parts, WorldModels.MaxOutfitsPerMesh + 4);

        // One of them is worn twice, so it is the one everybody ends up in.
        string favourite = outfits[2];
        outfits.Add(favourite);

        WorldModelSet set = LoadOutfits(outfits);
        Assert.Single(set.Models);

        WorldModel expected = WorldModels.Bake(
            BoatFixture, XbgModel.Parse(File.ReadAllBytes(BoatFixture)), WorldModels.FineTriangleBudget,
            onlyParts: new HashSet<string>(favourite.Split(';'), StringComparer.OrdinalIgnoreCase))!;
        Assert.Equal(expected.Indices.Length, set.Models[0].Indices.Length);

        // Every entity still draws, just all of them in the same clothes.
        Assert.Equal(outfits.Count, set.ModelIndicesByEntity.Count);
    }

    private const string BoatFixture = "Fixtures/Xbg/swampboat.xbg";

    private static string[] BoatParts()
        => [.. XbgModel.Parse(File.ReadAllBytes(BoatFixture)).Submeshes
            .Select(s => s.PartName).Where(p => p.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase)
            .Order(StringComparer.Ordinal)];

    /// <summary>Distinct non-empty part lists, taken as the bit patterns of 1, 2, 3...</summary>
    private static List<string> Outfits(string[] parts, int count)
        => [.. Enumerable.Range(1, count).Select(n => MeshRef.ParseParts(
            string.Join(';', parts.Where((_, i) => i < 30 && (n & (1 << i)) != 0))))];

    private static WorldModelSet LoadOutfits(IReadOnlyList<string> outfits)
    {
        var doc = new WorldSectorDocument
        {
            SourcePath = BoatFixture,
            SectorId = 0,
            PristineRoot = new FcbObject { TypeHash = WorldHashes.Entity },
        };

        byte[] mesh = File.ReadAllBytes(BoatFixture);
        List<WorldEntity> entities = [.. outfits.Select(parts =>
        {
            var graphics = new FcbObject { TypeHash = WorldHashes.CGraphicComponent };
            graphics.Values[WorldHashes.TextObjModel] = System.Text.Encoding.UTF8.GetBytes(BoatFixture);
            graphics.Values[WorldHashes.HidMeshName] = System.Text.Encoding.UTF8.GetBytes(parts);
            var components = new FcbObject { TypeHash = WorldHashes.Components };
            components.Children.Add(graphics);
            var node = new FcbObject { TypeHash = WorldHashes.Entity };
            node.Children.Add(components);
            return new WorldEntity { Node = node, HomeSector = doc, LayerPathId = "main", Position = Vector3.Zero };
        })];

        return WorldModels.Load(entities, EmptyIndex(),
            path => path.EndsWith(".xbm", StringComparison.OrdinalIgnoreCase) ? null : mesh);
    }

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
