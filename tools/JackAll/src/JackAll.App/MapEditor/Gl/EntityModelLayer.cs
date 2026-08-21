using System.Collections.Concurrent;
using JackAll.App.FileHandlers.Xbt;
using JackAll.Tools.World;
using JackAll.Tools.Xbt;
using OpenTK.Graphics.OpenGL4;
using OpenTK.Mathematics;

namespace JackAll.App.MapEditor.Gl;

/// <summary>
/// Draws every mesh-resolved entity as its real .xbg geometry: one VAO per unique mesh, one
/// instanced draw per material range per populated detail tier. Both tiers draw textured, with the
/// maps decoded on background threads and uploaded on a per-frame budget; a range whose maps have
/// not arrived yet draws flat in the entity's marker colour until they do.
/// </summary>
public sealed class EntityModelLayer : IDisposable
{
    /// <summary>Floats per instance: x y z, then the three Euler angles in radians, then r g b.</summary>
    private const int InstanceStride = 9;

    /// <summary>
    /// Where an instance lands, shared by the lit pass and the shadow pass. Billboards keep facing
    /// the camera in the shadow map rather than the sun: the card's shadow then matches the card
    /// that is actually on screen, where turning it edge-on to the sun would cast nothing at all.
    /// </summary>
    private const string TransformGlsl =
        """
        layout(location = 0) in vec3 position;
        layout(location = 1) in vec3 normal;
        layout(location = 2) in vec2 uv;
        layout(location = 3) in vec3 instancePosition;
        layout(location = 4) in vec3 instanceAngles;
        layout(location = 5) in vec3 tint;
        layout(location = 6) in vec2 vertexMask;
        uniform mat4 viewProjection;
        uniform vec3 cameraPosition;
        // Which way this mesh's card looks in model space, or zero for ordinary geometry that
        // keeps the orientation the world gave it.
        uniform vec2 billboardFacing;
        // The engine's Z-up Euler order: yaw about Z last, over pitch then roll. Columns, so
        // the product reads right-to-left as Rz * Rx * Ry.
        mat3 spin(vec3 a)
        {
            vec3 s = sin(a), c = cos(a);
            mat3 ry = mat3(c.y, 0.0, -s.y,   0.0, 1.0, 0.0,   s.y, 0.0, c.y);
            mat3 rx = mat3(1.0, 0.0, 0.0,    0.0, c.x, s.x,   0.0, -s.x, c.x);
            mat3 rz = mat3(c.z, s.z, 0.0,   -s.z, c.z, 0.0,   0.0, 0.0, 1.0);
            return rz * rx * ry;
        }
        // Turns the card so it looks at the camera, about Z only - a plant leans with the
        // ground, it does not tip back when you climb a hill.
        mat3 faceCamera(vec3 origin)
        {
            vec2 toCamera = cameraPosition.xy - origin.xy;
            if (dot(toCamera, toCamera) < 1e-6) { return spin(vec3(0.0)); }
            toCamera = normalize(toCamera);
            // The yaw that carries billboardFacing onto toCamera, as a difference of angles.
            float yaw = atan(toCamera.y, toCamera.x) - atan(billboardFacing.y, billboardFacing.x);
            return spin(vec3(0.0, 0.0, yaw));
        }
        mat3 instanceRotation()
        {
            return dot(billboardFacing, billboardFacing) > 0.5
                ? faceCamera(instancePosition)
                : spin(instanceAngles);
        }
        """;

    private const int VertexStrideBytes = WorldModel.FloatsPerVertex * sizeof(float);
    private const int InstanceStrideBytes = InstanceStride * sizeof(float);
    private const int MaxDecoders = 2;
    private const long UploadBudgetPerFrame = 8 << 20;

    /// <summary>One detail tier's material ranges and the live texture handles behind each.</summary>
    private sealed class Tier
    {
        public required IReadOnlyList<MaterialRange> Ranges;
        /// <summary>Indices into <see cref="Ranges"/> per pass, so neither pass walks the other's.
        /// </summary>
        public required int[] Opaque;
        public required int[] Blended;
        /// <summary>Live GL handles per range, one array per sampler the Generic shader binds; 0
        /// where that map is absent or still streaming.</summary>
        public required int[] Handles;
        public required int[] SecondHandles;
        public required int[] MaskHandles;

        public static Tier Of(IReadOnlyList<MaterialRange> ranges) => new()
        {
            Ranges = ranges,
            Opaque = [.. Enumerable.Range(0, ranges.Count).Where(r => ranges[r].Alpha != MaterialAlpha.Blend)],
            Blended = [.. Enumerable.Range(0, ranges.Count).Where(r => ranges[r].Alpha == MaterialAlpha.Blend)],
            Handles = new int[ranges.Count],
            SecondHandles = new int[ranges.Count],
            MaskHandles = new int[ranges.Count],
        };
    }

    private sealed class Mesh
    {
        public int Vao, VertexBuffer, IndexBuffer, InstanceBuffer;
        public IndexRange Fine, Coarse;
        /// <summary>Both tiers textured: the far ring is the file's coarsest LOD, not a flat tint.</summary>
        public required Tier FineTier;
        public required Tier CoarseTier;
        public required float[] Staging;
        public int FineCount, CoarseCount;
        public int FineFloats, CoarseFloats;
        public int CoarseStart;
        /// <summary>Instance index the attrib pointers target; -1 forces the first bind.</summary>
        public int PointedAt = -1;
        /// <summary>Model-space direction the card looks, or zero when the mesh is not a billboard.</summary>
        public System.Numerics.Vector2 Facing;
    }

