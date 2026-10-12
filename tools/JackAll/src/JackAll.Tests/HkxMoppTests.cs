using System.Numerics;
using JackAll.Tools.Hkx;

namespace JackAll.Tests;

/// <summary>
/// MOPP code, shipped or built here, reaches each child of its collection once and keeps every child a query box
/// touches.
/// </summary>
public sealed class HkxMoppTests
{
    // A MOPP over a list of six boxes; the seat is child 4.
    private const string Chair = "Hkx/chairbar01.hkx";

    // A MOPP over an extended mesh: triangle subparts, and a subpart of boxes keyed with the top bit set.
    private const string Garage = "Hkx/colonialgarage02_ruin.hkx";

    private const uint Seat = 4;
    private const int Probes = 500;

    [Fact]
    [Trait("Category", "RequiresFixture")]
    public void The_fixtures_were_actually_found() => Fixture.AssertPresent(Chair, Garage);

    /// <summary>
    /// Pins the query, diagonal splits included, against Havok's own code, and the children's keys and bounds
    /// against what Havok built it from. Havok fits a tilted child closer than its box, so a child counts as touched
    /// only where its own surface meets the query box.
    /// </summary>
    [Theory]
    [InlineData(Chair)]
    [InlineData(Garage)]
    public void Shipped_code_reaches_each_child_once_and_keeps_every_child_a_box_touches(string fixture)
    {
        if (Read(fixture) is not { } file)
        {
            return;
        }

        HkxObject mopp = Mopp(file);
        Dictionary<uint, (Vector3[] Points, int[] Triangles)> surfaces = Surfaces(file, mopp);
        (Vector4 info, byte[] code) = HkxMopp.Code(mopp);

        HkxMoppCheck check = HkxMopp.Check(HkxMopp.Items(mopp), info, code, Probes);

        Assert.Empty(check.Unreached);
        Assert.Empty(check.Stray);
        Assert.DoesNotContain(check.Lost, l => Touches(surfaces[l.Item.Key], l.Min, l.Max));
    }

    [Theory]
    [InlineData(Chair)]
    [InlineData(Garage)]
    public void Built_code_reaches_each_child_once_and_keeps_every_child_a_box_touches(string fixture)
    {
        if (Read(fixture) is not { } file)
        {
            return;
        }

        List<HkxChild> items = HkxMopp.Items(Mopp(file));
        (Vector4 info, byte[] code) = HkxMopp.Build(items);

        HkxMoppCheck check = HkxMopp.Check(items, info, code, Probes);

        Assert.Empty(check.Unreached);
        Assert.Empty(check.Stray);
        Assert.Empty(check.Lost);
    }

    [Fact]
    public void Built_code_culls_the_legs_from_a_box_on_the_seat()
    {
        if (Read(Chair) is not { } file)
        {
            return;
        }

        List<HkxChild> items = HkxMopp.Items(Mopp(file));
        (Vector4 info, byte[] code) = HkxMopp.Build(items);
        Vector3 centre = (items[(int)Seat].Min + items[(int)Seat].Max) / 2;

        Assert.Equal([Seat], HkxMopp.Query(info, code, centre - new Vector3(0.01f), centre + new Vector3(0.01f)));
    }

    [Fact]
    public void A_rebuilt_mopp_finds_a_moved_seat_after_a_round_trip()
    {
        if (Read(Chair) is not { } file)
        {
            return;
        }

        HkxObject mopp = Mopp(file);
        HkxObject seat = mopp.GetPointer("child.childShape")!.GetPointer($"childInfo[{Seat}].shape")!;
        float[] at = seat.GetReals("translation");
        seat.SetReals("translation", at[0], at[1], at[2] + 2, 0);
        Vector3 raised = new(at[0], at[1], at[2] + 2), reach = new(0.01f);
        (Vector4 shippedInfo, byte[] shippedCode) = HkxMopp.Code(mopp);

        HkxMopp.Rebuild(mopp);
        (Vector4 info, byte[] code) = HkxMopp.Code(Mopp(HkxFile.Parse(file.Write())));

        Assert.DoesNotContain(Seat, HkxMopp.Query(shippedInfo, shippedCode, raised - reach, raised + reach));
        Assert.Equal([Seat], HkxMopp.Query(info, code, raised - reach, raised + reach));
    }

