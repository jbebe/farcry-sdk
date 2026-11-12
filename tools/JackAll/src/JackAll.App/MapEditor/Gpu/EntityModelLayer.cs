using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.Numerics;
using System.Runtime.InteropServices;
using JackAll.App.FileHandlers.Xbt;
using JackAll.Tools.World;
using JackAll.Tools.Xbt;
using Vortice.Direct3D;
using Vortice.Direct3D11;
using Vortice.DXGI;
using Vortice.Mathematics;
using static JackAll.App.MapEditor.Gpu.GpuDevice;

namespace JackAll.App.MapEditor.Gpu;

/// <summary>
/// Draws every mesh-resolved entity as its real .xbg geometry: one vertex and index buffer per unique
/// mesh, one instanced draw per material range per populated detail tier. Both tiers draw textured,
/// with the maps decoded on background threads and uploaded on a per-frame budget; a range whose maps
/// have not arrived yet draws flat in the entity's marker colour until they do.
/// </summary>
public sealed class EntityModelLayer : IDisposable
{
    /// <summary>Floats per instance: x y z, then the three Euler angles in radians, then r g b.</summary>
    private const int InstanceStride = 9;

    /// <summary>
    /// The vertex streams and one range's material, shared by the lit pass and the shadow pass, and
    /// where an instance lands. Billboards keep facing the camera in the shadow map rather than the
    /// sun: the card's shadow then matches the card that is actually on screen, where turning it
    /// edge-on to the sun would cast nothing at all.
    /// </summary>
    private const string TransformHlsl =
        """
        struct VertexInput
        {
            float3 position : POSITION;
            float3 normal : NORMAL;
            float2 uv : TEXCOORD0;
            float2 uv1 : TEXCOORD1;
            float3 vertexColour : COLOR;
            float3 instancePosition : INSTANCE_POSITION;
            float3 instanceAngles : INSTANCE_ANGLES;
            float3 tint : INSTANCE_TINT;
        };

        cbuffer Params : register(b0)
        {
            float4x4 viewProjection;
            float3 cameraPosition;
        };

        // One material range, fixed while its mesh is resident; the use flags follow its maps
        // arriving and leaving.
        cbuffer Material : register(b2)
        {
            float3 tintBase;
            float useTexture;
            float3 tintColour;
            float useSecond;
            float3 tintSecond;
            float useMask;
            float3 specularBase;
            float useBlood;
            float3 specularColour;
            float specularPower;
            float2 diffuseTiling;
            float2 secondTiling;
            float2 maskTiling;
            float2 bloodTiling;
            float4 uvSets;
            int alphaMode;
            // MaterialTemplate as drawn: 0 Generic (Hair included), 1 Cloth, 2 Skin.
            int materialTemplate;
        };

        // One mesh, bound once for all its ranges.
        cbuffer MeshConstants : register(b3)
        {
            // Which way this mesh's card looks in model space, or zero for ordinary geometry that
            // keeps the orientation the world gave it.
            float2 billboardFacing;
        };

        // The engine's Euler order, as EulerXyz: about X, then Y, then Z. Applied as mul(v, m), so
        // the product reads left to right as Rx, Ry, Rz.
        float3x3 spin(float3 a)
        {
            float3 s = sin(a), c = cos(a);
            float3x3 ry = float3x3(c.y, 0.0, -s.y,   0.0, 1.0, 0.0,   s.y, 0.0, c.y);
            float3x3 rx = float3x3(1.0, 0.0, 0.0,    0.0, c.x, s.x,   0.0, -s.x, c.x);
            float3x3 rz = float3x3(c.z, s.z, 0.0,   -s.z, c.z, 0.0,   0.0, 0.0, 1.0);
            return mul(mul(rx, ry), rz);
        }
        // Turns the card so it looks at the camera, about Z only - a plant leans with the
        // ground, it does not tip back when you climb a hill.
        float3x3 faceCamera(float3 origin)
        {
            float2 toCamera = cameraPosition.xy - origin.xy;
            if (dot(toCamera, toCamera) < 1e-6) { return spin(0.0); }
            toCamera = normalize(toCamera);
            // The yaw that carries billboardFacing onto toCamera, as a difference of angles.
            float yaw = atan2(toCamera.y, toCamera.x) - atan2(billboardFacing.y, billboardFacing.x);
            return spin(float3(0.0, 0.0, yaw));
        }
        float3x3 instanceRotation(VertexInput i)
        {
            return dot(billboardFacing, billboardFacing) > 0.5
                ? faceCamera(i.instancePosition)
                : spin(i.instanceAngles);
        }

        Texture2D diffuse : register(t0);

        // What the vertex stages hand the pixel stages, depth pass and lit pass.
        struct DepthVaryings
        {
            precise float4 position : SV_Position;
            float2 texUv : TEXCOORD0;
            float2 texUv1 : TEXCOORD1;
        };
        struct Varyings
        {
            precise float4 position : SV_Position;
            float3 worldNormal : NORMAL;
            float3 worldPosition : TEXCOORD0;
            float3 baseColour : COLOR;
            float2 texUv : TEXCOORD1;
            float2 texUv1 : TEXCOORD2;
            float3 vertexWeights : TEXCOORD3;
        };
        """;

    /// <summary>A vertex as uploaded: position, signed-byte normal, both UV sets, byte vertex colour.
    /// The byte triples each carry a fourth byte of padding, which the formats read and the shaders
    /// ignore.</summary>
    private const int PackedStride = 36;

