using System.Numerics;
using JackAll.Core.Format.Fcb;
using JackAll.Tools.World;
using JackAll.Tools.Xbm;

namespace JackAll.Tests;

/// <summary>
/// A character's kit picks, read the way <c>CGraphicKitComponent</c> applies them, over the retail
/// world1 library and a sector holding both kits: two mercenaries and two female civilians.
/// </summary>
public class GraphicKitTests
{
    private const string Sector = "WorldSector/worldsector3944.data.fcb";
    private const string MercKit = "Xbg/merc_kit.xbg";

    // The materials of a caucasian mercenary's head, both Skin.
    private const string MercHead = "Xbm/ycloutier-m-2008021236491365.xbm";
    private const string MercBody = "Xbm/ycloutier-m-2008021236495412.xbm";

    // A Cloth material, the jeans the female civilians wear.
    private const string Jeans = "Xbm/vgault-m-2007111038465234.xbm";

    // A Skin material, the female civilians' body.
    private const string FemaleSkin = "Xbm/vgault-m-2008072076742427.xbm";

    // Colour 4 of the merc kit's caucasian library, 7627899 read red-first.
    private static readonly Vector3 CaucasianFour = new Vector3(0x7B, 0x64, 0x74) / 255f;

    [Fact]
    [Trait("Category", "RequiresFixture")]
    public void The_fixtures_were_actually_found()
        => Fixture.AssertPresent(
            FcbDocumentTests.World1, Sector, MercKit, XbgFixtures.FemaleCivilianKit, MercHead, MercBody,
            Jeans, FemaleSkin);

    /// <summary>A caucasian mercenary's head takes colour 4 of the kit's <c>caucasian</c> library.</summary>
    [Fact]
    public void A_mercenarys_colour_pick_resolves_through_the_kits_library()
    {
        if (Load() is not var (entities, archetypes)) return;

        WorldEntity merc = entities.Single(e => e.Name == "Blue_Faction.Assault_Caucasian_77");
        FcbObject archetype = archetypes.Winner(merc.ArchetypeName)!.Node;

        Dictionary<string, PartLook> looks = GraphicKit.Of(archetype)!.LooksOf(merc.Node, archetype);

        Assert.Equal((CaucasianFour, CaucasianFour), looks["P_MC_CAUCASIANHEAD01"].Colours);
    }

    /// <summary>The female kit takes no colours on the parts this civilian wears, and a pick naming
    /// no part - she carries one, with a texture index - reaches nothing.</summary>
    [Fact]
    public void Picks_on_parts_that_take_no_overwrite_change_nothing()
    {
        if (Load() is not var (entities, archetypes)) return;

        WorldEntity woman = entities.Single(e => e.Name == "1_Female_Civilian_NoDress_9");
        FcbObject archetype = archetypes.Winner(woman.ArchetypeName)!.Node;

        Assert.Empty(GraphicKit.Of(archetype)!.LooksOf(woman.Node, archetype));
    }

    /// <summary>
    /// Every mercenary bakes part by part, so the kit's geometry exists once however many outfits
    /// the world holds, and a head worn in a picked skin tone restyles that bake instead of copying it.
    /// </summary>
    [Fact]
    public void Kit_characters_bake_per_part_and_share_geometry_across_looks()
    {
        if (Load() is not var (entities, archetypes) || Fixture.Read(MercKit) is not { } kit) return;

        List<WorldEntity> mercs = [.. entities.Where(e => e.Name.StartsWith("Blue_Faction.", StringComparison.Ordinal))];
        Func<string, byte[]?> materials = Fixture.ByFileName("Xbm");
        WorldModelSet set = WorldModels.Load(mercs, archetypes,
            path => path.EndsWith("merc_kit.xbg", StringComparison.OrdinalIgnoreCase) ? kit : materials(path));

        WorldEntity merc = mercs.Single(e => e.Name == "Blue_Faction.Assault_Caucasian_77");
        int[] parts = set.ModelIndicesByEntity[merc];
        Assert.Equal(WorldModels.MeshRefs(merc.Node).Single().PartSet()!.Count, parts.Length);
        Assert.Contains(parts, i => set.Models[i].MaterialRanges.Any(r =>
            r.Surface.Template == MaterialTemplate.Skin && r.Surface.Tint == CaucasianFour));

        // Looks add models but never geometry: one array per part worn, however many looks.
        int partsWorn = mercs.SelectMany(e => WorldModels.MeshRefs(e.Node).Single().PartSet()!).Distinct().Count();
        Assert.Equal(partsWorn, set.Models.Select(m => m.Vertices).Distinct(ReferenceEqualityComparer.Instance).Count());
    }

