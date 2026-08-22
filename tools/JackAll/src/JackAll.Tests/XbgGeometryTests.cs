using JackAll.Tools.Xbg;

namespace JackAll.Tests;

/// <summary>
/// The geometry gate: every LOD's blocks regenerated from per-cluster geometry, and the file
/// required back.
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
    [Fact]
    public void Regenerating_every_lod_reproduces_the_file()
    {
        List<string> failures = [];
        int checkedFiles = 0;
        int clusters = 0;

        foreach (string path in Fc2Corpus.Find(".xbg"))
        {
            checkedFiles++;
            byte[] original = File.ReadAllBytes(path);
            try
            {
                XbgFile model = XbgFile.Parse(original);
                foreach (XbgLod lod in model.Lods)
                {
                    List<ClusterGeometry> geometries = XbgGeometry.ReadLod(model, lod);
                    clusters += geometries.Count;
                    lod.VertexData = [];
                    lod.IndexData = [];
                    foreach (XbgVertexBuffer buffer in lod.VertexBuffers)
                    {
                        buffer.Offset = uint.MaxValue;
                        buffer.VertexCount = 0;
                    }
                    XbgGeometry.WriteLod(model, lod, geometries);
                }

                byte[] rewritten = model.Write();
                if (!rewritten.AsSpan().SequenceEqual(original))
                {
                    failures.Add(Fc2Corpus.DescribeDifference(path, original, rewritten));
                }
            }
            catch (Exception error)
            {
                failures.Add($"{Path.GetFileName(path)}: {error.Message}");
            }
        }

        Assert.True(
            checkedFiles > 0 || !Fc2Corpus.Present,
            $"{Fc2Corpus.Root} holds no *.xbg, so this gate asserted nothing.");

        Assert.True(
            failures.Count == 0,
            $"{checkedFiles - failures.Count}/{checkedFiles} meshes rebuilt from regenerated "
            + $"geometry ({clusters} clusters). First failures:{Environment.NewLine}"
            + string.Join(Environment.NewLine, failures.Take(5)));
    }

    /// <summary>
    /// Every component decodes to file precision and back, which is what an editor handed float
    /// values depends on.
    /// </summary>
    [Fact]
    public void Every_vertex_buffer_survives_unpack_and_pack()
    {
        List<string> failures = [];
        int buffers = 0;

        foreach (string path in Fc2Corpus.Find(".xbg"))
        {
            XbgFile model = XbgFile.Parse(File.ReadAllBytes(path));
            foreach (XbgLod lod in model.Lods)
            {
                for (int index = 0; index < lod.VertexBuffers.Count; index++)
                {
                    XbgVertexBuffer buffer = lod.VertexBuffers[index];
                    buffers++;
                    VertexStream stream = VertexStream.Unpack(lod.VertexData, buffer, (int)buffer.VertexCount);
                    ReadOnlySpan<byte> original =
                        lod.VertexData.AsSpan((int)buffer.Offset, (int)(buffer.VertexCount * buffer.Stride));
                    if (!stream.Pack().AsSpan().SequenceEqual(original))
                    {
                        failures.Add($"{Path.GetFileName(path)}: buffer {index} does not survive unpack/pack");
                    }
                }
            }
        }

        Assert.True(buffers > 0 || !Fc2Corpus.Present, "No vertex buffer was examined.");
        Assert.True(
            failures.Count == 0,
            $"{buffers - failures.Count}/{buffers} buffers survived. First failures:{Environment.NewLine}"
            + string.Join(Environment.NewLine, failures.Take(5)));
    }

    /// <summary>
    /// Only three vertex format flag words ship, and every one of them stores int16 positions - so
    /// the float and half decode paths are unreachable against retail data.
    /// </summary>
    [Fact]
    public void Every_shipped_buffer_stores_int16_positions()
    {
        HashSet<uint> formats = [];
        foreach (string path in Fc2Corpus.Find(".xbg"))
        {
            XbgFile model = XbgFile.Parse(File.ReadAllBytes(path));
            foreach (XbgVertexBuffer buffer in model.Lods.SelectMany(lod => lod.VertexBuffers))
            {
                formats.Add(buffer.Flags);
                Assert.True(
                    (buffer.Flags & XbgFile.PosInt16) != 0,
                    $"{Path.GetFileName(path)}: flags 0x{buffer.Flags:X} are not int16 positions");
            }
        }

        Assert.True(formats.Count is 0 or 3, $"Expected three shipped formats, found {formats.Count}.");
    }

    [Fact]
    [Trait("Category", "RequiresFixture")]
    public void The_corpus_was_actually_found()
        => Assert.True(Fc2Corpus.Find(".xbg").Any(), Fc2Corpus.MissingMessage(".xbg"));
}