    private static readonly InputElementDescription[] Layout =
    [
        new("POSITION", 0, Format.R32G32B32_Float, 0, 0),
        new("NORMAL", 0, Format.R8G8B8A8_SNorm, 12, 0),
        new("TEXCOORD", 0, Format.R32G32_Float, 16, 0),
        new("TEXCOORD", 1, Format.R32G32_Float, 24, 0),
        new("COLOR", 0, Format.R8G8B8A8_UNorm, 32, 0),
        new("INSTANCE_POSITION", 0, Format.R32G32B32_Float, 0, 1, InputClassification.PerInstanceData, 1),
        new("INSTANCE_ANGLES", 0, Format.R32G32B32_Float, 12, 1, InputClassification.PerInstanceData, 1),
        new("INSTANCE_TINT", 0, Format.R32G32B32_Float, 24, 1, InputClassification.PerInstanceData, 1),
    ];

    /// <summary>The GPU memory past which meshes no stream placed lately are released.</summary>
    private const long MeshBudget = 32 << 20;

    private const long MeshUploadBudgetPerFrame = 4 << 20;

    /// <summary>The GPU memory past which textures no stream uses are released.</summary>
    private const long TextureBudget = 64 << 20;

    private const int InstanceStrideBytes = InstanceStride * sizeof(float);
    private const int MaxDecoders = 2;
    private const long UploadBudgetPerFrame = 8 << 20;

    /// <summary>The <c>Material</c> constant buffer, field for field.</summary>
    [StructLayout(LayoutKind.Sequential)]
    private struct MaterialConstants
    {
        public Vector3 TintBase;
        public float UseTexture;
        public Vector3 TintColour;
        public float UseSecond;
        public Vector3 TintSecond;
        public float UseMask;
        public Vector3 SpecularBase;
        public float UseBlood;
        public Vector3 SpecularColour;
        public float SpecularPower;
        public Vector2 DiffuseTiling;
        public Vector2 SecondTiling;
        public Vector2 MaskTiling;
        public Vector2 BloodTiling;
        public Vector4 UvSets;
        public int AlphaMode;
        public int MaterialTemplate;
        /// <summary>To the 16 bytes a constant buffer's size comes in.</summary>
        public Vector2 Pad;
    }

    /// <summary>One detail tier's material ranges and the live textures behind each.</summary>
    private sealed class Tier
    {
        public required IReadOnlyList<MaterialRange> Ranges;
        /// <summary>Indices into <see cref="Ranges"/> per pass, so neither pass walks the other's.
        /// </summary>
        public required int[] Opaque;
        public required int[] Blended;
        /// <summary>Live textures per range, one array per map the shader reads; null where that
        /// map is absent or still streaming.</summary>
        public required ID3D11ShaderResourceView?[] Diffuse;
        public required ID3D11ShaderResourceView?[] Second;
        public required ID3D11ShaderResourceView?[] Mask;
        public required ID3D11ShaderResourceView?[] Blood;
        /// <summary>Each range's <c>Material</c> buffer while the mesh is resident.</summary>
        public required ID3D11Buffer?[] Materials;

        /// <summary>Whether its maps have been queued for decoding.</summary>
        public bool Requested;

        public static Tier Of(IReadOnlyList<MaterialRange> ranges) => new()
        {
            Ranges = ranges,
            Opaque = [.. Enumerable.Range(0, ranges.Count).Where(r => ranges[r].Alpha != MaterialAlpha.Blend)],
            Blended = [.. Enumerable.Range(0, ranges.Count).Where(r => ranges[r].Alpha == MaterialAlpha.Blend)],
            Diffuse = new ID3D11ShaderResourceView?[ranges.Count],
            Second = new ID3D11ShaderResourceView?[ranges.Count],
            Mask = new ID3D11ShaderResourceView?[ranges.Count],
            Blood = new ID3D11ShaderResourceView?[ranges.Count],
            Materials = new ID3D11Buffer?[ranges.Count],
        };
    }

    private sealed class Mesh
    {
        public required WorldModel Model;
        /// <summary>Null until the mesh is first streamed, and again once it is evicted.</summary>
        public Geometry? Geometry;
        public ID3D11Buffer? InstanceBuffer;
        /// <summary>Its <c>MeshConstants</c> buffer while resident.</summary>
        public ID3D11Buffer? Constants;
        /// <summary>The stream that last placed it, so eviction takes the longest unused first.</summary>
        public int LastStreamed;
        /// <summary>Both tiers textured: the far ring is the file's coarsest LOD, not a flat tint.</summary>
        public required Tier FineTier;
        public required Tier CoarseTier;
        public required float[] Staging;
        /// <summary>Instances the entity rows can place; the staging grows to it on the next pass.</summary>
        public int Wanted;
        public int FineCount, CoarseCount;
        public int FineFloats, CoarseFloats;
        public int CoarseStart;
        /// <summary>Model-space direction the card looks, or zero when the mesh is not a billboard.</summary>
        public Vector2 Facing;

        /// <summary>Drawn only within <see cref="GrassDistance"/>.</summary>
        public bool Grass;

        public bool Resident => Geometry is not null;
    }

    /// <summary>What an entity contributes besides its live position and yaw: the meshes its
    /// graphics slots resolved to, all drawn at the entity's transform.</summary>
    private readonly record struct Row(int[] Models, float R, float G, float B);

    /// <summary>A texture decoded off-thread: DXT mips uploaded as-is, or (FourCc 0) one RGBA image.</summary>
    private sealed record Decoded(int Width, int Height, uint FourCc, IReadOnlyList<byte[]> Mips);

    private readonly ShaderProgram _program;
    private readonly Mesh[] _meshes;
    /// <summary>Vertex and index buffers per baked array, shared by every resident mesh drawing it -
    /// a kit look shares its part's.</summary>
    private readonly Dictionary<float[], Geometry> _buffers = new(ReferenceEqualityComparer.Instance);

    private sealed record Geometry(ID3D11Buffer Vertex, ID3D11Buffer Index, Format IndexFormat, long Bytes)
    {
        public int Users;
    }

