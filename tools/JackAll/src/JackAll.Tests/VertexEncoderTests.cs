using JackAll.Tools.Xbg;

namespace JackAll.Tests;

/// <summary>
/// Every vertex component decoded to float space and packed back has to return what shipped.
/// </summary>
/// <remarks>
/// The container round trip carries the vertex block through untouched, so it says nothing about
/// this. What this proves is the other half: that the float values an editor is handed quantise
/// back exactly, so an untouched part survives a decode-and-rebuild and a changed one only moves
/// where it was changed. Anything failing here is a component an exporter would silently corrupt.
/// </remarks>
public sealed class VertexEncoderTests
{
    [Fact]
    public void Every_component_quantises_back_to_what_shipped()
    {
        Dictionary<string, int> damaged = new(StringComparer.Ordinal);
        int buffers = 0;
        int files = 0;

        foreach (string path in Fc2Corpus.Find(".xbg"))
        {
            files++;
            XbgFile model = XbgFile.Parse(File.ReadAllBytes(path));
            VertexScales scales = VertexScales.Of(model);

            foreach (XbgLod lod in model.Lods)
            {
                foreach (XbgVertexBuffer buffer in lod.VertexBuffers)
                {
                    if (buffer.VertexCount == 0)
                    {
                        continue;
                    }

                    buffers++;
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
                        if (!produced.Components[name].AsSpan().SequenceEqual(original))
                        {
                            damaged[name] = damaged.GetValueOrDefault(name) + 1;
                        }
                    }
                }
            }
        }

        Assert.True(buffers > 0 || !Fc2Corpus.Present, "No vertex buffer was examined.");
        Assert.True(
            damaged.Count == 0,
            $"{buffers} buffers in {files} files. Components that moved: "
            + string.Join(", ", damaged.Select(pair => $"{pair.Key} in {pair.Value} buffers")));
    }

    /// <summary>
    /// With no template, a vertex falls back to the constants every shipped one carries - which is
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

    [Fact]
    [Trait("Category", "RequiresFixture")]
    public void The_corpus_was_actually_found()
        => Assert.True(Fc2Corpus.Find(".xbg").Any(), Fc2Corpus.MissingMessage(".xbg"));
}
