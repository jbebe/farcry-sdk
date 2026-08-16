using JackAll.Tools.World;
using OpenTK.Graphics.OpenGL4;

namespace JackAll.App.MapEditor.Gl;

/// <summary>
/// The map's per-sample surface types as an R8 texture, plus the 256-entry colour lookup the terrain
/// shader tints with. Surface-type ids are sparse and meaningless as numbers, so the colours come
/// from a fixed hue sweep keyed by id - stable between loads, and distinct enough to read a boundary.
/// </summary>
public sealed class SurfaceTypeTexture : IDisposable
{
    public int Handle { get; }
    public int PaletteHandle { get; }
    public int Side { get; }

    public SurfaceTypeTexture(WorldTerrain terrain)
    {
        Side = terrain.Side;

        Handle = GL.GenTexture();
        GL.BindTexture(TextureTarget.Texture2D, Handle);
        GL.PixelStore(PixelStoreParameter.UnpackAlignment, 1);
        GL.TexImage2D(TextureTarget.Texture2D, 0, PixelInternalFormat.R8, Side, Side, 0,
            PixelFormat.Red, PixelType.UnsignedByte, terrain.SurfaceTypes);
        // Nearest everywhere: interpolating between two ids would invent a third.
        GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.Nearest);
        GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)TextureMagFilter.Nearest);
        GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapS, (int)TextureWrapMode.ClampToEdge);
        GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapT, (int)TextureWrapMode.ClampToEdge);

        var colours = new byte[256 * 3];
        for (int id = 0; id < 256; id++)
        {
            (byte r, byte g, byte b) = ColourFor((byte)id);
            colours[id * 3] = r;
            colours[id * 3 + 1] = g;
            colours[id * 3 + 2] = b;
        }
        PaletteHandle = GL.GenTexture();
        GL.BindTexture(TextureTarget.Texture2D, PaletteHandle);
        GL.TexImage2D(TextureTarget.Texture2D, 0, PixelInternalFormat.Rgb8, 256, 1, 0,
            PixelFormat.Rgb, PixelType.UnsignedByte, colours);
        GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.Nearest);
        GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)TextureMagFilter.Nearest);
        GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapS, (int)TextureWrapMode.ClampToEdge);
        GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapT, (int)TextureWrapMode.ClampToEdge);
    }

    /// <summary>
    /// The tint a surface type is drawn with, also used for the legend swatches so the two agree.
    /// 0xFF (hole or missing sector) stays neutral grey rather than taking a hue of its own.
    /// </summary>
    public static (byte R, byte G, byte B) ColourFor(byte surfaceType)
    {
        if (surfaceType == 0xFF)
        {
            return (128, 128, 128);
        }

        // Golden-ratio hue stepping keeps consecutive ids far apart on the colour wheel.
        float hue = surfaceType * 0.618034f % 1f;
        float saturation = 0.55f;
        float value = 0.95f;
        int sextant = (int)(hue * 6f);
        float f = hue * 6f - sextant;
        float p = value * (1f - saturation);
        float q = value * (1f - f * saturation);
        float t = value * (1f - (1f - f) * saturation);
        (float r, float g, float b) = (sextant % 6) switch
        {
            0 => (value, t, p),
            1 => (q, value, p),
            2 => (p, value, t),
            3 => (p, q, value),
            4 => (t, p, value),
            _ => (value, p, q),
        };
        return ((byte)(r * 255), (byte)(g * 255), (byte)(b * 255));
    }

    public void Bind(TextureUnit surfaceUnit, TextureUnit paletteUnit)
    {
        GL.ActiveTexture(surfaceUnit);
        GL.BindTexture(TextureTarget.Texture2D, Handle);
        GL.ActiveTexture(paletteUnit);
        GL.BindTexture(TextureTarget.Texture2D, PaletteHandle);
    }

    public void Dispose()
    {
        GL.DeleteTexture(Handle);
        GL.DeleteTexture(PaletteHandle);
    }
}
