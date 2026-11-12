using System.Numerics;
using Vortice.Direct3D;
using Vortice.Direct3D11;
using Vortice.DXGI;
using static JackAll.App.MapEditor.Gpu.GpuDevice;

namespace JackAll.App.MapEditor.Gpu;

/// <summary>What the terrain draws this frame. Exposure and the presentation switch are scene-wide -
/// SceneLighting - rather than options here, so the terrain cannot run at a different one to the
/// models.</summary>
public readonly record struct TerrainDrawOptions(
    bool ShowTextures, bool TintBySurfaceType, bool ShowShadow);

/// <summary>
/// The 3D terrain: two grid patches over one height texture - a fine 1-unit ring that follows the
/// camera, and a coarse whole-world backdrop with a hole cut in it where the ring lands. No terrain
/// geometry is ever built on the CPU, so edits only need a texture update. Shading is per-pixel:
/// finite-difference normals from the same texture, one sun.
/// </summary>
/// <remarks>
/// The hole matters: both patches read the same heightfield, but the backdrop steps over it in
/// whole cells, and a step that spans a dip draws a surface metres above the real ground. Left
/// covering the camera it hides roads, ditches and anything else set into the terrain, and no depth
/// bias can fix that because the geometry really is higher.
/// </remarks>
public sealed class TerrainMesh3D : IDisposable
{
    /// <summary>Vertices per patch edge; 257x257 keeps both patches under half a million triangles.</summary>
    private const int PatchSide = 257;

    private readonly HeightTexture _heights;
    private readonly SurfaceTypeTexture _surfaces;
    private readonly TerrainTextureSet? _textures;
    private readonly ShaderProgram _program;

    /// <summary>The same geometry from the sun's point of view, depth only.</summary>
    private readonly ShaderProgram _depthProgram;
    private readonly PatchConstants _lit;
    private readonly PatchConstants _depth;
    private readonly ID3D11Buffer _indices;
    private readonly int _indexCount;
    private readonly int _uSurfaceTint;
    private readonly int _uTextureMix;
    private readonly int _uShadowMix;
    private readonly int _uCameraPosition;

