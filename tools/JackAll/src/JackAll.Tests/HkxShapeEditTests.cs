using System.Numerics;
using JackAll.Tools.Hkx;

namespace JackAll.Tests;

/// <summary>A convex shape moves, turns and grows where the drawn collision says it does, and survives a round trip.</summary>
public sealed class HkxShapeEditTests
{
    // One body at the origin: a MOPP over a list of six boxes, the seat (child 4) under a translate shape.
    private const string Chair = "Hkx/chairbar01.hkx";

    // A MOPP over an extended mesh with a subpart of 36 boxes.
    private const string Garage = "Hkx/colonialgarage02_ruin.hkx";

    [Fact]
    [Trait("Category", "RequiresFixture")]
    public void The_fixtures_were_actually_found() => Fixture.AssertPresent(Chair, Garage);

    [Fact]
    public void Each_shape_node_links_to_the_member_that_places_it()
    {
        if (Read(Chair) is not { } file)
        {
            return;
        }

        HkxNode seat = SeatBox(file);

        Assert.Equal("childInfo[4].shape", seat.Link!.Value.Path);
        Assert.Equal("hkpConvexTranslateShape", seat.Link.Value.Target!.Class.Name);
        Assert.Same(seat.Object, HkxShapeEdit.Leaf(seat.Link.Value));
    }

    [Fact]
    public void Moving_a_translated_box_moves_its_drawn_corners_and_survives_a_round_trip()
    {
        if (Read(Chair) is not { } file)
        {
            return;
        }

        HkxLink link = SeatBox(file).Link!.Value;
        Matrix4x4 local = HkxShapeEdit.Local(link.Target!) * Matrix4x4.CreateTranslation(0.5f, 0, 0);
        float before = SeatBox(file).Primitive!.ModelVertices().Min(v => v.X);

        HkxShapeEdit.Place(link, local);
        HkxFile reread = HkxFile.Parse(file.Write());

        Assert.Equal("hkpConvexTranslateShape", SeatBox(reread).Link!.Value.Target!.Class.Name);
        Assert.Equal(before + 0.5f, SeatBox(reread).Primitive!.ModelVertices().Min(v => v.X), 5);
    }

    [Fact]
    public void Turning_a_translated_box_makes_it_a_transform_shape_that_keeps_its_box()
    {
        if (Read(Chair) is not { } file)
        {
            return;
        }

        HkxNode seat = SeatBox(file);
        HkxLink link = seat.Link!.Value;
        int userData = link.Target!.GetInt("userData");
        Matrix4x4 local = Matrix4x4.CreateRotationZ(MathF.PI / 4) * HkxShapeEdit.Local(link.Target);

        HkxShapeEdit.Place(link, local);
        HkxNode turned = SeatBox(HkxFile.Parse(file.Write()));

        HkxObject wrapper = turned.Link!.Value.Target!;
        Assert.Equal("hkpConvexTransformShape", wrapper.Class.Name);
        Assert.Equal(userData, wrapper.GetInt("userData"));
        Assert.Equal(seat.Object!.GetReals("halfExtents"), turned.Object!.GetReals("halfExtents"));
        Assert.True(Distance(local, turned.Primitive!.ToModel) < 1e-5f);
    }

    [Fact]
    public void Moving_an_unwrapped_box_wraps_it_in_a_translate_shape()
    {
        if (Read(Chair) is not { } file)
        {
            return;
        }

        HkxLink link = SeatBox(file).Link!.Value;
        HkxObject box = HkxShapeEdit.Leaf(link);
        link.Owner.SetPointer(link.Path, box);

        HkxShapeEdit.Place(link, Matrix4x4.CreateTranslation(0, 0, 1));
        HkxNode moved = SeatBox(HkxFile.Parse(file.Write()));

        Assert.Equal("hkpConvexTranslateShape", moved.Link!.Value.Target!.Class.Name);
        Assert.Equal(new Vector3(0, 0, 1), moved.Primitive!.ToModel.Translation);
    }

    [Fact]
    public void Stretching_the_seat_top_up_keeps_its_bottom_in_place()
    {
        if (Read(Chair) is not { } file)
        {
            return;
        }

        HkxNode seat = SeatBox(file);
        HkxLink link = seat.Link!.Value;
        Vector3[] before = [.. seat.Primitive!.ModelVertices()];
        Matrix4x4 toModel = seat.Primitive.ToModel;
        Vector3 size = HkxShapeEdit.Size(HkxShapeEdit.Leaf(link));
        const int top = 4;

        Assert.Equal(before.Max(v => v.Z), HkxShapeEdit.Faces(HkxShapeEdit.Leaf(link), toModel)[top].Point.Z, 5);

        HkxShapeEdit.Stretch(link, HkxShapeEdit.ParentToModel(link, toModel), toModel, size, top, size.Z + 0.1f);
        Vector3[] after = [.. SeatBox(file).Primitive!.ModelVertices()];

        Assert.Equal(before.Min(v => v.Z), after.Min(v => v.Z), 5);
        Assert.Equal(before.Max(v => v.Z) + 0.1f, after.Max(v => v.Z), 5);
    }