    /// <summary>Cloth takes its base pair, print and dirt from their own slots, and reads the
    /// print and dirt off the UV set their groups map to.</summary>
    [Fact]
    public void A_cloth_material_reads_its_own_slots()
    {
        if (Fixture.Read(Jeans) is not { } bytes) return;

        MaterialSurface surface = WorldModels.SurfaceOf(XbmMaterial.Parse(bytes));

        Assert.Equal(MaterialTemplate.Cloth, surface.Template);
        Assert.Equal(@"graphics\actors\_textures\c_cm_cloth_05_s.xbt", surface.DiffuseTexturePath);
        Assert.Equal(@"graphics\actors\_textures\c_cm_gene_gray.xbt", surface.SecondDiffusePath);
        Assert.Equal(@"graphics\actors\_textures\c_cm_dirt_02_d.xbt", surface.BloodPath);
        Assert.Equal(new Vector3(0.306f, 0.369f, 0.471f), surface.TintBase);
        Assert.Equal(new Vector3(1.255f, 1.427f, 1.49f), surface.Tint);
        Assert.Equal(new Vector2(12f, 15f), surface.DiffuseTiling);
        Assert.Equal(new Vector4(0f, 1f, 0f, 0f), surface.UvSets);
    }

    [Fact]
    public void A_skin_material_reads_its_own_slots()
    {
        if (Fixture.Read(FemaleSkin) is not { } bytes) return;

        MaterialSurface surface = WorldModels.SurfaceOf(XbmMaterial.Parse(bytes));

        Assert.Equal(MaterialTemplate.Skin, surface.Template);
        Assert.Equal(@"graphics\actors\_textures\c_cm_body_nubi_female_d.xbt", surface.DiffuseTexturePath);
        Assert.Null(surface.SecondDiffusePath);
        Assert.Equal(new Vector3(0.761f, 0.761f, 0.761f), surface.TintBase);
        Assert.Equal(new Vector3(0.463f, 0.486f, 0.478f), surface.Tint);
    }

    /// <summary>A pick lands where the engine routes it per template; on Generic only the texture
    /// shows, because the colour pair lands on BaseColor1/2, which its shader never reads.</summary>
    [Fact]
    public void A_look_lands_on_the_parameters_its_template_routes_it_to()
    {
        var red = new Vector3(1, 0, 0);
        var blue = new Vector3(0, 0, 1);
        var look = new PartLook((red, blue), (@"a\print.xbt", 20f));

        MaterialSurface cloth = look.Apply(MaterialSurface.None with { Template = MaterialTemplate.Cloth });
        Assert.Equal((red, blue), (cloth.TintBase, cloth.Tint));
        Assert.Equal((@"a\print.xbt", new Vector2(20f)), (cloth.SecondDiffusePath, cloth.SecondDiffuseTiling));

        MaterialSurface skin = look.Apply(MaterialSurface.None with { Template = MaterialTemplate.Skin });
        Assert.Equal((red, blue), (skin.TintBase, skin.Tint));
        Assert.Equal((@"a\print.xbt", new Vector2(20f)), (skin.BloodPath, skin.BloodTiling));

        MaterialSurface hair = look.Apply(MaterialSurface.None with { Template = MaterialTemplate.Hair });
        Assert.Equal((red, red, blue), (hair.TintBase, hair.Tint, hair.SpecularColour));
        Assert.Equal(@"a\print.xbt", hair.DiffuseTexturePath);

        MaterialSurface generic = look.Apply(MaterialSurface.None);
        Assert.Equal((Vector3.One, Vector3.One), (generic.TintBase, generic.Tint));
        Assert.Equal(@"a\print.xbt", generic.DiffuseTexturePath);
    }

    private static (List<WorldEntity> Entities, ArchetypeIndex Archetypes)? Load()
        => Fixture.Read(Sector) is { } sector && ArchetypeChainTests.SinglePlayer.Value is { } archetypes
            ? (WorldModelsTests.BuildEntities(sector, 3944), archetypes)
            : null;
}
