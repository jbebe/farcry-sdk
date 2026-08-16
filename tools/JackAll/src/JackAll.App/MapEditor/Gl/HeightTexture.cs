using JackAll.Tools.World;
using OpenTK.Graphics.OpenGL4;

namespace JackAll.App.MapEditor.Gl;

/// <summary>
/// The map's heightfield as one R16 texture, sampled by the terrain mesh. Raw sdat encoding:
/// normalized value * 65535 / 128 = meters.
/// </summary>
public sealed class HeightTexture : IDisposable
{
    public int Handle { get; }
    public float MinNormalized { get; }
    public float MaxNormalized { get; }

    /// <summary>Height samples a side - the extent the sampling shaders normalize against.</summary>
    public int Side { get; }

    public HeightTexture(WorldTerrain terrain)
    {
        MinNormalized = terrain.MinHeight / 65535f;
        MaxNormalized = terrain.MaxHeight / 65535f;
        Side = terrain.Side;

        Handle = GL.GenTexture();
        GL.BindTexture(TextureTarget.Texture2D, Handle);
        GL.PixelStore(PixelStoreParameter.UnpackAlignment, 2);
        GL.TexImage2D(TextureTarget.Texture2D, 0, PixelInternalFormat.R16,
            Side, Side, 0,
            PixelFormat.Red, PixelType.UnsignedShort, terrain.Heights);
        GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.Linear);
        GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)TextureMagFilter.Linear);
        GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapS, (int)TextureWrapMode.ClampToEdge);
        GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapT, (int)TextureWrapMode.ClampToEdge);
    }

    public void Bind(TextureUnit unit)
    {
        GL.ActiveTexture(unit);
        GL.BindTexture(TextureTarget.Texture2D, Handle);
    }

    /// <summary>Re-uploads one edited rect of the heightfield - a brush dab touches a few hundred
    /// texels, never the 26M-sample texture.</summary>
    public void UpdateRegion(WorldTerrain terrain, int x0, int y0, int x1, int y1)
    {
        int width = x1 - x0 + 1;
        int height = y1 - y0 + 1;
        var region = new ushort[width * height];
        for (int y = 0; y < height; y++)
        {
            Array.Copy(terrain.Heights, (y0 + y) * Side + x0, region, y * width, width);
        }
        GL.BindTexture(TextureTarget.Texture2D, Handle);
        GL.PixelStore(PixelStoreParameter.UnpackAlignment, 2);
        GL.TexSubImage2D(TextureTarget.Texture2D, 0, x0, y0, width, height,
            PixelFormat.Red, PixelType.UnsignedShort, region);
    }

    public void Dispose() => GL.DeleteTexture(Handle);
}
