using JackAll.Tools.Xbg;

namespace JackAll.Tests;

/// <summary>
/// Every vertex component decoded to float space and packed back has to return what shipped, for
/// each of the three vertex formats that ship.
/// </summary>
/// <remarks>
/// The container round trip carries the vertex block through untouched, so it says nothing about
/// this. What this proves is the other half: that the float values an editor is handed quantise
/// back exactly, so an untouched part survives a decode-and-rebuild and a changed one only moves
/// where it was changed. Anything failing here is a component an exporter would silently corrupt.
/// </remarks>
public sealed class VertexEncoderTests
{
    [Theory]
    [MemberData(nameof(XbgFixtures.Formats), MemberType = typeof(XbgFixtures))]
    public void Every_component_quantises_back_to_what_shipped(string fixture)
    {
        if (Fixture.Read(fixture) is not { } bytes)
        {
            return;
        }

        XbgFile model = XbgFile.Parse(bytes);
        VertexScales scales = VertexScales.Of(model);
        foreach (XbgLod lod in model.Lods)
        {
            foreach (XbgVertexBuffer buffer in lod.VertexBuffers.Where(b => b.VertexCount > 0))
            {
                VertexStream stream = VertexStream.Unpack(lod.VertexData, buffer, (int)buffer.VertexCount);
                VertexStream produced = VertexEncoder.Encode(
                    buffer.Flags, stream.Count, scales,
                    new VertexData
                    {
                        Positions = stream.Positions(model.PosScale),
                        Uvs = stream.Uvs(scales.UvTranslate, scales.UvScale, 0),
                        Uvs1 = stream.Uvs(scales.UvTranslate, scales.UvScale, 1),
                        Normals = stream.Normals(),
                        Colours = stream.Colours(),
                        Skin = stream.Skin(),
                    },
                    stream);

                foreach ((string name, byte[] original) in stream.Components)
                {
                    Fixture.AssertSameBytes(
                        $"{fixture} {name} in flags 0x{buffer.Flags:X}", original, produced.Components[name]);
                }
            }
        }
    }

    /// <summary>
    /// With no template, a vertex falls back to the constants shipped vertices carry - which is
    /// what an authored part gets for the slots an editor cannot supply.
    /// </summary>
    [Fact]
    public void An_authored_vertex_falls_back_to_the_shipped_constants()
    {
        // Static geometry: int16 position, one UV set, a normal and a colour.
        const uint Flags = XbgFile.PosInt16 | XbgFile.Uv0 | XbgFile.Normal | XbgFile.Colour;
        var scales = new VertexScales(0.001f, 0.0f, 0.0001f);

        VertexStream stream = VertexEncoder.Encode(
            Flags, 1, scales,
            new VertexData { Positions = [(1.0f, -2.0f, 0.5f)] });

        Assert.Equal(1, stream.Count);
        Assert.Equal([(1.0f, -2.0f, 0.5f)], stream.Positions(scales.PosScale));

        // The fourth position slot and the fourth byte of a direction are constant across all
        // 14,319,419 shipped vertices, so an authored vertex has to carry them too.
        Assert.Equal(VertexEncoder.PositionW, BitConverter.ToInt16(stream.Components["pos"], 6));
        Assert.Equal(VertexEncoder.DirectionW, stream.Components["normal"][3]);
        Assert.Equal([(1.0f, 1.0f, 1.0f, 1.0f)], stream.Colours()!);
    }
}
