using System.Numerics;
using JackAll.Tools.Hkx;

namespace JackAll.Tests;

/// <summary>A body's saved bounds, object radius and MOPP code follow an edit to its shapes, and nothing else moves.</summary>
public sealed class HkxCachesTests
{
    // One body: a MOPP over a list of six boxes; the seat, child 4, is the topmost.
    private const string Chair = "Hkx/chairbar01.hkx";

    // Part modifiers that point into its MOPP code.
    private const string Truck = "Hkx/bigtruck.hkx";

    // A MOPP over an extended mesh.
    private const string Garage = "Hkx/colonialgarage02_ruin.hkx";

    [Fact]
    [Trait("Category", "RequiresFixture")]
    public void The_fixtures_were_actually_found() => Fixture.AssertPresent(Chair, Truck, Garage);

    [Theory]
    [InlineData(Chair)]
    [InlineData(Truck)]
    [InlineData(Garage)]
    public void An_unedited_file_keeps_every_cache_byte_for_byte(string fixture)
    {
        if (Fixture.Read(fixture) is not { } bytes)
        {
            return;
        }

        HkxFile file = HkxFile.Parse(bytes);

        Assert.Empty(HkxCaches.Refresh(file, HkxFile.Parse(bytes)));
        Assert.Equal(bytes, file.Write());
    }

    [Fact]
    public void Raising_the_seat_raises_the_list_bounds_and_the_object_radius_and_rebuilds_the_mopp()
    {
        if (Fixture.Read(Chair) is not { } bytes)
        {
            return;
        }

        HkxFile file = HkxFile.Parse(bytes);
        HkxObject body = file.Objects().Single(o => o.Class.Name == "hkpRigidBody");
        HkxObject mopp = body.GetPointer("collidable.shape")!, list = mopp.GetPointer("child.childShape")!;
        HkxObject seat = list.GetPointer("childInfo[4].shape")!;
        float[] at = seat.GetReals("translation"), half = list.GetReals("aabbHalfExtents"), centre = list.GetReals("aabbCenter");
        float radius = body.GetReals("motion.motionState.objectRadius")[0];
        seat.SetReals("translation", at[0], at[1], at[2] + 2, 0);

        List<string> written = HkxCaches.Refresh(file, HkxFile.Parse(bytes));

        float[] newHalf = list.GetReals("aabbHalfExtents"), newCentre = list.GetReals("aabbCenter");
        Assert.Contains("ChairBar01: collidable.shape (MOPP code)", written);
        Assert.Equal(centre[2] + half[2] + 2, newCentre[2] + newHalf[2], 4);
        Assert.Equal(centre[2] - half[2], newCentre[2] - newHalf[2], 4);
        Assert.Equal(half[0], newHalf[0]);
        Assert.Equal(half[3], newHalf[3]);
        Assert.Equal(centre[3], newCentre[3]);
        Assert.True(body.GetReals("motion.motionState.objectRadius")[0] > radius + 0.5f);
        (Vector4 info, byte[] code) = HkxMopp.Code(mopp);
        Vector3 raised = new(at[0], at[1], at[2] + 2);
        Assert.Equal([4u], HkxMopp.Query(info, code, raised - new Vector3(0.01f), raised + new Vector3(0.01f)));
    }

    [Fact]
    public void A_mopp_that_part_modifiers_point_into_is_not_rebuilt()
    {
        if (Fixture.Read(Truck) is not { } bytes)
        {
            return;
        }

        HkxFile file = HkxFile.Parse(bytes);
        HkxObject mesh = file.Objects().First(o => o.Class.Name == "hkpStorageExtendedMeshShape");
        mesh.SetReals("scaling", 1.5f, 1.5f, 1.5f, 0);

        Assert.Throws<InvalidOperationException>(() => HkxCaches.Refresh(file, HkxFile.Parse(bytes)));
    }
}
