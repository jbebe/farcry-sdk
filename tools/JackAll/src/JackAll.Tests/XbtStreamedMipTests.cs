using JackAll.Tools.Xbt;

namespace JackAll.Tests;

/// <summary>
/// The <c>_mip0.xbt</c> companion: a texture's real top level lives in a second file, and the file
/// that names it starts one level down. Against retail fixtures, because the only authority on how
/// the pair fits together is a pair the game shipped.
/// </summary>
public class XbtStreamedMipTests
{
    // A texture whose top level lives in a _mip0 companion.
    private const string Streamed = "XbtStreamed/stiresjunk01_d.xbt";

    // Streamed's top level.
    private const string Companion = "XbtStreamed/stiresjunk01_d_mip0.xbt";

    // A DXT1 texture of the same 256 side that keeps its top level in one file.
    internal const string Standalone = "XbtStreamed/desert_sand_still_d.xbt";

    private static readonly Func<string, byte[]?> ReadByPath = Fixture.ByFileName("XbtStreamed");

    [Fact]
    [Trait("Category", "RequiresFixture")]
    public void The_fixtures_were_actually_found() => Fixture.AssertPresent(Streamed, Companion, Standalone);

    [Fact]
    public void A_streamed_texture_names_its_companion_in_its_header()
    {
        if (Fixture.Read(Streamed) is not { } xbt) return;

        (byte[] header, _) = XbtTexture.Split(xbt);

        Assert.Equal(@"graphics\terrain\_textures\savannah\stiresjunk01_d_mip0.xbt",
            XbtTexture.CompanionPath(header));
    }

    [Fact]
    public void A_texture_that_keeps_its_top_level_names_no_companion()
    {
        if (Fixture.Read(Standalone) is not { } xbt) return;

        (byte[] header, _) = XbtTexture.Split(xbt);

        Assert.Null(XbtTexture.CompanionPath(header));
    }

    /// <summary>The point of the whole exercise: reading the named file alone gives half the
    /// texture in each axis, and nothing about it looks wrong until it fills the screen.</summary>
    [Fact]
    public void Reading_a_streamed_texture_puts_the_companions_level_back_on_top()
    {
        if (Fixture.Read(Streamed) is not { } xbt) return;

        (_, byte[] dds) = XbtTexture.Split(xbt);
        DdsSurface alone = DdsSurface.TryParse(dds)!;
        DdsSurface whole = XbtSurface.TryRead(xbt, ReadByPath)!;

        Assert.Equal(256, alone.Width);
        Assert.Equal(512, whole.Width);
        Assert.Equal(512, whole.Height);
        Assert.Equal(alone.Mips.Count + 1, whole.Mips.Count);
        Assert.Equal(alone.Mips[0], whole.Mips[1]);
    }

    [Fact]
    public void A_texture_with_no_companion_reads_exactly_as_it_is()
    {
        if (Fixture.Read(Standalone) is not { } xbt) return;

        DdsSurface surface = XbtSurface.TryRead(xbt, ReadByPath)!;

        Assert.Equal(256, surface.Width);
        Assert.Equal(DdsSurface.FourCcDxt1, surface.FourCc);
    }

    /// <summary>A companion that cannot be read leaves the smaller chain rather than failing the
    /// texture: half resolution beats no ground at all.</summary>
    [Fact]
    public void An_unreachable_companion_leaves_the_chain_it_found()
    {
        if (Fixture.Read(Streamed) is not { } xbt) return;

        DdsSurface surface = XbtSurface.TryRead(xbt, _ => null)!;

        Assert.Equal(256, surface.Width);
    }

    [Fact]
    public void A_companion_that_is_not_twice_the_size_is_refused()
    {
        if (Fixture.Read(Streamed) is not { } xbt || Fixture.Read(Standalone) is not { } wrong) return;

        (_, byte[] dds) = XbtTexture.Split(xbt);
        DdsSurface surface = DdsSurface.TryParse(dds)!;
        (_, byte[] wrongDds) = XbtTexture.Split(wrong);

        // Same format and same 256 side, so it is only the doubling rule that rejects it.
        DdsSurface stacked = surface.WithTopLevel(DdsSurface.TryParse(wrongDds)!);

        Assert.Equal(256, stacked.Width);
        Assert.Equal(surface.Mips.Count, stacked.Mips.Count);
    }
}
