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
    private readonly WorldTerrain _terrain;

    /// <summary>The id map, uploaded the first time surface types are drawn; 0 until then.</summary>
    private int _handle;

    public int PaletteHandle { get; }

    public SurfaceTypeTexture(WorldTerrain terrain)
    {
        _terrain = terrain;

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
        GL.TexImage2D(TextureTarget.Texture2D, 0, PixelInternalFormat.Srgb8, 256, 1, 0,
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

    /// <summary>Binds the id map and the palette; until the id map is first wanted, the palette also
    /// stands in for it, so the unmet sampler still reads a complete texture.</summary>
    public void Bind(TextureUnit surfaceUnit, TextureUnit paletteUnit, bool tinted)
    {
        if (tinted && _handle == 0)
        {
            _handle = UploadIds();
        }
        GL.ActiveTexture(surfaceUnit);
        GL.BindTexture(TextureTarget.Texture2D, _handle != 0 ? _handle : PaletteHandle);
        GL.ActiveTexture(paletteUnit);
        GL.BindTexture(TextureTarget.Texture2D, PaletteHandle);
    }

    private int UploadIds()
    {
        int handle = GL.GenTexture();
        GL.BindTexture(TextureTarget.Texture2D, handle);
        GL.PixelStore(PixelStoreParameter.UnpackAlignment, 1);
        GL.TexImage2D(TextureTarget.Texture2D, 0, PixelInternalFormat.R8, _terrain.Side, _terrain.Side, 0,
            PixelFormat.Red, PixelType.UnsignedByte, _terrain.SurfaceTypes);
        // Nearest everywhere: interpolating between two ids would invent a third.
        GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.Nearest);
        GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)TextureMagFilter.Nearest);
        GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapS, (int)TextureWrapMode.ClampToEdge);
        GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapT, (int)TextureWrapMode.ClampToEdge);
        return handle;
    }

    public void Dispose()
    {
        GL.DeleteTexture(_handle);
        GL.DeleteTexture(PaletteHandle);
    }
}
