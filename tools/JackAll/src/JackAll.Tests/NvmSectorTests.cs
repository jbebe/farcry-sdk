using System.Numerics;
using JackAll.Tools.World;

namespace JackAll.Tests;

/// <summary>Guards the navmesh sector codec against the retail bytes: anything a generated sector
/// gets wrong in layout, the engine reads as a different mesh.</summary>
public class NvmSectorTests
{
    /// <summary>A sector with cover points and links across its seam with the next, and an empty one.</summary>
    private static readonly string[] Sectors = ["Nvm/nv_2576.nvm", "Nvm/nv_2577.nvm", "Nvm/nv_3778.nvm"];

    [Fact]
    [Trait("Category", "RequiresFixture")]
    public void The_fixtures_were_actually_found() => Fixture.AssertPresent(Sectors);

    [Theory]
    [InlineData("Nvm/nv_2576.nvm")]
    [InlineData("Nvm/nv_2577.nvm")]
    [InlineData("Nvm/nv_3778.nvm")]
    public void RoundTripsARetailSectorByteForByte(string path)
    {
        if (Fixture.Read(path) is not { } bytes) return;

        Fixture.AssertSameBytes(path, bytes, NvmSectorFile.Encode(NvmSectorFile.Decode(bytes)));
    }

    [Theory]
    [InlineData("Nvm/nv_2576.nvm")]
    [InlineData("Nvm/nv_2577.nvm")]
    [InlineData("Nvm/nv_3778.nvm")]
    public void RebuildsTheQuadTreeTheEngineBaked(string path)
    {
        if (Fixture.Read(path) is not { } bytes) return;
        NvmSector sector = NvmSectorFile.Decode(bytes);

        (Vector2, Vector2, Vector2)[] triangles = [.. sector.Nodes.Select(n => (
            Flat(sector.Unpack(sector.Vertices[n.A])), Flat(sector.Unpack(sector.Vertices[n.B])),
            Flat(sector.Unpack(sector.Vertices[n.C]))))];
        (NvmQuad[] quads, ushort[] quadNodes) =
            NvmSectorFile.BuildQuadTree(sector.MinX, sector.MinY, sector.MaxX, sector.MaxY, triangles);

        Assert.Equal(341, quads.Length);
        Assert.Equal(sector.QuadNodes, quadNodes);
        Assert.Equal(sector.Quads.Select(Describe), quads.Select(Describe));
    }

    [Fact]
    public void PacksAsTheEngineDoesTruncatingTowardZero()
    {
        NvmSector sector = Empty() with { MinX = 1024, MinY = 2048, MaxX = 1088, MaxY = 2112 };

        Assert.Equal(new PackedVector(256, -512, 640), sector.Pack(new Vector3(1057.002f, 2078f, 20.02f)));
        Assert.Equal(new Vector3(1057f, 2078f, 20f), sector.Unpack(new PackedVector(256, -512, 640)));
    }

    [Fact]
    public void RejectsAnotherVersion()
    {
        byte[] bytes = NvmSectorFile.Encode(Empty());
        BitConverter.GetBytes(0x13900u).CopyTo(bytes, 8);

        Assert.Throws<InvalidDataException>(() => NvmSectorFile.Decode(bytes));
    }

    private static Vector2 Flat(Vector3 v) => new(v.X, v.Y);

    private static string Describe(NvmQuad q)
        => $"{q.MinX},{q.MaxX},{q.MinY},{q.MaxY} {q.Count}@{q.Start} {string.Join(',', q.Children ?? [])}";

    private static NvmSector Empty() => new()
    {
        SectorWord = 0,
        SectorId = 0,
        MinX = 0,
        MinY = 0,
        MaxX = 64,
        MaxY = 64,
        WaterLevel = NvmSector.NoWater,
        BuildId = 0,
        Unknown30 = 0x7F7F,
        Links = [],
        Nodes = [],
        Vertices = [],
        Covers = [],
        DynamicCovers = [],
        CoverVertices = [],
        Quads = [],
        QuadNodes = [],
    };
}
