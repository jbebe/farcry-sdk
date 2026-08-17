using OpenTK.Graphics.OpenGL4;
using OpenTK.Mathematics;

namespace JackAll.App.MapEditor.Gl;

/// <summary>What the terrain draws this frame.</summary>
/// <param name="Brightness">Final exposure multiplier; 1 is the raw shaded result.</param>
/// <param name="Haze">Scales the distance fog; 0 turns it off entirely.</param>
public readonly record struct TerrainDrawOptions(
    bool ShowTextures, bool TintBySurfaceType, bool ShowShadow, float Brightness, float Haze);

/// <summary>
/// The 3D terrain: two camera-following grid patches (a fine 1-unit-spacing ring and a coarse
/// 8-unit world backdrop) whose vertex shader pulls heights straight from the shared height
/// texture - no terrain geometry is ever built on the CPU, and edits only need a texture update.
/// Shading is per-fragment: finite-difference normals from the same texture, one sun.
/// </summary>
public sealed class TerrainMesh3D : IDisposable
{
    /// <summary>Vertices per patch edge; 257x257 keeps both patches under half a million triangles.</summary>
    private const int PatchSide = 257;

    private readonly HeightTexture _heights;
    private readonly SurfaceTypeTexture _surfaces;
    private readonly TerrainTextureSet? _textures;
    private readonly ShaderProgram _program;
    private readonly int _vao;
    private readonly int _indexCount;
    private readonly int _uViewProjection;
    private readonly int _uOrigin;
    private readonly int _uSpacing;
    private readonly int _uSurfaceTint;
    private readonly int _uTextureMix;
    private readonly int _uShadowMix;
    private readonly int _uBrightness;
    private readonly int _uHaze;
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
            """;
        _program = new ShaderProgram(
            $$"""
            #version 330 core
            uniform sampler2D heights;
            uniform mat4 viewProjection;
            uniform vec2 origin;
            uniform float spacing;
            uniform vec3 cameraPosition;
            {{constants}}
            out vec2 world;
            out float viewDistance;
            void main()
            {
                int row = gl_VertexID / patchSide;
                int col = gl_VertexID % patchSide;
                world = origin + vec2(col, row) * spacing;
                vec2 clamped = clamp(world, vec2(0.0), vec2(extent));
                float z = texture(heights, clamped / extent).r * metersPerRaw;
                viewDistance = distance(vec3(clamped, z), cameraPosition);
                gl_Position = viewProjection * vec4(clamped, z, 1.0);
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
            uniform float shadowMix;
            uniform sampler2D sectorLayers;
            uniform sampler2DArray detailTextures;
            uniform float layerTiling[64];
            uniform float layerProjAxis[64];
            uniform vec2 heightRange;
            uniform float surfaceTint;
            uniform float textureMix;
            uniform float brightness;
            uniform float haze;
            uniform vec3 sunDirection;
            uniform float weightSide;
            {{SceneLighting.SkyGlsl}}
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
                    float tiling = max(layerTiling[layer], 0.5);
                    colour += weight[i] * texture(detailTextures, vec3(layerUv(layer, height) / tiling, float(layer))).rgb;
                    used += weight[i];
                }

                // Only reachable now if a sector names no usable layer at all.
                if (used <= 0.001) { return vec3(0.5); }

