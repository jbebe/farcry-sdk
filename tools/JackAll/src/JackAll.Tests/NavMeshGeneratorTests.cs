using System.Numerics;
using JackAll.Core.Format.Fcb;
using JackAll.Tools.World;

namespace JackAll.Tests;

/// <summary>Guards the generated navmesh against what the engine assumes of one: every edge meets one
/// other or none, links point both ways and across seams, nothing stands inside an obstacle, and graph
/// ids part what no agent can walk between.</summary>
public class NavMeshGeneratorTests
{
    private const float Ground = 16.0625f;
    private static readonly int[] TwoByTwo = [0, 1, 2, 3];

    /// <summary>A 3 m x 1 m wall, 2 m tall, turned 30 degrees, near the middle of a 2 x 2 sector world.</summary>
    private static readonly Matrix4x4 WallTurn = Matrix4x4.CreateRotationZ(MathF.PI / 6);
    private static readonly NavMeshSolid Wall = NavMeshSolid.Box(new(60, 50, Ground + 1), new(1.5f, 0.5f, 1), WallTurn);

    private static Dictionary<int, NvmSector> Build(
        IReadOnlyList<NavMeshSolid> solids, NavMeshTerrain? terrain = null, IReadOnlyList<Vector3>? anchors = null,
        Func<int, float>? water = null)
        => NavMeshGenerator.Build(2, TwoByTwo, terrain ?? NavMeshTerrain.Flat(Ground), solids, anchors ?? [],
            water ?? (_ => NvmSector.NoWater));

    private static readonly Lazy<Dictionary<int, NvmSector>> WithWall = new(() => Build([Wall]));

    private static IEnumerable<(NvmSector Sector, NvmNode Node, Vector3 A, Vector3 B, Vector3 C)> Triangles(Dictionary<int, NvmSector> sectors)
        => sectors.Values.SelectMany(s => s.Nodes.Select(n => (s, n, s.Unpack(s.Vertices[n.A]), s.Unpack(s.Vertices[n.B]), s.Unpack(s.Vertices[n.C]))));

    private static Vector3 Centroid((NvmSector, NvmNode, Vector3 A, Vector3 B, Vector3 C) t) => (t.A + t.B + t.C) / 3;

    [Fact]
    public void EveryEdgeMeetsOneOtherOrNone()
    {
        Dictionary<int, NvmSector> sectors = WithWall.Value;

        foreach (NvmSector sector in sectors.Values)
        {
            for (int n = 0; n < sector.Nodes.Length; n++)
            {
                NvmNode node = sector.Nodes[n];
                Vector3[] corners = [.. new[] { node.A, node.B, node.C }.Select(i => sector.Unpack(sector.Vertices[i]))];
                for (int k = 0; k < 3; k++)
                {
                    uint link = sector.Links[node.LinkOffset + k];
                    if (link == NvmSector.NoLink)
                    {
                        continue;
                    }
                    NvmSector other = sectors[(int)(link & 0xFFFF)];
                    NvmNode neighbour = other.Nodes[link >> 16];
                    uint[] back = [.. Enumerable.Range(0, 3).Select(j => other.Links[neighbour.LinkOffset + j])];
                    Assert.Contains((uint)n << 16 | sector.SectorId, back);

                    Vector3[] theirs = [.. new[] { neighbour.A, neighbour.B, neighbour.C }.Select(i => other.Unpack(other.Vertices[i]))];
                    int j = Array.IndexOf(back, (uint)n << 16 | sector.SectorId);
                    Assert.Equal(corners[k], theirs[(j + 1) % 3]);
                    Assert.Equal(corners[(k + 1) % 3], theirs[j]);
                }
            }
        }
    }

    [Fact]
    public void ReadsBackAsOneMeshJoinedAcrossItsSeams()
    {
        Dictionary<int, NvmSector> sectors = WithWall.Value;

        NavMesh mesh = WorldNavMesh.Assemble([.. sectors.Values
            .Select(s => WorldNavMesh.ReadSector(NvmSectorFile.Encode(s))!)]);

        Assert.Equal(4, mesh.SectorCount);
        Assert.NotEmpty(mesh.SeamEdges);
        Assert.All(sectors.Values, s => Assert.All(s.Nodes, n => Assert.Equal(127, n.NormalZ)));
        // The ground is one graph; only slivers too narrow to fit an agent stand apart.
        float area = Triangles(sectors).Sum(t => PlanArea(t.A, t.B, t.C));
        float main = Triangles(sectors).Where(t => t.Node.GraphId == 1).Sum(t => PlanArea(t.A, t.B, t.C));
        Assert.True(main > area * 0.99f, $"graph 1 holds {main / area:P1}");
    }