    public TerrainMesh3D(HeightTexture heights, SurfaceTypeTexture surfaces, TerrainTextureSet? textures)
    {
        _heights = heights;
        _surfaces = surfaces;
        _textures = textures;

        string constants =
            $"""
            static const float extent = {heights.Side - 1}.0;
            static const float metersPerRaw = 65535.0 / 128.0;
            static const int patchSide = {PatchSide};
            static const float detailFullDistance = {ShaderProgram.Literal(TerrainTextureSet.DetailFullDistance)};
            static const float detailFadeDistance = {ShaderProgram.Literal(TerrainTextureSet.DetailFadeDistance)};
            """;
        // The patch carries no vertex buffer, so where the ground is lives entirely in this one
        // function. The shadow pass runs the same copy: two of them is two chances for a shadow to
        // land somewhere the surface is not.
        string vertexHlsl =
            $$"""
            Texture2D heights : register(t0);
            {{constants}}
            float3 terrainVertex(uint id, out float2 plane)
            {
                int row = id / patchSide;
                int col = id % patchSide;
                plane = origin + float2(col, row) * spacing;
                float2 clamped = clamp(plane, 0.0, extent);
                return float3(clamped, heights.SampleLevel(linearClamp, clamped / extent, 0).r * metersPerRaw);
            }
            """;

        string depthParams =
            """
            cbuffer Params : register(b0)
            {
                float4x4 viewProjection;
                float2 origin;
                float spacing;
                float4 clipRect;
            };

            struct DepthVaryings
            {
                precise float4 position : SV_Position;
                float2 world : TEXCOORD0;
            };
            """;
        _depthProgram = new ShaderProgram(
            $$"""
            {{depthParams}}
            {{vertexHlsl}}
            DepthVaryings main(uint id : SV_VertexID)
            {
                DepthVaryings o;
                o.position = toTarget(mul(viewProjection, float4(terrainVertex(id, o.world), 1.0)));
                return o;
            }
            """,
            $$"""
            {{depthParams}}
            void main(DepthVaryings v)
            {
                // The same cut the lit pass makes. The coarse patch bridges dips metres above the
                // real ground, and a shadow cast off that surface lands nowhere near it.
                if (all(v.world > clipRect.xy) && all(v.world < clipRect.zw))
                {
                    discard;
                }
            }
            """);
        _depth = new PatchConstants(_depthProgram);

        string litParams =
            $$"""
            cbuffer Params : register(b0)
            {
                float4x4 viewProjection;
                float2 origin;
                float spacing;
                // 0 when the world ships no baked albedo, which forces the detail blend to every distance.
                float bakedMix;
                // The world-space rectangle the fine ring covers, as (minX, minY, maxX, maxY). The
                // coarse pass throws away everything inside it; the fine pass passes an inverted
                // rectangle, which no pixel can be inside.
                float4 clipRect;
                float3 cameraPosition;
                float shadowMix;
                float surfaceTint;
                float2 heightRange;
                float textureMix;
                float weightSide;
                float sectorsPerSide;
                float layerTiling[{{TerrainTextureSet.MaxLayers}}];
                float layerProjAxis[{{TerrainTextureSet.MaxLayers}}];
                // Layers sharing a texture share a slice, so a layer index is not a slice index.
                float layerSlice[{{TerrainTextureSet.MaxLayers}}];
            };

            struct Varyings
            {
                precise float4 position : SV_Position;
                float2 world : TEXCOORD0;
                float viewDistance : TEXCOORD1;
            };
            """;
        _program = new ShaderProgram(
            $$"""
            {{litParams}}
            {{vertexHlsl}}
            Varyings main(uint id : SV_VertexID)
            {
                Varyings o;
                float3 ground = terrainVertex(id, o.world);
                o.viewDistance = distance(ground, cameraPosition);
                o.position = toTarget(mul(viewProjection, float4(ground, 1.0)));
                return o;
            }
            """,
            $$"""
            {{litParams}}
            Texture2D heights : register(t0);
            Texture2D surfaceTypes : register(t1);
            Texture2D surfacePalette : register(t2);
            Texture2D blendWeights : register(t3);
            Texture2D sectorLayers : register(t4);
            Texture2DArray detailTextures : register(t5);
            Texture2D terrainColour : register(t6);
            Texture2D terrainShadow : register(t7);
            Texture2D bakedDiffuse : register(t8);
            {{SceneLighting.SkyHlsl}}
            {{SceneLighting.SurfaceHlsl}}
            {{SceneLighting.ShadowHlsl}}
            {{constants}}

            // The four layer indices this sector blends, as the RGBA of one texel.
            float4 sectorLayerIndices(float2 world)
            {
                float2 sectorUv = (floor(world / 64.0) + 0.5) / sectorsPerSide;
                return sectorLayers.SampleLevel(pointClamp, sectorUv, 0) * 255.0;
            }

            // Every per-sector square the cooker writes is stored transposed, whichever file it lands
            // in - a 2x2 atlas quadrant or a sector's own shadow map. Verified by reassembling a
            // campaign cell and checking features run unbroken across sector boundaries.
            int2 atlasTexel(int2 w)
            {
                int2 sector = (w / 64) * 64;
                return sector + (w - sector).yx;
            }

            // Because of that transpose, texels next to each other in memory are not next to each
            // other in the world, so hardware filtering would blend unrelated ground. The four taps
            // are mapped individually and blended here instead.
            float3 sampleAtlas(Texture2D tex, float2 pos)
            {
                float2 p = clamp(pos, 0.5, weightSide - 0.5) - 0.5;
                int2 b = int2(floor(p));
                float2 f = p - float2(b);
                int2 hi = int(weightSide) - 1;
                float3 c00 = tex.Load(int3(atlasTexel(clamp(b, 0, hi)), 0)).rgb;
                float3 c10 = tex.Load(int3(atlasTexel(clamp(b + int2(1, 0), 0, hi)), 0)).rgb;
                float3 c01 = tex.Load(int3(atlasTexel(clamp(b + int2(0, 1), 0, hi)), 0)).rgb;
                float3 c11 = tex.Load(int3(atlasTexel(clamp(b + int2(1, 1), 0, hi)), 0)).rgb;
                return lerp(lerp(c00, c10, f.x), lerp(c01, c11, f.x), f.y);
            }

            // A layer is projected along one axis rather than always from above, which is how cliffs
            // avoid the stretching a top-down projection would give them. Linear in both inputs, so
            // the same call turns their screen derivatives into the uv's.
            float2 layerUv(int layer, float2 world, float height)
            {
                int axis = int(layerProjAxis[layer] + 0.5);
                if (axis == 0) { return float2(world.y, height); }
                if (axis == 1) { return float2(world.x, height); }
                return world;
            }

            // The detail taps sit under branches on distance and layer, where HLSL gives no implicit
            // gradients, so the screen derivatives of world and height come in from outside them.
            float3 blendedDetail(float2 world, float height, float4 dWorld, float2 dHeight)
            {
                float3 w = sampleAtlas(blendWeights, world);

                // The mask carries three weights but a sector names four layers: the fourth weight is
                // whatever the three leave over, and it belongs to the low byte - the cliff
                // projection. That is why a black mask texel means solid rock rather than nothing,
                // and why the sectors leaving the low byte unused are the ones with no cliffs.
                float4 idx = sectorLayerIndices(world);
                float chosen[4] = { idx.a, idx.b, idx.g, idx.r };
                float weight[4] = { w.r, w.g, w.b, max(0.0, 1.0 - (w.r + w.g + w.b)) };

                float3 colour = 0.0;
                float used = 0.0;
                [unroll] for (int i = 0; i < 4; i++)
                {
                    int layer = int(chosen[i] + 0.5);
                    if (layer >= {{TerrainTextureSet.MaxLayers}}) { continue; }
                    // Only a guard against a zero: the real periods run from well under a metre
                    // (sand, which leans on a fine normal map the game has and this does not) up to
                    // tens of metres for cliff rock.
                    float tiling = max(layerTiling[layer], 0.05);
                    float2 uv = layerUv(layer, world, height) / tiling;
                    float2 uvDx = layerUv(layer, dWorld.xy, dHeight.x) / tiling;
                    float2 uvDy = layerUv(layer, dWorld.zw, dHeight.y) / tiling;
                    colour += weight[i] * detailTextures.SampleGrad(anisoWrap,
                        float3(uv, layerSlice[layer]), uvDx, uvDy).rgb;
                    used += weight[i];
                }

                // Only reachable now if a sector names no usable layer at all.
                if (used <= 0.001) { return 0.2159; }

                // The colour atlas is a baked per-texel tint over the blended detail, mid-grey neutral.
                float3 tint = sampleAtlas(terrainColour, world);
                // 4.632 is 1/linear(0.5): the atlas is authored mid-grey neutral, and mid-grey
                // sRGB is 0.2159 once the sampler decodes it.
                return (colour / used) * tint * 4.632;
            }

            // How much of the detail blend survives at this distance. The engine crossfades the
            // ground into a baked albedo rather than letting the detail textures run out to the
            // horizon on a high mip - which is why its far terrain never shows a repeat - and it is
            // why everything past the fade costs one filtered tap instead of twelve fetches.
            float detailWeight(float viewDistance)
            {
                if (bakedMix < 0.5) { return 1.0; }
                return saturate((detailFadeDistance - viewDistance)
                    / (detailFadeDistance - detailFullDistance));
            }

            // Sampled like any ordinary texture: this one is straightened at upload rather than at
            // every tap, so it mips and filters on its own. No tint either - the engine multiplies
            // the colour atlas into the detail path only, because the bake already carries it.
            float3 groundColour(float2 world, float viewDistance, float height, float4 dWorld, float2 dHeight)
            {
                float detail = detailWeight(viewDistance);
                float2 uv = world / weightSide;
                float2 uvDx = dWorld.xy / weightSide;
                float2 uvDy = dWorld.zw / weightSide;
                if (detail <= 0.0) { return bakedDiffuse.SampleGrad(anisoClamp, uv, uvDx, uvDy).rgb; }
                if (detail >= 1.0) { return blendedDetail(world, height, dWorld, dHeight); }
                return lerp(bakedDiffuse.SampleGrad(anisoClamp, uv, uvDx, uvDy).rgb,
                    blendedDetail(world, height, dWorld, dHeight), detail);
            }

            float4 main(Varyings v) : SV_Target
            {
                // The two passes read one heightfield at different steps, so where the coarse step
                // bridges a dip - a road cutting is the obvious one - its surface sits metres above
                // the real ground and buries whatever is down there. Drawing it only outside the
                // fine ring is the fix; a depth bias cannot help when the geometry is genuinely
                // higher.
                if (all(v.world > clipRect.xy) && all(v.world < clipRect.zw))
                {
                    discard;
                }

                float2 uv = v.world / extent;
                float texel = 1.0 / extent;
                float hl = heights.SampleLevel(linearClamp, uv - float2(texel, 0.0), 0).r;
                float hr = heights.SampleLevel(linearClamp, uv + float2(texel, 0.0), 0).r;
                float hd = heights.SampleLevel(linearClamp, uv - float2(0.0, texel), 0).r;
                float hu = heights.SampleLevel(linearClamp, uv + float2(0.0, texel), 0).r;
                float3 normal = normalize(float3((hl - hr) * metersPerRaw, (hd - hu) * metersPerRaw, 2.0));

                float h = heights.SampleLevel(linearClamp, uv, 0).r;
                float height = h * metersPerRaw;
                float4 dWorld = float4(ddx(v.world), ddy(v.world));
                float2 dHeight = float2(ddx(height), ddy(height));
                float shade = (h - heightRange.x) / (heightRange.y - heightRange.x);
                float3 base = lerp(float3(0.24, 0.30, 0.18), float3(0.62, 0.56, 0.44), shade);

                // Each of the three below is a layer toggle, uploaded as 0 or 1, and each pulls its
                // own fetches - a dozen for the blend alone. Gating on the constant skips them where
                // scaling by it afterwards would have paid for them and thrown the result away.
                if (textureMix > 0.0)
                {
                    base = groundColour(v.world, v.viewDistance, height, dWorld, dHeight);
                }

                // Baked lighting: the low 16-bit channel, rescaled from the narrow band the data
                // occupies so it reads as shading rather than a flat dimming.
                float baked = 1.0;
                if (shadowMix > 0.0)
                {
                    baked = saturate((sampleAtlas(terrainShadow, v.world).r - 0.5) / 0.42);
                }

                // The surface id indexes the palette texture directly; r is the id scaled to 0..1,
                // so the lookup lands mid-texel at (id + 0.5) / 256.
                if (surfaceTint > 0.0)
                {
                    float id = surfaceTypes.SampleLevel(pointClamp, uv, 0).r;
                    float3 material = surfacePalette.SampleLevel(pointClamp,
                        float2(id * (255.0 / 256.0) + (0.5 / 256.0), 0.5), 0).rgb;
                    base = base * 0.35 + material * 0.75;
                }

                // The bake multiplies the sun rather than replacing N.L, which is what the engine's
                // own terrain shader does with its self-shadow map - relief from the normals
                // survives, and the bake darkens what the sun cannot reach. (The bake does
                // correlate 0.77 with dot(N, L), but a self-shadow map would: slopes facing away
                // from the sun are both dark and self-shadowed, so the correlation cannot separate
                // a lightmap from a shadow term. The engine's shader can, and did.)
                float light = max(dot(normal, sunDirection), 0.0);
                float bakedSun = light * baked;
                if (demo < 0.5)
                {
                    return float4(shadeDiffuse(base, normal, bakedSun, 1.0), 1.0);
                }

                float3 worldPos = float3(v.world, height);

                // The bake was lit by a 10-degree sun and this scene is lit by a 44-degree one, so
                // the two cannot both be right in the same place - only in different ones. Cast
                // shadows out to the last cascade, the bake past it, crossfaded where they meet.
                float sunAmount = lerp(
                    bakedSun,
                    light * sampleShadow(worldPos, v.viewDistance, light),
                    shadowFade(v.viewDistance));
                float3 lit = shadeSurface(base, normal, normalize(cameraPosition - worldPos),
                    sunDirection, sunAmount, 0.0, 0.0, v.position.xy);
                return float4(applyHaze(lit, v.viewDistance, height), 1.0);
            }
            """);
        _lit = new PatchConstants(_program);
        _uSurfaceTint = _program.Offset("surfaceTint");
        _uTextureMix = _program.Offset("textureMix");
        _uShadowMix = _program.Offset("shadowMix");
        _uCameraPosition = _program.Offset("cameraPosition");
        _program.Set(_program.Offset("heightRange"), new Vector2(heights.MinNormalized, heights.MaxNormalized));
        if (textures is not null)
        {
            _program.Set(_program.Offset("bakedMix"), textures.HasDiffuseAtlas ? 1f : 0f);
            _program.Set(_program.Offset("weightSide"), (float)textures.WeightSide);
            _program.Set(_program.Offset("sectorsPerSide"), textures.WeightSide / 64f);
            _program.Set(_program.Offset("layerTiling"), textures.Tiling);
            _program.Set(_program.Offset("layerProjAxis"), textures.ProjectionAxis);
            _program.Set(_program.Offset("layerSlice"), textures.LayerSlice);
        }

        // Positions come from SV_VertexID; only the triangulation needs real data.
        var indices = new uint[(PatchSide - 1) * (PatchSide - 1) * 6];
        int cursor = 0;
        for (int row = 0; row < PatchSide - 1; row++)
        {
            for (int col = 0; col < PatchSide - 1; col++)
            {
                uint v = (uint)(row * PatchSide + col);
                indices[cursor++] = v;
                indices[cursor++] = v + 1;
                indices[cursor++] = v + PatchSide;
                indices[cursor++] = v + 1;
                indices[cursor++] = v + PatchSide + 1;
                indices[cursor++] = v + PatchSide;
            }
        }
        _indexCount = indices.Length;
        _indices = Device.CreateBuffer(indices, BindFlags.IndexBuffer, ResourceUsage.Immutable);
    }

