using JackAll.Tools.Xbt;
using JackAll.Tools.World;
using Vortice.Direct3D11;
using Vortice.DXGI;
using static JackAll.App.MapEditor.Gpu.GpuDevice;

namespace JackAll.App.MapEditor.Gpu;

/// <summary>
/// Everything the terrain needs to look like the game does: the world-wide blend weights stitched
/// from the <c>atlas&lt;id&gt;_mask.xbt</c> files, a per-sector lookup of which four layers that
/// sector blends, and the layer textures themselves as one array.
/// </summary>
/// <remarks>
/// The masks stay DXT1-compressed on the GPU - they are laid straight into a world-sized
/// compressed texture, one 128x128 atlas at a time, which is block-aligned because a sector is 64
/// texels across. That costs about a sixth of what decoding them would.
/// </remarks>
public sealed class TerrainTextureSet : IDisposable
{
    /// <summary>
    /// The side of every slice of the detail array, and the size the game authors its terrain
    /// textures at: a 1024 file whose header points at the 2048 top level held in a separate
    /// <c>_mip0.xbt</c>. Over the 3.2-metre period a Tiling of 20 works out to, that is about 640
    /// texels per metre - the number that decides whether close-up ground reads as ground or as a
    /// blown-up thumbnail.
    /// </summary>
    private const int DetailSide = 2048;

    /// <summary>Levels of a 2048 chain down to 1x1. Every one is written here, so nothing is
    /// generated and no level is left holding whatever the driver had in it.</summary>
    private const int DetailLevels = 12;

    private const Format DetailFormat = Format.BC1_UNorm_SRgb;

    /// <summary>Mid-grey as a DXT1 endpoint - what a layer draws when its texture cannot be used.</summary>
    private const ushort NeutralGrey = 0x8410;

    /// <summary>
    /// World units a sector spans, which is the unit a layer's <c>Tiling</c> counts repeats in - so
    /// the texture's period is <c>SectorSide / Tiling</c> world units, not <c>Tiling</c> of them.
    /// </summary>
    /// <remarks>
    /// Read off the shipped textures rather than the engine: at one repeat per <c>Tiling</c> world
    /// units the tyres in <c>stiresjunk01_d</c> (Tiling 30) would be 10 m across, the stones in
    /// <c>urban_ground_d</c> (20) 0.8 m, and the dried-mud cells in <c>dessert_crackedearth_d</c>
    /// (20) 1.7 m. Dividing into the sector instead puts all three at 0.7 m, 13 cm and 27 cm, and
    /// gives the mountain rock layers (Tiling 2-3) the 20-30 m period a cliff face wants instead of
    /// a 2 m one. The engine's own baked far-field albedo agrees: it is one texel per world unit and
    /// carries no periodicity at a 20-texel lag, which a 20-unit repeat could not hide.
    ///
    /// What is NOT verified is the constant itself. The pixel shader multiplies world XY by
    /// <c>_DetailUVScaling</c>, and the XML's Tiling reaches the sector's static data untouched
    /// (Dunia.dll / FarCry2_server: LoadTerrainLayersFromXML -> STerrainLayer -> InitializeLayers),
    /// so the conversion happens in renderer code not yet located. 64 is the reading the measurements
    /// support and the natural one for a per-sector terrain editor, but it is inference.
    /// </remarks>
    private const float SectorSide = 64f;

    public ID3D11ShaderResourceView WeightView { get; }
    public ID3D11ShaderResourceView ColourView { get; }
    public ID3D11ShaderResourceView ShadowView { get; }

    /// <summary>
    /// The baked far-field albedo, one texel per world unit: what the ground fades into past
    /// <see cref="DetailFullDistance"/> instead of showing the detail textures at a high mip.
    /// </summary>
    public ID3D11ShaderResourceView DiffuseView { get; }

    /// <summary>False when the world ships no <c>atlas&lt;id&gt;_diffuse.xbt</c> at all, in which
    /// case the detail blend has to carry every distance on its own.</summary>
    public bool HasDiffuseAtlas { get; }

    /// <summary>
    /// How far the detail blend holds at full strength, and where it has faded entirely into the
    /// baked albedo. Both are the engine's own, from the <c>&lt;Terrain&gt;</c> block of
    /// <c>engine\settings\defaultrenderconfig.xml</c>: <c>TerrainDetailBlendViewDistance</c> and
    /// <c>TerrainDetailViewDistance</c>, 64 and 512 on high and above (medium 64/200, low 10/20).
    /// </summary>
    /// <remarks>Which of the two the ramp runs between is a reading, not a trace: the shader takes
    /// them as one <c>saturate(distance * a + b)</c> pair. It is the reading that makes all three
    /// profiles sensible, and it matches the community note that raising the blend distance costs
    /// performance - which only holds if it is where detail ends rather than a fade width.</remarks>
    public const float DetailFullDistance = 64f;

