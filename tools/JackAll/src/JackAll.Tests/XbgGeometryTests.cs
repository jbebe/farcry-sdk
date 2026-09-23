using JackAll.Tools.Xbg;

namespace JackAll.Tests;

/// <summary>
/// Regenerating every LOD's vertex and index blocks from per-cluster geometry reproduces the shipped
/// mesh, for each of the three vertex formats.
/// </summary>
/// <remarks>
/// <see cref="XbgFileTests"/> round-trips the container while carrying the vertex and index blocks
/// through untouched, so it says nothing about whether an exporter could produce them. This throws
/// them away and rebuilds, regenerating every buffer offset, index offset, face count, vertex count
/// and trailing word on the way. A writer that cannot reproduce an untouched file is not going to
/// be trusted with an edited one.
/// </remarks>
public sealed class XbgGeometryTests
{
    [Theory]
    [MemberData(nameof(XbgFixtures.Formats), MemberType = typeof(XbgFixtures))]
    public void Regenerating_every_lod_reproduces_the_file(string fixture)
    {
        if (Fixture.Read(fixture) is not { } original)
        {
            return;
        }

        XbgFile model = XbgFile.Parse(original);
        foreach (XbgLod lod in model.Lods)
        {
            List<ClusterGeometry> geometries = XbgGeometry.ReadLod(model, lod);
            lod.VertexData = [];
            lod.IndexData = [];
            foreach (XbgVertexBuffer buffer in lod.VertexBuffers)
            {
                buffer.Offset = uint.MaxValue;
                buffer.VertexCount = 0;
            }
            XbgGeometry.WriteLod(model, lod, geometries);
        }

        Fixture.AssertSameBytes(fixture, original, model.Write());
    }

    /// <summary>
    /// Every component decodes to file precision and back, which is what an editor handed float
    /// values depends on.
    /// </summary>
    [Theory]
    [MemberData(nameof(XbgFixtures.Formats), MemberType = typeof(XbgFixtures))]
    public void Every_vertex_buffer_survives_unpack_and_pack(string fixture)
    {
        if (Fixture.Read(fixture) is not { } bytes)
        {
            return;
        }

        foreach (XbgLod lod in XbgFile.Parse(bytes).Lods)
        {
            for (int index = 0; index < lod.VertexBuffers.Count; index++)
            {
                XbgVertexBuffer buffer = lod.VertexBuffers[index];
                VertexStream stream = VertexStream.Unpack(lod.VertexData, buffer, (int)buffer.VertexCount);
                Fixture.AssertSameBytes(
                    $"{fixture} buffer {index}",
                    lod.VertexData.AsSpan((int)buffer.Offset, (int)(buffer.VertexCount * buffer.Stride)),
                    stream.Pack());
            }
        }
    }

    /// <summary>
    /// All three shipped vertex formats store int16 positions, so the float and half decode paths are
    /// unreachable against retail data.
    /// </summary>
    [Theory]
    [MemberData(nameof(XbgFixtures.Formats), MemberType = typeof(XbgFixtures))]
    public void Every_shipped_format_stores_int16_positions(string fixture)
    {
        if (Fixture.Read(fixture) is not { } bytes)
        {
            return;
        }

        foreach (XbgVertexBuffer buffer in XbgFile.Parse(bytes).Lods.SelectMany(lod => lod.VertexBuffers))
        {
            Assert.True(
                (buffer.Flags & XbgFile.PosInt16) != 0,
                $"flags 0x{buffer.Flags:X} are not int16 positions");
        }
    }
}
