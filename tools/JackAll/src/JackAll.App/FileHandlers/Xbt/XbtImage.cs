using JackAll.Tools.Xbt;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace JackAll.App.FileHandlers.Xbt;

/// <summary>
/// Turns an <c>.xbt</c> into something WPF can show.
/// </summary>
/// <remarks>
/// The BCn decode itself lives in <see cref="XbtPixels"/>, which the map editor and the model packer
/// share; this is only the part that needs WPF. Shared rather than living in
/// <see cref="XbtFileHandler"/>, because the <c>.mgb</c> editor's material rows resolve their
/// texture path to an <c>.xbt</c> and preview it the same way (see <c>MgbTextureResolver</c>).
/// </remarks>
public static class XbtImage
{
    /// <summary>Null (with the reason in <paramref name="error"/>) if <paramref name="xbt"/> isn't a
    /// readable .xbt or its payload uses something the decoder can't handle.</summary>
    public static BitmapSource? TryDecode(byte[] xbt, out string? error)
    {
        if (XbtPixels.TryDecode(xbt) is not { } decoded)
        {
            error = "Not a readable .xbt, or its payload is not block-compressed.";
            return null;
        }

        error = null;
        return ToBitmap(decoded.Rgba, decoded.Width, decoded.Height);
    }

    /// <summary>
    /// The payload as tightly packed RGBA bytes, for callers uploading to the GPU rather than
    /// showing in WPF.
    /// </summary>
    public static (byte[] Rgba, int Width, int Height)? TryDecodeRgba(byte[] xbt)
        => XbtPixels.TryDecode(xbt);

    /// <summary>The same decode for a caller that already split the .xbt and holds the payload.</summary>
    public static (byte[] Rgba, int Width, int Height)? TryDecodeRgbaDds(byte[] dds)
        => XbtPixels.TryDecodeDds(dds);

    private static WriteableBitmap ToBitmap(byte[] rgba, int width, int height)
    {
        // WPF wants BGRA, so the red and blue channels swap on the way in.
        var buffer = new byte[rgba.Length];
        for (int at = 0; at < rgba.Length; at += 4)
        {
            buffer[at] = rgba[at + 2];
            buffer[at + 1] = rgba[at + 1];
            buffer[at + 2] = rgba[at];
            buffer[at + 3] = rgba[at + 3];
        }

        var bitmap = new WriteableBitmap(width, height, 96, 96, PixelFormats.Bgra32, null);
        bitmap.WritePixels(new Int32Rect(0, 0, width, height), buffer, width * 4, 0);
        bitmap.Freeze();
        return bitmap;
    }
}
