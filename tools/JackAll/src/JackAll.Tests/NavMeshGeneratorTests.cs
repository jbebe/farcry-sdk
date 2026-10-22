using System.Numerics;
using JackAll.Core.Format.Fcb;
using JackAll.Tools.World;

namespace JackAll.Tests;

/// <summary>Guards the generated navmesh against what the engine assumes of one: every edge meets one
/// other or none, links point both ways and across seams, and nothing stands inside an obstacle.</summary>
public class NavMeshGeneratorTests
{
    private const float Ground = 16.0625f;

    /// <summary>A 2 x 2 sector world with one 3 m x 1 m wall turned 30 degrees, near the middle.</summary>
    private static Dictionary<int, NvmSector> Build(out NavMeshObstacle wall)
    {
        var rotation = Matrix3x2.CreateRotation(MathF.PI / 6);
        wall = new NavMeshObstacle([.. new Vector2[] { new(-1.5f, -0.5f), new(1.5f, -0.5f), new(1.5f, 0.5f), new(-1.5f, 0.5f) }
            .Select(p => Vector2.Transform(p, rotation) + new Vector2(60, 50))]);
        return NavMeshGenerator.Build(2, [0, 1, 2, 3], (_, _) => Ground, [wall], _ => NvmSector.NoWater);
    }

    [Fact]
    public void EveryEdgeMeetsOneOtherOrNone()
    {
        Dictionary<int, NvmSector> sectors = Build(out _);

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
        Dictionary<int, NvmSector> sectors = Build(out _);

        NavMesh mesh = WorldNavMesh.Assemble([.. sectors.Values
            .Select(s => WorldNavMesh.ReadSector(NvmSectorFile.Encode(s))!)]);

        Assert.Equal(4, mesh.SectorCount);
        Assert.NotEmpty(mesh.SeamEdges);
        Assert.All(sectors.Values, s => Assert.All(s.Nodes, n => Assert.Equal(1, n.GraphId)));
        Assert.All(sectors.Values, s => Assert.All(s.Nodes, n => Assert.Equal(127, n.NormalZ)));
    }

    [Fact]
    public void LeavesTheObstacleOutAndHugsItsEdge()
    {
        Dictionary<int, NvmSector> sectors = Build(out NavMeshObstacle wall);

        NvmSector sector = sectors[0];
        Assert.All(sector.Nodes, n => Assert.False(wall.Contains(Flat(sector.Unpack(n.Centroid)))));
        Assert.Contains(sector.Nodes, n => Vector2.Distance(Flat(sector.Unpack(n.Centroid)), new(60, 50)) < 2.5f);
    }

    [Fact]
    public void MeasuresClearanceToTheNearestOpenEdge()
    {
        Dictionary<int, NvmSector> sectors = Build(out _);

        NvmSector sector = sectors[0];
        NvmNode open = sector.Nodes.MinBy(n => Vector2.Distance(Flat(sector.Unpack(n.Centroid)), new(64, 20)));
        NvmNode byTheWall = sector.Nodes.MinBy(n => Vector2.Distance(Flat(sector.Unpack(n.Centroid)), new(60, 51.2f)));

        // 64,20 is 20 m from the world's edge and 30 m from the wall, so it saturates at 10 m.
        Assert.Equal(250, open.Clearance[0]);
        Assert.InRange(byTheWall.Clearance[0], 0, 25);
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

        Assert.Equal(1, NavMeshGenerator.MarkHasNavMesh(root, new HashSet<int> { 1 }));
        // The hash LoadDep reads the flag by.
        Assert.Equal([1], group.Children[1].Values[0xC637A176]);
        Assert.False(group.Children[0].Values.ContainsKey(0xC637A176));
    }

    private static Vector2 Flat(Vector3 v) => new(v.X, v.Y);
}