    /// <summary>What an entity contributes besides its live position and yaw: the meshes its
    /// graphics slots resolved to, all drawn at the entity's transform.</summary>
    private readonly record struct Row(int[] Models, float R, float G, float B);

    /// <summary>A texture decoded off-thread: DXT mips uploaded as-is, or (FourCc 0) one RGBA image.</summary>
    private sealed record Decoded(int Width, int Height, uint FourCc, IReadOnlyList<byte[]> Mips);

    private readonly ShaderProgram _program;
    private readonly Mesh[] _meshes;
    private readonly Dictionary<WorldEntity, Row> _rows;
    private readonly Func<string, byte[]?> _readByPath;
    /// <summary>Diffuse handle per path: -1 decoding, 0 failed for good, else the GL texture.</summary>
    private readonly Dictionary<string, int> _textures = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentQueue<string> _requests = new();
    private readonly ConcurrentQueue<(string Path, Decoded? Texture)> _decoded = new();
    private int _activeDecoders;
    private bool _texturesChanged;
    private readonly int _uViewProjection;
    private readonly int _uCameraPosition;
    private readonly int _uSunDirection;
    private readonly int _uHaze;
    private readonly int _uUseTexture;
    private readonly int _uAlphaMode;
    private readonly int _uTintBase;
    private readonly int _uTintColour;
    private readonly int _uTintSecond;
    private readonly int _uDiffuseTiling;
    private readonly int _uSecondTiling;
    private readonly int _uMaskTiling;
    private readonly int _uUseSecond;
    private readonly int _uUseMask;
    private readonly int _uSpecularBase;
    private readonly int _uSpecularColour;
    private readonly int _uSpecularPower;
    private readonly int _uFogSetup;
    private readonly int _uFogTint;
    private readonly int _uBillboardFacing;
    /// <summary>Just the meshes carrying a blended range, so the second pass skips the rest.</summary>
    private readonly Mesh[] _blendedMeshes;

    private readonly ShaderProgram _depthProgram;
    private readonly ShadowBinding _shadow;
    private readonly OcclusionBinding _occlusion;
    private readonly int _dViewProjection;
    private readonly int _dCameraPosition;
    private readonly int _dBillboardFacing;
    private readonly int _dDiffuseTiling;
    private readonly int _dUseTexture;
    private readonly int _dAlphaMode;

    /// <summary>What the streamed diffuse textures currently hold on the GPU.</summary>
    public long TextureBytesResident { get; private set; }

    public EntityModelLayer(
        WorldModelSet set, Func<string, byte[]?> readByPath,
        Func<WorldEntity, (byte R, byte G, byte B)> colourOf)
        : this(set.Models, readByPath, CapacityOf(set))
    {
        _rows = new Dictionary<WorldEntity, Row>(set.ModelIndicesByEntity.Count);
        var colourByArchetype = new Dictionary<string, (float R, float G, float B)>(StringComparer.OrdinalIgnoreCase);
        foreach ((WorldEntity entity, int[] models) in set.ModelIndicesByEntity)
        {
            if (!colourByArchetype.TryGetValue(entity.ArchetypeName, out (float R, float G, float B) colour))
            {
                (byte r, byte g, byte b) = colourOf(entity);
                colour = (r / 255f, g / 255f, b / 255f);
                colourByArchetype[entity.ArchetypeName] = colour;
            }
            _rows[entity] = new Row(models, colour.R, colour.G, colour.B);
        }
    }

    private static int[] CapacityOf(WorldModelSet set)
    {
        var capacity = new int[set.Models.Count];
        foreach (int[] indices in set.ModelIndicesByEntity.Values)
        {
            foreach (int index in indices)
            {
                capacity[index]++;
            }
        }

        return capacity;
    }

