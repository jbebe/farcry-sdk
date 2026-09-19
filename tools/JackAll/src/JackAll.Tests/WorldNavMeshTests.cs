using System.Numerics;
using JackAll.Tools.World;

namespace JackAll.Tests;

/// <summary>Guards the hand-derived byte offsets of a per-sector navmesh and how sectors join: a
/// wrong stride or packing scale scatters every position, and a wrong link rule draws edges twice or
/// not at all.</summary>
public class WorldNavMeshTests
{
    private const string Folder = @".\Fixtures\Nvm";

    private static readonly uint[] None = [NavMeshSector.NoLink, NavMeshSector.NoLink, NavMeshSector.NoLink];

    private static uint Link(int sector, int node) => (uint)(node << 16) | (uint)sector;

    private static byte[] BuildSector(
        int sectorId, float minX, float minY, (short X, short Y, short Z)[] vertices,
        (int A, int B, int C, uint[] Links)[] nodes)
    {
        var b = new List<byte>();
        b.AddRange(BitConverter.GetBytes(0u));
        b.AddRange(BitConverter.GetBytes(0x4E764D68u));
        b.AddRange(BitConverter.GetBytes(0x14100u));
        b.AddRange(BitConverter.GetBytes(0u));
        b.AddRange(BitConverter.GetBytes((ushort)0));
        b.AddRange(BitConverter.GetBytes((ushort)sectorId));
        foreach (float f in new[] { minX, minY, minX + 64, minY + 64 })
        {
            b.AddRange(BitConverter.GetBytes(f));
        }
        b.AddRange(new byte[4 + 8 + 2]);

        b.AddRange(BitConverter.GetBytes((uint)nodes.Sum(n => n.Links.Length)));
        foreach ((_, _, _, uint[] links) in nodes)
        {
            foreach (uint link in links)
            {
                b.AddRange(BitConverter.GetBytes(link));
            }
        }

        b.AddRange(BitConverter.GetBytes((uint)nodes.Length));
        int linkOffset = 0;
        for (int i = 0; i < nodes.Length; i++)
        {
            (int a, int bb, int c, uint[] links) = nodes[i];
            var node = new byte[48];
            BitConverter.GetBytes(60u).CopyTo(node, 0);
            BitConverter.GetBytes((ushort)sectorId).CopyTo(node, 4);
            BitConverter.GetBytes((ushort)i).CopyTo(node, 6);
            node[0x0A] = (byte)links.Length;
            BitConverter.GetBytes((uint)linkOffset).CopyTo(node, 0x0B);
            BitConverter.GetBytes((short)((vertices[a].X + vertices[bb].X + vertices[c].X) / 3)).CopyTo(node, 0x0F);
            BitConverter.GetBytes((short)((vertices[a].Y + vertices[bb].Y + vertices[c].Y) / 3)).CopyTo(node, 0x11);
            BitConverter.GetBytes((short)((vertices[a].Z + vertices[bb].Z + vertices[c].Z) / 3)).CopyTo(node, 0x13);
            node[0x17] = 127;
            BitConverter.GetBytes((uint)a).CopyTo(node, 0x1A);
            BitConverter.GetBytes((uint)bb).CopyTo(node, 0x1E);
            BitConverter.GetBytes((uint)c).CopyTo(node, 0x22);
            b.AddRange(node);
            linkOffset += links.Length;
        }

        b.AddRange(BitConverter.GetBytes((uint)vertices.Length));
        foreach ((short x, short y, short z) in vertices)
        {
            b.AddRange(BitConverter.GetBytes(x));
            b.AddRange(BitConverter.GetBytes(y));
            b.AddRange(BitConverter.GetBytes(z));
        }
        return [.. b];
    }

    private static NavMeshSector Sector(int sectorId, float minX, params (int, int, int, uint[])[] nodes) =>
        WorldNavMesh.ReadSector(BuildSector(
            sectorId, minX, 2048, [(0, 0, 0), (256, 0, 0), (0, 256, 0), (256, 256, 0)], nodes))!;

    [Fact]
    public void DecodesVerticesAgainstItsSectorBoundingBox()
    {
        // A 64 m sector packs to a 128-unit span, so one step is 1/256 m; Z is always 1/32 m.
        NavMeshSector? sector = WorldNavMesh.ReadSector(BuildSector(
            2576, 1024, 2048, [(256, -512, 640), (256, -512, 640), (256, -512, 640)],
            [(0, 1, 2, None)]));

        Assert.NotNull(sector);
        Assert.Equal(new Vector3(1057f, 2078f, 20f), sector.Vertices[0]);
        Assert.Equal(new Vector3(1057f, 2078f, 20f), Assert.Single(sector.Centroids));
    }

    [Fact]
    public void ReadsTrianglesAndTheirLinks()
    {
        NavMeshSector sector = Sector(2576, 1024,
            (0, 1, 2, [NavMeshSector.NoLink, Link(2576, 1), NavMeshSector.NoLink]),
            (2, 1, 3, [Link(2576, 0), NavMeshSector.NoLink, NavMeshSector.NoLink, Link(2577, 4)]));

        Assert.Equal([0, 1, 2, 2, 1, 3], sector.Triangles);
        Assert.Equal([0, 3, 7], sector.LinkStart);
        Assert.Equal(Link(2577, 4), sector.Links[6]);
    }

