using System.Numerics;
using JackAll.Tools.World;

namespace JackAll.Tests;

/// <summary>Guards the hand-derived byte offsets of a per-sector navmesh: a wrong node stride or
/// packing scale scatters every position, which a synthetic sector catches immediately.</summary>
public class WorldNavMeshTests
{
    private static byte[] BuildSector(short x, short y, short z, sbyte normalZ)
    {
        var b = new List<byte>();
        b.AddRange(BitConverter.GetBytes(0u));
        b.AddRange(BitConverter.GetBytes(0x4E764D68u));
        b.AddRange(BitConverter.GetBytes(0x14100u));
        b.AddRange(BitConverter.GetBytes(0u));
        b.AddRange(BitConverter.GetBytes((ushort)0));
        b.AddRange(BitConverter.GetBytes((ushort)2576));
        foreach (float f in new[] { 1024f, 2048f, 1088f, 2112f })
        {
            b.AddRange(BitConverter.GetBytes(f));
        }
        b.AddRange(new byte[4 + 8 + 2]);

        // The quadtree's index array, then the node array.
        b.AddRange(BitConverter.GetBytes(2u));
        b.AddRange(new byte[8]);
        b.AddRange(BitConverter.GetBytes(1u));

        var node = new byte[48];
        BitConverter.GetBytes(x).CopyTo(node, 15);
        BitConverter.GetBytes(y).CopyTo(node, 17);
        BitConverter.GetBytes(z).CopyTo(node, 19);
        node[21] = 0;
        node[22] = 0;
        node[23] = (byte)normalZ;
        b.AddRange(node);
        return [.. b];
    }

    [Fact]
    public void DecodesNodeAgainstItsSectorBoundingBox()
    {
        // A 64 m sector packs to a 128-unit span, so one step is 1/256 m; Z is always 1/32 m.
        List<NavMeshNode>? nodes = WorldNavMesh.ReadSector(BuildSector(256, -512, 640, 127));

        NavMeshNode node = Assert.Single(nodes!);
        Assert.Equal(new Vector3(1057f, 2078f, 20f), node.Position);
        Assert.Equal(1f, node.Normal.Z, 0.01);
    }

    [Theory]
    [InlineData(4, 0x4E764D67u)]
    [InlineData(8, 0x13000u)]
    public void RejectsFilesItCannotDecode(int offset, uint value)
    {
        byte[] bytes = BuildSector(0, 0, 0, 127);
        BitConverter.GetBytes(value).CopyTo(bytes, offset);

        Assert.Null(WorldNavMesh.ReadSector(bytes));
    }
}