    /// <summary>
    /// The geometry-only half of the layer: meshes, buffers and the shader, with no entities behind
    /// them. The scatter builds on this - two million placements cannot each be an entity - and the
    /// entity constructor chains through it.
    /// </summary>
    public EntityModelLayer(
        IReadOnlyList<WorldModel> models, Func<string, byte[]?> readByPath, int[] capacity)
    {
        _readByPath = readByPath;
        _rows = [];

        _meshes = new Mesh[models.Count];
        var blended = new List<Mesh>();
        for (int i = 0; i < models.Count; i++)
        {
            WorldModel model = models[i];
            var mesh = new Mesh
            {
                Fine = model.Fine,
                Coarse = model.Coarse,
                FineTier = Tier.Of(model.MaterialRanges),
                CoarseTier = Tier.Of(model.CoarseMaterialRanges),
                Staging = new float[capacity[i] * InstanceStride],
                Facing = model.BillboardFacing ?? System.Numerics.Vector2.Zero,
            };
            if (mesh.FineTier.Blended.Length > 0 || mesh.CoarseTier.Blended.Length > 0)
            {
                blended.Add(mesh);
            }

            mesh.VertexBuffer = GL.GenBuffer();
            GL.BindBuffer(BufferTarget.ArrayBuffer, mesh.VertexBuffer);
            GL.BufferData(BufferTarget.ArrayBuffer, model.Vertices.Length * sizeof(float),
                model.Vertices, BufferUsageHint.StaticDraw);

            mesh.IndexBuffer = GL.GenBuffer();
            mesh.InstanceBuffer = GL.GenBuffer();
            GL.BindBuffer(BufferTarget.ArrayBuffer, mesh.InstanceBuffer);
            GL.BufferData(BufferTarget.ArrayBuffer, Math.Max(1, capacity[i]) * InstanceStrideBytes,
                IntPtr.Zero, BufferUsageHint.DynamicDraw);

            mesh.Vao = GL.GenVertexArray();
            GL.BindVertexArray(mesh.Vao);
            GL.BindBuffer(BufferTarget.ArrayBuffer, mesh.VertexBuffer);
            GL.EnableVertexAttribArray(0);
            GL.VertexAttribPointer(0, 3, VertexAttribPointerType.Float, false, VertexStrideBytes, 0);
            GL.EnableVertexAttribArray(1);
            GL.VertexAttribPointer(1, 3, VertexAttribPointerType.Float, false, VertexStrideBytes, 12);
            GL.EnableVertexAttribArray(2);
            GL.VertexAttribPointer(2, 2, VertexAttribPointerType.Float, false, VertexStrideBytes, 24);
            GL.EnableVertexAttribArray(6);
            GL.VertexAttribPointer(6, 2, VertexAttribPointerType.Float, false, VertexStrideBytes, 32);
            GL.EnableVertexAttribArray(3);
            GL.EnableVertexAttribArray(4);
            GL.EnableVertexAttribArray(5);
            PointInstanceAttribs(mesh, 0);
            GL.VertexAttribDivisor(3, 1);
            GL.VertexAttribDivisor(4, 1);
            GL.VertexAttribDivisor(5, 1);
            GL.BindBuffer(BufferTarget.ElementArrayBuffer, mesh.IndexBuffer);
            GL.BufferData(BufferTarget.ElementArrayBuffer, model.Indices.Length * sizeof(int),
                model.Indices, BufferUsageHint.StaticDraw);

            _meshes[i] = mesh;
        }
        _blendedMeshes = [.. blended];
        GL.BindVertexArray(0);

        _depthProgram = new ShaderProgram(
            $$"""
            #version 330 core
            {{TransformGlsl}}
            invariant gl_Position;
            out vec2 texUv;
            void main()
            {
                texUv = uv;
                gl_Position =
                    viewProjection * vec4(instanceRotation() * position + instancePosition, 1.0);
            }
            """,
            """
            #version 330 core
            in vec2 texUv;
            uniform sampler2D diffuse;
            uniform vec2 diffuseTiling;
            uniform float useTexture;
            uniform int alphaMode;
            void main()
            {
                // The same coverage test the lit pass makes. Without it a tree casts the shadow of
                // the cards its leaves are painted on.
                if (useTexture > 0.5 && alphaMode == 1
                    && texture(diffuse, texUv * diffuseTiling).a < 0.5)
                {
                    discard;
                }
            }
            """);
        _dViewProjection = _depthProgram.UniformLocation("viewProjection");
        _dCameraPosition = _depthProgram.UniformLocation("cameraPosition");
        _dBillboardFacing = _depthProgram.UniformLocation("billboardFacing");
        _dDiffuseTiling = _depthProgram.UniformLocation("diffuseTiling");
        _dUseTexture = _depthProgram.UniformLocation("useTexture");
        _dAlphaMode = _depthProgram.UniformLocation("alphaMode");

        _program = new ShaderProgram(
            $$"""
            #version 330 core
            {{TransformGlsl}}
            invariant gl_Position;
            out vec3 worldNormal;
            out vec3 worldPosition;
            out vec3 baseColour;
            out vec2 texUv;
            out vec2 maskWeights;
            void main()
            {
                mat3 rotation = instanceRotation();
                worldPosition = rotation * position + instancePosition;
                // Pure rotation, so the normal rotates the same way - no inverse-transpose needed.
                worldNormal = rotation * normal;
                baseColour = tint;
                texUv = uv;
                maskWeights = vertexMask;
                gl_Position = viewProjection * vec4(worldPosition, 1.0);
            }
            """,
            $$"""
            #version 330 core
            in vec3 worldNormal;
            in vec3 worldPosition;
            in vec3 baseColour;
            in vec2 texUv;
            in vec2 maskWeights;
            uniform vec3 cameraPosition;
            uniform vec3 sunDirection;
            uniform float haze;
            uniform float useTexture;
            uniform int alphaMode;
            uniform vec3 tintBase;
            uniform vec3 tintColour;
            uniform vec3 tintSecond;
            uniform vec2 diffuseTiling;
            uniform vec2 secondTiling;
            uniform vec2 maskTiling;
            uniform float useSecond;
            uniform float useMask;
            uniform vec3 specularBase;
            uniform vec3 specularColour;
            uniform float specularPower;
            uniform sampler2D diffuse;
            uniform sampler2D diffuse2;
            uniform sampler2D maskMap;
            out vec4 fragment;
            {{SceneLighting.SkyGlsl}}
            {{SceneLighting.SurfaceGlsl}}
            {{SceneLighting.ShadowGlsl}}
            void main()
            {
                vec3 albedo = baseColour;
                float coverage = 1.0;
                vec4 texel = vec4(1.0);
                vec2 weights = vec2(0.0);
                if (useTexture > 0.5)
                {
                    texel = texture(diffuse, texUv * diffuseTiling);
                    // Only materials that asked for it read alpha as coverage; on the rest it is a
                    // gloss or spec mask and would erase the surface.
                    if (alphaMode == 1 && texel.a < 0.5) { discard; }
                    if (alphaMode == 2) { coverage = texel.a; }

                    // The engine's Generic shader, decoded from its own bytecode. The mask gates
                    // both blends: green picks how much of layer 2 shows, blue how far layer 1's
                    // tint travels from Base to Color1. A material with no mask samples white, which
                    // is why an unmasked surface takes its tint in full.
                    weights = clamp(maskWeights, 0.0, 1.0);
                    if (useMask > 0.5)
                    {
                        weights *= texture(maskMap, texUv * maskTiling).gb;
                    }

                    albedo = srgbToLinear(texel.rgb) * mix(tintBase, tintColour, weights.y);
                    if (useSecond > 0.5)
                    {
                        vec3 layer2 =
                            srgbToLinear(texture(diffuse2, texUv * secondTiling).rgb) * tintSecond;
                        albedo = mix(albedo, layer2, weights.x);
                    }
                }
                else
                {
                    // A range still streaming its maps, or one with none to stream, stands in for
                    // the average of a textured surface; a saturated marker colour at full
                    // additive lighting would flash as a glowing placeholder instead.
                    albedo = srgbToLinear(baseColour) * 0.55;
                }
                vec3 n = normalize(worldNormal);
                // Many parts are single-sided shells, so light whichever side is showing. The test
                // is against the eye, not gl_FrontFacing: the meshes are wound clockwise the way
                // D3D wants, which GL reads as back-facing on every outward triangle, and taking
                // that at its word flips every normal into the surface and kills the sun term.
                bool flipped = dot(n, cameraPosition - worldPosition) < 0.0;
                if (flipped) { n = -n; }

                // An opaque material's diffuse alpha is its gloss mask - the same fact the coverage
                // branch above steps around. A flipped normal is a guess about which way a shell
                // faces: good enough for diffuse, not good enough to hang a mirror highlight on, so
                // the inside of a shell stays matte.
                float specMask = (useTexture > 0.5 && alphaMode == 0 && !flipped) ? texel.a : 0.0;
                vec3 spec = mix(specularBase, specularColour, weights.y) * specMask;

                vec3 toEye = normalize(cameraPosition - worldPosition);
                float viewDistance = distance(cameraPosition, worldPosition);
                float ndotl = max(dot(n, sunDirection), 0.0);

                // Nothing baked here to fall back on, so past the cascades a mesh is simply lit.
                float sunAmount = ndotl
                    * mix(1.0, sampleShadow(worldPosition, viewDistance, ndotl),
                          shadowFade(viewDistance));
                vec3 lit = shadeSurface(albedo, n, toEye, sunDirection, sunAmount, spec, specularPower);
                fragment = vec4(applyHaze(lit, viewDistance, worldPosition.z, haze), coverage);
            }
            """);
        _uViewProjection = _program.UniformLocation("viewProjection");
        _uCameraPosition = _program.UniformLocation("cameraPosition");
        _uSunDirection = _program.UniformLocation("sunDirection");
        _uHaze = _program.UniformLocation("haze");
        _uBillboardFacing = _program.UniformLocation("billboardFacing");
        _shadow = new ShadowBinding(_program);
        _occlusion = new OcclusionBinding(_program);
        _uUseTexture = _program.UniformLocation("useTexture");
        _uAlphaMode = _program.UniformLocation("alphaMode");
        _uTintBase = _program.UniformLocation("tintBase");
        _uTintColour = _program.UniformLocation("tintColour");
        _uTintSecond = _program.UniformLocation("tintSecond");
        _uDiffuseTiling = _program.UniformLocation("diffuseTiling");
        _uSecondTiling = _program.UniformLocation("secondTiling");
        _uMaskTiling = _program.UniformLocation("maskTiling");
        _uUseSecond = _program.UniformLocation("useSecond");
        _uUseMask = _program.UniformLocation("useMask");
        _uSpecularBase = _program.UniformLocation("specularBase");
        _uSpecularColour = _program.UniformLocation("specularColour");
        _uSpecularPower = _program.UniformLocation("specularPower");
        _uFogSetup = _program.UniformLocation("fogSetup");
        _uFogTint = _program.UniformLocation("fogTint");
        _program.Use();
        GL.Uniform1(_program.UniformLocation("diffuse"), 0);
        GL.Uniform1(_program.UniformLocation("diffuse2"), 1);
        GL.Uniform1(_program.UniformLocation("maskMap"), 2);
    }

