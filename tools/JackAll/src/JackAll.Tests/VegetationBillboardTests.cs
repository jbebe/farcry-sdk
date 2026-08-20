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
}
