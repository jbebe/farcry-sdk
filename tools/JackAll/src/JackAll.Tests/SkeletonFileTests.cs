using JackAll.Tools.Skeleton;

namespace JackAll.Tests;

/// <summary>
/// The `.skeleton` gate: re-serialising every shipped rig has to return its bytes.
/// </summary>
/// <remarks>
/// <c>Write(Parse(x)) == x</c> over the retail set proves the reader and the writer at once, which
/// matters here because the format has no chunk lengths - a bone's constraint payload is sized by a
/// one-byte kind, so a wrong width silently reinterprets every bone after it rather than throwing.
/// The Python codec this was ported from reaches 81 of 81; anything less is a port defect.
/// </remarks>
public sealed class SkeletonFileTests
{
    private const string Extension = ".skeleton";

    [Fact]
    public void Reserialises_every_shipped_rig_byte_for_byte()
    {
        List<string> failures = [];
        int checked_ = 0;

        foreach (string path in Fc2Corpus.Find(Extension))
        {
            checked_++;
            byte[] original = File.ReadAllBytes(path);
            try
            {
                byte[] rewritten = SkeletonFile.Parse(original).Write();
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
            failures.Count == 0,
            $"{checked_ - failures.Count}/{checked_} rigs round-tripped. First failures:{Environment.NewLine}"
            + string.Join(Environment.NewLine, failures.Take(5)));
    }

    /// <summary>
    /// The human rig, as a named check that the decode means something rather than merely surviving.
    /// </summary>
    [Fact]
    public void The_character_rig_parses_to_the_recorded_shape()
    {
        string? path = Fc2Corpus.Find(Extension)
            .FirstOrDefault(p => Path.GetFileName(p).Equals("pelvis_ref.skeleton", StringComparison.OrdinalIgnoreCase));
        if (path is null)
        {
            return;
        }

        SkeletonFile skeleton = SkeletonFile.Parse(File.ReadAllBytes(path));

        Assert.Equal(119, skeleton.Bones.Count);
        Assert.Equal(30, skeleton.Handles.Count);

        // Translation is animated on exactly these two, which is what a .mab's separate
        // translation masks have to agree with.
        string[] translating = [.. skeleton.TranslationBoneIds
            .Where(id => id != SkeletonFile.NoBone)
            .Select(id => skeleton.Bones[id].Name)];
        Assert.Equal(["Pelvis", "Camera"], translating);

        Assert.NotNull(skeleton.BoneByName("R Hand"));
    }

    /// <summary>Sibling links are derived from each bone's parent, so rebuilding must be a no-op.</summary>
    [Fact]
    public void Rebuilding_the_hierarchy_reproduces_the_shipped_links()
    {
        foreach (string path in Fc2Corpus.Find(Extension))
        {
            SkeletonFile skeleton = SkeletonFile.Parse(File.ReadAllBytes(path));
            (ushort, ushort)[] before = [.. skeleton.Bones.Select(b => (b.FirstChild, b.NextSibling))];

            skeleton.RebuildHierarchy();

            (ushort, ushort)[] after = [.. skeleton.Bones.Select(b => (b.FirstChild, b.NextSibling))];
            Assert.True(before.SequenceEqual(after), $"{Path.GetFileName(path)}: links disagree");
        }
    }

    [Fact]
    [Trait("Category", "RequiresFixture")]
    public void The_corpus_was_actually_found()
        => Assert.True(Fc2Corpus.Find(Extension).Any(), Fc2Corpus.MissingMessage(Extension));
}