    /// <summary>
    /// Re-buckets the visible entities into per-mesh fine/coarse instance streams around the
    /// camera's sector and uploads them, returning the entities left for billboard markers: the
    /// mesh-less plus everything beyond the coarse ring. Fine instances pack forward from the
    /// front of each mesh's staging array, coarse ones backward from its end, so one pass fills
    /// both without counting first.
    /// </summary>
    /// <summary>
    /// The same visibility pass over bare placements. Only the ones inside the coarse ring reach a
    /// buffer, so a world scattering millions of them streams the few thousand around the camera.
    /// </summary>
    public void SetVisible(ScatterInstance[] scatter, Vector3 cameraPosition)
    {
        int cameraX = (int)MathF.Floor(cameraPosition.X / WorldModels.SectorMeters);
        int cameraY = (int)MathF.Floor(cameraPosition.Y / WorldModels.SectorMeters);

        foreach (Mesh mesh in _meshes)
        {
            mesh.FineCount = 0;
            mesh.CoarseCount = 0;
            mesh.FineFloats = 0;
            mesh.CoarseFloats = mesh.Staging.Length;
        }

        foreach (ScatterInstance instance in scatter)
        {
            int distance = Math.Max(
                Math.Abs((int)MathF.Floor(instance.Position.X / WorldModels.SectorMeters) - cameraX),
                Math.Abs((int)MathF.Floor(instance.Position.Y / WorldModels.SectorMeters) - cameraY));
            if (distance > WorldModels.CoarseRadius)
            {
                continue;
            }

            Place(_meshes[instance.Model], instance.Position, System.Numerics.Vector3.Zero,
                distance <= WorldModels.FineRadius, 1f, 1f, 1f);
        }

        UploadStaging();
    }

