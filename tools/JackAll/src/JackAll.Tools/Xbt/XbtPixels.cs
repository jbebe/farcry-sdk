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
}
