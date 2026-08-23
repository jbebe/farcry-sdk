using System.Text.Json;
using JackAll.Tools.Fc2Model;

namespace JackAll.Tests;

/// <summary>
/// The pack's material gate: decode every shipped material to the format-free document a
/// <c>.fc2model</c> carries, build it back, and require the file.
/// </summary>
/// <remarks>
/// It runs the document through JSON on the way, because that is how it actually travels - so a
/// property the serialiser drops or reorders fails here rather than in a mod nobody can explain.
/// </remarks>
public sealed class MaterialDocumentTests
{
    // The options a pack actually writes with, so this gate covers the shape that ships rather
    // than a shape only it uses.
    private static readonly JsonSerializerOptions Json = Fc2ModelJson.Compact;

    [Fact]
    public void Every_shipped_material_survives_the_trip_through_json()
    {
        List<string> failures = [];
        int checkedFiles = 0;

        foreach (string path in Fc2Corpus.Find(".xbm"))
        {
            checkedFiles++;
            byte[] original = File.ReadAllBytes(path);
            try
            {
                MaterialDocument document = MaterialDocument.Parse(original);
                string text = JsonSerializer.Serialize(document, Json);
                byte[] produced = JsonSerializer.Deserialize<MaterialDocument>(text, Json)!.ToXbm();
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
            $"{Fc2Corpus.Root} holds no *.xbm, so this gate asserted nothing.");

        Assert.True(
            failures.Count == 0,
            $"{checkedFiles - failures.Count}/{checkedFiles} materials survived. First failures:"
            + Environment.NewLine + string.Join(Environment.NewLine, failures.Take(5)));
    }

    /// <summary>
    /// The duplicate key one shipped material carries has to survive JSON, which is the case an
    /// ordered list exists for.
    /// </summary>
    [Fact]
    public void A_repeated_key_survives_serialisation()
    {
        int repeaters = 0;
        foreach (string path in Fc2Corpus.Find(".xbm"))
        {
            byte[] original = File.ReadAllBytes(path);
            MaterialDocument document = MaterialDocument.Parse(original);
            int keys = document.Textures.Select(t => t.Slot)
                .Concat(document.Floats.Select(f => f.Key))
                .Concat(document.Integers.Select(i => i.Key))
                .Count();
            int distinct = document.Textures.Select(t => t.Slot)
                .Concat(document.Floats.Select(f => f.Key))
                .Concat(document.Integers.Select(i => i.Key))
                .Distinct(StringComparer.Ordinal)
                .Count();
            if (keys == distinct)
            {
                continue;
            }

            repeaters++;
            string text = JsonSerializer.Serialize(document, Json);
            Assert.True(
                JsonSerializer.Deserialize<MaterialDocument>(text, Json)!.ToXbm().AsSpan().SequenceEqual(original),
                $"{Path.GetFileName(path)} repeats a key and did not survive JSON");
        }

        Assert.True(
            repeaters > 0 || !Fc2Corpus.Present,
            "No shipped material repeated a key, so this never exercised the duplicate.");
    }

    [Fact]
    [Trait("Category", "RequiresFixture")]
    public void The_corpus_was_actually_found()
        => Assert.True(Fc2Corpus.Find(".xbm").Any(), Fc2Corpus.MissingMessage(".xbm"));
}