    /// <summary>Writes one placement into a mesh's staging, fine from the front and coarse from the
    /// back. A mesh whose staging is full drops the rest, which only a scatter can reach.</summary>
    private static void Place(
        Mesh mesh, System.Numerics.Vector3 position, System.Numerics.Vector3 angles, bool fine,
        float r, float g, float b)
    {
        int at;
        if (fine)
        {
            if (mesh.FineFloats + InstanceStride > mesh.CoarseFloats) return;
            at = mesh.FineFloats;
            mesh.FineFloats += InstanceStride;
            mesh.FineCount++;
        }
        else
        {
            if (mesh.CoarseFloats - InstanceStride < mesh.FineFloats) return;
            mesh.CoarseFloats -= InstanceStride;
            at = mesh.CoarseFloats;
            mesh.CoarseCount++;
        }

        mesh.Staging[at] = position.X;
        mesh.Staging[at + 1] = position.Y;
        mesh.Staging[at + 2] = position.Z;
        mesh.Staging[at + 3] = angles.X;
        mesh.Staging[at + 4] = angles.Y;
        mesh.Staging[at + 5] = angles.Z;
        mesh.Staging[at + 6] = r;
        mesh.Staging[at + 7] = g;
        mesh.Staging[at + 8] = b;
    }

    private void UploadStaging()
    {
        foreach (Mesh mesh in _meshes)
        {
            mesh.PointedAt = -1;
            mesh.CoarseStart = mesh.CoarseFloats / InstanceStride;
            GL.BindBuffer(BufferTarget.ArrayBuffer, mesh.InstanceBuffer);
            if (mesh.FineCount > 0)
            {
                GL.BufferSubData(BufferTarget.ArrayBuffer, IntPtr.Zero,
                    mesh.FineCount * InstanceStrideBytes, mesh.Staging);
                RequestTextures(mesh.FineTier);
            }
            if (mesh.CoarseCount > 0)
            {
                GL.BufferSubData(BufferTarget.ArrayBuffer, (IntPtr)(mesh.CoarseFloats * sizeof(float)),
                    mesh.CoarseCount * InstanceStrideBytes, ref mesh.Staging[mesh.CoarseFloats]);
                RequestTextures(mesh.CoarseTier);
            }
        }
    }

    public List<WorldEntity> SetVisible(List<WorldEntity> visible, Vector3 cameraPosition)
    {
        int cameraX = (int)MathF.Floor(cameraPosition.X / WorldModels.SectorMeters);
        int cameraY = (int)MathF.Floor(cameraPosition.Y / WorldModels.SectorMeters);

        foreach (Mesh mesh in _meshes)
        {
            mesh.FineCount = 0;
            mesh.CoarseCount = 0;
            mesh.FineFloats = 0;
            mesh.CoarseFloats = mesh.Staging.Length;
        }

        var leftover = new List<WorldEntity>(visible.Count);
        foreach (WorldEntity entity in visible)
        {
            System.Numerics.Vector3 position = entity.Position!.Value;
            int distance = Math.Max(
                Math.Abs((int)MathF.Floor(position.X / WorldModels.SectorMeters) - cameraX),
                Math.Abs((int)MathF.Floor(position.Y / WorldModels.SectorMeters) - cameraY));
            if (distance > WorldModels.CoarseRadius || !_rows.TryGetValue(entity, out Row row))
            {
                leftover.Add(entity);
                continue;
            }

            bool fine = distance <= WorldModels.FineRadius;
            System.Numerics.Vector3 angles = entity.Angles * (MathF.PI / 180f);
            foreach (int model in row.Models)
            {
                Place(_meshes[model], position, angles, fine, row.R, row.G, row.B);
            }
        }

        UploadStaging();
        return leftover;
    }