    private int _stream;

    /// <summary>Meshes the current streams place that are not resident yet.</summary>
    private int _waiting;
    private readonly Dictionary<WorldEntity, Row> _rows;

    /// <summary>The shown entities with meshes, by the sector each was filed under.</summary>
    private readonly SectorIndex<WorldEntity> _entities = new();
    private readonly Dictionary<WorldEntity, (int X, int Y)> _filedAt = [];
    private readonly Func<string, byte[]?> _readByPath;
    /// <summary>Map per path: null while decoding or once it failed for good, else the upload.</summary>
    private readonly Dictionary<string, ID3D11ShaderResourceView?> _textures = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Each uploaded texture's size, and the stream that last placed a mesh drawing it.</summary>
    private readonly Dictionary<string, (long Bytes, int LastStreamed)> _textureUse = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentQueue<string> _requests = new();
    private readonly ConcurrentQueue<(string Path, Decoded? Texture)> _decoded = new();
    private int _activeDecoders;
    private bool _texturesChanged;
    private readonly int _uViewProjection;
    private readonly int _uCameraPosition;
    /// <summary>Just the meshes carrying a blended range, so the second pass skips the rest.</summary>
    private readonly Mesh[] _blendedMeshes;

    private readonly ShaderProgram _depthProgram;
    private readonly int _dViewProjection;
    private readonly int _dCameraPosition;

    private readonly ID3D11Buffer[] _streams = new ID3D11Buffer[2];
    private static readonly uint[] Strides = [PackedStride, InstanceStrideBytes];
    private static readonly uint[] Offsets = [0, 0];

    /// <summary>What the streamed diffuse textures currently hold on the GPU.</summary>
    public long TextureBytesResident { get; private set; }

    /// <summary>What the resident meshes' vertex, index and instance buffers hold on the GPU.</summary>
    public long MeshBytesResident { get; private set; }

