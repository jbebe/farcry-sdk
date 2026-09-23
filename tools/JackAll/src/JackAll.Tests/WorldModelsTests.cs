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
    private const string SectorFixture = WorldSectorFragmentTests.Sector56;
    private const string LibraryFixture = FcbDocumentTests.Worlds;
    private const string LibraryPath = @"worlds\world1\generated\entitylibrary.fcb";

    // Any mesh at all, served for every path a sector names.
    private const string MeshFixture = XbgFixtures.Character;

    // A vehicle of 18 separately slotted pieces, with two states of its bumper and water tank.
    private const string Buggy = XbgFixtures.Buggy;

    // A mesh with enough named parts to make more distinct outfits than the cap allows.
    private const string Boat = XbgFixtures.SwampBoat;

    // A layered material whose two diffuse layers are different swatches.
    private const string Swaps = "XbmSwatch/swaps.xbm";

    // A layered material pointing both layers at the same swatch.
    private const string Flat = "XbmSwatch/flat.xbm";

    // An opaque material, the shape most of the retail set has.
    private const string OpaqueMaterial = "Xbm/ggauthier-m-1337050735309082.xbm";

    // An alpha-blended material.
    private const string BlendedMaterial = "XbmAlpha/blended.xbm";

    // An alpha-tested material.
    private const string MaskedMaterial = "XbmAlpha/masked.xbm";

    [Fact]
    [Trait("Category", "RequiresFixture")]
    public void The_fixtures_were_actually_found()
        => Fixture.AssertPresent(
            SectorFixture, LibraryFixture, MeshFixture, Buggy, Boat, Swaps, Flat,
            OpaqueMaterial, BlendedMaterial, MaskedMaterial);

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
    public void Sector_entities_resolve_their_flat_mesh_paths()
    {
        if (Fixture.Read(SectorFixture) is not { } sector) return;

        List<string> paths = [.. BuildEntities(sector).Select(e => e.Node).SelectMany(WorldModels.MeshPaths)];

        Assert.Equal(10, paths.Count);
        Assert.All(paths, p => Assert.EndsWith(".xbg", p));
        Assert.All(paths, p => Assert.Equal(p.ToLowerInvariant(), p));
        Assert.All(paths, p => Assert.DoesNotContain('/', p));
    }

    /// <summary>Entity-library shape: the slot fields sit in a nested "object" child.</summary>
    [Fact]
    public void Library_archetypes_resolve_their_nested_mesh_paths()
    {
        if (!Fixture.Present(LibraryFixture)) return;

        ArchetypeIndex index = ArchetypeIndex.Load([new ArchetypeLayer(LibraryPath)], ReadLibrary);
        List<string> paths = [.. index.Names.SelectMany(n => WorldModels.MeshPaths(index.Winner(n)!.Node))];

        Assert.Equal(333, paths.Count);
        Assert.Contains(@"graphics\objects\mapcompass\compass.xbg", paths);
    }

    /// <summary>A component's several graphics slots name parts inside one mesh file rather than
    /// separate files - hidMeshName is what differs between them - so an archetype with several
    /// slots still resolves to the single .xbg its component draws.</summary>
    [Fact]
    public void Several_slots_on_one_component_resolve_to_a_single_mesh()
    {
        if (!Fixture.Present(LibraryFixture)) return;

        ArchetypeIndex index = ArchetypeIndex.Load([new ArchetypeLayer(LibraryPath)], ReadLibrary);
        List<FcbObject> multiSlot = [.. index.Names
            .Select(n => index.Winner(n)!.Node)
            .Where(node => FcbEntityFields.FindComponent(node, WorldHashes.CGraphicComponent) is { } component
                && component.Children.Count(c => c.TypeHash == WorldHashes.GraphicObject) > 1)];

        Assert.NotEmpty(multiSlot);
        Assert.All(multiSlot, node => Assert.Single(WorldModels.MeshPaths(node)));
    }

    [Fact]
    public void A_reader_that_misses_everything_fails_every_path_without_throwing()
    {
        if (Fixture.Read(SectorFixture) is not { } sector) return;

        WorldModelSet set = WorldModels.Load(BuildEntities(sector), EmptyIndex(), _ => null);

        Assert.Empty(set.Models);
        Assert.Empty(set.ModelIndicesByEntity);
        Assert.True(set.FailedPathCount > 0);
    }

    [Fact]
    public void A_reader_that_serves_a_real_mesh_maps_every_resolving_entity()
    {
        if (Fixture.Read(SectorFixture) is not { } sector || Fixture.Read(MeshFixture) is not { } mesh) return;

        List<WorldEntity> entities = BuildEntities(sector);
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
    public void Foreign_bytes_behind_a_material_path_do_not_disturb_the_load()
    {
        if (Fixture.Read(SectorFixture) is not { } sector || Fixture.Read(MeshFixture) is not { } mesh) return;

        WorldModelSet set = WorldModels.Load(
            BuildEntities(sector), EmptyIndex(),
            path => path.EndsWith(".xbm", StringComparison.OrdinalIgnoreCase) ? [0, 1, 2, 3, 255] : mesh);

        Assert.NotEmpty(set.Models);
        Assert.NotEmpty(set.Models.SelectMany(m => m.MaterialRanges));
        Assert.All(set.Models.SelectMany(m => m.MaterialRanges), r => Assert.Null(r.DiffuseTexturePath));
    }

    /// <summary>DiffuseTexture1 outranks any other DiffuseTexture slot; a material naming none has
    /// no layer 1.</summary>
    [Fact]
    public void The_diffuse_slot_prefers_layer_one()
    {
        Assert.Equal(@"a\d.xbt", WorldModels.DiffuseTextureOf(
            Material(textures: [("DiffuseTexture2", @"a\d2.xbt"), ("DiffuseTexture1", @"a\d.xbt")])));
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
    public void Real_layered_materials_resolve_through_the_parser()
    {
        if (Fixture.Read(Swaps) is not { } swaps) return;

        MaterialSurface surface = WorldModels.SurfaceOf(XbmMaterial.Parse(swaps));

        Assert.EndsWith("grey.xbt", surface.DiffuseTexturePath, StringComparison.OrdinalIgnoreCase);
        Assert.EndsWith("clay02_d.xbt", surface.SecondDiffusePath, StringComparison.OrdinalIgnoreCase);
        Assert.NotNull(surface.MaskPath);

        // The other fixture points both layers at the same swatch, which is a real and legitimate
        // shape: the mask still decides the tint even when there is nothing to blend to.
        if (Fixture.Read(Flat) is not { } flatBytes) return;

        MaterialSurface flat = WorldModels.SurfaceOf(XbmMaterial.Parse(flatBytes));
        Assert.EndsWith("grey.xbt", flat.DiffuseTexturePath, StringComparison.OrdinalIgnoreCase);
        Assert.EndsWith("grey.xbt", flat.SecondDiffusePath, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>End-to-end over real materials, because the flags only reach
    /// <see cref="WorldModels.AlphaOf"/> if the .xbm parser surfaces them as plain integers. The
    /// opaque case is the one that matters most: most of the retail set is opaque, and reading its
    /// alpha as coverage erases the surface.</summary>
    [Theory]
    [InlineData(OpaqueMaterial, MaterialAlpha.Opaque)]
    [InlineData(BlendedMaterial, MaterialAlpha.Blend)]
    [InlineData(MaskedMaterial, MaterialAlpha.Mask)]
    public void Real_materials_classify_through_the_parser(string fixture, MaterialAlpha expected)
    {
        if (Fixture.Read(fixture) is not { } bytes) return;

        Assert.Equal(expected, WorldModels.AlphaOf(XbmMaterial.Parse(bytes)));
    }

    private static byte[]? ReadLibrary(string path)
        => path.Equals(LibraryPath, StringComparison.OrdinalIgnoreCase) ? Fixture.Read(LibraryFixture) : null;

    /// <summary>
    /// Every retail vehicle fills one graphics slot per wheel, panel and light - 18 on the buggy, 45
    /// on the big truck - and none of them names the whole mesh. Those slots are one object, so they
    /// merge into one reference carrying every part.
    /// </summary>
    [Fact]
    public void One_slot_per_part_merges_into_one_reference()
    {
        string[] pieces = ["CHASSIS", "WHEELBACK_L", "WHEELBACK_R", "BUMPER_STATE01"];

        IReadOnlyList<MeshRef> refs = WorldModels.MeshRefs(VehicleNode("buggy.xbg", pieces));

        MeshRef only = Assert.Single(refs);
        Assert.Equal("buggy.xbg", only.Path);
        Assert.Equal(new HashSet<string>(pieces, StringComparer.OrdinalIgnoreCase), only.PartSet());
    }

    /// <summary>A slot naming no part draws everything, so a part list beside it adds nothing.</summary>
    [Fact]
    public void A_whole_mesh_slot_subsumes_the_part_lists_beside_it()
    {
        var graphics = new FcbObject { TypeHash = WorldHashes.CGraphicComponent };
        graphics.Values[WorldHashes.TextObjModel] = System.Text.Encoding.UTF8.GetBytes("rover.xbg");
        AddSlot(graphics, "rover.xbg", "CHASSIS");

        MeshRef only = Assert.Single(WorldModels.MeshRefs(Wrap(graphics)));

        Assert.Equal("", only.Parts);
        Assert.Null(only.PartSet());
    }

    /// <summary>
    /// A vehicle's slots all belong to one entity, so however many it has they bake as one model
    /// that keeps every one of them - an earlier per-slot count left a Land Rover drawing nothing
    /// but its grille.
    /// </summary>
    [Fact]
    public void A_vehicles_many_pieces_bake_as_one_model()
    {
        if (Fixture.Read(Buggy) is not { } buggy) return;

        // The buggy's own 18 pieces, every one a separate slot.
        string[] pieces = PartsOf(buggy);

        WorldModelSet set = LoadVehicles(Buggy, buggy, pieces, copies: 3);

        Assert.Single(set.Models);
        Assert.Equal(3, set.ModelIndicesByEntity.Count);

        // The whole vehicle, not one piece of it: the merged bake matches asking for all the parts.
        WorldModel expected = WorldModels.Bake(
            Buggy, XbgModel.Parse(buggy), WorldModels.FineTriangleBudget,
            onlyParts: new HashSet<string>(pieces, StringComparer.OrdinalIgnoreCase))!;
        Assert.Equal(expected.Indices.Length, set.Models[0].Indices.Length);
        Assert.True(expected.Indices.Length > 0);
    }

    /// <summary>
    /// The wheels have to actually be there. Merging the slots is what lets the state filter compare
    /// a part's variants, so the bumper keeps one of its two and the wheels keep all four.
    /// </summary>
    [Fact]
    public void A_merged_vehicle_keeps_every_wheel_and_one_bumper()
    {
        if (Fixture.Read(Buggy) is not { } buggy) return;

        string[] pieces = PartsOf(buggy);
        XbgModel mesh = XbgModel.Parse(buggy);
        WorldModel model = WorldModels.Bake(
            Buggy, mesh, WorldModels.FineTriangleBudget,
            onlyParts: new HashSet<string>(pieces, StringComparer.OrdinalIgnoreCase))!;

        int Triangles(string part) => WorldModels.Bake(
            Buggy, mesh, WorldModels.FineTriangleBudget,
            onlyParts: new HashSet<string> { part })?.Fine.Count / 3 ?? 0;

        int wheels = Triangles("WHEELBACK_L_STATE01") + Triangles("WHEELBACK_R_STATE01")
            + Triangles("WHEELFONT_L_STATE01") + Triangles("WHEELFONT_R_STATE01");
        Assert.True(wheels > 0, "the buggy fixture should have four wheels to find");

        // One bumper of the two, and the chassis and wheels beside it - not a lone grille.
        Assert.True(model.Fine.Count / 3 > wheels + Triangles("BUMPER_STATE01"),
            "the merged buggy should be more than its bumper and wheels");
        Assert.True(
            model.Fine.Count / 3 < Triangles("BUMPER_STATE01") + Triangles("BUMPER_STATE02")
                + Triangles("CHASSIS") + wheels + Triangles("WATERTANK_STATE01")
                + Triangles("WATERTANK_STATE02"),
            "both bumper states should not draw at once");
    }

    private static string[] PartsOf(byte[] mesh)
        => [.. XbgModel.Parse(mesh).Submeshes
            .Select(s => s.PartName).Where(p => p.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase)
            .Order(StringComparer.Ordinal)];

    private static FcbObject VehicleNode(string mesh, IEnumerable<string> pieces)
    {
        var graphics = new FcbObject { TypeHash = WorldHashes.CGraphicComponent };
        foreach (string piece in pieces)
        {
            AddSlot(graphics, mesh, piece);
        }

        return Wrap(graphics);
    }

    private static void AddSlot(FcbObject graphics, string mesh, string piece)
    {
        var slot = new FcbObject { TypeHash = WorldHashes.GraphicObject };
        slot.Values[WorldHashes.TextObjModel] = System.Text.Encoding.UTF8.GetBytes(mesh);
        slot.Values[WorldHashes.HidMeshName] = System.Text.Encoding.UTF8.GetBytes(piece);
        graphics.Children.Add(slot);
    }

    private static FcbObject Wrap(FcbObject graphics)
    {
        var components = new FcbObject { TypeHash = WorldHashes.Components };
        components.Children.Add(graphics);
        var node = new FcbObject { TypeHash = WorldHashes.Entity };
        node.Children.Add(components);
        return node;
    }

    private static WorldModelSet LoadVehicles(string fixture, byte[] mesh, IReadOnlyList<string> pieces, int copies)
    {
        var doc = new WorldSectorDocument
        {
            SourcePath = fixture,
            SectorId = 0,
            PristineRoot = new FcbObject { TypeHash = WorldHashes.Entity },
        };

        List<WorldEntity> entities = [.. Enumerable.Range(0, copies).Select(_ => new WorldEntity
        {
            Node = VehicleNode(fixture, pieces),
            HomeSector = doc,
            LayerPathId = "main",
            Position = Vector3.Zero,
        })];

        return WorldModels.Load(entities, EmptyIndex(),
            path => path.EndsWith(".xbm", StringComparison.OrdinalIgnoreCase) ? null : mesh);
    }

    /// <summary>Outfits over a mesh that is not a kit each keep their own geometry.</summary>
    [Fact]
    public void Outfits_over_a_non_kit_mesh_each_keep_their_own_geometry()
    {
        if (Fixture.Read(Boat) is not { } boat) return;

        string[] parts = PartsOf(boat);
        WorldModelSet set = LoadOutfits(boat, [parts[0], parts[1], $"{parts[0]};{parts[1]}"]);

        Assert.Equal(3, set.Models.Count);
    }

    private static WorldModelSet LoadOutfits(byte[] mesh, IReadOnlyList<string> outfits)
    {
        var doc = new WorldSectorDocument
        {
            SourcePath = Boat,
            SectorId = 0,
            PristineRoot = new FcbObject { TypeHash = WorldHashes.Entity },
        };

        List<WorldEntity> entities = [.. outfits.Select(parts =>
        {
            var graphics = new FcbObject { TypeHash = WorldHashes.CGraphicComponent };
            graphics.Values[WorldHashes.TextObjModel] = System.Text.Encoding.UTF8.GetBytes(Boat);
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

    internal static List<WorldEntity> BuildEntities(byte[] sector, int sectorId = 56)
    {
        var doc = new WorldSectorDocument
        {
            SourcePath = $"worldsector{sectorId}.data.fcb",
            SectorId = sectorId,
            PristineRoot = FcbDocument.Deserialize(sector),
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
                Name = FcbEntityFields.ReadString(node, WorldHashes.HidName),
                ArchetypeName = FcbEntityFields.ReadString(node, WorldHashes.TplCreatureType),
                Position = FcbEntityFields.ReadVector3(node, WorldHashes.HidPos)
                    ?? FcbEntityFields.ReadVector3(node, WorldHashes.HidPosPrecise),
            })];
    }
}

/// <summary>
/// The specular half of a material: real data on nearly every retail surface (2,129 of 2,208 name a
/// non-zero SpecularPower), so what these pin is the reading, not whether to bother.
/// </summary>
public class WorldModelsSpecularTests
{
    private static XbmMaterial Material((string Key, string Value)[]? properties = null) => new()
    {
        Name = "m",
        Template = "t",
        Textures = [],
        Properties = [.. (properties ?? []).Select(p => new XbmProperty { Key = p.Key, Value = p.Value })],
    };

    [Fact]
    public void The_specular_pair_reads_like_the_diffuse_pair()
    {
        Assert.Equal(
            (new Vector3(0.098f, 0.055f, 0f), new Vector3(1.569f, 1.012f, 0.526f)),
            WorldModels.SpecularsOf(Material(
                [("SpecularColorBase", "0.098, 0.055, 0"), ("SpecularColor1", "1.569, 1.012, 0.526")])));

        // Naming only one of the two uses it for both, same as the diffuse rule.
        Assert.Equal(
            (new Vector3(0.5f, 0.5f, 0.5f), new Vector3(0.5f, 0.5f, 0.5f)),
            WorldModels.SpecularsOf(Material([("SpecularColorBase", "0.5, 0.5, 0.5")])));
    }

    /// <summary>The fallback that must differ from the tints: an absent diffuse tint means "leave
    /// the texture alone" (white), an absent specular colour means "no highlight" (black). Inverted,
    /// every material in the game grows a highlight it never asked for.</summary>
    [Fact]
    public void A_material_naming_no_specular_gets_black_not_white()
        => Assert.Equal((Vector3.Zero, Vector3.Zero), WorldModels.SpecularsOf(Material()));

    [Fact]
    public void Specular_reaches_the_surface_with_its_power()
    {
        MaterialSurface surface = WorldModels.SurfaceOf(Material(
            [("SpecularColor1", "2, 1.255, 0.196"), ("SpecularPower", "8")]));

        Assert.Equal(8f, surface.SpecularPower);
        Assert.Equal(new Vector3(2f, 1.255f, 0.196f), surface.SpecularColour);
        // Kept as authored past 1 - the renderer tames it, the parse does not.
        Assert.True(surface.SpecularColour.X > 1f);
    }

    [Fact]
    public void An_absurd_power_clamps_and_a_broken_one_reads_zero()
    {
        Assert.Equal(128f, WorldModels.SurfaceOf(Material([("SpecularPower", "9999")])).SpecularPower);
        Assert.Equal(0f, WorldModels.SurfaceOf(Material([("SpecularPower", "-3")])).SpecularPower);
        Assert.Equal(0f, WorldModels.SurfaceOf(Material([("SpecularPower", "shiny")])).SpecularPower);
    }

    /// <summary>The untextured coarse tier draws with None, and its guarantee of never catching a
    /// highlight is exactly these three zeros.</summary>
    [Fact]
    public void The_unresolved_material_carries_no_specular()
    {
        Assert.Equal(Vector3.Zero, MaterialSurface.None.SpecularBase);
        Assert.Equal(Vector3.Zero, MaterialSurface.None.SpecularColour);
        Assert.Equal(0f, MaterialSurface.None.SpecularPower);
    }

    /// <summary>Two surfaces differing only in specular must compare unequal, because the renderer's
    /// change-filtered uniform upload relies on record equality to notice the difference - a member
    /// this misses is a member that silently never reaches the GPU.</summary>
    [Fact]
    public void Specular_participates_in_surface_equality()
    {
        MaterialSurface a = MaterialSurface.None;
        MaterialSurface b = MaterialSurface.None with { SpecularPower = 8f };
        MaterialSurface c = MaterialSurface.None with { SpecularColour = Vector3.One };

        Assert.NotEqual(a, b);
        Assert.NotEqual(a, c);
    }
}