    private void RequestTextures(Tier tier)
    {
        foreach (MaterialRange range in tier.Ranges)
        {
            // All three of the Generic shader's maps, because the mask is what decides how much of
            // the tint and of layer 2 reaches the surface - without it a material renders as its
            // fully-tinted first layer.
            Request(range.Surface.DiffuseTexturePath);
            Request(range.Surface.SecondDiffusePath);
            Request(range.Surface.MaskPath);
        }
        StartDecoders();

        void Request(string? path)
        {
            if (path is not null && _textures.TryAdd(path, -1))
            {
                _requests.Enqueue(path);
            }
        }
    }

    /// <summary>A couple of long-running workers instead of one task per texture, so a dense ring
    /// cannot flood the thread pool with blocking archive reads.</summary>
    private void StartDecoders()
    {
        while (true)
        {
            int active = _activeDecoders;
            if (active >= MaxDecoders || _requests.IsEmpty)
            {
                return;
            }
            if (Interlocked.CompareExchange(ref _activeDecoders, active + 1, active) == active)
            {
                Task.Run(DecodeLoop);
            }
        }
    }

    private void DecodeLoop()
    {
        while (_requests.TryDequeue(out string? path))
        {
            _decoded.Enqueue((path, DecodeTexture(path)));
        }
        Interlocked.Decrement(ref _activeDecoders);
        if (!_requests.IsEmpty)
        {
            StartDecoders();
        }
    }

    public void Draw(Matrix4 viewProjection, Vector3 cameraPosition, float haze)
    {
        // Budgeted so a ring change worth of decodes cannot dump hundreds of uploads in one frame.
        long uploadedBefore = TextureBytesResident;
        while (TextureBytesResident - uploadedBefore < UploadBudgetPerFrame
            && _decoded.TryDequeue(out (string Path, Decoded? Texture) item))
        {
            _textures[item.Path] = item.Texture is { } texture ? Upload(texture) : 0;
            _texturesChanged = true;
        }
        if (_texturesChanged)
        {
            _texturesChanged = false;
            RefreshRangeHandles();
        }

        _program.Use();
        GL.UniformMatrix4(_uViewProjection, false, ref viewProjection);
        GL.Uniform3(_uCameraPosition, cameraPosition);
        _shadow.Apply();
        _occlusion.Apply();
        GL.Uniform3(_uSunDirection, SceneLighting.SunDirection);
        GL.Uniform1(_uHaze, haze);
        SceneLighting.SetFogUniforms(_uFogSetup, _uFogTint);
        GL.Enable(EnableCap.DepthTest);
        GL.ActiveTexture(TextureUnit.Texture0);

        float lastUseTexture = -1f, lastUseSecond = -1f, lastUseMask = -1f;
        int lastAlphaMode = -1;
        MaterialSurface lastSurface = default;
        void SetSurface(int handle, int second, int mask, MaterialSurface surface)
        {
            float useTexture = handle > 0 ? 1f : 0f;
            if (lastUseTexture != useTexture)
            {
                GL.Uniform1(_uUseTexture, lastUseTexture = useTexture);
            }
            // A map that is absent, failed or still streaming leaves its layer switched off rather
            // than sampling whatever happens to be bound.
            float useSecond = second > 0 ? 1f : 0f;
            if (lastUseSecond != useSecond)
            {
                GL.Uniform1(_uUseSecond, lastUseSecond = useSecond);
            }
            float useMask = mask > 0 ? 1f : 0f;
            if (lastUseMask != useMask)
            {
                GL.Uniform1(_uUseMask, lastUseMask = useMask);
            }
            if (lastAlphaMode != (int)surface.Alpha)
            {
                GL.Uniform1(_uAlphaMode, lastAlphaMode = (int)surface.Alpha);
            }
            if (lastSurface != surface)
            {
                // Authored material colours, so linearised here rather than per fragment.
                var tintBase = SceneLighting.Linear(surface.TintBase);
                var tint = SceneLighting.Linear(surface.Tint);
                var tintSecond = SceneLighting.Linear(surface.SecondTint);
                GL.Uniform3(_uTintBase, tintBase.X, tintBase.Y, tintBase.Z);
                GL.Uniform3(_uTintColour, tint.X, tint.Y, tint.Z);
                GL.Uniform3(_uTintSecond, tintSecond.X, tintSecond.Y, tintSecond.Z);
                GL.Uniform2(_uDiffuseTiling, surface.DiffuseTiling.X, surface.DiffuseTiling.Y);
                GL.Uniform2(_uSecondTiling, surface.SecondDiffuseTiling.X, surface.SecondDiffuseTiling.Y);
                GL.Uniform2(_uMaskTiling, surface.MaskTiling.X, surface.MaskTiling.Y);
                GL.Uniform3(_uSpecularBase, surface.SpecularBase.X, surface.SpecularBase.Y, surface.SpecularBase.Z);
                GL.Uniform3(_uSpecularColour,
                    surface.SpecularColour.X, surface.SpecularColour.Y, surface.SpecularColour.Z);
                GL.Uniform1(_uSpecularPower, surface.SpecularPower);
            }
            lastSurface = surface;
        }

        // One tier's ranges over the instances of one partition of the mesh's instance buffer.
        void DrawRanges(Mesh mesh, Tier tier, int[] which, int firstInstance, int instanceCount)
        {
            PointInstanceAttribs(mesh, firstInstance);
            foreach (int i in which)
            {
                MaterialRange range = tier.Ranges[i];
                int handle = tier.Handles[i];
                int second = tier.SecondHandles[i];
                int mask = tier.MaskHandles[i];
                SetSurface(handle, second, mask, range.Surface);
                if (handle > 0)
                {
                    GL.ActiveTexture(TextureUnit.Texture0);
                    GL.BindTexture(TextureTarget.Texture2D, handle);
                }
                if (second > 0)
                {
                    GL.ActiveTexture(TextureUnit.Texture1);
                    GL.BindTexture(TextureTarget.Texture2D, second);
                }
                if (mask > 0)
                {
                    GL.ActiveTexture(TextureUnit.Texture2);
                    GL.BindTexture(TextureTarget.Texture2D, mask);
                }
                DrawRange(new IndexRange(range.Start, range.Count), instanceCount);
            }
        }

        void DrawTiers(Mesh mesh, Func<Tier, int[]> which)
        {
            if (mesh.FineCount > 0)
            {
                DrawRanges(mesh, mesh.FineTier, which(mesh.FineTier), 0, mesh.FineCount);
            }
            if (mesh.CoarseCount > 0)
            {
                DrawRanges(mesh, mesh.CoarseTier, which(mesh.CoarseTier), mesh.CoarseStart, mesh.CoarseCount);
            }
        }

        foreach (Mesh mesh in _meshes)
        {
            if (mesh.FineCount + mesh.CoarseCount == 0)
            {
                continue;
            }

            GL.BindVertexArray(mesh.Vao);
            GL.Uniform2(_uBillboardFacing, mesh.Facing.X, mesh.Facing.Y);
            DrawTiers(mesh, tier => tier.Opaque);
        }

        if (_blendedMeshes.Length > 0)
        {
            // Glass and its like, over the solid geometry. Unsorted, so overlapping panes blend in
            // draw order; depth writes stay off so they cannot occlude each other.
            GL.Enable(EnableCap.Blend);
            GL.BlendFunc(BlendingFactor.SrcAlpha, BlendingFactor.OneMinusSrcAlpha);
            GL.DepthMask(false);
            foreach (Mesh mesh in _blendedMeshes)
            {
                if (mesh.FineCount + mesh.CoarseCount > 0)
                {
                    GL.BindVertexArray(mesh.Vao);
                    GL.Uniform2(_uBillboardFacing, mesh.Facing.X, mesh.Facing.Y);
                    DrawTiers(mesh, tier => tier.Blended);
                }
            }
            GL.DepthMask(true);
            GL.Disable(EnableCap.Blend);
        }
        GL.BindVertexArray(0);
    }