    [Fact]
    public void LeavesTheObstacleOutAndHugsItsEdge()
    {
        Matrix4x4.Invert(WallTurn, out Matrix4x4 untwist);
        bool InWall(Vector3 p)
        {
            Vector3 local = Vector3.Transform(p - new Vector3(60, 50, 0), untwist);
            return MathF.Abs(local.X) < 1.4f && MathF.Abs(local.Y) < 0.4f;
        }

        var triangles = Triangles(WithWall.Value).ToList();

        Assert.DoesNotContain(triangles, t => InWall(Centroid(t)));
        Assert.Contains(triangles, t => Vector2.Distance(new(Centroid(t).X, Centroid(t).Y), new(60, 50)) < 2.5f);
        // The top of the wall is walkable but nothing could get there, so it is not kept.
        Assert.All(triangles, t => Assert.InRange(Centroid(t).Z, Ground - 0.05f, Ground + 0.05f));
    }

    [Fact]
    public void MeasuresClearanceToTheNearestOpenEdge()
    {
        var triangles = Triangles(WithWall.Value).ToList();

        var open = triangles.MinBy(t => Vector2.Distance(new(Centroid(t).X, Centroid(t).Y), new(64, 20)));
        var byTheWall = triangles.MinBy(t => Vector2.Distance(new(Centroid(t).X, Centroid(t).Y), new(60, 51.2f)));

        // 64,20 is 20 m from the world's edge and 30 m from the wall, so it saturates at 10 m.
        Assert.Equal(250, open.Node.Clearance[0]);
        Assert.InRange(byTheWall.Node.Clearance[0], 0, 25);
    }

    [Fact]
    public void LaysCoversAlongTheWall()
    {
        NvmSector sector = WithWall.Value[0];

        Assert.NotEmpty(sector.Covers);
        HashSet<ushort> graphs = [.. sector.Nodes.Select(n => n.GraphId)];
        foreach (NvmCover cover in sector.Covers)
        {
            Vector3 start = sector.Unpack(sector.CoverVertices[cover.Vertex]), end = sector.Unpack(sector.CoverVertices[cover.Vertex + 1]);
            Assert.InRange(Vector2.Distance(new((start.X + end.X) / 2, (start.Y + end.Y) / 2), new(60, 50)), 0, 2.5f);
            // The wall is 2 m tall, as high as a cover is measured.
            Assert.Equal(250, cover.Height);
            Assert.Contains(cover.GraphId, graphs);
        }
    }

    [Fact]
    public void ACliffPartsTheGroundIntoTwoGraphs()
    {
        var cliff = NavMeshTerrain.Flat(Ground) with { Height = (x, _) => x < 64 ? Ground : Ground + 10 };

        var triangles = Triangles(Build([], cliff)).ToList();

        ushort low = triangles.First(t => Centroid(t).X < 60).Node.GraphId;
        ushort high = triangles.First(t => Centroid(t).X > 68).Node.GraphId;
        Assert.NotEqual(low, high);
        Assert.All(triangles, t => Assert.True(t.Node.NormalZ > 89, "nothing as steep as the cliff is walkable"));
    }

    [Fact]
    public void FollowsRollingGround()
    {
        static float Hill(float x, float y) => Ground + 3 * MathF.Sin(x / 9) * MathF.Cos(y / 11);
        var rolling = NavMeshTerrain.Flat(Ground) with { Height = (x, y) => Hill(x, y) };

        var triangles = Triangles(Build([], rolling)).ToList();

        Assert.True(triangles.Sum(t => PlanArea(t.A, t.B, t.C)) > 128 * 128 * 0.99f, "all of it is walkable");
        Assert.All(triangles, t => Assert.InRange(Centroid(t).Z - Hill(Centroid(t).X, Centroid(t).Y), -0.5f, 0.5f));
    }