    /// <inheritdoc cref="DetailFullDistance"/>
    public const float DetailFadeDistance = 512f;

    /// <summary>The side of one baked atlas, covering a 2x2 block of sectors.</summary>
    private const int AtlasSide = 128;

    /// <summary>Levels of the baked albedo kept on the GPU. Level 4 leaves a sector one 4x4 block
    /// across, which is as far as its transpose can be undone by moving whole blocks.</summary>
    private const int BakedLevels = 5;

    public ID3D11ShaderResourceView SectorLayerView { get; }
    public ID3D11ShaderResourceView DetailView { get; }
    public int WeightSide { get; }
    public int LayersLoaded { get; }

    /// <summary>Layers whose texture could not be read or decoded; they draw neutral grey.</summary>
    public IReadOnlyList<string> FailedLayers { get; }

    /// <summary>World units each layer's texture repeats over, indexed by layer index. Derived from
    /// the layer's <c>Tiling</c> via <see cref="SectorSide"/>, not equal to it.</summary>
    public float[] Tiling { get; } = new float[MaxLayers];

    /// <summary>Projection plane per layer: 0 = X, 1 = Y, 2 = Z.</summary>
    public float[] ProjectionAxis { get; } = new float[MaxLayers];

    /// <summary>Which slice of the detail array each layer samples. Layers sharing a texture share a
    /// slice - world1 names 45 layers over 25 distinct textures - and slice 0 is the neutral grey a
    /// layer falls back to.</summary>
    public float[] LayerSlice { get; } = new float[MaxLayers];

    /// <summary>What the detail array costs on the GPU, for the status line.</summary>
    public long DetailBytes { get; }

    public const int MaxLayers = 64;

    public TerrainTextureSet(
        TerrainMap map,
        SectorDetailLayers detailLayers,
        TerrainLayerTable table,
        Func<string, byte[]?> readByPath)
    {
        WeightSide = map.SectorsPerSide * SdatQuadsPerSector;

        Dictionary<int, string> sectorPaths = map.Sectors.ToDictionary(s => s.SectorId, s => s.Path);
        (WeightView, _) = BuildAtlasTexture(map, sectorPaths, readByPath, "mask");
        (ColourView, _) = BuildAtlasTexture(map, sectorPaths, readByPath, "color");
        (DiffuseView, int bakedAtlases) = BuildBakedDiffuse(map, sectorPaths, readByPath);
        HasDiffuseAtlas = bakedAtlases > 0;
        ShadowView = BuildShadowTexture(map, readByPath);

        // One texel per sector, carrying its four layer indices.
        SectorLayerView = Upload.Texture(detailLayers.Indices, Format.R8G8B8A8_UNorm,
            map.SectorsPerSide, map.SectorsPerSide);

        Array.Fill(Tiling, SectorSide / 20f);
        Array.Fill(ProjectionAxis, 2f);

        (LayersLoaded, FailedLayers, int slices, DetailView) = BuildDetailArray(table, readByPath);
        DetailBytes = ArrayBytes(slices);
    }

    /// <summary>
    /// Fills the detail array with the shipped DXT1 blocks - no decode, no resample, no generated
    /// mips - and returns how many layers found a texture, which ones did not, and how many slices
    /// the array ended up with.
    /// </summary>
    /// <remarks>
    /// One slice per distinct texture rather than per layer: world1 spreads 45 layers over 25
    /// textures, so this is close to half the memory and half the archive reads. What a layer keeps
    /// to itself is its tiling and projection, not its pixels.
    /// </remarks>
    private (int Loaded, IReadOnlyList<string> Failed, int Slices, ID3D11ShaderResourceView Array) BuildDetailArray(
        TerrainLayerTable table, Func<string, byte[]?> readByPath)
    {
        var failed = new List<string>();
        var layersByPath = new Dictionary<string, List<TerrainLayer>>(StringComparer.OrdinalIgnoreCase);
        foreach (TerrainLayer layer in table.Layers)
        {
            if (layer.Index >= MaxLayers)
            {
                continue;
            }
            Tiling[layer.Index] = SectorSide / layer.Tiling;
            ProjectionAxis[layer.Index] = layer.ProjectionAxis;

            if (string.IsNullOrEmpty(layer.TexturePath))
            {
                failed.Add(layer.Name);
                continue;
            }
            if (!layersByPath.TryGetValue(layer.TexturePath, out List<TerrainLayer>? group))
            {
                layersByPath[layer.TexturePath] = group = [];
            }
            group.Add(layer);
        }

        // Slice 0 is the fallback grey, so a layer index no table entry claims - a sector's mask can
        // name one - draws flat instead of out of whatever the driver left in the slice.
        int slices = layersByPath.Count + 1;
        var array = new Upload.Staged(DetailFormat, DetailSide, DetailSide, DetailLevels, slices);
        FillSlice(array, 0, surface: null);

        int loaded = 0, slice = 1;
        foreach ((string path, List<TerrainLayer> group) in layersByPath)
        {
            int repeat = FillSlice(array, slice, ReadDetail(path, readByPath));
            if (repeat == 0)
            {
                failed.AddRange(group.Select(l => l.Name));
                slice++;
                continue;
            }

            foreach (TerrainLayer layer in group)
            {
                LayerSlice[layer.Index] = slice;
                Tiling[layer.Index] = SectorSide / layer.Tiling * repeat;
                loaded++;
            }
            slice++;
        }
        return (loaded, failed, slices, array.Create());
    }

