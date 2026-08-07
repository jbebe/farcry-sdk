using BCnEncoder.Decoder;
using BCnEncoder.Shared;
using CommunityToolkit.HighPerformance;
using JackAll.Tools.Format;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace JackAll.App.FileHandlers.Xbt;

/// <summary>
/// Decodes an <c>.xbt</c>'s DDS payload into something WPF can show.
/// </summary>
/// <remarks>
/// Shared rather than living in <see cref="XbtFileHandler"/>, because the <c>.mgb</c> editor's
/// material rows resolve their texture path to an <c>.xbt</c> and preview it the same way (see
/// <c>MgbTextureResolver</c>) - one BCn decode path, not two that can drift apart.
/// </remarks>
public static class XbtImage
{
    /// <summary>Null (with the reason in <paramref name="error"/>) if <paramref name="xbt"/> isn't a
    /// readable .xbt or its DDS payload uses something <see cref="BcDecoder"/> can't decode.</summary>
    public static BitmapSource? TryDecode(byte[] xbt, out string? error)
    {
        try
        {
            (_, byte[] dds) = XbtTexture.Split(xbt);
            var decoder = new BcDecoder();
            using var stream = new MemoryStream(dds);
            error = null;
            return ToBitmap(decoder.Decode2D(stream));
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return null;
        }
    }

    private static WriteableBitmap ToBitmap(Memory2D<ColorRgba32> pixels)
    {
        int width = pixels.Width;
        int height = pixels.Height;
        ColorRgba32[,] rows = pixels.ToArray();

        byte[] buffer = new byte[width * height * 4];
        int i = 0;
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                ColorRgba32 c = rows[y, x];
                buffer[i++] = c.b;
                buffer[i++] = c.g;
                buffer[i++] = c.r;
                buffer[i++] = c.a;
            }
        }

        var bitmap = new WriteableBitmap(width, height, 96, 96, PixelFormats.Bgra32, null);
        bitmap.WritePixels(new Int32Rect(0, 0, width, height), buffer, width * 4, 0);
        bitmap.Freeze();
        return bitmap;
    }
}
