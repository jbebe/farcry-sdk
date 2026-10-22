using System.Numerics;
using JackAll.Tools.World;

namespace JackAll.Tests;

/// <summary>Guards the hand-derived byte offsets of a per-sector navmesh and how sectors join: a
/// wrong stride or packing scale scatters every position, and a wrong link rule draws edges twice or
/// not at all.</summary>
public class WorldNavMeshTests
{
    /// <summary>Two neighbouring retail sectors, so links across the seam between them resolve.</summary>
    private static readonly string[] Sectors = ["Nvm/nv_2576.nvm", "Nvm/nv_2577.nvm"];

    private static readonly uint[] None = [NvmSector.NoLink, NvmSector.NoLink, NvmSector.NoLink];

    private static uint Link(int sector, int node) => (uint)(node << 16) | (uint)sector;

    private static byte[] BuildSector(
        int sectorId, float minX, float minY, (short X, short Y, short Z)[] vertices,
        (int A, int B, int C, uint[] Links)[] nodes)
    {
        PackedVector Centroid(int a, int b, int c) => new(
            (short)((vertices[a].X + vertices[b].X + vertices[c].X) / 3),
            (short)((vertices[a].Y + vertices[b].Y + vertices[c].Y) / 3),
            (short)((vertices[a].Z + vertices[b].Z + vertices[c].Z) / 3));

        int linkOffset = 0;
        return NvmSectorFile.Encode(new NvmSector
        {
            SectorWord = 0,
            SectorId = (ushort)sectorId,
            MinX = minX,
            MinY = minY,
            MaxX = minX + 64,
            MaxY = minY + 64,
            WaterLevel = NvmSector.NoWater,
            BuildId = 0,
            Unknown30 = 0,
            Links = [.. nodes.SelectMany(n => n.Links)],
            Nodes = [.. nodes.Select(n =>
            {
                var node = new NvmNode(1, (byte)n.Links.Length, (uint)linkOffset, Centroid(n.A, n.B, n.C),
                    0, 0, 127, false, false, (uint)n.A, (uint)n.B, (uint)n.C, default);
                linkOffset += n.Links.Length;
                return node;
            })],
            Vertices = [.. vertices.Select(v => new PackedVector(v.X, v.Y, v.Z))],
            Covers = [],
            DynamicCovers = [],
            CoverVertices = [],
            Quads = [],
            QuadNodes = [],
        });
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
            (0, 1, 2, [NvmSector.NoLink, Link(2576, 1), NvmSector.NoLink]),
            (2, 1, 3, [Link(2576, 0), NvmSector.NoLink, NvmSector.NoLink, Link(2577, 4)]));

        Assert.Equal([0, 1, 2, 2, 1, 3], sector.Triangles);
        Assert.Equal([0, 3, 7], sector.LinkStart);
        Assert.Equal(Link(2577, 4), sector.Links[6]);
    }

    [Fact]
    public void SortsEdgesIntoInteriorBoundaryAndSeam()
    {
        NavMeshSector a = Sector(2576, 1024,
            (0, 1, 2, [NvmSector.NoLink, Link(2576, 1), Link(2577, 0)]),
            (2, 1, 3, [Link(2576, 0), NvmSector.NoLink, NvmSector.NoLink]));
        NavMeshSector b = Sector(2577, 1088,
            (0, 1, 2, [Link(2576, 0), NvmSector.NoLink, NvmSector.NoLink]));

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
            (0, 1, 2, [Link(2576, 1), NvmSector.NoLink, NvmSector.NoLink]),
            (2, 1, 3, [Link(2576, 0), Link(2576, 2), NvmSector.NoLink]),
            (1, 3, 2, [Link(2576, 1), NvmSector.NoLink, NvmSector.NoLink]));

        NavMesh mesh = WorldNavMesh.Assemble([sector]);

        Assert.Equal([0, 1, 1, 2], mesh.Links);
        Assert.Equal([0, 1, 1, 3], mesh.InteriorEdges);
    }

    [Fact]
    public void AnEdgeOnlyOneSideLinksIsStillDrawn()
    {
        NavMeshSector sector = Sector(2576, 1024,
            (0, 1, 2, None),
            (2, 1, 3, [Link(2576, 0), NvmSector.NoLink, NvmSector.NoLink]));

        NavMesh mesh = WorldNavMesh.Assemble([sector]);

        Assert.Equal([2, 1], mesh.InteriorEdges);
        Assert.Equal([1, 0], mesh.Links);
    }

    [Fact]
    public void ALinkIntoAnUnloadedSectorIsASeam()
    {
        NavMeshSector sector = Sector(2576, 1024,
            (0, 1, 2, [Link(2600, 0), NvmSector.NoLink, NvmSector.NoLink]));

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
    public void The_fixtures_were_actually_found() => Fixture.AssertPresent(Sectors);

    [Fact]
    public void EveryRetailNodeSitsAtTheCentreOfItsTriangle()
    {
        if (!Fixture.Present(Sectors)) return;

        NavMeshSector[] sectors = [.. Sectors.Select(s => WorldNavMesh.ReadSector(Fixture.Read(s)!)!)];

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