    /// <summary>
    /// Null unless the texture is a square power-of-two DXT1 - which every shipped terrain layer is,
    /// and the only thing one DXT1 array can hold. Reads the top level out of the <c>_mip0.xbt</c>
    /// companion the header names, without which every layer would be half the size it should be.
    /// </summary>
    private static DdsSurface? ReadDetail(string path, Func<string, byte[]?> readByPath)
    {
        if (readByPath(path) is not { } xbt || XbtSurface.TryRead(xbt, readByPath) is not { } surface)
        {
            return null;
        }

        bool squarePowerOfTwo = surface.Width == surface.Height &&
            surface.Width > 0 && (surface.Width & (surface.Width - 1)) == 0;
        return squarePowerOfTwo && surface.FourCc == DdsSurface.FourCcDxt1 ? surface : null;
    }

    /// <summary>
    /// Writes one texture's whole chain into a slice and returns how many times it repeats across it;
    /// 0 for a slice with no usable texture, which is filled neutral instead.
    /// </summary>
    /// <remarks>
    /// A texture smaller than the slice is repeated rather than magnified - a whole-block copy, so
    /// still nothing is decoded - and the caller multiplies its layers' tiling by the same factor,
    /// which lands the pattern at exactly the world scale the game gives it.
    /// </remarks>
    private static int FillSlice(Upload.Staged array, int slice, DdsSurface? surface)
    {
        if (surface is null)
        {
            for (int level = 0; level < DetailLevels; level++)
            {
                FillLevel(array, slice, level, NeutralGrey);
            }
            return 0;
        }

        int top = surface.Width, skipped = 0;
        while (top > DetailSide)
        {
            top /= 2;
            skipped++;
        }
        int repeat = DetailSide / top;
        ushort average = Average(surface);

        for (int level = 0; level < DetailLevels; level++)
        {
            int source = skipped + level;
            int side = top >> level;

            // Below 4x4 a repeat is no longer a whole number of blocks, so from there down the slice
            // gets the texture's own average colour - which is what a mip that small converges to.
            if (source >= surface.Mips.Count || (side < 4 && repeat > 1))
            {
                FillLevel(array, slice, level, average);
                continue;
            }

            byte[] blocks = surface.Mips[source];
            for (int y = 0; y < repeat; y++)
            {
                for (int x = 0; x < repeat; x++)
                {
                    array.Write(level, slice, x * side, y * side, side, side, blocks);
                }
            }
        }
        return repeat;
    }

    /// <summary>One flat colour across a level of a slice, as DXT1 blocks with both endpoints equal
    /// and every index 0.</summary>
    private static void FillLevel(Upload.Staged array, int slice, int level, ushort colour)
    {
        int side = DetailSide >> level;
        var blocks = new byte[Upload.LevelBytes(DetailFormat, side)];
        for (int i = 0; i < blocks.Length; i += Upload.BlockBytes(DetailFormat))
        {
            blocks[i] = blocks[i + 2] = (byte)colour;
            blocks[i + 1] = blocks[i + 3] = (byte)(colour >> 8);
        }
        array.Write(level, slice, 0, 0, side, side, blocks);
    }

    /// <summary>The texture's overall colour, read straight off the endpoints of its smallest mip -
    /// no decode needed, they are already 5:6:5.</summary>
    private static ushort Average(DdsSurface surface)
    {
        byte[] smallest = surface.Mips[^1];
        int c0 = smallest[0] | smallest[1] << 8;
        int c1 = smallest[2] | smallest[3] << 8;
        int r = ((c0 >> 11) + (c1 >> 11)) / 2;
        int g = (((c0 >> 5) & 0x3F) + ((c1 >> 5) & 0x3F)) / 2;
        int b = ((c0 & 0x1F) + (c1 & 0x1F)) / 2;
        return (ushort)(r << 11 | g << 5 | b);
    }

