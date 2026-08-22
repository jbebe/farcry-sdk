using JackAll.Tools.Xbg;

namespace JackAll.Tests;

/// <summary>
/// The authoring gate: build every shipped mesh from decoded content alone and require the file
/// back.
/// </summary>
/// <remarks>
/// <see cref="XbgFileTests"/> round-trips a container it parsed, and <see cref="XbgGeometryTests"/>
/// regenerates the geometry inside one. This goes further: it constructs a brand-new
/// <see cref="XbgFile"/> holding only what a format-free pack would carry - names, transforms,
/// bounds, materials, palettes and geometry - blanks every field the container derives, and derives
/// them back.
/// <para>
/// Passing is what says a mesh can be authored rather than transplanted into a donor, which is the
/// difference between a modeller being able to add a part or an LOD and being stuck with whatever
/// the donor happened to have.
/// </para>
/// </remarks>
public sealed class XbgAuthorTests
{
    /// <summary>Values nothing in the container derives, so a pack has to carry them.</summary>
    private const string Carried =
        "HeaderWords[0] and the material list's trailing word";

    [Fact]
    public void Builds_every_shipped_mesh_from_decoded_content_alone()
    {
        List<string> failures = [];
        int checkedFiles = 0;

        foreach (string path in Fc2Corpus.Find(".xbg"))
        {
            checkedFiles++;
            byte[] original = File.ReadAllBytes(path);
            try
            {
                byte[] produced = Originate(XbgFile.Parse(original));
                if (!produced.AsSpan().SequenceEqual(original))
                {
                    failures.Add(Fc2Corpus.DescribeDifference(path, original, produced));
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
            $"{checkedFiles - failures.Count}/{checkedFiles} meshes built from decoded content "
            + $"alone, carrying only {Carried}. First failures:{Environment.NewLine}"
            + string.Join(Environment.NewLine, failures.Take(5)));
    }

    /// <summary>
    /// A new container holding this one's content, with every derivable field blanked so
    /// <see cref="XbgFile.Derive"/> has to produce it rather than inherit it.
    /// </summary>
    private static byte[] Originate(XbgFile source)
    {
        var built = new XbgFile
        {
            HeaderWords = [source.HeaderWords[0], 0, 0, 0, 0],
            Materials = [.. source.Materials],
            MaterialWord = source.MaterialWord,
            Box = [.. source.Box],
            Sphere = [.. source.Sphere],
            PosCompress = [.. source.PosCompress],
            UvCompress = [.. source.UvCompress],
        };

        foreach (XbgNode node in source.Nodes)
        {
            built.Nodes.Add(new XbgNode
            {
                Name = node.Name,
                Parent = node.Parent,
                Rotation = [.. node.Rotation],
                Translation = [.. node.Translation],
                Scale = [.. node.Scale],
                SkinIndex = node.SkinIndex,
                Extent = node.Extent,
                // Blanked: Derive has to supply these from the name and the node order.
                NameHash = 0xDEADBEEF,
                FirstChild = 0,
                NextSibling = 0,
                Weight = 0.0f,
            });
        }

        foreach (float[] matrix in source.BindMatrices)
        {
            built.BindMatrices.Add([.. matrix]);
        }

        foreach (XbgPart part in source.Parts)
        {
            var fresh = new XbgPart
            {
                Name = part.Name,
                LodMetric = part.LodMetric,
                Bounds = [.. part.Bounds],
                Lod = -1,
                Reserved = 0xDEADBEEF,
            };
            foreach (XbgCluster cluster in part.Clusters)
            {
                fresh.Clusters.Add(new XbgCluster
                {
                    MaterialIndex = cluster.MaterialIndex,
                    Flags = cluster.Flags,
                    Palette = [.. cluster.Palette],
                    // Blanked: the geometry writer supplies these.
                    FaceCount = 0,
                    Stride = 0,
                    VertexCount = 0,
                });
            }
            built.Parts.Add(fresh);
        }

        foreach (XbgPartRef reference in source.PartRefs)
        {
            built.PartRefs.Add(new XbgPartRef { NameHash = 0xDEADBEEF, Node = reference.Node });
        }

        foreach (XbgLod lod in source.Lods)
        {
            built.Lods.Add(new XbgLod
            {
                Distance = lod.Distance,
                VertexBuffers = [.. lod.VertexBuffers.Select(b => new XbgVertexBuffer
                {
                    Flags = b.Flags,
                    Stride = b.Stride,
                    VertexCount = 0,
                    Offset = uint.MaxValue,
                })],
                Submeshes = [.. lod.Submeshes.Select(s => new XbgSubmeshRef
                {
                    Buffer = s.Buffer,
                    Part = s.Part,
                    Cluster = s.Cluster,
                    IndexOffset = 0,
                    Trailing = [0, 0, 0],
                })],
                VertexData = [],
                IndexData = [],
            });
        }

        foreach (XbgChunk chunk in source.Chunks)
        {
            built.Chunks.Add(new XbgChunk { Tag = chunk.Tag, Word0 = 0xDEADBEEF, Raw = chunk.Raw });
        }

        built.Derive();
        foreach ((XbgLod from, XbgLod to) in source.Lods.Zip(built.Lods))
        {
            XbgGeometry.WriteLod(built, to, XbgGeometry.ReadLod(source, from));
        }
        return built.Write();
    }
}
