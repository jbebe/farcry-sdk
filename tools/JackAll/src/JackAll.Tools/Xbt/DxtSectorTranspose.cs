namespace JackAll.Tools.Xbt;

/// <summary>
/// Straightens the per-sector transpose the terrain cooker writes into its atlas textures, moving
/// whole DXT1 blocks instead of decoding.
/// </summary>
/// <remarks>
/// Every per-sector square the cooker writes is stored transposed - a texel at (x, y) inside a
/// sector holds what belongs at (y, x). A reader can undo that at sampling time, but then it can
/// never let the hardware filter or mip the texture, because neighbouring texels in memory are not
/// neighbours in the world.
///
/// Undoing it up front costs nothing in quality: transposing a square of 4x4 blocks is a move of
/// whole blocks plus a mirror of the 4x4 selector grid inside each one, and a DXT1 block's two
/// endpoints do not care where the block sits. So the shipped compression survives byte for byte.
/// Only squares at least one block across can be handled this way, which is what bounds how far
/// down a mip chain it can go.
/// </remarks>
public static class DxtSectorTranspose
{
    private const int BlockBytes = 8;
    private const int BlockSide = 4;

    /// <summary>
    /// Mirrors each <paramref name="sectorSide"/>-texel square of a DXT1 level about its diagonal.
    /// Its own inverse, so it also applies the transpose to straight data.
    /// </summary>
    /// <param name="blocks">One mip level's DXT1 block stream, <paramref name="side"/> square.</param>
    /// <exception cref="ArgumentException">A square smaller than one block, or a level that is not a
    /// whole number of blocks or of sectors.</exception>
    public static byte[] Mirror(byte[] blocks, int side, int sectorSide)
    {
        if (sectorSide < BlockSide || side % sectorSide != 0 || sectorSide % BlockSide != 0)
        {
            throw new ArgumentException(
                $"A {side} level cannot be mirrored in {sectorSide}-texel squares.", nameof(sectorSide));
        }

        int stride = side / BlockSide, span = sectorSide / BlockSide;
        var mirrored = new byte[blocks.Length];
        for (int y = 0; y < stride; y++)
        {
            for (int x = 0; x < stride; x++)
            {
                int originX = x - x % span, originY = y - y % span;
                int source = ((originY + (x - originX)) * stride + originX + (y - originY)) * BlockBytes;
                int destination = (y * stride + x) * BlockBytes;
                if (source + BlockBytes > blocks.Length || destination + BlockBytes > mirrored.Length)
                {
                    continue;
                }

                blocks.AsSpan(source, 4).CopyTo(mirrored.AsSpan(destination));
                BitConverter.TryWriteBytes(mirrored.AsSpan(destination + 4),
                    MirrorSelectors(BitConverter.ToUInt32(blocks, source + 4)));
            }
        }
        return mirrored;
    }

    /// <summary>A block's 4x4 grid of 2-bit selectors, mirrored about its own diagonal.</summary>
    public static uint MirrorSelectors(uint selectors)
    {
        uint mirrored = 0;
        for (int y = 0; y < BlockSide; y++)
        {
            for (int x = 0; x < BlockSide; x++)
            {
                mirrored |= ((selectors >> ((y * BlockSide + x) * 2)) & 3u) << ((x * BlockSide + y) * 2);
            }
        }
        return mirrored;
    }
}
