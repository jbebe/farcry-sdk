using JackAll.Tools.World;
using Vortice.Direct3D11;
using Vortice.DXGI;

namespace JackAll.App.MapEditor.Gpu;

/// <summary>
/// The map's per-sample surface types as an R8 texture, plus the 256-entry colour lookup the terrain
/// shader tints with. Surface-type ids are sparse and meaningless as numbers, so the colours come
/// from a fixed hue sweep keyed by id - stable between loads, and distinct enough to read a boundary.
/// Both are read with the point sampler: interpolating between two ids would invent a third.
/// </summary>
public sealed class SurfaceTypeTexture : IDisposable
{
    private readonly WorldTerrain _terrain;

    /// <summary>The id map, uploaded the first time surface types are drawn; null until then.</summary>
    private ID3D11ShaderResourceView? _ids;

    public ID3D11ShaderResourceView PaletteView { get; }

    public SurfaceTypeTexture(WorldTerrain terrain)
    {
        _terrain = terrain;

        var colours = new byte[256 * 4];
        for (int id = 0; id < 256; id++)
        {
            (byte r, byte g, byte b) = ColourFor((byte)id);
            colours[id * 4] = r;
            colours[id * 4 + 1] = g;
            colours[id * 4 + 2] = b;
            colours[id * 4 + 3] = 255;
        }
        PaletteView = Upload.Texture(colours, Format.R8G8B8A8_UNorm_SRgb, 256, 1);
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

    /// <summary>The id map, uploaded on the first call that wants it tinted; null until then, which
    /// the shader never reads untinted.</summary>
    public ID3D11ShaderResourceView? Ids(bool tinted)
    {
        if (tinted && _ids is null)
        {
            _ids = Upload.Texture(_terrain.SurfaceTypes, Format.R8_UNorm, _terrain.Side, _terrain.Side);
        }
        return _ids;
    }

    public void Dispose()
    {
        _ids?.Dispose();
        PaletteView.Dispose();
    }
}