    public void Draw(Matrix4x4 viewProjection, Vector3 cameraPosition, TerrainDrawOptions options)
    {
        using GpuState pass = new();
        _program.Use();
        _program.Set(_lit.ViewProjection, viewProjection);
        _program.Set(_uSurfaceTint, options.TintBySurfaceType ? 1f : 0f);
        _program.Set(_uTextureMix, _textures is not null && options.ShowTextures ? 1f : 0f);
        _program.Set(_uShadowMix, _textures is not null && options.ShowShadow ? 1f : 0f);
        _program.Set(_uCameraPosition, cameraPosition);
        Context.VSSetShaderResource(0, _heights.View);
        Context.PSSetShaderResource(0, _heights.View);
        Context.PSSetShaderResource(1, _surfaces.Ids(options.TintBySurfaceType)!);
        Context.PSSetShaderResource(2, _surfaces.PaletteView);
        _textures?.Bind(weights: 3, sectorLayers: 4, detail: 5, colour: 6, shadow: 7, bakedDiffuse: 8);
        DrawPatches(_program, _lit, cameraPosition);
    }

    /// <summary>The same two patches from the sun's point of view. Casting terrain matters for what
    /// stands on it - a hut in a hill's shade stays lit otherwise, because the baked lightmap the
    /// terrain carries covers only the ground.</summary>
    public void DrawDepth(Matrix4x4 lightViewProjection, Vector3 cameraPosition)
    {
        using GpuState pass = new();
        _depthProgram.Use();
        _depthProgram.Set(_depth.ViewProjection, lightViewProjection);
        Context.VSSetShaderResource(0, _heights.View);
        DrawPatches(_depthProgram, _depth, cameraPosition);
    }

