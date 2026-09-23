using JackAll.Tools.Fc2Model;
using JackAll.Tools.Png;
using JackAll.Tools.Xbt;

namespace JackAll.Tests;

/// <summary>
/// A shipped texture decodes to the pixels a <c>.fc2model</c> carries and rebuilds around them with
/// the header, codec and mip split it shipped with.
/// </summary>
/// <remarks>
/// Block compression is lossy, so unlike every other format here this one cannot be held to
/// returning its bytes. What can be held exactly is everything around the pixels - the header, the
/// codec, the mip split and the PNG - and what is left is a measured quality floor rather than a
/// claim.
/// </remarks>
public sealed class TextureDocumentTests
{
    // DXT1, with its top level split into a _mip0 companion.
    private const string Split = "Texture/woodweapons_02_d.xbt";

    // The companion holding Split's top level.
    private const string SplitCompanion = "Texture/woodweapons_02_d_mip0.xbt";

    // DXT5, in one file.
    private const string Dxt5 = "Texture/fuelpilefuelgauge_d.xbt";

    // Plain 32-bit pixels, which only two sky-dome textures ship as.
    private const string Uncompressed = "Texture/sun_flare.xbt";

    // DXT1, in one file.
    private const string Dxt1 = XbtStreamedMipTests.Standalone;

    private static readonly Func<string, byte[]?> ReadByPath = Fixture.ByFileName("Texture");

    /// <summary>
    /// A shipped texture decodes, and its pixels survive PNG exactly - which is the half of the
    /// trip that has to be lossless.
    /// </summary>
    [Theory]
    [InlineData(Split)]
    [InlineData(Dxt5)]
    [InlineData(Uncompressed)]
    public void A_shipped_texture_decodes_and_survives_png(string fixture)
    {
        if (Fixture.Read(fixture) is not { } xbt)
        {
            return;
        }

        TextureDocument document = TextureDocument.From(xbt, ReadByPath);
        (byte[] rgba, int width, int height) = PngImage.Decode(document.ToPng());

        Assert.Equal((document.Width, document.Height), (width, height));
        Fixture.AssertSameBytes($"{fixture} through PNG", document.Rgba, rgba);
    }

    /// <summary>
    /// Rebuilding a split texture puts the levels back where the engine expects them: the companion
    /// carries one level at twice the base's size. Inverted, a texture is half or double resolution
    /// in game only, which is why this is asserted rather than eyeballed.
    /// </summary>
    [Fact]
    public void A_split_texture_rebuilds_with_its_top_level_in_the_companion()
    {
        if (Fixture.Read(Split) is not { } xbt)
        {
            return;
        }

        TextureDocument document = TextureDocument.From(xbt, ReadByPath);
        (byte[] rebuilt, byte[]? companion) = document.ToXbt();
        Assert.NotNull(companion);

        DdsSurface baseSurface = DdsSurface.TryParse(XbtTexture.Split(rebuilt).Dds)!;
        DdsSurface topSurface = DdsSurface.TryParse(XbtTexture.Split(companion).Dds)!;

        Assert.Equal(document.Width, topSurface.Width);
        Assert.Equal(document.Height, topSurface.Height);
        Assert.Equal(document.Width / 2, baseSurface.Width);
        Assert.Equal(document.Height / 2, baseSurface.Height);
        Assert.Single(topSurface.Mips);

        // The headers are the ones that shipped, since neither can be synthesized.
        Assert.Equal(document.Header, XbtTexture.Split(rebuilt).Header);
        Assert.Equal(document.CompanionHeader, XbtTexture.Split(companion).Header);
    }

    /// <summary>
    /// What re-encoding costs, as a measured number rather than a claim. Re-compressing data that
    /// is already block-compressed should be close to a no-op, because the four palette colours of
    /// a block are the best fit of themselves.
    /// </summary>
    [Theory]
    [InlineData(Dxt1)]
    [InlineData(Dxt5)]
    public void Re_encoding_a_texture_stays_close_to_what_shipped(string fixture)
    {
        if (Fixture.Read(fixture) is not { } xbt)
        {
            return;
        }

        TextureDocument document = TextureDocument.From(xbt, ReadByPath);
        (byte[] rebuilt, _) = document.ToXbt();
        (byte[] Rgba, int Width, int Height) again = XbtPixels.TryDecode(rebuilt)
            ?? throw new InvalidDataException("The re-encoded texture does not decode.");

        Assert.Equal((document.Width, document.Height), (again.Width, again.Height));
        double psnr = Psnr(document.Rgba, again.Rgba);
        Assert.True(psnr > 30.0, $"Re-encoding cost {psnr:0.0} dB; expected better than 30 dB.");
    }

    [Fact]
    [Trait("Category", "RequiresFixture")]
    public void The_fixtures_were_actually_found()
        => Fixture.AssertPresent(Split, SplitCompanion, Dxt5, Uncompressed);

    private static double Psnr(byte[] expected, byte[] actual)
    {
        double sum = 0.0;
        for (int i = 0; i < expected.Length; i++)
        {
            double error = expected[i] - actual[i];
            sum += error * error;
        }
        double mse = sum / expected.Length;
        return mse == 0.0 ? double.MaxValue : 10.0 * Math.Log10(255.0 * 255.0 / mse);
    }
}
