using System.Numerics;
using JackAll.Tools.Hkx;

namespace JackAll.Tests;

/// <summary>
/// A shipped <c>.hkx</c>'s collision, flattened to triangles in model space, lands where the file's own cached
/// bounds and planes say it is.
/// </summary>
public sealed class HkxShapesTests
{
    // A prop: one body, a MOPP over a list of six boxes, each placed by a transform or translate shape.
    private const string Chair = "Hkx/chairbar01.hkx";

    // Convex hulls with connectivity, a tilted body, and three hinges.
    private const string Truck = "Hkx/bigtruck.hkx";

    // An extended mesh holding both triangle subparts and a subpart of boxes.
    private const string Garage = "Hkx/colonialgarage02_ruin.hkx";

    [Fact]
    [Trait("Category", "RequiresFixture")]
    public void The_fixtures_were_actually_found() => Fixture.AssertPresent(Chair, Truck, Garage);

    [Fact]
    public void The_chair_is_one_body_of_six_boxes_under_a_mopp()
    {
        if (Read(Chair) is not { } nodes)
        {
            return;
        }

        HkxNode body = Assert.Single(nodes.Single().Children);
        HkxNode mopp = Assert.Single(body.Children);
        HkxNode list = Assert.Single(mopp.Children);

        Assert.Equal("ChairBar01", body.Label);
        Assert.Equal("MOPP", mopp.Label);
        Assert.Equal("List (6)", list.Label);
        Assert.Equal(6, body.Primitives().Count(p => p.Kind == HkxShapeKind.Box));
    }

    /// <summary>Pins the translations, the convex radius and the axes against the list's own cached bounds.</summary>
    [Fact]
    public void The_chair_boxes_fill_the_bounds_its_list_caches()
    {
        if (Read(Chair) is not { } nodes)
        {
            return;
        }

        HkxNode list = nodes.Single().Children.Single().Children.Single().Children.Single();
        Vector3[] points = [.. list.Primitives().SelectMany(p => p.ModelVertices())];
        float[] centre = list.Object!.GetReals("aabbCenter"), half = list.Object.GetReals("aabbHalfExtents");

        for (int axis = 0; axis < 3; axis++)
        {
            Assert.Equal(centre[axis] - half[axis], points.Min(p => p[axis]), 4);
            Assert.Equal(centre[axis] + half[axis], points.Max(p => p[axis]), 4);
        }
    }

    /// <summary>A transposed matrix would rotate the tilted steering wheel the other way.</summary>
    [Fact]
    public void A_tilted_body_places_its_shape_by_its_own_rotation()
    {
        if (Read(Truck) is not { } nodes)
        {
            return;
        }

        HkxNode wheel = Body(nodes, "HIGH_SteeringWheel");
        float[] q = wheel.Object!.GetReals("motion.motionState.sweptTransform.rotation0");
        Matrix4x4 rotation = Matrix4x4.CreateFromQuaternion(new Quaternion(q[0], q[1], q[2], q[3]));
        Matrix4x4 placed = wheel.Primitives().First().ToModel;

        Assert.True(Math.Abs(q[3]) < 0.99, "the steering wheel should be tilted");
        Assert.Equal(rotation.M11, placed.M11, 4);
        Assert.Equal(rotation.M12, placed.M12, 4);
        Assert.Equal(rotation.M23, placed.M23, 4);
        Assert.Equal(rotation.M32, placed.M32, 4);
    }

    [Fact]
    public void A_hull_rebuilds_the_planes_the_file_stores()
    {
        if (Read(Truck) is not { } nodes)
        {
            return;
        }

        HkxPrimitive door = Body(nodes, "DoorLeftState02").Primitives().Single();
        float[] stored = door.Shape.GetRealArray("planeEquations");
        Vector4[] planes = HkxShapes.HullPlanes(door.Shape);

        Assert.Equal(HkxShapeKind.Hull, door.Kind);
        Assert.Equal(stored.Length / 4, planes.Length);
        Assert.All(planes, p => Assert.Contains(Enumerable.Range(0, stored.Length / 4),
            i => Vector4.Distance(p, new Vector4(stored[i * 4], stored[i * 4 + 1], stored[i * 4 + 2], stored[i * 4 + 3])) < 1e-3));
        Assert.Equal(-1.009022f, door.ToModel.M41, 4);
        Assert.Equal(1.667295f, door.ToModel.M42, 4);
        Assert.Equal(1.799415f, door.ToModel.M43, 4);
    }

    [Fact]
    public void Each_hinge_names_the_two_bodies_it_joins()
    {
        if (Read(Truck) is not { } nodes)
        {
            return;
        }

        HkxNode[] hinges = [.. Physics(nodes).Children.Where(n => n.Object?.Class.Name == "hkpConstraintInstance")];

        Assert.Equal(3, hinges.Length);
        Assert.Contains(hinges, h => h.Label.Contains("(DoorLeftState02 -> Chassis)") || h.Label.Contains("(Chassis -> DoorLeftState02)"));
    }

    [Fact]
    public void An_extended_mesh_draws_its_triangles_and_its_boxes()
    {
        if (Read(Garage) is not { } nodes)
        {
            return;
        }

        HkxPrimitive[] primitives = [.. nodes.SelectMany(n => n.Primitives())];
        HkxPrimitive first = primitives.First(p => p.Kind == HkxShapeKind.Mesh);

        Assert.Equal(20, primitives.Count(p => p.Kind == HkxShapeKind.Mesh));
        Assert.Equal(36, primitives.Count(p => p.Kind == HkxShapeKind.Box));
        Assert.Equal(8, first.Vertices.Length);
        Assert.Equal(36, first.Triangles.Length);
        Assert.All(primitives.Where(p => p.Kind == HkxShapeKind.Mesh), p => Assert.All(p.Triangles, i => Assert.InRange(i, 0, p.Vertices.Length - 1)));
    }

    [Theory]
    [InlineData("hkpCapsuleShape", 1f)]
    [InlineData("hkpSphereShape", 0f)]
    public void A_rounded_shape_stays_its_radius_from_its_segment(string className, float length)
    {
        HkxObject shape = HkxEdit.Create(className);
        shape.SetReals("radius", 0.25f);
        if (className == "hkpCapsuleShape")
        {
            shape.SetReals("vertexB", 0, 0, length, 0);
        }

        HkxPrimitive capsule = HkxShapes.Leaf(shape, Matrix4x4.Identity)!;

        Assert.All(capsule.Vertices, v => Assert.Equal(0.25f, Vector3.Distance(v, new Vector3(0, 0, Math.Clamp(v.Z, 0, length))), 5));
    }

    private static List<HkxNode>? Read(string fixture)
        => Fixture.Read(fixture) is { } bytes ? HkxShapes.Read(HkxFile.Parse(bytes)) : null;

    private static HkxNode Physics(List<HkxNode> nodes) => nodes.Single(n => n.Label == "Physics Data");

    private static HkxNode Body(List<HkxNode> nodes, string name) => Physics(nodes).Children.Single(n => n.Label == name);
}
