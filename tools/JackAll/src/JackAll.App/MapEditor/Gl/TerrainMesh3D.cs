using OpenTK.Graphics.OpenGL4;
using OpenTK.Mathematics;

namespace JackAll.App.MapEditor.Gl;

/// <summary>What the terrain draws this frame. Exposure and the presentation switch are scene-wide -
/// SceneLighting - rather than options here, so the terrain cannot run at a different one to the
/// models.</summary>
public readonly record struct TerrainDrawOptions(
    bool ShowTextures, bool TintBySurfaceType, bool ShowShadow);

/// <summary>
/// The 3D terrain: two grid patches over one height texture - a fine 1-unit ring that follows the
/// camera, and a coarse whole-world backdrop with a hole cut in it where the ring lands. No terrain
/// geometry is ever built on the CPU, so edits only need a texture update. Shading is per-fragment:
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
    private readonly ShadowBinding _shadow;
    private readonly OcclusionBinding _occlusion;
    private readonly int _dViewProjection;
    private readonly int _dOrigin;
    private readonly int _dSpacing;
    private readonly int _dClipRect;
    private readonly int _vao;
    private readonly int _ebo;
    private readonly int _indexCount;
    private readonly int _uViewProjection;
    private readonly int _uOrigin;
    private readonly int _uSpacing;
    private readonly int _uClipRect;
    private readonly int _uSurfaceTint;
    private readonly int _uTextureMix;
    private readonly int _uShadowMix;
    private readonly int _uFogSetup;
    private readonly int _uFogTint;
    private readonly int _uDemo;
    private readonly int _uSunDirection;
    private readonly int _uCameraPosition;

    public TerrainMesh3D(HeightTexture heights, SurfaceTypeTexture surfaces, TerrainTextureSet? textures)
    {
        _heights = heights;
        _surfaces = surfaces;
        _textures = textures;

        string constants =
            $"""
            const float extent = {heights.Side - 1}.0;
            const float metersPerRaw = 65535.0 / 128.0;
            const int patchSide = {PatchSide};
            const float detailFullDistance = {TerrainTextureSet.DetailFullDistance:0.0};
            const float detailFadeDistance = {TerrainTextureSet.DetailFadeDistance:0.0};
            """;
        // The patch carries no vertex buffer, so where the ground is lives entirely in this one
        // function. The shadow pass runs the same copy: two of them is two chances for a shadow to
        // land somewhere the surface is not.
        string vertexGlsl =
            $$"""
            uniform sampler2D heights;
            uniform mat4 viewProjection;
            uniform vec2 origin;
            uniform float spacing;
            {{constants}}
            vec3 terrainVertex(out vec2 plane)
            {
                int row = gl_VertexID / patchSide;
                int col = gl_VertexID % patchSide;
                plane = origin + vec2(col, row) * spacing;
                vec2 clamped = clamp(plane, vec2(0.0), vec2(extent));
                return vec3(clamped, texture(heights, clamped / extent).r * metersPerRaw);
            }
            """;

        _depthProgram = new ShaderProgram(
            $$"""
            #version 330 core
            {{vertexGlsl}}
            invariant gl_Position;
            out vec2 world;
            void main()
            {
                gl_Position = viewProjection * vec4(terrainVertex(world), 1.0);
            }
            """,
            """
            #version 330 core
            in vec2 world;
            uniform vec4 clipRect;
            void main()
            {
                // The same cut the lit pass makes. The coarse patch bridges dips metres above the
                // real ground, and a shadow cast off that surface lands nowhere near it.
                if (all(greaterThan(world, clipRect.xy)) && all(lessThan(world, clipRect.zw)))
                {
                    discard;
                }
            }
            """);
        _dViewProjection = _depthProgram.UniformLocation("viewProjection");
        _dOrigin = _depthProgram.UniformLocation("origin");
        _dSpacing = _depthProgram.UniformLocation("spacing");
        _dClipRect = _depthProgram.UniformLocation("clipRect");

        _program = new ShaderProgram(
            $$"""
            #version 330 core
            {{vertexGlsl}}
            uniform vec3 cameraPosition;
            invariant gl_Position;
            out vec2 world;
            out float viewDistance;
            void main()
            {
                vec3 ground = terrainVertex(world);
                viewDistance = distance(ground, cameraPosition);
                gl_Position = viewProjection * vec4(ground, 1.0);
            }
            """,
            $$"""
            #version 330 core
            uniform sampler2D heights;
            uniform sampler2D surfaceTypes;
            uniform sampler2D surfacePalette;
            uniform sampler2D blendWeights;
            uniform sampler2D terrainColour;
            uniform sampler2D terrainShadow;
            uniform sampler2D bakedDiffuse;
            // 0 when the world ships no baked albedo, which forces the detail blend to every distance.
            uniform float bakedMix;
            uniform float shadowMix;
            uniform sampler2D sectorLayers;
            uniform sampler2DArray detailTextures;
            uniform float layerTiling[64];
            uniform float layerProjAxis[64];
            // Layers sharing a texture share a slice, so a layer index is not a slice index.
            uniform float layerSlice[64];
            uniform vec2 heightRange;
            uniform float surfaceTint;
            uniform float textureMix;
            uniform vec3 sunDirection;
            uniform vec3 cameraPosition;
            uniform float weightSide;
            // The world-space rectangle the fine ring covers, as (minX, minY, maxX, maxY). The
            // coarse pass throws away everything inside it; the fine pass passes an inverted
            // rectangle, which no fragment can be inside.
            uniform vec4 clipRect;
            {{SceneLighting.SkyGlsl}}
            {{SceneLighting.SurfaceGlsl}}
            {{SceneLighting.ShadowGlsl}}
            in float viewDistance;
            uniform float sectorsPerSide;
            in vec2 world;
            out vec4 fragment;
            {{constants}}

            // The four layer indices this sector blends, as the RGBA of one texel.
            vec4 sectorLayerIndices()
            {
                vec2 sectorUv = (floor(world / 64.0) + 0.5) / sectorsPerSide;
                return texture(sectorLayers, sectorUv) * 255.0;
            }

            // Every per-sector square the cooker writes is stored transposed, whichever file it lands
            // in - a 2x2 atlas quadrant or a sector's own shadow map. Verified by reassembling a
            // campaign cell and checking features run unbroken across sector boundaries.
            ivec2 atlasTexel(ivec2 w)
            {
                ivec2 sector = (w / 64) * 64;
                return sector + (w - sector).yx;
            }

            // Because of that transpose, texels next to each other in memory are not next to each
            // other in the world, so hardware filtering would blend unrelated ground. The four taps
            // are mapped individually and blended here instead.
            vec3 sampleAtlas(sampler2D tex, vec2 pos)
            {
                vec2 p = clamp(pos, 0.5, weightSide - 0.5) - 0.5;
                ivec2 b = ivec2(floor(p));
                vec2 f = p - vec2(b);
                ivec2 hi = ivec2(int(weightSide) - 1);
                vec3 c00 = texelFetch(tex, atlasTexel(clamp(b, ivec2(0), hi)), 0).rgb;
                vec3 c10 = texelFetch(tex, atlasTexel(clamp(b + ivec2(1, 0), ivec2(0), hi)), 0).rgb;
                vec3 c01 = texelFetch(tex, atlasTexel(clamp(b + ivec2(0, 1), ivec2(0), hi)), 0).rgb;
                vec3 c11 = texelFetch(tex, atlasTexel(clamp(b + ivec2(1, 1), ivec2(0), hi)), 0).rgb;
                return mix(mix(c00, c10, f.x), mix(c01, c11, f.x), f.y);
            }

            // A layer is projected along one axis rather than always from above, which is how cliffs
            // avoid the stretching a top-down projection would give them.
            vec2 layerUv(int layer, float height)
            {
                int axis = int(layerProjAxis[layer] + 0.5);
                if (axis == 0) { return vec2(world.y, height); }
                if (axis == 1) { return vec2(world.x, height); }
                return world;
            }

            vec3 blendedDetail(float height)
            {
                vec3 w = sampleAtlas(blendWeights, world);

                // The mask carries three weights but a sector names four layers: the fourth weight is
                // whatever the three leave over, and it belongs to the low byte - the cliff
                // projection. That is why a black mask texel means solid rock rather than nothing,
                // and why the sectors leaving the low byte unused are the ones with no cliffs.
                vec4 idx = sectorLayerIndices();
                float chosen[4] = float[4](idx.a, idx.b, idx.g, idx.r);
                float weight[4] = float[4](w.r, w.g, w.b, max(0.0, 1.0 - (w.r + w.g + w.b)));

                vec3 colour = vec3(0.0);
                float used = 0.0;
                for (int i = 0; i < 4; i++)
                {
                    int layer = int(chosen[i] + 0.5);
                    if (layer >= 64) { continue; }
                    // Only a guard against a zero: the real periods run from well under a metre
                    // (sand, which leans on a fine normal map the game has and this does not) up to
                    // tens of metres for cliff rock.
                    float tiling = max(layerTiling[layer], 0.05);
                    colour += weight[i] * texture(detailTextures,
                        vec3(layerUv(layer, height) / tiling, layerSlice[layer])).rgb;
                    used += weight[i];
                }

                // Only reachable now if a sector names no usable layer at all.
                if (used <= 0.001) { return vec3(0.2159); }

                // The colour atlas is a baked per-texel tint over the blended detail, mid-grey neutral.
                vec3 tint = sampleAtlas(terrainColour, world);
                // 4.632 is 1/linear(0.5): the atlas is authored mid-grey neutral, and mid-grey
                // sRGB is 0.2159 once the sampler decodes it.
                return (colour / used) * tint * 4.632;
            }

            // How much of the detail blend survives at this distance. The engine crossfades the
            // ground into a baked albedo rather than letting the detail textures run out to the
            // horizon on a high mip - which is why its far terrain never shows a repeat - and it is
            // why everything past the fade costs one filtered tap instead of twelve fetches.
            float detailWeight()
            {
                if (bakedMix < 0.5) { return 1.0; }
                return clamp((detailFadeDistance - viewDistance)
                    / (detailFadeDistance - detailFullDistance), 0.0, 1.0);
            }

            // Sampled like any ordinary texture: this one is straightened at upload rather than at
            // every tap, so it mips and filters on its own. No tint either - the engine multiplies
            // the colour atlas into the detail path only, because the bake already carries it.
            // The gradients are handed in rather than taken here: this sits inside a branch on
            // distance, and a fetch under divergent control flow has no defined implicit level.
            vec3 groundColour(float height, vec2 uvDx, vec2 uvDy)
            {
                float detail = detailWeight();
                vec2 uv = world / weightSide;
                if (detail <= 0.0) { return textureGrad(bakedDiffuse, uv, uvDx, uvDy).rgb; }
                if (detail >= 1.0) { return blendedDetail(height); }
                return mix(textureGrad(bakedDiffuse, uv, uvDx, uvDy).rgb, blendedDetail(height), detail);
            }

            void main()
            {
                // The two passes read one heightfield at different steps, so where the coarse step
                // bridges a dip - a road cutting is the obvious one - its surface sits metres above
                // the real ground and buries whatever is down there. Drawing it only outside the
                // fine ring is the fix; a depth bias cannot help when the geometry is genuinely
                // higher.
                if (all(greaterThan(world, clipRect.xy)) && all(lessThan(world, clipRect.zw)))
                {
                    discard;
                }

                vec2 uv = world / extent;
                float texel = 1.0 / extent;
                float hl = texture(heights, uv - vec2(texel, 0.0)).r;
                float hr = texture(heights, uv + vec2(texel, 0.0)).r;
                float hd = texture(heights, uv - vec2(0.0, texel)).r;
                float hu = texture(heights, uv + vec2(0.0, texel)).r;
                vec3 normal = normalize(vec3((hl - hr) * metersPerRaw, (hd - hu) * metersPerRaw, 2.0));

                float h = texture(heights, uv).r;
                float shade = (h - heightRange.x) / (heightRange.y - heightRange.x);
                vec3 base = mix(vec3(0.24, 0.30, 0.18), vec3(0.62, 0.56, 0.44), shade);

                vec2 bakedUv = world / weightSide;
                base = mix(base, groundColour(h * metersPerRaw, dFdx(bakedUv), dFdy(bakedUv)), textureMix);

                // Baked lighting: the low 16-bit channel, rescaled from the narrow band the data
                // occupies so it reads as shading rather than a flat dimming. Four texel fetches,
                // so the layer's own toggle gates them rather than scaling them away afterwards.
                float baked = 1.0;
                if (shadowMix > 0.0)
                {
                    baked = clamp((sampleAtlas(terrainShadow, world).r - 0.5) / 0.42, 0.0, 1.0);
                }

                // The surface id indexes the palette texture directly; r is the id scaled to 0..1,
                // so the lookup lands mid-texel at (id + 0.5) / 256.
                if (surfaceTint > 0.0)
                {
                    float id = texture(surfaceTypes, uv).r;
                    vec3 material = texture(surfacePalette,
                        vec2(id * (255.0 / 256.0) + (0.5 / 256.0), 0.5)).rgb;
                    base = mix(base, base * 0.35 + material * 0.75, surfaceTint);
                }

                // The bake multiplies the sun rather than replacing N.L, which is what the engine's
                // own terrain shader does with its self-shadow map - relief from the normals
                // survives, and the bake darkens what the sun cannot reach. (The bake does
                // correlate 0.77 with dot(N, L), but a self-shadow map would: slopes facing away
                // from the sun are both dark and self-shadowed, so the correlation cannot separate
                // a lightmap from a shadow term. The engine's shader can, and did.)
                // Keep this source ASCII: a stray non-ASCII byte, even inside a comment, makes the
                // GLSL tokeniser stop dead and report an unexpected end of file.
                float light = max(dot(normal, sunDirection), 0.0);
                vec3 worldPos = vec3(world, h * metersPerRaw);
                float bakedSun = light * mix(1.0, baked, shadowMix);

                // Presentation off: the ground stops here, with none of the cascade lookup, the
                // occlusion tap, the highlight or the haze below ever reached.
                if (demo < 0.5)
                {
                    fragment = vec4(shadeFlat(base, bakedSun), 1.0);
                    return;
                }

                // The bake was lit by a 10-degree sun and this scene is lit by a 44-degree one, so
                // the two cannot both be right in the same place - only in different ones. Cast
                // shadows out to the last cascade, the bake past it, crossfaded where they meet.
                float sunAmount = mix(
                    bakedSun,
                    light * sampleShadow(worldPos, viewDistance, light),
                    shadowFade(viewDistance));
                vec3 lit = shadeSurface(base, normal, normalize(cameraPosition - worldPos),
                    sunDirection, sunAmount, vec3(0.0), 0.0);
                fragment = vec4(applyHaze(lit, viewDistance, h * metersPerRaw), 1.0);
            }
            """);
        _uViewProjection = _program.UniformLocation("viewProjection");
        _uOrigin = _program.UniformLocation("origin");
        _uSpacing = _program.UniformLocation("spacing");
        _uClipRect = _program.UniformLocation("clipRect");
        _uSurfaceTint = _program.UniformLocation("surfaceTint");
        _uTextureMix = _program.UniformLocation("textureMix");
        _uShadowMix = _program.UniformLocation("shadowMix");
        _shadow = new ShadowBinding(_program);
        _occlusion = new OcclusionBinding(_program);
        _uFogSetup = _program.UniformLocation("fogSetup");
        _uFogTint = _program.UniformLocation("fogTint");
        _uDemo = _program.UniformLocation("demo");
        _uSunDirection = _program.UniformLocation("sunDirection");
        _uCameraPosition = _program.UniformLocation("cameraPosition");
        _program.Use();
        GL.Uniform2(_program.UniformLocation("heightRange"), heights.MinNormalized, heights.MaxNormalized);
        GL.Uniform1(_program.UniformLocation("heights"), 0);
        GL.Uniform1(_program.UniformLocation("surfaceTypes"), 1);
        GL.Uniform1(_program.UniformLocation("surfacePalette"), 2);
        GL.Uniform1(_program.UniformLocation("blendWeights"), 3);
        GL.Uniform1(_program.UniformLocation("sectorLayers"), 4);
        GL.Uniform1(_program.UniformLocation("detailTextures"), 5);
        GL.Uniform1(_program.UniformLocation("terrainColour"), 6);
        GL.Uniform1(_program.UniformLocation("terrainShadow"), 7);
        GL.Uniform1(_program.UniformLocation("bakedDiffuse"), 8);
        if (textures is not null)
        {
            GL.Uniform1(_program.UniformLocation("bakedMix"), textures.HasDiffuseAtlas ? 1f : 0f);
            GL.Uniform1(_program.UniformLocation("weightSide"), (float)textures.WeightSide);
            GL.Uniform1(_program.UniformLocation("sectorsPerSide"), textures.WeightSide / 64f);
            GL.Uniform1(_program.UniformLocation("layerTiling"), textures.Tiling.Length, textures.Tiling);
            GL.Uniform1(_program.UniformLocation("layerProjAxis"),
                textures.ProjectionAxis.Length, textures.ProjectionAxis);
            GL.Uniform1(_program.UniformLocation("layerSlice"),
                textures.LayerSlice.Length, textures.LayerSlice);
        }

        // Positions come from gl_VertexID; only the triangulation needs real data.
        var indices = new int[(PatchSide - 1) * (PatchSide - 1) * 6];
        int cursor = 0;
        for (int row = 0; row < PatchSide - 1; row++)
        {
            for (int col = 0; col < PatchSide - 1; col++)
            {
                int v = row * PatchSide + col;
                indices[cursor++] = v;
                indices[cursor++] = v + 1;
                indices[cursor++] = v + PatchSide;
                indices[cursor++] = v + 1;
                indices[cursor++] = v + PatchSide + 1;
                indices[cursor++] = v + PatchSide;
            }
        }
        _indexCount = indices.Length;

        _vao = GL.GenVertexArray();
        GL.BindVertexArray(_vao);
        _ebo = GL.GenBuffer();
        GL.BindBuffer(BufferTarget.ElementArrayBuffer, _ebo);
        GL.BufferData(BufferTarget.ElementArrayBuffer, indices.Length * sizeof(int), indices, BufferUsageHint.StaticDraw);
    }

    public void Draw(Matrix4 viewProjection, Vector3 cameraPosition, TerrainDrawOptions options)
    {
        _program.Use();
        GL.UniformMatrix4(_uViewProjection, false, ref viewProjection);
        GL.Uniform1(_uSurfaceTint, options.TintBySurfaceType ? 1f : 0f);
        GL.Uniform1(_uTextureMix, _textures is not null && options.ShowTextures ? 1f : 0f);
        GL.Uniform1(_uShadowMix, _textures is not null && options.ShowShadow ? 1f : 0f);
        SceneLighting.SetSkyUniforms(_uDemo, _uFogSetup, _uFogTint);
        GL.Uniform3(_uSunDirection, SceneLighting.SunDirection);
        GL.Uniform3(_uCameraPosition, cameraPosition);
        _shadow.Apply();
        _occlusion.Apply();
        _heights.Bind(TextureUnit.Texture0);
        _surfaces.Bind(TextureUnit.Texture1, TextureUnit.Texture2);
        _textures?.Bind(TextureUnit.Texture3, TextureUnit.Texture4, TextureUnit.Texture5,
            TextureUnit.Texture6, TextureUnit.Texture7, TextureUnit.Texture8);
        GL.BindVertexArray(_vao);
        GL.Enable(EnableCap.DepthTest);
        DrawPatches(cameraPosition, new PatchUniforms(_uOrigin, _uSpacing, _uClipRect));
    }

    /// <summary>The same two patches from the sun's point of view. Casting terrain matters for what
    /// stands on it - a hut in a hill's shade stays lit otherwise, because the baked lightmap the
    /// terrain carries covers only the ground.</summary>
    public void DrawDepth(Matrix4 lightViewProjection, Vector3 cameraPosition)
    {
        _depthProgram.Use();
        GL.UniformMatrix4(_dViewProjection, false, ref lightViewProjection);
        _heights.Bind(TextureUnit.Texture0);
        GL.BindVertexArray(_vao);
        DrawPatches(cameraPosition, new PatchUniforms(_dOrigin, _dSpacing, _dClipRect));
    }

    /// <summary>Where one program keeps the three uniforms a patch draw sets.</summary>
    private readonly record struct PatchUniforms(int Origin, int Spacing, int ClipRect);

    /// <summary>The coarse backdrop then the fine ring, against whichever program's uniforms are
    /// passed in - the lit pass and the shadow pass have to submit identical geometry.</summary>
    private void DrawPatches(Vector3 cameraPosition, PatchUniforms uniforms)
    {
        const float fineSpacing = 1f;
        float coarseSpacing = (_heights.Side - 1f) / (PatchSide - 1);
        float half = (PatchSide - 1) * fineSpacing / 2f;
        // Snapped to whole units so the fine grid doesn't swim as the camera pans.
        float ox = MathF.Floor(cameraPosition.X - half);
        float oy = MathF.Floor(cameraPosition.Y - half);

        // Coarse whole-world backdrop, cut away wherever the fine ring will cover it, then the fine
        // ring itself. The hole is inset by one coarse cell so the two still overlap in a thin band
        // rather than leaving a crack where their tessellations disagree - and the depth offset
        // keeps the fine ring winning inside that band.
        GL.Enable(EnableCap.PolygonOffsetFill);
        GL.PolygonOffset(1f, 1f);
        GL.Uniform4(uniforms.ClipRect,
            ox + coarseSpacing, oy + coarseSpacing,
            ox + half * 2f - coarseSpacing, oy + half * 2f - coarseSpacing);
        DrawPatch(uniforms, coarseSpacing, originX: 0, originY: 0);
        GL.Disable(EnableCap.PolygonOffsetFill);

        // An inverted rectangle: no fragment is inside it, so the fine ring clips nothing.
        GL.Uniform4(uniforms.ClipRect, 1f, 1f, -1f, -1f);
        DrawPatch(uniforms, fineSpacing, ox, oy);
    }

    private void DrawPatch(PatchUniforms uniforms, float spacing, float originX, float originY)
    {
        GL.Uniform2(uniforms.Origin, originX, originY);
        GL.Uniform1(uniforms.Spacing, spacing);
        GL.DrawElements(PrimitiveType.Triangles, _indexCount, DrawElementsType.UnsignedInt, 0);
    }

    public void Dispose()
    {
        _program.Dispose();
        _depthProgram.Dispose();
        GL.DeleteBuffer(_ebo);
        GL.DeleteVertexArray(_vao);
    }
}