                // The colour atlas is a baked per-texel tint over the blended detail, mid-grey neutral.
                vec3 tint = sampleAtlas(terrainColour, world);
                return (colour / used) * tint * 2.0;
            }

            void main()
            {
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

                base = mix(base, blendedDetail(h * metersPerRaw), textureMix);

                // Baked lighting: the low 16-bit channel, rescaled from the narrow band the data
                // occupies so it reads as shading rather than a flat dimming.
                float baked = sampleAtlas(terrainShadow, world).r;
                baked = clamp((baked - 0.5) / 0.42, 0.0, 1.0);

                // The surface id indexes the palette texture directly; r is the id scaled to 0..1,
                // so the lookup lands mid-texel at (id + 0.5) / 256.
                float id = texture(surfaceTypes, uv).r;
                vec3 material = texture(surfacePalette, vec2(id * (255.0 / 256.0) + (0.5 / 256.0), 0.5)).rgb;
                base = mix(base, base * 0.35 + material * 0.75, surfaceTint);

                // The bake is a lightmap rather than plain occlusion - it correlates 0.77 with
                // dot(N, L) for a low western sun - so it stands in for the sun instead of
                // multiplying with it. One shading term, whichever source is available, never both.
                // Keep this source ASCII: a stray non-ASCII byte, even inside a comment, makes the
                // GLSL tokeniser stop dead and report an unexpected end of file.
                float light = max(dot(normal, sunDirection), 0.0);
                float shading = mix(0.35 + 0.65 * light, 0.35 + 0.65 * baked, shadowMix);
                vec3 lit = base * shading * brightness;
                fragment = vec4(applyHaze(lit, viewDistance, h * metersPerRaw, haze), 1.0);
            }
            """);
        _uViewProjection = _program.UniformLocation("viewProjection");
        _uOrigin = _program.UniformLocation("origin");
        _uSpacing = _program.UniformLocation("spacing");
        _uSurfaceTint = _program.UniformLocation("surfaceTint");
        _uTextureMix = _program.UniformLocation("textureMix");
        _uShadowMix = _program.UniformLocation("shadowMix");
        _uBrightness = _program.UniformLocation("brightness");
        _uHaze = _program.UniformLocation("haze");
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
        if (textures is not null)
        {
            GL.Uniform1(_program.UniformLocation("weightSide"), (float)textures.WeightSide);
            GL.Uniform1(_program.UniformLocation("sectorsPerSide"), textures.WeightSide / 64f);
            GL.Uniform1(_program.UniformLocation("layerTiling"), textures.Tiling.Length, textures.Tiling);
            GL.Uniform1(_program.UniformLocation("layerProjAxis"),
                textures.ProjectionAxis.Length, textures.ProjectionAxis);
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
        int ebo = GL.GenBuffer();
        GL.BindBuffer(BufferTarget.ElementArrayBuffer, ebo);
        GL.BufferData(BufferTarget.ElementArrayBuffer, indices.Length * sizeof(int), indices, BufferUsageHint.StaticDraw);
    }

    public void Draw(Matrix4 viewProjection, Vector3 cameraPosition, TerrainDrawOptions options)
    {
        _program.Use();
        GL.UniformMatrix4(_uViewProjection, false, ref viewProjection);
        GL.Uniform1(_uSurfaceTint, options.TintBySurfaceType ? 1f : 0f);
        GL.Uniform1(_uTextureMix, _textures is not null && options.ShowTextures ? 1f : 0f);
        GL.Uniform1(_uShadowMix, _textures is not null && options.ShowShadow ? 1f : 0f);
        GL.Uniform1(_uBrightness, options.Brightness);
        GL.Uniform1(_uHaze, options.Haze);
        GL.Uniform3(_uSunDirection, SceneLighting.SunDirection);
        GL.Uniform3(_uCameraPosition, cameraPosition);
        _heights.Bind(TextureUnit.Texture0);
        _surfaces.Bind(TextureUnit.Texture1, TextureUnit.Texture2);
        _textures?.Bind(TextureUnit.Texture3, TextureUnit.Texture4, TextureUnit.Texture5,
            TextureUnit.Texture6, TextureUnit.Texture7);
        GL.BindVertexArray(_vao);
        GL.Enable(EnableCap.DepthTest);

        // Coarse whole-world backdrop, then the fine ring around the camera drawn over it; slight
        // depth offset on the backdrop avoids z-fighting where they overlap.
        GL.Enable(EnableCap.PolygonOffsetFill);
        GL.PolygonOffset(1f, 1f);
        DrawPatch(spacing: (_heights.Side - 1f) / (PatchSide - 1), originX: 0, originY: 0);
        GL.Disable(EnableCap.PolygonOffsetFill);

        const float fineSpacing = 1f;
        float half = (PatchSide - 1) * fineSpacing / 2f;
        // Snapped to whole units so the fine grid doesn't swim as the camera pans.
        float ox = MathF.Floor(cameraPosition.X - half);
        float oy = MathF.Floor(cameraPosition.Y - half);
        DrawPatch(fineSpacing, ox, oy);
    }

    private void DrawPatch(float spacing, float originX, float originY)
    {
        GL.Uniform2(_uOrigin, originX, originY);
        GL.Uniform1(_uSpacing, spacing);
        GL.DrawElements(PrimitiveType.Triangles, _indexCount, DrawElementsType.UnsignedInt, 0);
    }

    public void Dispose()
    {
        _program.Dispose();
        GL.DeleteVertexArray(_vao);
    }
}