    private void RefreshRangeHandles()
    {
        foreach (Mesh mesh in _meshes)
        {
            foreach (Tier tier in new[] { mesh.FineTier, mesh.CoarseTier })
            {
                for (int i = 0; i < tier.Ranges.Count; i++)
                {
                    MaterialSurface surface = tier.Ranges[i].Surface;
                    tier.Handles[i] = Resolve(surface.DiffuseTexturePath);
                    tier.SecondHandles[i] = Resolve(surface.SecondDiffusePath);
                    tier.MaskHandles[i] = Resolve(surface.MaskPath);
                }
            }
        }

        int Resolve(string? path)
            => path is not null && _textures.TryGetValue(path, out int handle) && handle > 0 ? handle : 0;
    }

    /// <summary>
    /// Every opaque range from the sun's point of view, depth only. Blended ranges are left out:
    /// glass casting a solid shadow reads worse than glass casting none.
    /// </summary>
    public void DrawDepth(Matrix4 lightViewProjection, Vector3 cameraPosition)
    {
        _depthProgram.Use();
        GL.UniformMatrix4(_dViewProjection, false, ref lightViewProjection);
        GL.Uniform3(_dCameraPosition, cameraPosition);

        foreach (Mesh mesh in _meshes)
        {
            if (mesh.FineCount + mesh.CoarseCount == 0)
            {
                continue;
            }

            GL.BindVertexArray(mesh.Vao);
            GL.Uniform2(_dBillboardFacing, mesh.Facing.X, mesh.Facing.Y);
            DepthTier(mesh, mesh.FineTier, 0, mesh.FineCount);
            DepthTier(mesh, mesh.CoarseTier, mesh.CoarseStart, mesh.CoarseCount);
        }
        GL.BindVertexArray(0);

        void DepthTier(Mesh mesh, Tier tier, int firstInstance, int instanceCount)
        {
            if (instanceCount == 0)
            {
                return;
            }

            PointInstanceAttribs(mesh, firstInstance);
            foreach (int i in tier.Opaque)
            {
                MaterialRange range = tier.Ranges[i];
                int handle = tier.Handles[i];
                GL.Uniform1(_dUseTexture, handle > 0 ? 1f : 0f);
                GL.Uniform1(_dAlphaMode, (int)range.Surface.Alpha);
                if (handle > 0)
                {
                    GL.Uniform2(_dDiffuseTiling,
                        range.Surface.DiffuseTiling.X, range.Surface.DiffuseTiling.Y);
                    GL.ActiveTexture(TextureUnit.Texture0);
                    GL.BindTexture(TextureTarget.Texture2D, handle);
                }
                DrawRange(new IndexRange(range.Start, range.Count), instanceCount);
            }
        }
    }