    /// <summary>Every level of every slice, for the status line.</summary>
    private static long ArrayBytes(int slices)
    {
        long total = 0;
        for (int level = 0; level < DetailLevels; level++)
        {
            total += Upload.LevelBytes(DetailFormat, DetailSide >> level);
        }
        return total * slices;
    }

    private const int SdatQuadsPerSector = 64;

    /// <summary>
    /// Stitches one kind of atlas into a world-sized texture, kept DXT1-compressed. Each 128x128
    /// atlas lands at its block's world position; the transposed layout inside a sector is handled
    /// when sampling, not here.
    /// </summary>
    /// <returns>The view, and how many atlases actually landed in it - zero means the world ships
    /// none of that kind.</returns>
    private (ID3D11ShaderResourceView View, int Loaded) BuildAtlasTexture(
        TerrainMap map, Dictionary<int, string> sectorPaths, Func<string, byte[]?> readByPath, string kind)
    {
        int loaded = 0;
        // The colour atlas is albedo; the mask beside it is blend data. Only one is sRGB.
        Format format = kind == "color" ? Format.BC1_UNorm_SRgb : Format.BC1_UNorm;
        var texture = new Upload.Staged(format, WeightSide, WeightSide);

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
            texture.Write(0, 0, col * SdatQuadsPerSector, row * SdatQuadsPerSector, 128, 128, blocks);
            loaded++;
        }
        return (texture.Create(), loaded);
    }

    /// <summary>
    /// The baked far-field albedo, stitched world-sized with its shipped mip chain and, unlike the
    /// mask and colour atlases, straightened out of the cooker's per-sector transpose first.
    /// </summary>
    /// <remarks>
    /// Straightening it is what lets the hardware filter and mip it, which matters more here than
    /// anywhere else: this is the texture the whole distance draws from, at one texel per world unit,
    /// where a screen pixel covers several texels and point-ish sampling would crawl. It stays
    /// lossless - a transpose inside a 64-texel sector is a move of whole DXT1 blocks plus a mirror
    /// of the 4x4 selector grid inside each, and neither touches the endpoints.
    /// </remarks>
    private (ID3D11ShaderResourceView View, int Loaded) BuildBakedDiffuse(
        TerrainMap map, Dictionary<int, string> sectorPaths, Func<string, byte[]?> readByPath)
    {
        var texture = new Upload.Staged(Format.BC1_UNorm_SRgb, WeightSide, WeightSide, BakedLevels);

        int loaded = 0;
        foreach (int atlasId in AtlasIds(map))
        {
            if (ReadAtlas(sectorPaths, atlasId, readByPath, "diffuse") is not { } dds ||
                DdsSurface.TryParse(dds) is not { Width: AtlasSide, Height: AtlasSide } surface ||
                surface.FourCc != DdsSurface.FourCcDxt1)
            {
                continue;
            }

            int row = atlasId / map.SectorsPerSide, col = atlasId % map.SectorsPerSide;
            for (int level = 0; level < Math.Min(BakedLevels, surface.Mips.Count); level++)
            {
                int sector = SdatQuadsPerSector >> level;
                int side = AtlasSide >> level;
                byte[] blocks = DxtSectorTranspose.Mirror(surface.Mips[level], side, sector);
                texture.Write(level, 0, col * sector, row * sector, side, side, blocks);
            }
            loaded++;
        }
        return (texture.Create(), loaded);
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
    /// of the first is kept, the one the terrain shades with.
    /// </summary>
    private ID3D11ShaderResourceView BuildShadowTexture(TerrainMap map, Func<string, byte[]?> readByPath)
    {
        var texture = new Upload.Staged(Format.R8_UNorm, WeightSide, WeightSide);

        const int tile = SdatQuadsPerSector;
        var texels = new byte[tile * tile];
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
                texels[i] = dds[128 + i * 4 + 1];
            }

            int row = sectorId / map.SectorsPerSide, col = sectorId % map.SectorsPerSide;
            texture.Write(0, 0, col * tile, row * tile, tile, tile, texels);
        }
        return texture.Create();
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

    public void Bind(int weights, int sectorLayers, int detail, int colour, int shadow, int bakedDiffuse)
    {
        Context.PSSetShaderResource((uint)weights, WeightView);
        Context.PSSetShaderResource((uint)sectorLayers, SectorLayerView);
        Context.PSSetShaderResource((uint)detail, DetailView);
        Context.PSSetShaderResource((uint)colour, ColourView);
        Context.PSSetShaderResource((uint)shadow, ShadowView);
        Context.PSSetShaderResource((uint)bakedDiffuse, DiffuseView);
    }

    public void Dispose()
    {
        foreach (ID3D11ShaderResourceView view in (ID3D11ShaderResourceView[])
                     [WeightView, ColourView, DiffuseView, ShadowView, SectorLayerView, DetailView])
        {
            view.Dispose();
        }
    }
}