    [Fact]
    public void SortsEdgesIntoInteriorBoundaryAndSeam()
    {
        NavMeshSector a = Sector(2576, 1024,
            (0, 1, 2, [NavMeshSector.NoLink, Link(2576, 1), Link(2577, 0)]),
            (2, 1, 3, [Link(2576, 0), NavMeshSector.NoLink, NavMeshSector.NoLink]));
        NavMeshSector b = Sector(2577, 1088,
            (0, 1, 2, [Link(2576, 0), NavMeshSector.NoLink, NavMeshSector.NoLink]));

        NavMesh mesh = WorldNavMesh.Assemble([a, b]);

        Assert.Equal(3, mesh.NodeCount);
        Assert.Equal(2, mesh.SectorCount);
        // The edge the two triangles of the first sector share, drawn once.
        Assert.Equal([1, 2], mesh.InteriorEdges);
        // The second sector's vertices follow the first sector's four.
        Assert.Equal([2, 0, 4, 5], mesh.SeamEdges);
        Assert.Equal([0, 1, 1, 3, 3, 2, 5, 6, 6, 4], mesh.BoundaryEdges);
        Assert.Equal([0, 1, 0, 2], mesh.Links);
    }

    [Fact]
    public void ANeighbouringPairIsLinkedOnceWhereverItSits()
    {
        NavMeshSector sector = Sector(2576, 1024,
            (0, 1, 2, [Link(2576, 1), NavMeshSector.NoLink, NavMeshSector.NoLink]),
            (2, 1, 3, [Link(2576, 0), Link(2576, 2), NavMeshSector.NoLink]),
            (1, 3, 2, [Link(2576, 1), NavMeshSector.NoLink, NavMeshSector.NoLink]));

        NavMesh mesh = WorldNavMesh.Assemble([sector]);

        Assert.Equal([0, 1, 1, 2], mesh.Links);
        Assert.Equal([0, 1, 1, 3], mesh.InteriorEdges);
    }

    [Fact]
    public void AnEdgeOnlyOneSideLinksIsStillDrawn()
    {
        NavMeshSector sector = Sector(2576, 1024,
            (0, 1, 2, None),
            (2, 1, 3, [Link(2576, 0), NavMeshSector.NoLink, NavMeshSector.NoLink]));

        NavMesh mesh = WorldNavMesh.Assemble([sector]);

        Assert.Equal([2, 1], mesh.InteriorEdges);
        Assert.Equal([1, 0], mesh.Links);
    }

    [Fact]
    public void ALinkIntoAnUnloadedSectorIsASeam()
    {
        NavMeshSector sector = Sector(2576, 1024,
            (0, 1, 2, [Link(2600, 0), NavMeshSector.NoLink, NavMeshSector.NoLink]));

        NavMesh mesh = WorldNavMesh.Assemble([sector]);

        Assert.Equal([0, 1], mesh.SeamEdges);
        Assert.Empty(mesh.Links);
    }

    [Theory]
    [InlineData(4, 0x4E764D67u)]
    [InlineData(8, 0x13000u)]
    public void RejectsFilesItCannotDecode(int offset, uint value)
    {
        byte[] bytes = BuildSector(2576, 1024, 2048, [(0, 0, 0)],
            [(0, 0, 0, None)]);
        BitConverter.GetBytes(value).CopyTo(bytes, offset);

        Assert.Null(WorldNavMesh.ReadSector(bytes));
    }

    [Fact]
    [Trait("Category", "RequiresFixture")]
    public void EveryRetailNodeSitsAtTheCentreOfItsTriangle()
    {
        if (!Directory.Exists(Folder)) return;

        NavMeshSector[] sectors = [.. new[] { "nv_2576.nvm", "nv_2577.nvm" }
            .Select(name => WorldNavMesh.ReadSector(File.ReadAllBytes(Path.Combine(Folder, name)))!)];

        foreach (NavMeshSector sector in sectors)
        {
            for (int n = 0; n < sector.NodeCount; n++)
            {
                Vector3 centre = (sector.Vertices[sector.Triangles[n * 3]] +
                                  sector.Vertices[sector.Triangles[n * 3 + 1]] +
                                  sector.Vertices[sector.Triangles[n * 3 + 2]]) / 3;
                Vector3 off = Vector3.Abs(centre - sector.Centroids[n]);
                // Two quantisation steps in X and Y, two in Z.
                Assert.True(off.X < 0.01f && off.Y < 0.01f && off.Z < 0.07f, $"sector {sector.SectorId} node {n}");
                Assert.True(sector.LinkStart[n + 1] - sector.LinkStart[n] >= 3);
            }
        }

        NavMesh mesh = WorldNavMesh.Assemble(sectors);
        Assert.NotEmpty(mesh.SeamEdges);
        Assert.NotEmpty(mesh.BoundaryEdges);
        Assert.All(mesh.Links, node => Assert.InRange(node, 0, mesh.NodeCount - 1));
        (int Low, int High)[] pairs = [.. mesh.Links.Chunk(2).Select(p => (Math.Min(p[0], p[1]), Math.Max(p[0], p[1])))];
        // At least one link crosses the seam between the two sectors.
        Assert.Contains(pairs, p => p.Low < sectors[0].NodeCount && p.High >= sectors[0].NodeCount);
        Assert.Equal(pairs.Length, pairs.Distinct().Count());
    }
}
