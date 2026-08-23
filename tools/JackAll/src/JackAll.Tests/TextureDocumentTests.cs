using JackAll.Tools.Fc2Model;
using JackAll.Tools.Png;
using JackAll.Tools.Xbt;

namespace JackAll.Tests;

/// <summary>
/// The pack's texture gate.
/// </summary>
/// <remarks>
/// Block compression is lossy, so unlike every other format here this one cannot be held to
/// returning its bytes. What can be held exactly is everything around the pixels - the header, the
/// codec, the mip split and the PNG - and what is left is a measured quality floor rather than a
/// claim.
/// </remarks>
public sealed class TextureDocumentTests
{
    /// <summary>
    /// The textures a model can actually reference.
    /// </summary>
    /// <remarks>
    /// Only the graphics tree, because the corpus also holds 14,964 <c>.xbt</c> under <c>sdat</c>
    /// that are not colour images at all - 32 bits carrying two 16-bit channels (masks
    /// <c>0x0000FFFF</c> and <c>0xFFFF0000</c>), which is terrain data wearing a texture's
    /// extension. Nothing a model names resolves there.
    /// </remarks>
    private static IEnumerable<string> ModelTextures()
        => Fc2Corpus.Find(".xbt").Where(path =>
            path.Contains($"{Path.DirectorySeparatorChar}graphics{Path.DirectorySeparatorChar}",
                StringComparison.OrdinalIgnoreCase));

    /// <summary>Textures the corpus holds by their own path, for resolving companions.</summary>
    private static Func<string, byte[]?> Reader()
    {
        Dictionary<string, string> byName = [];
        foreach (string path in ModelTextures())
        {
            byName[Path.GetFileName(path).ToLowerInvariant()] = path;
        }
        return wanted =>
        {
            string name = Path.GetFileName(wanted.Replace('\\', '/')).ToLowerInvariant();
            return byName.TryGetValue(name, out string? found) ? File.ReadAllBytes(found) : null;
        };
    }

    /// <summary>
    /// Every shipped texture decodes, and its pixels survive PNG exactly - which is the half of the
    /// trip that has to be lossless.
    /// </summary>
    [Fact]
    public void Every_shipped_texture_decodes_and_survives_png()
    {
        Func<string, byte[]?> read = Reader();
        List<string> failures = [];
        int checkedFiles = 0;
        int paired = 0;

        foreach (string path in ModelTextures())
        {
            // A companion is not a texture in its own right; it is read through its base.
            if (Path.GetFileNameWithoutExtension(path).EndsWith("_mip0", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            checkedFiles++;
            try
            {
                TextureDocument document = TextureDocument.From(File.ReadAllBytes(path), read);
                paired += document.CompanionHeader is not null ? 1 : 0;

                (byte[] rgba, int width, int height) = PngImage.Decode(document.ToPng());
                if (width != document.Width || height != document.Height)
                {
                    failures.Add($"{Path.GetFileName(path)}: PNG came back {width}x{height}, not {document.Width}x{document.Height}");
                }
                else if (!rgba.AsSpan().SequenceEqual(document.Rgba))
                {
                    failures.Add($"{Path.GetFileName(path)}: pixels changed through PNG");
                }
            }
            catch (Exception error)
            {
                failures.Add($"{Path.GetFileName(path)}: {error.Message}");
            }
        }

        Assert.True(
            checkedFiles > 0 || !Fc2Corpus.Present,
            $"{Fc2Corpus.Root} holds no *.xbt, so this gate asserted nothing.");

        // If nothing were paired the mip merge would never run, and half the corpus is split.
        Assert.True(
            paired > 0 || !Fc2Corpus.Present,
            "No texture named a companion, so the merge was never exercised.");

        Assert.True(
            failures.Count == 0,
            $"{checkedFiles - failures.Count}/{checkedFiles} textures decoded, {paired} with a "
            + $"companion. First failures:{Environment.NewLine}"
            + string.Join(Environment.NewLine, failures.Take(5)));
    }

    /// <summary>
    /// Rebuilding a split texture puts the levels back where the engine expects them: the companion
    /// carries one level at twice the base's size. Inverted, a texture is half or double resolution
    /// in game only, which is why this is asserted rather than eyeballed.
    /// </summary>
    [Fact]
    public void A_split_texture_rebuilds_with_its_top_level_in_the_companion()
    {
        Func<string, byte[]?> read = Reader();
        string? path = ModelTextures()
            .FirstOrDefault(p => !Path.GetFileNameWithoutExtension(p).EndsWith("_mip0", StringComparison.OrdinalIgnoreCase)
                                 && XbtTexture.CompanionPath(XbtTexture.Split(File.ReadAllBytes(p)).Header) is not null);
        if (path is null)
        {
            return;
        }

        TextureDocument document = TextureDocument.From(File.ReadAllBytes(path), read);
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
    [Fact]
    public void Re_encoding_a_texture_stays_close_to_what_shipped()
    {
        Func<string, byte[]?> read = Reader();
        List<string> sampled = [.. ModelTextures()
            .Where(p => !Path.GetFileNameWithoutExtension(p).EndsWith("_mip0", StringComparison.OrdinalIgnoreCase))
            .Take(24)];
        if (sampled.Count == 0)
        {
            return;
        }

        double worst = double.MaxValue;
        string worstName = "";
        foreach (string path in sampled)
        {
            TextureDocument document = TextureDocument.From(File.ReadAllBytes(path), read);
            (byte[] rebuilt, _) = document.ToXbt();
            if (XbtPixels.TryDecode(rebuilt) is not { } again)
            {
                continue;
            }

            // The base starts one level down when there is a companion, so compare like with like.
            if (again.Width != document.Width || again.Height != document.Height)
            {
                continue;
            }

            double psnr = Psnr(document.Rgba, again.Rgba);
            if (psnr < worst)
            {
                worst = psnr;
                worstName = Path.GetFileName(path);
            }
        }

        Assert.True(
            worst is double.MaxValue or > 30.0,
            $"Worst re-encode was {worst:0.0} dB on {worstName}; expected better than 30 dB.");
    }

    [Fact]
    [Trait("Category", "RequiresFixture")]
    public void The_corpus_was_actually_found()
        => Assert.True(Fc2Corpus.Find(".xbt").Any(), Fc2Corpus.MissingMessage(".xbt"));

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
