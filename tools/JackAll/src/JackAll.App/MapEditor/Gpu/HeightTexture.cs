using JackAll.Tools.World;
using Vortice.Direct3D11;
using Vortice.DXGI;

namespace JackAll.App.MapEditor.Gpu;

/// <summary>
/// The map's heightfield as one R16 texture, sampled by the terrain mesh with the linear clamped
/// sampler. Raw sdat encoding: normalized value * 65535 / 128 = meters.
/// </summary>
public sealed class HeightTexture(WorldTerrain terrain) : IDisposable
{
    public ID3D11ShaderResourceView View { get; } =
        Upload.Texture(terrain.Heights, Format.R16_UNorm, terrain.Side, terrain.Side);

    public float MinNormalized { get; } = terrain.MinHeight / 65535f;
    public float MaxNormalized { get; } = terrain.MaxHeight / 65535f;

    /// <summary>Height samples a side - the extent the sampling shaders normalize against.</summary>
    public int Side { get; } = terrain.Side;

    public void Dispose() => View.Dispose();
}