    /// <summary>With the body tilted, a placement that ignored the parent would land the seat elsewhere.</summary>
    [Fact]
    public void A_shape_placed_in_the_model_lands_there_under_a_tilted_body()
    {
        if (Read(Chair) is not { } file)
        {
            return;
        }

        Matrix4x4 tilt = Matrix4x4.CreateRotationX(0.6f) * Matrix4x4.CreateTranslation(1, 2, 3);
        file.Objects().Single(o => o.Class.Name == "hkpRigidBody").SetReals("motion.motionState.transform", HkxShapes.TransformReals(tilt));
        HkxNode seat = SeatBox(file);
        Matrix4x4 now = seat.Primitive!.ToModel;
        Matrix4x4 parent = HkxShapeEdit.ParentToModel(seat.Link!.Value, now);
        Matrix4x4 wanted = now * Matrix4x4.CreateTranslation(-now.Translation) * Matrix4x4.CreateRotationZ(0.5f)
            * Matrix4x4.CreateTranslation(now.Translation + Vector3.UnitZ);

        HkxShapeEdit.PlaceInModel(seat.Link.Value, parent, wanted);

        Assert.True(Distance(tilt, parent) < 1e-5f);
        Assert.True(Distance(wanted, SeatBox(file).Primitive!.ToModel) < 1e-4f);
    }

    [Theory]
    [InlineData(Chair, "childInfo[4].shape", "childInfo[6].shape")]
    [InlineData(Garage, "shapesSubparts[0].childShapes[3]", "shapesSubparts[0].childShapes[36]")]
    public void A_box_added_beside_a_shape_lands_where_asked_and_saves_clean(string fixture, string like, string path)
    {
        if (Read(fixture) is not { } file || Fixture.Read(fixture) is not { } bytes)
        {
            return;
        }

        HkxNode sibling = Node(file, like);
        Matrix4x4 parent = HkxShapeEdit.ParentToModel(sibling.Link!.Value, sibling.Primitive!.ToModel);
        int before = HkxMopp.Items(Mopp(file)).Count;

        HkxObject box = HkxShapeEdit.AddBox(sibling.Link.Value, parent, new Vector3(0.2f, 0.3f, 0.4f), Matrix4x4.CreateTranslation(0, 0, 1.5f));
        HkxCaches.Refresh(file, HkxFile.Parse(bytes));
        HkxFile reread = HkxFile.Parse(file.Write());

        Assert.Equal(before + 1, HkxMopp.Items(Mopp(reread)).Count);
        Assert.True(Vector3.Distance(new Vector3(0, 0, 1.5f), Node(reread, path).Primitive!.ToModel.Translation) < 1e-4f);
        Assert.Equal(new Vector3(0.2f, 0.3f, 0.4f), HkxShapeEdit.Size(box));
        Assert.Empty(HkxLint.Check(reread));
    }

    [Theory]
    [InlineData(Chair, "childInfo[0].shape")]
    [InlineData(Garage, "shapesSubparts[0].childShapes[3]")]
    public void A_shape_taken_out_saves_clean(string fixture, string path)
    {
        if (Read(fixture) is not { } file || Fixture.Read(fixture) is not { } bytes)
        {
            return;
        }

        int before = HkxMopp.Items(Mopp(file)).Count;

        HkxShapeEdit.Remove(Node(file, path).Link!.Value);
        HkxCaches.Refresh(file, HkxFile.Parse(bytes));
        HkxFile reread = HkxFile.Parse(file.Write());

        Assert.Equal(before - 1, HkxMopp.Items(Mopp(reread)).Count);
        Assert.Empty(HkxLint.Check(reread));
    }

    [Fact]
    public void A_box_never_shrinks_below_twice_its_radius()
    {
        if (Read(Chair) is not { } file)
        {
            return;
        }

        HkxObject box = HkxShapeEdit.Leaf(SeatBox(file).Link!.Value);
        float radius = box.GetReals("radius")[0];

        HkxShapeEdit.Resize(box, Vector3.Zero);

        Assert.Equal(new Vector3(radius * 2), HkxShapeEdit.Size(box));
    }

    private static HkxNode SeatBox(HkxFile file) => Node(file, "childInfo[4].shape");

    // The drawn shape a member places.
    private static HkxNode Node(HkxFile file, string path)
        => HkxShapes.Read(file).SelectMany(n => n.Descendants()).Single(n => n.Primitive is not null && n.Link?.Path == path);

    private static HkxObject Mopp(HkxFile file) => file.Objects().First(o => o.Class.Name == "hkpMoppBvTreeShape");

    private static float Distance(Matrix4x4 a, Matrix4x4 b) => Enumerable.Range(0, 16).Max(i => MathF.Abs(a[i / 4, i % 4] - b[i / 4, i % 4]));

    private static HkxFile? Read(string fixture) => Fixture.Read(fixture) is { } bytes ? HkxFile.Parse(bytes) : null;
}
