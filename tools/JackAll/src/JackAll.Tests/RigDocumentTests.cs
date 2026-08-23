using System.Text.Json;
using JackAll.Tools.Fc2Model;
using JackAll.Tools.Skeleton;

namespace JackAll.Tests;

/// <summary>
/// The pack's rig gate: every shipped <c>.skeleton</c> through JSON and back has to return its bytes.
/// </summary>
/// <remarks>
/// A rig needs no separate document type - <see cref="SkeletonFile"/> is already decoded, carrying
/// names, rest poses, constraints and sockets rather than anything about framing. Serialising it
/// directly is what the pack does, so this is the shape that actually travels.
/// </remarks>
public sealed class RigDocumentTests
{
    // The options a pack actually writes with, so this gate covers the shape that ships rather
    // than a shape only it uses.
    private static readonly JsonSerializerOptions Json = Fc2ModelJson.Compact;

    [Fact]
    public void Every_shipped_rig_survives_the_trip_through_json()
    {
        List<string> failures = [];
        int checkedFiles = 0;

        foreach (string path in Fc2Corpus.Find(".skeleton"))
        {
            checkedFiles++;
            byte[] original = File.ReadAllBytes(path);
            try
            {
                string text = JsonSerializer.Serialize(SkeletonFile.Parse(original), Json);
                byte[] produced = JsonSerializer.Deserialize<SkeletonFile>(text, Json)!.Write();
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
            $"{Fc2Corpus.Root} holds no *.skeleton, so this gate asserted nothing.");

        Assert.True(
            failures.Count == 0,
            $"{checkedFiles - failures.Count}/{checkedFiles} rigs survived. First failures:"
            + Environment.NewLine + string.Join(Environment.NewLine, failures.Take(5)));
    }

    [Fact]
    [Trait("Category", "RequiresFixture")]
    public void The_corpus_was_actually_found()
        => Assert.True(Fc2Corpus.Find(".skeleton").Any(), Fc2Corpus.MissingMessage(".skeleton"));
}
