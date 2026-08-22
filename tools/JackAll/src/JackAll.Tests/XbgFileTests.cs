using JackAll.Tools.Xbg;

namespace JackAll.Tests;

/// <summary>
/// The container gate: re-serialising every shipped `.xbg` and `.xbm` has to return its bytes.
/// </summary>
/// <remarks>
/// The writer regenerates every chunk size, payload size, sub-chunk count and the header's own byte
/// count rather than echoing what it parsed, so a pass means the framing is genuinely understood.
/// An `.xbm` is the same container with a material chunk and no geometry, which is why both run
/// here. The Python codec reaches 3,133 and 2,379; anything less is a port defect.
/// </remarks>
public sealed class XbgFileTests
{
    [Theory]
    [InlineData(".xbg", "meshes")]
    [InlineData(".xbm", "materials")]
    public void Reserialises_every_shipped_file_byte_for_byte(string extension, string what)
    {
        List<string> failures = [];
        int checkedFiles = 0;

        foreach (string path in Fc2Corpus.Find(extension))
        {
            checkedFiles++;
            byte[] original = File.ReadAllBytes(path);
            try
            {
                byte[] rewritten = XbgFile.Parse(original).Write();
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

        // Without this, a corpus holding none of this extension passes on zero files.
        Assert.True(
            checkedFiles > 0 || !Fc2Corpus.Present,
            $"{Fc2Corpus.Root} holds no *{extension}, so this gate asserted nothing.");

        Assert.True(
            failures.Count == 0,
            $"{checkedFiles - failures.Count}/{checkedFiles} {what} round-tripped. First failures:"
            + Environment.NewLine + string.Join(Environment.NewLine, failures.Take(5)));
    }

    /// <summary>
    /// The AK-47, as a named check that the decode means something rather than merely surviving.
    /// </summary>
    [Fact]
    public void The_rifle_parses_to_the_recorded_shape()
    {
        string? path = Fc2Corpus.Find(".xbg")
            .FirstOrDefault(p => Path.GetFileName(p).Equals("ak47.xbg", StringComparison.OrdinalIgnoreCase));
        if (path is null)
        {
            return;
        }

        XbgFile model = XbgFile.Parse(File.ReadAllBytes(path));

        Assert.Equal(XbgFile.VersionFc2, model.Version);
        Assert.Equal(5, model.Lods.Count);
        Assert.Equal(11, model.Parts.Count);
        Assert.Equal(9, model.Nodes.Count);

        // DIKS carries one entry per part, always.
        Assert.Equal(model.Parts.Count, model.PartRefs.Count);
        Assert.Contains(model.Nodes, node => node.Name == "FX_FIRE");

        // Every part's LOD tier is the _LODn suffix on its own name.
        foreach (XbgPart part in model.Parts)
        {
            int suffix = part.Name.LastIndexOf("_LOD", StringComparison.OrdinalIgnoreCase);
            Assert.True(suffix >= 0, $"{part.Name} carries no _LOD suffix");
            Assert.Equal(int.Parse(part.Name[(suffix + 4)..]), part.Lod);
        }
    }

    /// <summary>
    /// A static cluster's palette is all empty and a skinned one is a contiguous prefix of node
    /// indices then padding - the community rule that a skinned palette never holds -1 is wrong.
    /// </summary>
    [Fact]
    public void Bone_palettes_are_a_prefix_then_padding()
    {
        foreach (string path in Fc2Corpus.Find(".xbg"))
        {
            XbgFile model = XbgFile.Parse(File.ReadAllBytes(path));
            foreach (XbgCluster cluster in model.Parts.SelectMany(part => part.Clusters))
            {
                int used = cluster.Palette.Count(slot => slot != XbgFile.EmptySlot);
                Assert.True(
                    cluster.Palette.Take(used).All(slot => slot != XbgFile.EmptySlot)
                    && cluster.Palette.Skip(used).All(slot => slot == XbgFile.EmptySlot),
                    $"{Path.GetFileName(path)}: palette is not a prefix then padding");
                Assert.True(
                    cluster.IsSkinned || used == 0,
                    $"{Path.GetFileName(path)}: a static cluster names {used} bones");
            }
        }
    }

    [Fact]
    [Trait("Category", "RequiresFixture")]
    public void The_corpus_was_actually_found()
        => Assert.True(Fc2Corpus.Find(".xbg").Any(), Fc2Corpus.MissingMessage(".xbg"));
}
