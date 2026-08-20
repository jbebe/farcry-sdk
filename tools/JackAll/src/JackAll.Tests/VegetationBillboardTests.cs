using System.Numerics;
using JackAll.Tools.World;
using JackAll.Tools.Xbg;
using JackAll.Tools.Xbm;

namespace JackAll.Tests;

/// <summary>
/// The camera-facing vegetation cards: how a mesh is known to be one, and which way its card looks
/// before anything turns it. Against the retail impostor, because both answers come out of shipped
/// data rather than a naming convention.
/// </summary>
public class VegetationBillboardTests
{
    private const string Folder = @".\Fixtures\Billboard";

    private static byte[]? Fixture(string name)
        => File.Exists(Path.Combine(Folder, name)) ? File.ReadAllBytes(Path.Combine(Folder, name)) : null;

    [Fact]
    [Trait("Category", "RequiresFixture")]
    public void The_fixtures_were_actually_found()
    {
        if (!Directory.Exists(Folder)) return;

        Assert.NotNull(Fixture("facingbush.xbg"));
        Assert.NotNull(Fixture("facingbush.xbm"));
    }

    [Fact]
    [Trait("Category", "RequiresFixture")]
    public void The_impostors_material_declares_itself_a_billboard()
    {
        if (Fixture("facingbush.xbm") is not { } xbm) return;

        Assert.True(WorldModels.SurfaceOf(XbmMaterial.Parse(xbm)).Billboard);
    }

    /// <summary>Ordinary geometry has to come back false, or every mesh would spin to the camera.</summary>
    [Fact]
    [Trait("Category", "RequiresFixture")]
    public void An_ordinary_material_does_not()
    {
        string path = @".\Fixtures\XbmAlpha\masked.xbm";
        if (!File.Exists(path)) return;

        Assert.False(WorldModels.SurfaceOf(XbmMaterial.Parse(File.ReadAllBytes(path))).Billboard);
    }

    /// <summary>
    /// The card is authored looking down -Y. Nothing in the format says so, which is why the bake
    /// measures it - and why this pins the measurement against a real file.
    /// </summary>
    [Fact]
    [Trait("Category", "RequiresFixture")]
    public void The_impostors_card_is_measured_looking_along_negative_y()
    {
        if (Fixture("facingbush.xbg") is not { } xbg || Fixture("facingbush.xbm") is not { } xbm) return;

        MaterialSurface surface = WorldModels.SurfaceOf(XbmMaterial.Parse(xbm));
        WorldModel model = WorldModels.Bake(
            "facingbush.xbg", XbgModel.Parse(xbg), WorldModels.FineTriangleBudget, _ => surface)!;

        Assert.NotNull(model.BillboardFacing);
        Vector2 facing = model.BillboardFacing.Value;
        Assert.True(facing.Y < -0.95f, $"facing was {facing}, not the -Y the card is built on");
        Assert.True(Math.Abs(facing.X) < 0.1f, $"facing was {facing}, which is not square to the card");
    }

    [Fact]
    [Trait("Category", "RequiresFixture")]
    public void A_mesh_no_material_marks_has_no_facing()
    {
        if (Fixture("facingbush.xbg") is not { } xbg) return;

        WorldModel model = WorldModels.Bake(
            "facingbush.xbg", XbgModel.Parse(xbg), WorldModels.FineTriangleBudget, _ => MaterialSurface.None)!;

        Assert.Null(model.BillboardFacing);
    }

    [Fact]
    [Trait("Category", "RequiresFixture")]
    public void Scaling_an_impostor_sets_its_height_and_leaves_its_facing_alone()
    {
        if (Fixture("facingbush.xbg") is not { } xbg || Fixture("facingbush.xbm") is not { } xbm) return;

        MaterialSurface surface = WorldModels.SurfaceOf(XbmMaterial.Parse(xbm));
        WorldModel model = WorldModels.Bake(
            "facingbush.xbg", XbgModel.Parse(xbg), WorldModels.FineTriangleBudget, _ => surface)!;

        WorldModel scaled = model.ScaledToHeight(6f);

        Assert.Equal(6f, scaled.LocalMax.Z - scaled.LocalMin.Z, 3);
        Assert.Equal(model.BillboardFacing, scaled.BillboardFacing);
        Assert.Same(model.Indices, scaled.Indices);
    }
}

/// <summary>Which impostor a RealTree species borrows, and at what height.</summary>
public class VegetationStandInTests
{
    [Theory]
    [InlineData(@"graphics\vegetation\savannah\realtrees\rt_tree_acacia.rtx", "facing_bush_large_savannah.xbg", 12f)]
    [InlineData(@"graphics\vegetation\jungle\realtrees\rt_canopy_jungle.rtx", "facing_bush_large.xbg", 12f)]
    [InlineData(@"graphics\vegetation\savannah\realtrees\rt_tree_acacia_small.rtx", "facing_bush_large_savannah.xbg", 6f)]
    [InlineData(@"graphics\vegetation\jungle\realtrees\rt_bush_cofee_large.rtx", "facingbush.xbg", 4.2f)]
    [InlineData(@"graphics\vegetation\savannah\realtrees\rt_bush_tamarix_small.rtx", "facing_bush_savannah.xbg", 1.5f)]
    [InlineData(@"graphics\vegetation\desert\realtrees\rt_saguaro_cactus_line_01.rtx", "facing_bush_savannah.xbg", 4f)]
    public void A_species_borrows_a_card_at_the_size_its_name_implies(
        string resource, string mesh, float height)
    {
        StandIn? found = VegetationStandIn.For(resource);
        Assert.NotNull(found);
        StandIn standIn = found.Value;

        Assert.EndsWith(mesh, standIn.Mesh);
        Assert.Equal(height, standIn.Height, 3);
    }

    /// <summary>Everything the substitution can name has to be bakeable, or a species silently
    /// falls back to a marker.</summary>
    [Fact]
    public void Every_card_a_species_can_borrow_is_one_the_loader_bakes()
    {
        string[] species =
        [
            @"graphics\vegetation\savannah\realtrees\rt_tree_acacia.rtx",
            @"graphics\vegetation\jungle\realtrees\rt_canopy_jungle.rtx",
            @"graphics\vegetation\jungle\realtrees\hy_banana_big.rtx",
            @"graphics\vegetation\desert\realtrees\hy_aloes_01.rtx",
            @"graphics\vegetation\jungle\realtrees\rt_tree_palm.rtx",
        ];

        foreach (string one in species)
        {
            Assert.Contains(VegetationStandIn.For(one)!.Value.Mesh, VegetationStandIn.Meshes);
        }
    }

    [Fact]
    public void An_ordinary_mesh_stands_in_for_itself()
        => Assert.Null(VegetationStandIn.For(@"graphics\vegetation\savannah\1_grasssavannah_a.xbg"));
}
