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
    // The impostor's card mesh.
    private const string Mesh = "Billboard/facingbush.xbg";

    // The impostor's material, which declares it a billboard.
    private const string Material = "Billboard/facingbush.xbm";

    [Fact]
    [Trait("Category", "RequiresFixture")]
    public void The_fixtures_were_actually_found() => Fixture.AssertPresent(Mesh, Material);

    [Fact]
    public void The_impostors_material_declares_itself_a_billboard()
    {
        if (Fixture.Read(Material) is not { } xbm) return;

        Assert.True(WorldModels.SurfaceOf(XbmMaterial.Parse(xbm)).Billboard);
    }

    /// <summary>Ordinary geometry has to come back false, or every mesh would spin to the camera.</summary>
    [Fact]
    public void An_ordinary_material_does_not()
    {
        if (Fixture.Read(XbmFixtures.Masked) is not { } xbm) return;

        Assert.False(WorldModels.SurfaceOf(XbmMaterial.Parse(xbm)).Billboard);
    }

    /// <summary>
    /// The card is authored looking down -Y. Nothing in the format says so, which is why the bake
    /// measures it - and why this pins the measurement against a real file.
    /// </summary>
    [Fact]
    public void The_impostors_card_is_measured_looking_along_negative_y()
    {
        if (Fixture.Read(Mesh) is not { } xbg || Fixture.Read(Material) is not { } xbm) return;

        MaterialSurface surface = WorldModels.SurfaceOf(XbmMaterial.Parse(xbm));
        WorldModel model = WorldModels.Bake(
            "facingbush.xbg", XbgModel.Parse(xbg), WorldModels.FineTriangleBudget, _ => surface)!;

        Assert.NotNull(model.BillboardFacing);
        Vector2 facing = model.BillboardFacing.Value;
        Assert.True(facing.Y < -0.95f, $"facing was {facing}, not the -Y the card is built on");
        Assert.True(Math.Abs(facing.X) < 0.1f, $"facing was {facing}, which is not square to the card");
    }

    [Fact]
    public void A_mesh_no_material_marks_has_no_facing()
    {
        if (Fixture.Read(Mesh) is not { } xbg) return;

        WorldModel model = WorldModels.Bake(
            "facingbush.xbg", XbgModel.Parse(xbg), WorldModels.FineTriangleBudget, _ => MaterialSurface.None)!;

        Assert.Null(model.BillboardFacing);
    }
}