    [Fact]
    public void ASlopeTooSteepToWalkIsLeftOut()
    {
        // A 60 degree bank across the middle of the world, flat ground either side.
        var bank = NavMeshTerrain.Flat(Ground) with { Height = (x, _) => Ground + Math.Clamp(x - 60, 0, 8) * MathF.Tan(MathF.PI / 3) };

        var triangles = Triangles(Build([], bank)).ToList();

        Assert.DoesNotContain(triangles, t => Centroid(t).X is > 61 and < 67);
        Assert.Contains(triangles, t => Centroid(t).X < 59);
        Assert.Contains(triangles, t => Centroid(t).X > 69);
    }

    [Fact]
    public void WaterIsAFloorUnderTheSurfaceInAGraphOfItsOwn()
    {
        const float level = Ground + 3;

        var triangles = Triangles(Build([], water: id => id == 0 ? level : NvmSector.NoWater)).ToList();

        var wet = triangles.Where(t => t.Sector.SectorId == 0 && Centroid(t).X < 60 && Centroid(t).Y < 60).ToList();
        Assert.All(wet, t => Assert.True(t.Node.UnderWater));
        Assert.All(wet, t => Assert.Equal(level - 0.62f, Centroid(t).Z, 0.05f));
        Assert.DoesNotContain(triangles, t => t.Sector.SectorId != 0 && t.Node.GraphId == wet[0].Node.GraphId);
    }

    [Fact]
    public void ARaisedPlatformIsKeptOnlyWithAnAgentOnIt()
    {
        NavMeshSolid platform = NavMeshSolid.Box(new(30, 30, Ground + 0.5f), new(4, 4, 0.5f), Matrix4x4.Identity);
        bool OnTop((NvmSector, NvmNode, Vector3, Vector3, Vector3) t) => Centroid(t).Z > Ground + 0.9f;

        Assert.DoesNotContain(Triangles(Build([platform])), OnTop);
        Assert.Contains(Triangles(Build([platform], anchors: [new(30, 30, Ground + 1)])), OnTop);
    }

    [Fact]
    public void BothFloorsOfATableAreWalked()
    {
        NavMeshSolid slab = NavMeshSolid.Box(new(30, 30, Ground + 2.6f), new(4, 4, 0.1f), Matrix4x4.Identity);

        var over = Triangles(Build([slab], anchors: [new(30, 30, Ground + 2.7f)])).Where(t => Covers(t, new(30, 30))).ToList();

        Assert.Contains(over, t => MathF.Abs(Centroid(t).Z - Ground) < 0.05f && !t.Node.AboveTerrain);
        Assert.Contains(over, t => MathF.Abs(Centroid(t).Z - (Ground + 2.7f)) < 0.05f && t.Node.AboveTerrain);
    }

    private static bool Covers((NvmSector, NvmNode, Vector3 A, Vector3 B, Vector3 C) t, Vector2 p)
    {
        static float Side(Vector3 a, Vector3 b, Vector2 p) => (b.X - a.X) * (p.Y - a.Y) - (b.Y - a.Y) * (p.X - a.X);
        return Side(t.A, t.B, p) >= 0 && Side(t.B, t.C, p) >= 0 && Side(t.C, t.A, p) >= 0;
    }

    [Fact]
    public void MarksTheSectorsItBuiltInSectorsDep()
    {
        uint worldSector = FcbClassDefinitions.Crc32Ascii("CWorldSector");
        FcbObject Entry(int id)
        {
            var entry = new FcbObject { TypeHash = worldSector };
            entry.Values[FcbClassDefinitions.Crc32Ascii("SectorId")] = BitConverter.GetBytes(id);
            return entry;
        }
        var group = new FcbObject();
        group.Children.AddRange([Entry(0), Entry(1)]);
        var root = new FcbObject();
        root.Children.Add(group);

        Assert.Equal(1, SectorsDep.Mark(root, SectorsDep.HasNavMesh, new HashSet<int> { 1 }));
        // The hash LoadDep reads the flag by.
        Assert.Equal([1], group.Children[1].Values[0xC637A176]);
        Assert.False(group.Children[0].Values.ContainsKey(0xC637A176));
    }

    private static float PlanArea(Vector3 a, Vector3 b, Vector3 c)
        => MathF.Abs((b.X - a.X) * (c.Y - a.Y) - (b.Y - a.Y) * (c.X - a.X)) / 2;
}