    /// <summary>GL 3.3 has no baseInstance, so drawing the coarse partition means repointing the
    /// instance attributes at its first entry.</summary>
    private static void PointInstanceAttribs(Mesh mesh, int firstInstance)
    {
        if (mesh.PointedAt == firstInstance)
        {
            return;
        }

        mesh.PointedAt = firstInstance;
        GL.BindBuffer(BufferTarget.ArrayBuffer, mesh.InstanceBuffer);
        int baseOffset = firstInstance * InstanceStrideBytes;
        GL.VertexAttribPointer(3, 3, VertexAttribPointerType.Float, false, InstanceStrideBytes, baseOffset);
        GL.VertexAttribPointer(4, 3, VertexAttribPointerType.Float, false, InstanceStrideBytes, baseOffset + 12);
        GL.VertexAttribPointer(5, 3, VertexAttribPointerType.Float, false, InstanceStrideBytes, baseOffset + 24);
    }

    private static void DrawRange(IndexRange range, int instanceCount)
        => GL.DrawElementsInstanced(PrimitiveType.Triangles, range.Count, DrawElementsType.UnsignedInt,
            (IntPtr)(range.Start * sizeof(int)), instanceCount);

    /// <summary>Null when the bytes are missing or not a decodable .xbt; the range then keeps its
    /// tint for good.</summary>
    private Decoded? DecodeTexture(string path)
    {
        if (_readByPath(path) is not { } xbt)
        {
            return null;
        }

        // Includes the top level from the "_mip0.xbt" companion, which is where half the game's
        // textures keep it.
        if (XbtSurface.TryRead(xbt, _readByPath) is { } surface)
        {
            return new Decoded(surface.Width, surface.Height, surface.FourCc, surface.Mips);
        }

        // Not a plain DXT payload - let the BCn decoder produce RGBA instead.
        try
        {
            (_, byte[] dds) = XbtTexture.Split(xbt);
            return XbtImage.TryDecodeRgbaDds(dds) is { } rgba
                ? new Decoded(rgba.Width, rgba.Height, 0, [rgba.Rgba])
                : null;
        }
        catch (Exception)
        {
            return null;
        }
    }

    private int Upload(Decoded texture)
    {
        int handle = GL.GenTexture();
        GL.BindTexture(TextureTarget.Texture2D, handle);
        bool mipmapped;
        if (texture.FourCc != 0)
        {
            InternalFormat format = texture.FourCc switch
            {
                DdsSurface.FourCcDxt3 => InternalFormat.CompressedRgbaS3tcDxt3Ext,
                DdsSurface.FourCcDxt5 => InternalFormat.CompressedRgbaS3tcDxt5Ext,
                _ => InternalFormat.CompressedRgbaS3tcDxt1Ext,
            };
            int w = texture.Width, h = texture.Height;
            for (int level = 0; level < texture.Mips.Count; level++)
            {
                byte[] mip = texture.Mips[level];
                GL.CompressedTexImage2D(TextureTarget.Texture2D, level, format, w, h, 0, mip.Length, mip);
                TextureBytesResident += mip.Length;
                w = Math.Max(1, w / 2);
                h = Math.Max(1, h / 2);
            }
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMaxLevel, texture.Mips.Count - 1);
            mipmapped = texture.Mips.Count > 1;
        }
        else
        {
            GL.TexImage2D(TextureTarget.Texture2D, 0, PixelInternalFormat.Rgba8,
                texture.Width, texture.Height, 0, PixelFormat.Rgba, PixelType.UnsignedByte, texture.Mips[0]);
            GL.GenerateMipmap(GenerateMipmapTarget.Texture2D);
            TextureBytesResident += (long)texture.Width * texture.Height * 4 * 4 / 3;
            mipmapped = true;
        }

        GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter,
            (int)(mipmapped ? TextureMinFilter.LinearMipmapLinear : TextureMinFilter.Linear));
        GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)TextureMagFilter.Linear);
        GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapS, (int)TextureWrapMode.Repeat);
        GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapT, (int)TextureWrapMode.Repeat);
        if (mipmapped)
        {
            GlSampling.Anisotropic(TextureTarget.Texture2D);
        }
        return handle;
    }

    public void Dispose()
    {
        _program.Dispose();
        _depthProgram.Dispose();
        foreach (Mesh mesh in _meshes)
        {
            GL.DeleteVertexArray(mesh.Vao);
            GL.DeleteBuffer(mesh.VertexBuffer);
            GL.DeleteBuffer(mesh.IndexBuffer);
            GL.DeleteBuffer(mesh.InstanceBuffer);
        }
        foreach (int handle in _textures.Values)
        {
            if (handle > 0)
            {
                GL.DeleteTexture(handle);
            }
        }
    }
}