    // Separating axes between a convex surface and a box: the box's three, each face normal, and each edge crossed with each box axis.
    private static bool Touches((Vector3[] Points, int[] Triangles) s, Vector3 min, Vector3 max)
    {
        Vector3 centre = (min + max) / 2, half = (max - min) / 2;
        Vector3[] boxAxes = [Vector3.UnitX, Vector3.UnitY, Vector3.UnitZ];
        List<Vector3> axes = [.. boxAxes];
        for (int t = 0; t < s.Triangles.Length; t += 3)
        {
            Vector3 a = s.Points[s.Triangles[t]], b = s.Points[s.Triangles[t + 1]], c = s.Points[s.Triangles[t + 2]];
            Vector3[] edges = [b - a, c - b, a - c];
            axes.Add(Vector3.Cross(edges[0], edges[1]));
            axes.AddRange(edges.SelectMany(e => boxAxes.Select(x => Vector3.Cross(e, x))));
        }
        return axes.All(axis =>
        {
            float reach = Vector3.Dot(half, Vector3.Abs(axis));
            float[] projected = [.. s.Points.Select(v => Vector3.Dot(v - centre, axis))];
            return projected.Min() <= reach && projected.Max() >= -reach;
        });
    }

    // Each child's surface by its key, as drawn in model space; each fixture's one body sits at the origin.
    private static Dictionary<uint, (Vector3[] Points, int[] Triangles)> Surfaces(HkxFile file, HkxObject mopp)
    {
        HkxNode collection = HkxShapes.Read(file).SelectMany(Flatten).Single(n => n.Object == mopp).Children.Single();
        var surfaces = new Dictionary<uint, (Vector3[] Points, int[] Triangles)>();
        static (Vector3[], int[]) Surface(HkxNode n)
        {
            HkxPrimitive p = n.Primitives().Single();
            return ([.. p.ModelVertices()], p.Triangles);
        }

        if (collection.Object!.Class.Name == "hkpListShape")
        {
            for (int i = 0; i < collection.Children.Count; i++)
            {
                surfaces[(uint)i] = Surface(collection.Children[i]);
            }
            return surfaces;
        }

        int shift = 32 - collection.Object.GetInt("numBitsForSubpartIndex");
        HkxNode[] subparts = [.. collection.Children.Where(n => n.Primitive is not null)], groups = [.. collection.Children.Where(n => n.Primitive is null)];
        for (int s = 0; s < subparts.Length; s++)
        {
            HkxPrimitive p = subparts[s].Primitive!;
            Vector3[] points = [.. p.ModelVertices()];
            for (int k = 0; k < p.Triangles.Length / 3; k++)
            {
                surfaces[(uint)s << shift | (uint)k] = ([.. p.Triangles[(k * 3)..(k * 3 + 3)].Select(i => points[i])], [0, 1, 2]);
            }
        }
        for (int g = 0; g < groups.Length; g++)
        {
            for (int k = 0; k < groups[g].Children.Count; k++)
            {
                surfaces[0x80000000 | (uint)g << shift | (uint)k] = Surface(groups[g].Children[k]);
            }
        }
        return surfaces;
    }

    private static IEnumerable<HkxNode> Flatten(HkxNode node) => node.Children.SelectMany(Flatten).Prepend(node);

    private static HkxFile? Read(string fixture) => Fixture.Read(fixture) is { } bytes ? HkxFile.Parse(bytes) : null;

    private static HkxObject Mopp(HkxFile file) => file.Objects().First(o => o.Class.Name == "hkpMoppBvTreeShape");
}