    /// <summary>Where one program keeps the constants a patch draw sets.</summary>
    private readonly record struct PatchConstants(int ViewProjection, int Origin, int Spacing, int ClipRect)
    {
        public PatchConstants(ShaderProgram program)
            : this(program.Offset("viewProjection"), program.Offset("origin"), program.Offset("spacing"),
                program.Offset("clipRect"))
        {
        }
    }

    /// <summary>The coarse backdrop then the fine ring, through whichever program is passed in - the
    /// lit pass and the shadow pass have to submit identical geometry.</summary>
    private void DrawPatches(ShaderProgram program, PatchConstants constants, Vector3 cameraPosition)
    {
        Context.IASetIndexBuffer(_indices, Format.R32_UInt, 0);

        const float fineSpacing = 1f;
        float coarseSpacing = (_heights.Side - 1f) / (PatchSide - 1);
        float half = (PatchSide - 1) * fineSpacing / 2f;
        // Snapped to whole units so the fine grid doesn't swim as the camera pans.
        float ox = MathF.Floor(cameraPosition.X - half);
        float oy = MathF.Floor(cameraPosition.Y - half);

        // Coarse whole-world backdrop, cut away wherever the fine ring will cover it, then the fine
        // ring itself. The hole is inset by one coarse cell so the two still overlap in a thin band
        // rather than leaving a crack where their tessellations disagree - and the depth bias
        // keeps the fine ring winning inside that band.
        GpuState.DepthBias(true);
        program.Set(constants.ClipRect, new Vector4(
            ox + coarseSpacing, oy + coarseSpacing,
            ox + half * 2f - coarseSpacing, oy + half * 2f - coarseSpacing));
        DrawPatch(program, constants, coarseSpacing, originX: 0, originY: 0);
        GpuState.DepthBias(false);

        // An inverted rectangle: no pixel is inside it, so the fine ring clips nothing.
        program.Set(constants.ClipRect, new Vector4(1f, 1f, -1f, -1f));
        DrawPatch(program, constants, fineSpacing, ox, oy);
    }

    private void DrawPatch(ShaderProgram program, PatchConstants constants, float spacing, float originX, float originY)
    {
        program.Set(constants.Origin, new Vector2(originX, originY));
        program.Set(constants.Spacing, spacing);
        program.DrawIndexed(_indexCount);
        FrameStats.Drawn(_indexCount / 3);
    }

    public void Dispose()
    {
        _program.Dispose();
        _depthProgram.Dispose();
        _indices.Dispose();
    }
}
