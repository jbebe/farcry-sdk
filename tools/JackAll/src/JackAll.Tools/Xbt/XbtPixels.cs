using JackAll.Core.Format;
using BCnEncoder.Decoder;
using BCnEncoder.Shared;

namespace JackAll.Tools.Xbt;

/// <summary>
/// Decodes an <c>.xbt</c>'s block-compressed payload to straight RGBA.
/// </summary>
/// <remarks>
/// One BCn path for everything that needs pixels rather than blocks - the file viewer, the map
/// editor's texture sets, the <c>.mgb</c> material rows and the model packer - so they cannot drift
/// apart. Callers that upload the compressed blocks to a GPU untouched want
/// <see cref="DdsSurface"/> instead.
/// </remarks>
public static class XbtPixels
{
    /// <summary>
    /// Tightly packed RGBA, four bytes per pixel, row-major from the top. Null when the bytes are
    /// not a readable <c>.xbt</c> or the payload is not something the decoder handles.
    /// </summary>
    public static (byte[] Rgba, int Width, int Height)? TryDecode(byte[] xbt)
    {
        try
        {
            (_, byte[] dds) = XbtTexture.Split(xbt);
            return TryDecodeDds(dds);
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>The same decode for a caller that already split the file and holds the payload.</summary>
    public static (byte[] Rgba, int Width, int Height)? TryDecodeDds(byte[] dds)
    {
        if (TryDecodeUncompressed(dds) is { } plain)
        {
            return plain;
        }

        try
        {
            using var stream = new MemoryStream(dds);
            ColorRgba32[,] rows = new BcDecoder().Decode2D(stream).ToArray();
            int height = rows.GetLength(0);
            int width = rows.GetLength(1);

            var rgba = new byte[width * height * 4];
            int at = 0;
            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    ColorRgba32 colour = rows[y, x];
                    rgba[at++] = colour.r;
                    rgba[at++] = colour.g;
                    rgba[at++] = colour.b;
                    rgba[at++] = colour.a;
                }
            }
            return (rgba, width, height);
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>
    /// A payload that is plain 32-bit pixels rather than blocks, which the block decoder refuses.
    /// </summary>
    /// <remarks>
    /// Two shipped model textures are stored this way, both sky domes, and neither rendered anywhere
    /// before. Terrain also uses the uncompressed form for something that is not an image at all -
    /// 32 bits carrying two 16-bit channels - so the channel masks are read rather than assumed, and
    /// anything that is not a straight colour layout is left alone.
    /// </remarks>
    private static (byte[] Rgba, int Width, int Height)? TryDecodeUncompressed(byte[] dds)
    {
        const uint Rgb = 0x40;
        const int HeaderSize = 128;
        if (dds.Length < HeaderSize || ByteCursor.U32(dds, 0) != 0x20534444
            || (ByteCursor.U32(dds, 80) & Rgb) == 0 || ByteCursor.U32(dds, 88) != 32)
        {
            return null;
        }

        int height = (int)ByteCursor.U32(dds, 12);
        int width = (int)ByteCursor.U32(dds, 16);
        (uint red, uint green, uint blue, uint alpha) =
            (ByteCursor.U32(dds, 92), ByteCursor.U32(dds, 96),
             ByteCursor.U32(dds, 100), ByteCursor.U32(dds, 104));

        // Every channel has to sit in its own byte; the terrain layout packs two 16-bit values
        // instead, and reading that as colour would produce nonsense rather than a failure.
        if (!IsByteChannel(red) || !IsByteChannel(green) || !IsByteChannel(blue)
            || (alpha != 0 && !IsByteChannel(alpha))
            || width <= 0 || height <= 0
            || dds.Length < HeaderSize + (width * height * 4))
        {
            return null;
        }

        var rgba = new byte[width * height * 4];
        for (int i = 0; i < width * height; i++)
        {
            uint pixel = ByteCursor.U32(dds, HeaderSize + (i * 4));
            rgba[i * 4] = Channel(pixel, red);
            rgba[(i * 4) + 1] = Channel(pixel, green);
            rgba[(i * 4) + 2] = Channel(pixel, blue);
            rgba[(i * 4) + 3] = alpha == 0 ? (byte)255 : Channel(pixel, alpha);
        }
        return (rgba, width, height);
    }

    private static bool IsByteChannel(uint mask)
        => mask is 0x000000FF or 0x0000FF00 or 0x00FF0000 or 0xFF000000;

    private static byte Channel(uint pixel, uint mask)
        => (byte)((pixel & mask) >> System.Numerics.BitOperations.TrailingZeroCount(mask));
}
