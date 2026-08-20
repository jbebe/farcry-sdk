using JackAll.Tools.Xbt;

namespace JackAll.Tests;

/// <summary>
/// Straightening a terrain atlas's per-sector transpose by moving DXT1 blocks, checked against the
/// texel-level swap a shader would otherwise do at every tap - on a retail atlas, because the
/// transpose is a fact about what the cooker wrote.
/// </summary>
public class DxtSectorTransposeTests
{
    private const string Fixture = @".\Fixtures\Sdat\atlas5126_diffuse.xbt";
    private const int AtlasSide = 128;
    private const int SectorSide = 64;

    private static DdsSurface? Atlas()
    {
        if (!File.Exists(Fixture))
        {
            return null;
        }

        (_, byte[] dds) = XbtTexture.Split(File.ReadAllBytes(Fixture));
        return DdsSurface.TryParse(dds);
    }

    /// <summary>Where a shader reads a world texel from, given the stored layout.</summary>
    private static (int X, int Y) Swapped(int x, int y)
    {
        int originX = x - x % SectorSide, originY = y - y % SectorSide;
        return (originX + (y - originY), originY + (x - originX));
    }

    /// <summary>A texel's endpoints and its own 2-bit selector, which together are its colour.</summary>
    private static (uint Endpoints, uint Selector) Texel(byte[] blocks, int side, int x, int y)
    {
        int block = ((y / 4) * (side / 4) + x / 4) * 8;
        uint selectors = BitConverter.ToUInt32(blocks, block + 4);
        return (BitConverter.ToUInt32(blocks, block),
            (selectors >> (((y % 4) * 4 + x % 4) * 2)) & 3u);
    }

    [Fact]
    [Trait("Category", "RequiresFixture")]
    public void The_fixture_was_actually_found()
    {
        if (!Directory.Exists(@".\Fixtures\Sdat")) return;

        Assert.NotNull(Atlas());
    }

    /// <summary>
    /// The whole point: reading the mirrored blocks straight has to land on exactly the texel that
    /// reading the stored blocks through the swap would. Endpoints and selector both, so this covers
    /// the move of whole blocks and the mirror of the grid inside each.
    /// </summary>
    [Fact]
    [Trait("Category", "RequiresFixture")]
    public void Mirroring_up_front_reads_the_same_as_swapping_at_every_tap()
    {
        if (Atlas() is not { } atlas) return;

        byte[] stored = atlas.Mips[0];
        byte[] mirrored = DxtSectorTranspose.Mirror(stored, AtlasSide, SectorSide);

        for (int y = 0; y < AtlasSide; y++)
        {
            for (int x = 0; x < AtlasSide; x++)
            {
                (int sx, int sy) = Swapped(x, y);
                Assert.Equal(Texel(stored, AtlasSide, sx, sy), Texel(mirrored, AtlasSide, x, y));
            }
        }
    }

    [Fact]
    [Trait("Category", "RequiresFixture")]
    public void Mirroring_twice_gives_the_bytes_back()
    {
        if (Atlas() is not { } atlas) return;

        byte[] stored = atlas.Mips[0];

        Assert.Equal(stored, DxtSectorTranspose.Mirror(
            DxtSectorTranspose.Mirror(stored, AtlasSide, SectorSide), AtlasSide, SectorSide));
    }

    /// <summary>
    /// The reason to bother: the two sectors inside one atlas are neighbours in the world, so once
    /// the transpose is undone the ground has to run across the seam between them no more roughly
    /// than it does inside either one. Stored, that seam is a cliff.
    /// </summary>
    [Fact]
    [Trait("Category", "RequiresFixture")]
    public void The_sector_seam_only_disappears_once_the_transpose_is_undone()
    {
        if (Atlas() is not { } atlas) return;

        byte[] stored = atlas.Mips[0];
        byte[] mirrored = DxtSectorTranspose.Mirror(stored, AtlasSide, SectorSide);

        Assert.True(Seam(stored) > 4 * Interior(stored),
            $"stored seam {Seam(stored):N4} was not the discontinuity it should be");
        Assert.True(Seam(mirrored) <= Interior(mirrored),
            $"mirrored seam {Seam(mirrored):N4} still exceeds its interior {Interior(mirrored):N4}");
    }

    /// <summary>Mean step in brightness across the atlas's internal sector boundary.</summary>
    private static double Seam(byte[] blocks)
    {
        double total = 0;
        for (int y = 0; y < AtlasSide; y++)
        {
            total += Math.Abs(Luma(blocks, SectorSide - 1, y) - Luma(blocks, SectorSide, y));
        }
        return total / AtlasSide;
    }

    /// <summary>The same step, averaged over every column that is not the seam.</summary>
    private static double Interior(byte[] blocks)
    {
        double total = 0;
        int counted = 0;
        for (int y = 0; y < AtlasSide; y++)
        {
            for (int x = 0; x < AtlasSide - 1; x++)
            {
                if (x == SectorSide - 1)
                {
                    continue;
                }
                total += Math.Abs(Luma(blocks, x, y) - Luma(blocks, x + 1, y));
                counted++;
            }
        }
        return total / counted;
    }

    /// <summary>Close enough for a continuity check: the block's two endpoints, averaged.</summary>
    private static double Luma(byte[] blocks, int x, int y)
    {
        int block = ((y / 4) * (AtlasSide / 4) + x / 4) * 8;
        return (Brightness(BitConverter.ToUInt16(blocks, block)) +
            Brightness(BitConverter.ToUInt16(blocks, block + 2))) / 2;
    }

    private static double Brightness(ushort rgb565)
        => 0.299 * ((rgb565 >> 11) & 0x1F) / 31 +
           0.587 * ((rgb565 >> 5) & 0x3F) / 63 +
           0.114 * (rgb565 & 0x1F) / 31;
}
