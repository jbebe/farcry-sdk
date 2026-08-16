using JackAll.App.FileHandlers.Xbt;
using JackAll.Tools.World;
using OpenTK.Graphics.OpenGL4;

namespace JackAll.App.MapEditor.Gl;

/// <summary>
/// Everything the terrain needs to look like the game does: the world-wide blend weights stitched
/// from the <c>atlas&lt;id&gt;_mask.xbt</c> files, a per-sector lookup of which four layers that
/// sector blends, and the layer textures themselves as one array.
/// </summary>
/// <remarks>
/// The masks stay DXT1-compressed on the GPU - they are uploaded straight into a world-sized
/// compressed texture, one 128x128 atlas at a time, which is block-aligned because a sector is 64
/// texels across. That costs about a sixth of what decoding them would.
/// </remarks>
public sealed class TerrainTextureSet : IDisposable
{
    /// <summary>Every detail texture is resampled to this before going into the array.</summary>
    private const int DetailSize = 256;

    public int WeightHandle { get; }
    public int ColourHandle { get; }
    public int ShadowHandle { get; }
    public int SectorLayerHandle { get; }
    public int DetailArrayHandle { get; }
    public int WeightSide { get; }
    public int LayersLoaded { get; }

    /// <summary>Layers whose texture could not be read or decoded; they draw neutral grey.</summary>
    public IReadOnlyList<string> FailedLayers { get; }

    /// <summary>World units each layer's texture repeats over, indexed by layer index.</summary>
    public float[] Tiling { get; } = new float[MaxLayers];

    /// <summary>Projection plane per layer: 0 = X, 1 = Y, 2 = Z.</summary>
    public float[] ProjectionAxis { get; } = new float[MaxLayers];

    public const int MaxLayers = 64;