    /// <summary>Whether a texture or a mesh is still queued, decoding or waiting to upload. Uploading
    /// happens inside <see cref="Draw"/>, so a viewport that only redraws on demand has to keep
    /// asking for frames while this is true or they never arrive.</summary>
    public bool Streaming => !_requests.IsEmpty || !_decoded.IsEmpty || _activeDecoders > 0 || _waiting > 0;

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
                Model = model,
                FineTier = Tier.Of(model.MaterialRanges),
                CoarseTier = Tier.Of(model.CoarseMaterialRanges),
                Staging = new float[capacity[i] * InstanceStride],
                Wanted = capacity[i],
                Facing = model.BillboardFacing ?? Vector2.Zero,
                Grass = model.MaterialRanges.Any(r => r.Surface.Grass),
            };
            if (mesh.FineTier.Blended.Length > 0 || mesh.CoarseTier.Blended.Length > 0)
            {
                blended.Add(mesh);
            }

            _meshes[i] = mesh;
        }
        _blendedMeshes = [.. blended];

        _depthProgram = new ShaderProgram(
            $$"""
            {{TransformHlsl}}
            DepthVaryings main(VertexInput i)
            {
                DepthVaryings o;
                o.texUv = i.uv;
                o.texUv1 = i.uv1;
                o.position = toTarget(mul(viewProjection,
                    float4(mul(i.position, instanceRotation(i)) + i.instancePosition, 1.0)));
                return o;
            }
            """,
            $$"""
            {{TransformHlsl}}
            void main(DepthVaryings v)
            {
                // The same coverage test the lit pass makes. Without it a tree casts the shadow of
                // the cards its leaves are painted on.
                if (useTexture > 0.5 && alphaMode == 1
                    && diffuse.Sample(anisoWrap, lerp(v.texUv, v.texUv1, uvSets.x) * diffuseTiling).a < 0.5)
                {
                    discard;
                }
            }
            """,
            Layout);
        _dViewProjection = _depthProgram.Offset("viewProjection");
        _dCameraPosition = _depthProgram.Offset("cameraPosition");

        _program = new ShaderProgram(
            $$"""
            {{TransformHlsl}}
            Varyings main(VertexInput i)
            {
                Varyings o;
                float3x3 rotation = instanceRotation(i);
                o.worldPosition = mul(i.position, rotation) + i.instancePosition;
                // Pure rotation, so the normal rotates the same way - no inverse-transpose needed.
                o.worldNormal = mul(i.normal, rotation);
                o.baseColour = i.tint;
                o.texUv = i.uv;
                o.texUv1 = i.uv1;
                o.vertexWeights = i.vertexColour;
                o.position = toTarget(mul(viewProjection, float4(o.worldPosition, 1.0)));
                return o;
            }
            """,
            $$"""
            {{TransformHlsl}}
            Texture2D diffuse2 : register(t1);
            Texture2D maskMap : register(t2);
            Texture2D bloodMap : register(t3);
            {{SceneLighting.SkyHlsl}}
            {{SceneLighting.SurfaceHlsl}}
            {{SceneLighting.ShadowHlsl}}
            float4 main(Varyings v) : SV_Target
            {
                float3 albedo = v.baseColour;
                float coverage = 1.0;
                float4 texel = 1.0;
                float4 mask = 1.0;
                float3 weights = saturate(v.vertexWeights);
                if (useTexture > 0.5)
                {
                    texel = diffuse.Sample(anisoWrap, lerp(v.texUv, v.texUv1, uvSets.x) * diffuseTiling);
                    // Only materials that asked for it read alpha as coverage; on the rest it is a
                    // gloss or spec mask and would erase the surface.
                    if (alphaMode == 1 && texel.a < 0.5) { discard; }
                    if (alphaMode == 2) { coverage = texel.a; }

                    // The engine's own shaders, decoded from their bytecode. A material with no
                    // mask samples white, which is why an unmasked surface takes its tint in full.
                    if (useMask > 0.5)
                    {
                        mask = maskMap.Sample(anisoWrap, lerp(v.texUv, v.texUv1, uvSets.z) * maskTiling);
                    }

                    // Generic gates its tint pair by the mask as well as the vertex; Cloth and Skin
                    // by the vertex alone.
                    float tintWeight = weights.b * (materialTemplate == 0 ? mask.b : 1.0);
                    albedo = srgbToLinear(texel.rgb) * lerp(tintBase, tintColour, tintWeight);
                    if (useSecond > 0.5)
                    {
                        float3 layer2 = srgbToLinear(diffuse2.Sample(anisoWrap,
                            lerp(v.texUv, v.texUv1, uvSets.y) * secondTiling).rgb) * tintSecond;
                        albedo = lerp(albedo, layer2, mask.g * weights.g);
                    }
                    if (useBlood > 0.5)
                    {
                        float3 blood = srgbToLinear(bloodMap.Sample(anisoWrap,
                            lerp(v.texUv, v.texUv1, uvSets.w) * bloodTiling).rgb);
                        albedo *= lerp(1.0, blood, materialTemplate == 1 ? weights.r : weights.g);
                    }
                }
                else
                {
                    // A range still streaming its maps, or one with none to stream, stands in for
                    // the average of a textured surface; a saturated marker colour at full
                    // additive lighting would flash as a glowing placeholder instead.
                    albedo = srgbToLinear(v.baseColour) * 0.55;
                }
                float3 n = normalize(v.worldNormal);
                // Many parts are single-sided shells, so light whichever side is showing. The test
                // is against the eye rather than the rasterizer's facing, which the game's winding
                // and this renderer's do not agree on.
                bool flipped = dot(n, cameraPosition - v.worldPosition) < 0.0;
                if (flipped) { n = -n; }
                float ndotl = max(dot(n, sunDirection), 0.0);
                if (demo < 0.5)
                {
                    return float4(shadeDiffuse(albedo, n, ndotl, 1.0), coverage);
                }

                // An opaque Generic material's diffuse alpha is its gloss mask. A flipped normal is
                // only a guess at which way a shell faces, so the inside of a shell stays matte.
                float3 spec = materialTemplate == 1 ? lerp(specularBase * mask.b, specularColour, mask.r)
                    : materialTemplate == 2 ? specularBase * mask.g
                    : lerp(specularBase, specularColour, weights.b * mask.b) * (alphaMode == 0 ? texel.a : 0.0);
                spec *= (useTexture > 0.5 && !flipped) ? 1.0 : 0.0;

                float3 toEye = normalize(cameraPosition - v.worldPosition);
                float viewDistance = distance(cameraPosition, v.worldPosition);

                // Nothing baked here to fall back on, so past the cascades a mesh is simply lit.
                float sunAmount = ndotl
                    * lerp(1.0, sampleShadow(v.worldPosition, viewDistance, ndotl),
                           shadowFade(viewDistance));
                float3 lit = shadeSurface(albedo, n, toEye, sunDirection, sunAmount, spec, specularPower,
                    v.position.xy);
                return float4(applyHaze(lit, viewDistance, v.worldPosition.z), coverage);
            }
            """,
            Layout);
        _uViewProjection = _program.Offset("viewProjection");
        _uCameraPosition = _program.Offset("cameraPosition");
    }

    /// <summary>The scatter placements this layer draws, by sector; null for the entity layer.</summary>
    public SectorIndex<ScatterInstance>? Scatter { get; init; }

    /// <summary>How far from the camera scatter grass draws.</summary>
    private const float GrassDistance = 100f;

    /// <summary>
    /// Re-buckets what stands within <paramref name="ring"/> sectors of the camera's into per-mesh
    /// fine/coarse instance streams and uploads them. Fine instances pack forward from the front of
    /// each mesh's staging array, coarse ones backward from its end, so one pass fills both without
    /// counting first.
    /// </summary>
    public void Stream(Vector3 cameraPosition, int ring)
    {
        foreach (Mesh mesh in _meshes)
        {
            if (mesh.Wanted * InstanceStride > mesh.Staging.Length)
            {
                // Doubled, so pasting copies one at a time does not reallocate on every paste.
                int capacity = mesh.Wanted * 2;
                if (mesh.InstanceBuffer is not null)
                {
                    MeshBytesResident += (capacity * InstanceStride - mesh.Staging.Length) * sizeof(float);
                    mesh.InstanceBuffer.Dispose();
                    mesh.InstanceBuffer = InstanceBuffer(capacity * InstanceStride);
                }
                mesh.Staging = new float[capacity * InstanceStride];
            }

            mesh.FineCount = 0;
            mesh.CoarseCount = 0;
            mesh.FineFloats = 0;
            mesh.CoarseFloats = mesh.Staging.Length;
        }

        (int X, int Y) camera = WorldModels.SectorOf(cameraPosition);
        foreach ((List<WorldEntity> entities, bool fine) in InRing(_entities))
        {
            foreach (WorldEntity entity in entities)
            {
                Row row = _rows[entity];
                Vector3 angles = entity.Angles * (MathF.PI / 180f);
                foreach (int model in row.Models)
                {
                    Place(_meshes[model], entity.Position!.Value, angles, fine, row.R, row.G, row.B);
                }
            }
        }

        var eye = new Vector2(cameraPosition.X, cameraPosition.Y);
        foreach ((List<ScatterInstance> placements, bool fine) in InRing(Scatter))
        {
            foreach (ScatterInstance placement in placements)
            {
                Mesh mesh = _meshes[placement.Model];
                if (mesh.Grass && Vector2.DistanceSquared(
                        eye, new Vector2(placement.Position.X, placement.Position.Y)) > GrassDistance * GrassDistance)
                {
                    continue;
                }
                Place(mesh, placement.Position, Vector3.Zero, fine, 1f, 1f, 1f);
            }
        }

        _stream++;
        _waiting = 0;
        foreach (Mesh mesh in _meshes)
        {
            if (mesh.FineCount + mesh.CoarseCount > 0)
            {
                mesh.LastStreamed = _stream;
                _waiting += mesh.Resident ? 0 : 1;
                MarkTextures(mesh.FineCount > 0 ? mesh.FineTier : null);
                MarkTextures(mesh.CoarseCount > 0 ? mesh.CoarseTier : null);
            }
        }
        Evict();
        EvictTextures();
        UploadStaging();

        IEnumerable<(List<T> Items, bool Fine)> InRing<T>(SectorIndex<T>? index)
            => (index?.Ring(camera, ring) ?? []).Select(s => (s.Items, s.Distance <= WorldModels.FineRadius));
    }

    /// <summary>Files the shown entities under their sectors for <see cref="Stream"/>. Needed when
    /// which entities are shown changes; a move needs only <see cref="Moved"/>.</summary>
    public void SetVisible(IEnumerable<WorldEntity> visible)
    {
        _entities.Clear();
        _filedAt.Clear();
        foreach (WorldEntity entity in visible)
        {
            if (_rows.ContainsKey(entity))
            {
                File(entity, WorldModels.SectorOf(entity.Position!.Value));
            }
        }
    }

    /// <summary>Re-files shown entities whose placement changed; true when any of them draws here.</summary>
    public bool Moved(IEnumerable<WorldEntity> entities)
    {
        bool drawn = false;
        foreach (WorldEntity entity in entities)
        {
            if (!_filedAt.TryGetValue(entity, out (int X, int Y) filed))
            {
                continue;
            }
            drawn = true;
            (int X, int Y) sector = WorldModels.SectorOf(entity.Position!.Value);
            if (sector != filed)
            {
                _entities.Remove(filed, entity);
                File(entity, sector);
            }
        }
        return drawn;
    }

    private void File(WorldEntity entity, (int X, int Y) sector)
    {
        _entities.Add(sector, entity);
        _filedAt[entity] = sector;
    }

    private static ID3D11Buffer InstanceBuffer(int floats)
        => Device.CreateBuffer((uint)(Math.Max(1, floats) * sizeof(float)), BindFlags.VertexBuffer);

    /// <summary>Uploads a mesh's buffers, sharing the geometry another resident mesh already uploaded.</summary>
    private void MakeResident(Mesh mesh)
    {
        WorldModel model = mesh.Model;
        if (!_buffers.TryGetValue(model.Vertices, out Geometry? geometry))
        {
            _buffers[model.Vertices] = geometry = UploadGeometry(model);
            MeshBytesResident += geometry.Bytes;
        }
        geometry.Users++;
        mesh.Geometry = geometry;

        mesh.InstanceBuffer = InstanceBuffer(mesh.Staging.Length);
        var facing = new Vector4(mesh.Facing, 0f, 0f);
        mesh.Constants = Device.CreateBuffer(in facing, new BufferDescription(16, BindFlags.ConstantBuffer, ResourceUsage.Immutable));
        MeshBytesResident += mesh.Staging.Length * sizeof(float);
        WriteMaterials(mesh.FineTier);
        WriteMaterials(mesh.CoarseTier);
    }

    /// <summary>One baked array in the uploaded layout, with 16-bit indices where its vertices allow.</summary>
    private static Geometry UploadGeometry(WorldModel model)
    {
        float[] source = model.Vertices;
        int count = source.Length / WorldModel.FloatsPerVertex;
        var packed = new byte[count * PackedStride];
        for (int v = 0; v < count; v++)
        {
            Span<byte> to = packed.AsSpan(v * PackedStride, PackedStride);
            ReadOnlySpan<float> from = source.AsSpan(v * WorldModel.FloatsPerVertex, WorldModel.FloatsPerVertex);
            for (int k = 0; k < 3; k++)
            {
                BinaryPrimitives.WriteSingleLittleEndian(to[(k * 4)..], from[k]);
                to[12 + k] = (byte)(sbyte)Math.Clamp(MathF.Round(from[3 + k] * 127f), -127f, 127f);
                to[32 + k] = (byte)Math.Clamp(MathF.Round(from[10 + k] * 255f), 0f, 255f);
            }
            for (int k = 0; k < 4; k++)
            {
                BinaryPrimitives.WriteSingleLittleEndian(to[(16 + k * 4)..], from[6 + k]);
            }
        }

        ID3D11Buffer vertex = Device.CreateBuffer(packed, BindFlags.VertexBuffer, ResourceUsage.Immutable);
        bool shortIndices = count <= ushort.MaxValue + 1;
        ID3D11Buffer index;
        long indexBytes;
        if (shortIndices)
        {
            ushort[] narrow = [.. model.Indices.Select(i => (ushort)i)];
            indexBytes = narrow.Length * sizeof(ushort);
            index = Device.CreateBuffer(narrow, BindFlags.IndexBuffer, ResourceUsage.Immutable);
        }
        else
        {
            indexBytes = model.Indices.Length * sizeof(int);
            index = Device.CreateBuffer(model.Indices, BindFlags.IndexBuffer, ResourceUsage.Immutable);
        }
        return new Geometry(vertex, index, shortIndices ? Format.R16_UInt : Format.R32_UInt, packed.Length + indexBytes);
    }

    /// <summary>Fills a resident tier's <c>Material</c> buffers from its ranges and the maps they have
    /// now. Authored material colours, so linearised here once rather than per pixel.</summary>
    private static void WriteMaterials(Tier tier)
    {
        for (int i = 0; i < tier.Ranges.Count; i++)
        {
            WriteMaterial(tier, i);
        }
    }

    private static void WriteMaterial(Tier tier, int i)
    {
        MaterialSurface surface = tier.Ranges[i].Surface;
        var constants = new MaterialConstants
        {
            TintBase = SceneLighting.Linear(surface.TintBase),
            TintColour = SceneLighting.Linear(surface.Tint),
            TintSecond = SceneLighting.Linear(surface.SecondTint),
            SpecularBase = surface.SpecularBase,
            SpecularColour = surface.SpecularColour,
            SpecularPower = surface.SpecularPower,
            DiffuseTiling = surface.DiffuseTiling,
            SecondTiling = surface.SecondDiffuseTiling,
            MaskTiling = surface.MaskTiling,
            BloodTiling = surface.BloodTiling,
            UvSets = surface.UvSets,
            AlphaMode = (int)surface.Alpha,
            MaterialTemplate = surface.Template == MaterialTemplate.Hair ? 0 : (int)surface.Template,
            // A map that is absent, failed or still streaming leaves its layer switched off
            // rather than sampling whatever happens to be bound.
            UseTexture = tier.Diffuse[i] is null ? 0f : 1f,
            UseSecond = tier.Second[i] is null ? 0f : 1f,
            UseMask = tier.Mask[i] is null ? 0f : 1f,
            UseBlood = tier.Blood[i] is null ? 0f : 1f,
        };
        if (tier.Materials[i] is { } buffer)
        {
            Context.UpdateSubresource(in constants, buffer);
        }
        else
        {
            tier.Materials[i] = Device.CreateBuffer(in constants,
                new BufferDescription((uint)Marshal.SizeOf<MaterialConstants>(), BindFlags.ConstantBuffer));
        }
    }

    private void MarkTextures(Tier? tier)
    {
        foreach (MaterialRange range in tier?.Ranges ?? [])
        {
            foreach (string? path in (string?[])[range.Surface.DiffuseTexturePath, range.Surface.SecondDiffusePath,
                         range.Surface.MaskPath, range.Surface.BloodPath])
            {
                if (path is not null && _textureUse.TryGetValue(path, out (long Bytes, int LastStreamed) use))
                {
                    _textureUse[path] = (use.Bytes, _stream);
                }
            }
        }
    }

    /// <summary>Releases the textures no stream has used for longest until the layer fits its
    /// budget. A released map is read again the next time a mesh drawing it is streamed.</summary>
    private void EvictTextures()
    {
        if (TextureBytesResident <= TextureBudget)
        {
            return;
        }
        bool evicted = false;
        foreach ((string path, (long bytes, _)) in _textureUse.Where(u => u.Value.LastStreamed != _stream)
                     .OrderBy(u => u.Value.LastStreamed).ToList())
        {
            _textures[path]?.Dispose();
            _textures.Remove(path);
            _textureUse.Remove(path);
            TextureBytesResident -= bytes;
            evicted = true;
            if (TextureBytesResident <= TextureBudget)
            {
                break;
            }
        }
        if (!evicted)
        {
            return;
        }
        _texturesChanged = true;
        foreach (Mesh mesh in _meshes)
        {
            mesh.FineTier.Requested = false;
            mesh.CoarseTier.Requested = false;
        }
    }

    /// <summary>Releases the meshes no stream has placed for longest until the layer fits its budget.</summary>
    private void Evict()
    {
        if (MeshBytesResident <= MeshBudget)
        {
            return;
        }
        foreach (Mesh mesh in _meshes.Where(m => m.Resident && m.LastStreamed != _stream).OrderBy(m => m.LastStreamed))
        {
            Release(mesh);
            if (MeshBytesResident <= MeshBudget)
            {
                return;
            }
        }
    }

    private void Release(Mesh mesh)
    {
        mesh.InstanceBuffer?.Dispose();
        mesh.Constants?.Dispose();
        mesh.Constants = null;
        mesh.InstanceBuffer = null;
        MeshBytesResident -= mesh.Staging.Length * sizeof(float);
        ReleaseMaterials(mesh.FineTier);
        ReleaseMaterials(mesh.CoarseTier);

        Geometry geometry = mesh.Geometry!;
        mesh.Geometry = null;
        if (--geometry.Users == 0)
        {
            geometry.Vertex.Dispose();
            geometry.Index.Dispose();
            _buffers.Remove(mesh.Model.Vertices);
            MeshBytesResident -= geometry.Bytes;
        }
    }

    private static void ReleaseMaterials(Tier tier)
    {
        for (int i = 0; i < tier.Materials.Length; i++)
        {
            tier.Materials[i]?.Dispose();
            tier.Materials[i] = null;
        }
    }

    /// <summary>Writes one placement into a mesh's staging, fine from the front and coarse from the
    /// back. A mesh whose staging is full drops the rest, which only a scatter can reach.</summary>
    private static void Place(Mesh mesh, Vector3 position, Vector3 angles, bool fine, float r, float g, float b)
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
            mesh.CoarseStart = mesh.CoarseFloats / InstanceStride;
            if (mesh.Resident)
            {
                UploadInstances(mesh);
            }
        }
    }

    private void UploadInstances(Mesh mesh)
    {
        if (mesh.FineCount > 0)
        {
            WriteInstances(mesh, 0, mesh.FineFloats);
            RequestTextures(mesh.FineTier);
        }
        if (mesh.CoarseCount > 0)
        {
            WriteInstances(mesh, mesh.CoarseFloats, mesh.Staging.Length);
            RequestTextures(mesh.CoarseTier);
        }
    }

    /// <summary>The staging floats from <paramref name="from"/> to <paramref name="to"/> into the same
    /// place in the instance buffer.</summary>
    private static void WriteInstances(Mesh mesh, int from, int to)
        => Context.UpdateSubresource(mesh.Staging.AsSpan(from, to - from), mesh.InstanceBuffer!,
            region: new Box(from * sizeof(float), 0, 0, to * sizeof(float), 1, 1));

    /// <summary>Uploads the streamed meshes not resident yet, as many as one frame's budget allows;
    /// the rest draw from a later frame.</summary>
    private void UploadWaiting()
    {
        if (_waiting == 0)
        {
            return;
        }
        long budget = MeshUploadBudgetPerFrame;
        foreach (Mesh mesh in _meshes)
        {
            if (budget <= 0)
            {
                return;
            }
            if (!mesh.Resident && mesh.FineCount + mesh.CoarseCount > 0)
            {
                long before = MeshBytesResident;
                MakeResident(mesh);
                UploadInstances(mesh);
                budget -= MeshBytesResident - before;
                _waiting--;
            }
        }
    }

    /// <summary>Draws <paramref name="copy"/> with <paramref name="original"/>'s meshes and tint.
    /// Returns those meshes, or null when the original draws none.</summary>
    public int[]? AddCopy(WorldEntity copy, WorldEntity original)
    {
        if (!_rows.TryGetValue(original, out Row row))
        {
            return null;
        }

        _rows[copy] = row;
        foreach (int model in row.Models)
        {
            _meshes[model].Wanted++;
        }
        return row.Models;
    }

    private void RequestTextures(Tier tier)
    {
        if (tier.Requested)
        {
            return;
        }
        tier.Requested = true;
        foreach (MaterialRange range in tier.Ranges)
        {
            // Every map, because the mask is what decides how much of the tint and of layer 2
            // reaches the surface - without it a material renders as its fully-tinted first layer.
            Request(range.Surface.DiffuseTexturePath);
            Request(range.Surface.SecondDiffusePath);
            Request(range.Surface.MaskPath);
            Request(range.Surface.BloodPath);
        }
        StartDecoders();

        void Request(string? path)
        {
            if (path is not null && _textures.TryAdd(path, null))
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

    public void Draw(Matrix4x4 viewProjection, Vector3 cameraPosition)
    {
        UploadWaiting();

        // Budgeted so a ring change worth of decodes cannot dump hundreds of uploads in one frame.
        long uploadedBefore = TextureBytesResident;
        while (TextureBytesResident - uploadedBefore < UploadBudgetPerFrame
            && _decoded.TryDequeue(out (string Path, Decoded? Texture) item))
        {
            long before = TextureBytesResident;
            ID3D11ShaderResourceView? texture = item.Texture is { } decoded ? UploadTexture(decoded) : null;
            _textures[item.Path] = texture;
            if (texture is not null)
            {
                _textureUse[item.Path] = (TextureBytesResident - before, _stream);
            }
            _texturesChanged = true;
        }
        if (_texturesChanged)
        {
            _texturesChanged = false;
            RefreshRangeTextures();
        }

        using GpuState pass = new();
        _program.Use();
        _program.Set(_uViewProjection, viewProjection);
        _program.Set(_uCameraPosition, cameraPosition);

        // Slots 0-3 are this layer's own, so what it bound last is still bound.
        var bound = new ID3D11ShaderResourceView?[4];
        void Bind(int slot, ID3D11ShaderResourceView? view)
        {
            if (view is not null && !ReferenceEquals(bound[slot], view))
            {
                Context.PSSetShaderResource((uint)slot, bound[slot] = view);
            }
        }

        // One tier's ranges over the instances of one partition of the mesh's instance buffer.
        void DrawRanges(Tier tier, int[] which, int firstInstance, int instanceCount)
        {
            foreach (int i in which)
            {
                Context.PSSetConstantBuffer(2, tier.Materials[i]);
                Bind(0, tier.Diffuse[i]);
                Bind(1, tier.Second[i]);
                Bind(2, tier.Mask[i]);
                Bind(3, tier.Blood[i]);
                DrawRange(_program, tier.Ranges[i], firstInstance, instanceCount);
            }
        }

        void DrawTiers(Mesh mesh, Func<Tier, int[]> which)
        {
            BindStreams(mesh);
            if (mesh.FineCount > 0)
            {
                DrawRanges(mesh.FineTier, which(mesh.FineTier), 0, mesh.FineCount);
            }
            if (mesh.CoarseCount > 0)
            {
                DrawRanges(mesh.CoarseTier, which(mesh.CoarseTier), mesh.CoarseStart, mesh.CoarseCount);
            }
        }

        foreach (Mesh mesh in _meshes)
        {
            if (mesh.Resident && mesh.FineCount + mesh.CoarseCount > 0)
            {
                DrawTiers(mesh, tier => tier.Opaque);
            }
        }

        if (_blendedMeshes.Length > 0)
        {
            // Glass and its like, over the solid geometry. Unsorted, so overlapping panes blend in
            // draw order; depth writes stay off so they cannot occlude each other.
            GpuState.AlphaBlend(true);
            GpuState.Depth(DepthMode.TestOnly);
            foreach (Mesh mesh in _blendedMeshes)
            {
                if (mesh.Resident && mesh.FineCount + mesh.CoarseCount > 0)
                {
                    DrawTiers(mesh, tier => tier.Blended);
                }
            }
        }
    }

    /// <summary>Re-reads which maps each range has, and rewrites the use flags of the resident ones.</summary>
    private void RefreshRangeTextures()
    {
        foreach (Mesh mesh in _meshes)
        {
            foreach (Tier tier in new[] { mesh.FineTier, mesh.CoarseTier })
            {
                for (int i = 0; i < tier.Ranges.Count; i++)
                {
                    MaterialSurface surface = tier.Ranges[i].Surface;
                    bool changed = Resolve(ref tier.Diffuse[i], surface.DiffuseTexturePath)
                        | Resolve(ref tier.Second[i], surface.SecondDiffusePath)
                        | Resolve(ref tier.Mask[i], surface.MaskPath)
                        | Resolve(ref tier.Blood[i], surface.BloodPath);
                    if (changed && mesh.Resident)
                    {
                        WriteMaterial(tier, i);
                    }
                }
            }
        }

        // True when the map came or went, which is what the range's use flags follow.
        bool Resolve(ref ID3D11ShaderResourceView? slot, string? path)
        {
            ID3D11ShaderResourceView? now =
                path is not null && _textures.TryGetValue(path, out ID3D11ShaderResourceView? texture) ? texture : null;
            bool flipped = (slot is null) != (now is null);
            slot = now;
            return flipped;
        }
    }

    /// <summary>
    /// Every opaque range from the sun's point of view, depth only. Blended ranges are left out:
    /// glass casting a solid shadow reads worse than glass casting none.
    /// </summary>
    public void DrawDepth(Matrix4x4 lightViewProjection, Vector3 cameraPosition)
    {
        using GpuState pass = new();
        _depthProgram.Use();
        _depthProgram.Set(_dViewProjection, lightViewProjection);
        _depthProgram.Set(_dCameraPosition, cameraPosition);

        foreach (Mesh mesh in _meshes)
        {
            if (!mesh.Resident || mesh.FineCount + mesh.CoarseCount == 0)
            {
                continue;
            }

            BindStreams(mesh);
            DepthTier(mesh.FineTier, 0, mesh.FineCount);
            DepthTier(mesh.CoarseTier, mesh.CoarseStart, mesh.CoarseCount);
        }

        void DepthTier(Tier tier, int firstInstance, int instanceCount)
        {
            if (instanceCount == 0)
            {
                return;
            }

            foreach (int i in tier.Opaque)
            {
                Context.PSSetConstantBuffer(2, tier.Materials[i]);
                if (tier.Diffuse[i] is { } diffuse)
                {
                    Context.PSSetShaderResource(0, diffuse);
                }
                DrawRange(_depthProgram, tier.Ranges[i], firstInstance, instanceCount);
            }
        }
    }

    /// <summary>A mesh's streams and its own constants, for all of its ranges.</summary>
    private void BindStreams(Mesh mesh)
    {
        _streams[0] = mesh.Geometry!.Vertex;
        _streams[1] = mesh.InstanceBuffer!;
        Context.IASetVertexBuffers(0, 2, _streams, Strides, Offsets);
        Context.IASetIndexBuffer(mesh.Geometry.Index, mesh.Geometry.IndexFormat, 0);
        Context.VSSetConstantBuffer(3, mesh.Constants);
    }

    private static void DrawRange(ShaderProgram program, MaterialRange range, int firstInstance, int instanceCount)
    {
        program.DrawIndexedInstanced(range.Count, instanceCount, range.Start, firstInstance);
        FrameStats.Drawn((long)range.Count / 3 * instanceCount);
    }

    /// <summary>Null when the bytes are missing or not a decodable .xbt; the range then keeps its
    /// tint for good.</summary>
    private Decoded? DecodeTexture(string path)
    {
        if (_readByPath(path) is not { } xbt)
        {
            return null;
        }

        // Includes the top level from the "_mip0.xbt" companion, which is where half the game's
        // textures keep it. Direct3D wants a compressed texture's top level in whole blocks, so a
        // map smaller than a block takes the RGBA path below.
        if (XbtSurface.TryRead(xbt, _readByPath) is { } surface && surface.Width % 4 == 0 && surface.Height % 4 == 0)
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

    private ID3D11ShaderResourceView UploadTexture(Decoded texture)
    {
        if (texture.FourCc != 0)
        {
            Format format = texture.FourCc switch
            {
                DdsSurface.FourCcDxt3 => Format.BC2_UNorm,
                DdsSurface.FourCcDxt5 => Format.BC3_UNorm,
                _ => Format.BC1_UNorm,
            };
            TextureBytesResident += texture.Mips.Sum(mip => (long)mip.Length);
            return Upload.Immutable(format, texture.Width, texture.Height, [.. texture.Mips], texture.Mips.Count);
        }

        // The whole chain, generated from the one level the decoder gave.
        using ID3D11Texture2D created = Device.CreateTexture2D(new Texture2DDescription(Format.R8G8B8A8_UNorm,
            (uint)texture.Width, (uint)texture.Height, 1, 0, BindFlags.ShaderResource | BindFlags.RenderTarget,
            ResourceUsage.Default, CpuAccessFlags.None, 1, 0, ResourceOptionFlags.GenerateMips));
        Context.UpdateSubresource(texture.Mips[0], created, 0, (uint)texture.Width * 4);
        ID3D11ShaderResourceView view = Device.CreateShaderResourceView(created);
        Context.GenerateMips(view);
        TextureBytesResident += (long)texture.Width * texture.Height * 4 * 4 / 3;
        return view;
    }

    public void Dispose()
    {
        _program.Dispose();
        _depthProgram.Dispose();
        foreach (Mesh mesh in _meshes)
        {
            mesh.InstanceBuffer?.Dispose();
            mesh.Constants?.Dispose();
            ReleaseMaterials(mesh.FineTier);
            ReleaseMaterials(mesh.CoarseTier);
        }
        foreach (Geometry geometry in _buffers.Values)
        {
            geometry.Vertex.Dispose();
            geometry.Index.Dispose();
        }
        foreach (ID3D11ShaderResourceView? texture in _textures.Values)
        {
            texture?.Dispose();
        }
    }
}
