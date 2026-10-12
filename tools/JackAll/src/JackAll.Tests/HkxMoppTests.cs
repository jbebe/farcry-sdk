using System.Numerics;
using JackAll.Tools.Hkx;

namespace JackAll.Tests;

/// <summary>
/// MOPP code, shipped or built here, reaches each child of its collection and keeps every child a query box touches.
/// </summary>
public sealed class HkxMoppTests
{
    // A MOPP over a list of six boxes; the seat is child 4.
    private const string Chair = "Hkx/chairbar01.hkx";

    // A MOPP over an extended mesh: triangle subparts, and a subpart of boxes keyed with the top bit set.
    private const string Garage = "Hkx/colonialgarage02_ruin.hkx";

    private const uint Seat = 4;

    [Fact]
    [Trait("Category", "RequiresFixture")]
    public void The_fixtures_were_actually_found() => Fixture.AssertPresent(Chair, Garage);

    [Theory]
    [InlineData(Chair)]
    [InlineData(Garage)]
    public void Shipped_code_reaches_each_child_once(string fixture)
    {
        if (Mopp(fixture) is not { } mopp)
        {
            return;
        }

        Assert.Equal(Keys(HkxMopp.Items(mopp.GetPointer("child.childShape")!)), HkxMopp.Keys(HkxMopp.Code(mopp).Code).Order());
    }

    /// <summary>
    /// Pins the query, diagonal splits included, against Havok's own code. Havok fits a tilted child closer than its
    /// box, so a child counts as touched only where its own surface meets the query box.
    /// </summary>
    [Theory]
    [InlineData(Chair)]
    [InlineData(Garage)]
    public void Shipped_code_keeps_every_child_a_box_touches(string fixture)
    {
        if (Fixture.Read(fixture) is not { } bytes)
        {
            return;
        }

        HkxFile file = HkxFile.Parse(bytes);
        HkxObject mopp = file.Objects().First(o => o.Class.Name == "hkpMoppBvTreeShape");
        Dictionary<uint, (Vector3[] Points, int[] Triangles)> surfaces = Surfaces(file, mopp);
        (Vector4 info, byte[] code) = HkxMopp.Code(mopp);

        AssertKeepsTouched(HkxMopp.Items(mopp.GetPointer("child.childShape")!), info, code, (key, q) => Touches(surfaces[key], q));
    }

    [Theory]
    [InlineData(Chair)]
    [InlineData(Garage)]
    public void Built_code_reaches_each_child_once(string fixture)
    {
        if (Mopp(fixture) is not { } mopp)
        {
            return;
        }

        List<HkxMoppItem> items = HkxMopp.Items(mopp.GetPointer("child.childShape")!);
        Assert.Equal(Keys(items), HkxMopp.Keys(HkxMopp.Build(items).Code).Order());
    }

    [Theory]
    [InlineData(Chair)]
    [InlineData(Garage)]
    public void Built_code_keeps_every_child_a_box_touches(string fixture)
    {
        if (Mopp(fixture) is not { } mopp)
        {
            return;
        }

        List<HkxMoppItem> items = HkxMopp.Items(mopp.GetPointer("child.childShape")!);
        (Vector4 info, byte[] code) = HkxMopp.Build(items);
        AssertKeepsTouched(items, info, code);
    }

    [Fact]
    public void Built_code_culls_the_legs_from_a_box_on_the_seat()
    {
        if (Mopp(Chair) is not { } mopp)
        {
            return;
        }

        List<HkxMoppItem> items = HkxMopp.Items(mopp.GetPointer("child.childShape")!);
        (Vector4 info, byte[] code) = HkxMopp.Build(items);
        Vector3 centre = (items[(int)Seat].Min + items[(int)Seat].Max) / 2;

        Assert.Equal([Seat], HkxMopp.Query(info, code, centre - new Vector3(0.01f), centre + new Vector3(0.01f)));
    }

    [Fact]
    public void A_rebuilt_mopp_finds_a_moved_seat_after_a_round_trip()
    {
        if (Fixture.Read(Chair) is not { } bytes)
        {
            return;
        }

        HkxFile file = HkxFile.Parse(bytes);
        HkxObject mopp = file.Objects().First(o => o.Class.Name == "hkpMoppBvTreeShape");
        HkxObject seat = mopp.GetPointer("child.childShape")!.GetPointer($"childInfo[{Seat}].shape")!;
        float[] at = seat.GetReals("translation");
        seat.SetReals("translation", at[0], at[1], at[2] + 2, 0);
        Vector3 raised = new(at[0], at[1], at[2] + 2);
        Vector3 reach = new(0.01f);
        (Vector4 shippedInfo, byte[] shippedCode) = HkxMopp.Code(mopp);

        HkxMopp.Rebuild(mopp);
        HkxObject reread = HkxFile.Parse(file.Write()).Objects().First(o => o.Class.Name == "hkpMoppBvTreeShape");
        (Vector4 info, byte[] code) = HkxMopp.Code(reread);

        Assert.DoesNotContain(Seat, HkxMopp.Query(shippedInfo, shippedCode, raised - reach, raised + reach));
        Assert.Equal([Seat], HkxMopp.Query(info, code, raised - reach, raised + reach));
    }

    // No probe may lose a child whose bounds, and where `exactly` says so whose surface, it touches.
    private static void AssertKeepsTouched(List<HkxMoppItem> items, Vector4 info, byte[] code,
        Func<uint, (Vector3 Min, Vector3 Max), bool>? exactly = null)
    {
        List<string> lost = [.. HkxMopp.Probes(items, 500).SelectMany(q => HkxMopp.Lost(items, info, code, q)
            .Where(i => exactly?.Invoke(i.Key, q) ?? true)
            .Select(i => $"key 0x{i.Key:X8} lost to the box {q.Min} - {q.Max}"))];
        Assert.Empty(lost);
    }

    // Separating axes between a convex surface and a box: the box's three, each face normal, and each edge crossed with each box axis.
    private static bool Touches((Vector3[] Points, int[] Triangles) s, (Vector3 Min, Vector3 Max) q)
    {
        Vector3 centre = (q.Min + q.Max) / 2, half = (q.Max - q.Min) / 2;
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
        var surfaces = new Dictionary<uint, (Vector3[], int[])>();
        if (collection.Object!.Class.Name == "hkpListShape")
        {
            for (int i = 0; i < collection.Children.Count; i++)
            {
                HkxPrimitive p = collection.Children[i].Primitives().Single();
                surfaces[(uint)i] = ([.. p.ModelVertices()], p.Triangles);
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
                HkxPrimitive p = groups[g].Children[k].Primitives().Single();
                surfaces[0x80000000 | (uint)g << shift | (uint)k] = ([.. p.ModelVertices()], p.Triangles);
            }
        }
        return surfaces;
    }

    private static IEnumerable<HkxNode> Flatten(HkxNode node) => node.Children.SelectMany(Flatten).Prepend(node);

    private static IEnumerable<uint> Keys(List<HkxMoppItem> items) => items.Select(i => i.Key).Order();

    private static HkxObject? Mopp(string fixture)
        => Fixture.Read(fixture) is { } bytes ? HkxFile.Parse(bytes).Objects().First(o => o.Class.Name == "hkpMoppBvTreeShape") : null;
}