    public TerrainTextureSet(
        TerrainMap map,
        SectorDetailLayers detailLayers,
        TerrainLayerTable table,
        Func<string, byte[]?> readByPath)
    {
        WeightSide = map.SectorsPerSide * SdatQuadsPerSector;

        Dictionary<int, string> sectorPaths = map.Sectors.ToDictionary(s => s.SectorId, s => s.Path);
        WeightHandle = BuildAtlasTexture(map, sectorPaths, readByPath, "mask");
        ColourHandle = BuildAtlasTexture(map, sectorPaths, readByPath, "color");
        ShadowHandle = BuildShadowTexture(map, readByPath);

        // One texel per sector, carrying its four layer indices.
        SectorLayerHandle = GL.GenTexture();
        GL.BindTexture(TextureTarget.Texture2D, SectorLayerHandle);
        GL.PixelStore(PixelStoreParameter.UnpackAlignment, 1);
        GL.TexImage2D(TextureTarget.Texture2D, 0, PixelInternalFormat.Rgba8,
            map.SectorsPerSide, map.SectorsPerSide, 0,
            PixelFormat.Rgba, PixelType.UnsignedByte, detailLayers.Indices);
        GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.Nearest);
        GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)TextureMagFilter.Nearest);
        GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapS, (int)TextureWrapMode.ClampToEdge);
        GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapT, (int)TextureWrapMode.ClampToEdge);

        DetailArrayHandle = GL.GenTexture();
        GL.BindTexture(TextureTarget.Texture2DArray, DetailArrayHandle);
        int layerCount = Math.Max(table.Layers.Count, 1);

        // Every slice starts neutral grey. A layer whose texture is missing or in a format the
        // decoder refuses would otherwise be drawn from uninitialised memory, which shows up as
        // blocky garbage wherever that layer is weighted.
        var neutral = new byte[DetailSize * DetailSize * 4];
        Array.Fill(neutral, (byte)128);
        GL.TexImage3D(TextureTarget.Texture2DArray, 0, PixelInternalFormat.Rgba8,
            DetailSize, DetailSize, layerCount, 0, PixelFormat.Rgba, PixelType.UnsignedByte, IntPtr.Zero);
        for (int layer = 0; layer < layerCount; layer++)
        {
            GL.TexSubImage3D(TextureTarget.Texture2DArray, 0, 0, 0, layer,
                DetailSize, DetailSize, 1, PixelFormat.Rgba, PixelType.UnsignedByte, neutral);
        }

        Array.Fill(Tiling, 20f);
        Array.Fill(ProjectionAxis, 2f);
        int loaded = 0;
        var failed = new List<string>();
        foreach (TerrainLayer layer in table.Layers)
        {
            if (layer.Index < MaxLayers)
            {
                Tiling[layer.Index] = layer.Tiling;
                ProjectionAxis[layer.Index] = layer.ProjectionAxis;
            }
            if (string.IsNullOrEmpty(layer.TexturePath) ||
                readByPath(layer.TexturePath) is not { } xbt ||
                XbtImage.TryDecodeRgba(xbt) is not { } decoded)
            {
                failed.Add(layer.Name);
                continue;
            }

            byte[] scaled = Resample(decoded.Rgba, decoded.Width, decoded.Height, DetailSize);
            GL.TexSubImage3D(TextureTarget.Texture2DArray, 0, 0, 0, layer.Index,
                DetailSize, DetailSize, 1, PixelFormat.Rgba, PixelType.UnsignedByte, scaled);
            loaded++;
        }
        LayersLoaded = loaded;
        FailedLayers = failed;

        GL.GenerateMipmap(GenerateMipmapTarget.Texture2DArray);
        GL.TexParameter(TextureTarget.Texture2DArray, TextureParameterName.TextureMinFilter,
            (int)TextureMinFilter.LinearMipmapLinear);
        GL.TexParameter(TextureTarget.Texture2DArray, TextureParameterName.TextureMagFilter, (int)TextureMagFilter.Linear);
        GL.TexParameter(TextureTarget.Texture2DArray, TextureParameterName.TextureWrapS, (int)TextureWrapMode.Repeat);
        GL.TexParameter(TextureTarget.Texture2DArray, TextureParameterName.TextureWrapT, (int)TextureWrapMode.Repeat);
    }

    private const int SdatQuadsPerSector = 64;

    /// <summary>
    /// Stitches one kind of atlas into a world-sized texture, kept DXT1-compressed. Each 128x128
    /// atlas lands at its block's world position; the transposed layout inside a sector is handled
    /// when sampling, not here.
    /// </summary>
    private int BuildAtlasTexture(
        TerrainMap map, Dictionary<int, string> sectorPaths, Func<string, byte[]?> readByPath, string kind)
    {
        int handle = GL.GenTexture();
        GL.BindTexture(TextureTarget.Texture2D, handle);
        GL.CompressedTexImage2D(TextureTarget.Texture2D, 0,
            InternalFormat.CompressedRgbS3tcDxt1Ext, WeightSide, WeightSide, 0,
            WeightSide * WeightSide / 2, IntPtr.Zero);
        GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.Linear);
        GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)TextureMagFilter.Linear);
        GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapS, (int)TextureWrapMode.ClampToEdge);
        GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapT, (int)TextureWrapMode.ClampToEdge);

        foreach (int atlasId in AtlasIds(map))
        {
            int row = atlasId / map.SectorsPerSide, col = atlasId % map.SectorsPerSide;
            if (ReadAtlas(sectorPaths, atlasId, readByPath, kind) is not { } dds)
            {
                continue;
            }

            // Skip the 128-byte DDS header; the rest is the DXT1 block stream.
            byte[] blocks = dds[128..];
            int expected = 128 * 128 / 2;
            if (blocks.Length < expected)
            {
                continue;
            }
            GL.CompressedTexSubImage2D(TextureTarget.Texture2D, 0,
                col * SdatQuadsPerSector, row * SdatQuadsPerSector, 128, 128,
                InternalFormat.CompressedRgbS3tcDxt1Ext, expected, blocks);
        }
        return handle;
    }

    /// <summary>One atlas covers a 2x2 block of sectors, so only even rows and columns name one.</summary>
    private static IEnumerable<int> AtlasIds(TerrainMap map)
    {
        for (int row = 0; row < map.SectorsPerSide; row += 2)
        {
            for (int col = 0; col < map.SectorsPerSide; col += 2)
            {
                yield return row * map.SectorsPerSide + col;
            }
        }
    }

    /// <summary>
    /// The baked lighting, one 64x64 map per sector rather than one per 2x2 block. Its DDS payload is
    /// uncompressed with two 16-bit channels (masks <c>0000FFFF</c> / <c>FFFF0000</c>); the high byte
    /// of each is kept, which is ample for a shading term and a quarter of the memory.
    /// </summary>
    private int BuildShadowTexture(TerrainMap map, Func<string, byte[]?> readByPath)
    {
        int handle = GL.GenTexture();
        GL.BindTexture(TextureTarget.Texture2D, handle);
        GL.PixelStore(PixelStoreParameter.UnpackAlignment, 1);
        GL.TexImage2D(TextureTarget.Texture2D, 0, PixelInternalFormat.Rg8, WeightSide, WeightSide, 0,
            PixelFormat.Rg, PixelType.UnsignedByte, IntPtr.Zero);
        GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.Linear);
        GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)TextureMagFilter.Linear);
        GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapS, (int)TextureWrapMode.ClampToEdge);
        GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapT, (int)TextureWrapMode.ClampToEdge);

        const int tile = SdatQuadsPerSector;
        var texels = new byte[tile * tile * 2];
        foreach ((string path, int sectorId) in map.Sectors)
        {
            string shadowPath = path.Replace($"sd{sectorId}.sdat", $"sd{sectorId}_shadow.xbt",
                StringComparison.OrdinalIgnoreCase);
            if (readByPath(shadowPath) is not { } xbt)
            {
                continue;
            }

            byte[] dds;
            try { dds = JackAll.Tools.Xbt.XbtTexture.Split(xbt).Dds; }
            catch (Exception) { continue; }
            if (dds.Length < 128 + tile * tile * 4)
            {
                continue;
            }

            for (int i = 0; i < tile * tile; i++)
            {
                texels[i * 2] = dds[128 + i * 4 + 1];
                texels[i * 2 + 1] = dds[128 + i * 4 + 3];
            }

            int row = sectorId / map.SectorsPerSide, col = sectorId % map.SectorsPerSide;
            GL.TexSubImage2D(TextureTarget.Texture2D, 0, col * tile, row * tile, tile, tile,
                PixelFormat.Rg, PixelType.UnsignedByte, texels);
        }
        return handle;
    }

    /// <summary>Derived from the sector's own path so the level folder never has to be recomputed.</summary>
    private static byte[]? ReadAtlas(
        Dictionary<int, string> sectorPaths, int atlasId, Func<string, byte[]?> readByPath, string kind)
    {
        if (!sectorPaths.TryGetValue(atlasId, out string? path))
        {
            return null;
        }
        string atlasPath = path.Replace($"sd{atlasId}.sdat", $"atlas{atlasId}_{kind}.xbt",
            StringComparison.OrdinalIgnoreCase);
        return readByPath(atlasPath) is { } xbt ? JackAll.Tools.Xbt.XbtTexture.Split(xbt).Dds : null;
    }

    /// <summary>Nearest-neighbour resample - the detail textures only need to agree on one size.</summary>
    private static byte[] Resample(byte[] rgba, int width, int height, int size)
    {
        if (width == size && height == size)
        {
            return rgba;
        }

        var output = new byte[size * size * 4];
        for (int y = 0; y < size; y++)
        {
            int sy = Math.Min(y * height / size, height - 1);
            for (int x = 0; x < size; x++)
            {
                int sx = Math.Min(x * width / size, width - 1);
                Array.Copy(rgba, (sy * width + sx) * 4, output, (y * size + x) * 4, 4);
            }
        }
        return output;
    }

    public void Bind(TextureUnit weights, TextureUnit sectorLayers, TextureUnit detail,
        TextureUnit colour, TextureUnit shadow)
    {
        GL.ActiveTexture(weights);
        GL.BindTexture(TextureTarget.Texture2D, WeightHandle);
        GL.ActiveTexture(sectorLayers);
        GL.BindTexture(TextureTarget.Texture2D, SectorLayerHandle);
        GL.ActiveTexture(detail);
        GL.BindTexture(TextureTarget.Texture2DArray, DetailArrayHandle);
        GL.ActiveTexture(colour);
        GL.BindTexture(TextureTarget.Texture2D, ColourHandle);
        GL.ActiveTexture(shadow);
        GL.BindTexture(TextureTarget.Texture2D, ShadowHandle);
    }

    public void Dispose()
    {
        GL.DeleteTexture(WeightHandle);
        GL.DeleteTexture(ColourHandle);
        GL.DeleteTexture(ShadowHandle);
        GL.DeleteTexture(SectorLayerHandle);
        GL.DeleteTexture(DetailArrayHandle);
    }
}
