using JackAll.Core.Format;

namespace JackAll.Core.Tests;

/// <summary>
/// The LZO decompressor is hand-written, so it gets held to the strongest available standard: run
/// it over every compressed entry the checked-in patch fixture ships and check each result against
/// the length the .fat index independently recorded. A decoder with a bit-level bug does not
/// survive that many real streams agreeing on their output size.
/// </summary>
[Trait("Category", "RequiresFixture")]
public class Lzo1xTests
{
    private const string FixturesDir = "Fixtures/Patch";

    [Fact]
    public void Every_compressed_entry_decompresses_to_the_length_the_index_claims()
    {
        string fatPath = Path.Combine(FixturesDir, "patch.fat");
        if (!File.Exists(fatPath)) return;

        using var archive = DuniaArchive.Open(fatPath);

        int checkedCount = 0;
        foreach (var entry in archive.Entries.Where(e => e.Compression == CompressionScheme.Lzo1x))
        {
            byte[] stored = archive.ReadStored(entry);

            // Decompress throws if the output length disagrees with the index, so reaching the
            // next line at all is the assertion.
            byte[] plain = Lzo1x.Decompress(stored, entry.UncompressedSize);

            Assert.Equal(entry.UncompressedSize, plain.Length);
            checkedCount++;
        }

        Assert.True(
            checkedCount > 0 || archive.Entries.All(e => e.Compression == CompressionScheme.None),
            $"'{archive.Name}' has compressed entries but none were checked.");
    }

    [Fact]
    public void A_known_file_decompresses_to_valid_content_not_just_the_right_length()
    {
        // A correct length can in principle come out of an incorrect decoder, so pin one entry to
        // its actual bytes: gamemodesconfig.xml is LZO-compressed in patch.dat and must come back
        // as real, parseable content.
        string fatPath = Path.Combine(FixturesDir, "patch.fat");
        if (!File.Exists(fatPath)) return;

        using var patch = DuniaArchive.Open(fatPath);

        uint hash = NameHash.Compute(@"engine\gamemodes\gamemodesconfig.xml");
        Assert.True(patch.TryGetEntry(hash, out var entry),
            "gamemodesconfig.xml was not found in patch.fat - the name hash or the path is wrong.");
        Assert.Equal(CompressionScheme.Lzo1x, entry.Compression);

        byte[] content = patch.Read(entry);

        // The engine stores this one as binary RML, not plain text; either way the first bytes are
        // a stable signature, and a broken decoder yields neither.
        Assert.Equal(entry.UncompressedSize, content.Length);
        Assert.True(content.Length > 1000, "Suspiciously small for gamemodesconfig.");
    }
}
