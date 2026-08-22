using JackAll.Tools.Fc2Model;
using JackAll.Tools.Xbg;

namespace JackAll.Tests;

/// <summary>
/// The pack's mesh gate: decode every shipped mesh to the format-free document a
/// <c>.fc2model</c> carries, build it back, and require the file.
/// </summary>
/// <remarks>
/// This is the claim the whole pack design rests on - that a mesh can travel as names, transforms,
/// bounds and float-space geometry, with no Dunia bytes except the two words nothing derives and the
/// bodies of chunks nothing decodes. If it holds, an editor can carry no format code and still hand
/// back something the game loads.
/// </remarks>
public sealed class MeshDocumentTests
{
    [Fact]
    public void Every_shipped_mesh_survives_the_trip_through_the_document()
    {
        List<string> failures = [];
        int checkedFiles = 0;

        foreach (string path in Fc2Corpus.Find(".xbg"))
        {
            checkedFiles++;
            byte[] original = File.ReadAllBytes(path);
            try
            {
                byte[] produced = MeshDocument.From(XbgFile.Parse(original)).ToXbg().Write();
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
            $"{checkedFiles - failures.Count}/{checkedFiles} meshes survived. First failures:"
            + Environment.NewLine + string.Join(Environment.NewLine, failures.Take(5)));
    }

    /// <summary>
    /// The document holds no Dunia bytes beyond the two words nothing derives and the chunks nothing
    /// decodes - which is what makes it safe for an editor that knows no formats.
    /// </summary>
    [Fact]
    public void The_document_carries_no_container_bookkeeping()
    {
        string? path = Fc2Corpus.Find(".xbg")
            .FirstOrDefault(p => Path.GetFileName(p).Equals("ak47.xbg", StringComparison.OrdinalIgnoreCase));
        if (path is null)
        {
            return;
        }

        MeshDocument document = MeshDocument.From(XbgFile.Parse(File.ReadAllBytes(path)));

        // Only the two rare chunks travel as bytes; the ten mandatory ones are all decoded.
        Assert.All(document.Chunks, chunk => Assert.Empty(chunk.Body));

        Assert.Equal(11, document.Parts.Count);
        Assert.Equal(9, document.Nodes.Count);
        Assert.Equal(5, document.Lods.Count);
        Assert.Contains(document.Nodes, node => node.Name == "FX_FIRE");

        // Geometry is float space: a rifle is under a couple of metres in every direction.
        foreach (MeshGeometry geometry in document.Lods[0].Geometry)
        {
            Assert.All(geometry.Vertices.Positions!, p =>
                Assert.True(Math.Abs(p.X) < 2.0f && Math.Abs(p.Y) < 2.0f && Math.Abs(p.Z) < 2.0f));
        }
    }

    [Fact]
    [Trait("Category", "RequiresFixture")]
    public void The_corpus_was_actually_found()
        => Assert.True(Fc2Corpus.Find(".xbg").Any(), Fc2Corpus.MissingMessage(".xbg"));
}
