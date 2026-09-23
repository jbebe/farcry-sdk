using JackAll.Core.Format;

namespace JackAll.Tests;

/// <summary>
/// Everything the tool does — every override, every rebuilt patch — rests on this bit-packing being
/// exactly right, and the only authority on "right" is what Ubisoft actually shipped, so the format
/// tests run against a shipped <c>patch.fat</c>. A synthetic fixture would just test our own
/// assumptions back at us.
/// </summary>
public class FatArchiveTests
{
    /// <summary>A shipped patch archive, which every test built on a real install stands up from.</summary>
    public const string Fat = "Patch/patch.fat";
    public const string Dat = "Patch/patch.dat";

    [Fact]
    [Trait("Category", "RequiresFixture")]
    public void The_fixtures_were_actually_found() => Fixture.AssertPresent(Fat, Dat);

    [Fact]
    public void Reserializing_a_shipped_fat_reproduces_it_byte_for_byte()
    {
        if (Fixture.Locate(Fat) is not { } fatPath) return;

        byte[] original = File.ReadAllBytes(fatPath);

        var archive = FatArchive.Read(fatPath);

        using var rewritten = new MemoryStream();
        archive.Write(rewritten);

        Assert.Equal(original, rewritten.ToArray());
    }

    [Fact]
    public void Shipped_entries_are_sorted_by_hash_and_obey_the_engines_invariants()
    {
        if (Fixture.Locate(Fat) is not { } fatPath) return;

        var archive = FatArchive.Read(fatPath);

        // The engine binary-searches the table; if this ever failed, our writer's sort would be
        // imposing an order the game doesn't actually use.
        Assert.Equal(
            archive.Entries.Select(e => e.Hash).Order().ToArray(),
            archive.Entries.Select(e => e.Hash).ToArray());

        // The "uncompressed size is 0 when there's no compression" rule, verified against real data
        // rather than taken on faith from Gibbed's source.
        foreach (var entry in archive.Entries.Where(e => e.Compression == CompressionScheme.None))
        {
            Assert.Equal(0, entry.UncompressedSize);
        }
    }

    [Fact]
    public void FromEntries_sorts_by_hash_so_callers_cannot_produce_an_unsearchable_index()
    {
        var archive = FatArchive.FromEntries(
        [
            new FatEntry(0xFFFF0000, Offset: 100, CompressedSize: 10, UncompressedSize: 0, CompressionScheme.None),
            new FatEntry(0x0000FFFF, Offset: 0, CompressedSize: 20, UncompressedSize: 0, CompressionScheme.None),
        ]);

        Assert.Equal([0x0000FFFFu, 0xFFFF0000u], archive.Entries.Select(e => e.Hash));
    }

    [Fact]
    public void Writing_an_uncompressed_entry_with_a_nonzero_uncompressed_size_is_rejected()
    {
        // This is the mistake that produces a subtly-wrong archive the game reads as garbage,
        // rather than one that fails loudly — so it has to be impossible to make.
        var archive = FatArchive.FromEntries(
        [
            new FatEntry(1, Offset: 0, CompressedSize: 10, UncompressedSize: 10, CompressionScheme.None),
        ]);

        using var stream = new MemoryStream();
        Assert.Throws<InvalidOperationException>(() => archive.Write(stream));
    }

    [Theory]
    [InlineData("worlds/world1/generated/entitylibrary.fcb")]
    [InlineData(@"worlds\world1\generated\entitylibrary.fcb")]
    [InlineData(@"WORLDS\World1\\Generated\EntityLibrary.FCB")]
    public void Name_hashing_is_insensitive_to_case_and_separator_style(string path)
    {
        // All three spellings must land on one hash, or a mod authored on one machine silently
        // fails to apply on another.
        Assert.Equal(
            NameHash.Compute("worlds/world1/generated/entitylibrary.fcb"),
            NameHash.Compute(path));
    }
}
