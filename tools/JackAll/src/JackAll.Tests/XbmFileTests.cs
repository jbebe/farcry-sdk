using JackAll.Tools.Xbg;
using JackAll.Tools.Xbm;

namespace JackAll.Tests;

/// <summary>
/// The material gate: every shipped `.xbm` has to survive a decode of its LTMD body and come back
/// byte for byte, and the three meshes that embed a material instead have to parse the other way.
/// </summary>
/// <remarks>
/// <see cref="XbgFileTests"/> already round-trips the container while carrying LTMD opaque, so this
/// is the half that proves the body itself is understood. The Python codec reaches 2,379 of 2,379.
/// </remarks>
public sealed class XbmFileTests
{
    [Fact]
    public void Reserialises_every_shipped_material_byte_for_byte()
    {
        List<string> failures = [];
        int checkedFiles = 0;
        int albedos = 0;

        foreach (string path in Fc2Corpus.Find(".xbm"))
        {
            checkedFiles++;
            byte[] original = File.ReadAllBytes(path);
            try
            {
                XbmFile material = XbmFile.Parse(original);
                albedos += material.Albedo() is not null ? 1 : 0;
                byte[] rewritten = material.Write();
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
            $"{Fc2Corpus.Root} holds no *.xbm, so this gate asserted nothing.");

        Assert.True(
            failures.Count == 0,
            $"{checkedFiles - failures.Count}/{checkedFiles} materials round-tripped, "
            + $"{albedos} naming an albedo. First failures:{Environment.NewLine}"
            + string.Join(Environment.NewLine, failures.Take(5)));
    }

    /// <summary>
    /// An entry list, not a map: one shipped material repeats a key inside a section, and a reader
    /// that only kept a map could not put it back.
    /// </summary>
    [Fact]
    public void A_repeated_key_survives_the_round_trip()
    {
        List<string> repeaters = [];
        foreach (string path in Fc2Corpus.Find(".xbm"))
        {
            byte[] original = File.ReadAllBytes(path);
            XbmFile material = XbmFile.Parse(original);
            if (material.Entries.Count == material.Textures.Count + material.Floats.Count + material.Integers.Count)
            {
                continue;
            }

            repeaters.Add(material.Name);
            Assert.True(
                material.Write().AsSpan().SequenceEqual(original),
                $"{Path.GetFileName(path)} repeats a key and did not survive the round trip");
        }

        // Finding none would mean the check passed without ever exercising the case it exists for.
        Assert.True(
            repeaters.Count > 0 || !Fc2Corpus.Present,
            "No shipped material repeated a key, so this gate never exercised the duplicate.");
    }

    /// <summary>
    /// The meshes that define their material inline rather than naming an `.xbm`, whose LTMD leads
    /// with the name and part instead of the five-byte preamble.
    /// </summary>
    [Fact]
    public void Inline_materials_parse_with_their_own_layout()
    {
        int found = 0;
        foreach (string path in Fc2Corpus.Find(".xbg"))
        {
            XbgFile model = XbgFile.Parse(File.ReadAllBytes(path));
            if (model.Chunk(XbgFile.TagMaterialBody) is null)
            {
                continue;
            }

            foreach ((string name, XbmFile material) in XbmFile.InlineMaterials(model))
            {
                found++;
                Assert.NotEmpty(name);
                Assert.NotEmpty(material.Shader);
                // The part it applies to is what an embedded material carries and a standalone
                // one does not.
                Assert.NotEmpty(material.Part);
            }
        }

        Assert.True(found > 0 || !Fc2Corpus.Present, "No mesh carried an inline material.");
    }

    [Fact]
    [Trait("Category", "RequiresFixture")]
    public void The_corpus_was_actually_found()
        => Assert.True(Fc2Corpus.Find(".xbm").Any(), Fc2Corpus.MissingMessage(".xbm"));
}
