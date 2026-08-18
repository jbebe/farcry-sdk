using System.Collections.Concurrent;
using JackAll.App.FileHandlers.Xbt;
using JackAll.Tools.World;
using JackAll.Tools.Xbt;
using OpenTK.Graphics.OpenGL4;
using OpenTK.Mathematics;

namespace JackAll.App.MapEditor.Gl;

/// <summary>
/// Draws every mesh-resolved entity as its real .xbg geometry: one VAO per unique mesh, one
/// instanced draw per populated detail tier. The fine tier draws textured per material range, with
/// diffuse textures decoded on background threads and uploaded on a per-frame budget; the coarse
/// tier stays flat-tinted in the entity's marker colour.
/// </summary>
public sealed class EntityModelLayer : IDisposable
{
    /// <summary>Floats per instance: x y z yawRadians r g b.</summary>
    private const int InstanceStride = 7;

    private const int VertexStrideBytes = WorldModel.FloatsPerVertex * sizeof(float);
    private const int InstanceStrideBytes = InstanceStride * sizeof(float);
    private const int MaxDecoders = 2;
    private const long UploadBudgetPerFrame = 8 << 20;

    private sealed class Mesh
    {
        public int Vao, VertexBuffer, IndexBuffer, InstanceBuffer;
        public IndexRange Fine, Coarse;
        public required IReadOnlyList<MaterialRange> Ranges;
        public required float[] Staging;
        public required int[] RangeHandles;
        public int FineCount, CoarseCount;
        public int FineFloats, CoarseFloats;
        public int CoarseStart;
        /// <summary>Instance index the attrib pointers target; -1 forces the first bind.</summary>
        public int PointedAt = -1;
    }

    /// <summary>What an entity contributes besides its live position and yaw.</summary>
    private readonly record struct Row(int Model, float R, float G, float B);

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

    /// <summary>What the streamed diffuse textures currently hold on the GPU.</summary>
    public long TextureBytesResident { get; private set; }

    public EntityModelLayer(
        WorldModelSet set, Func<string, byte[]?> readByPath,
        Func<WorldEntity, (byte R, byte G, byte B)> colourOf)
    {
        _readByPath = readByPath;

        // The staging and instance buffers are sized once for every entity that can ever map to
        // the mesh; visibility and ring changes only shrink the live counts.
        var capacity = new int[set.Models.Count];
        foreach (int index in set.ModelIndexByEntity.Values)
        {
            capacity[index]++;
        }

        _rows = new Dictionary<WorldEntity, Row>(set.ModelIndexByEntity.Count);
        var colourByArchetype = new Dictionary<string, (float R, float G, float B)>(StringComparer.OrdinalIgnoreCase);
        foreach ((WorldEntity entity, int model) in set.ModelIndexByEntity)
        {
            if (!colourByArchetype.TryGetValue(entity.ArchetypeName, out (float R, float G, float B) colour))
            {
                (byte r, byte g, byte b) = colourOf(entity);
                colour = (r / 255f, g / 255f, b / 255f);
                colourByArchetype[entity.ArchetypeName] = colour;
            }
            _rows[entity] = new Row(model, colour.R, colour.G, colour.B);
        }

        _meshes = new Mesh[set.Models.Count];
        for (int i = 0; i < set.Models.Count; i++)
        {
            WorldModel model = set.Models[i];
            var mesh = new Mesh
            {
                Fine = model.Fine,
                Coarse = model.Coarse,
                Ranges = model.MaterialRanges,
                Staging = new float[capacity[i] * InstanceStride],
                RangeHandles = new int[model.MaterialRanges.Count],
            };

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
            GL.EnableVertexAttribArray(3);
            GL.EnableVertexAttribArray(4);
            PointInstanceAttribs(mesh, 0);
            GL.VertexAttribDivisor(3, 1);
            GL.VertexAttribDivisor(4, 1);
            GL.BindBuffer(BufferTarget.ElementArrayBuffer, mesh.IndexBuffer);
            GL.BufferData(BufferTarget.ElementArrayBuffer, model.Indices.Length * sizeof(int),
                model.Indices, BufferUsageHint.StaticDraw);

            _meshes[i] = mesh;
        }
        GL.BindVertexArray(0);

        _program = new ShaderProgram(
            """
            #version 330 core
            layout(location = 0) in vec3 position;
            layout(location = 1) in vec3 normal;
            layout(location = 2) in vec2 uv;
            layout(location = 3) in vec4 posYaw;
            layout(location = 4) in vec3 tint;
            uniform mat4 viewProjection;
            out vec3 worldNormal;
            out vec3 worldPosition;
            out vec3 baseColour;
            out vec2 texUv;
            void main()
            {
                float s = sin(posYaw.w), c = cos(posYaw.w);
                vec3 rotated = vec3(position.x * c - position.y * s,
                                    position.x * s + position.y * c,
                                    position.z);
                worldPosition = rotated + posYaw.xyz;
                // Pure rotation, so the normal rotates the same way - no inverse-transpose needed.
                worldNormal = vec3(normal.x * c - normal.y * s, normal.x * s + normal.y * c, normal.z);
                baseColour = tint;
                texUv = uv;
                gl_Position = viewProjection * vec4(worldPosition, 1.0);
            }
            """,
            $$"""
            #version 330 core
            in vec3 worldNormal;
            in vec3 worldPosition;
            in vec3 baseColour;
            in vec2 texUv;
            uniform vec3 cameraPosition;
            uniform vec3 sunDirection;
            uniform float haze;
            uniform float useTexture;
            uniform sampler2D diffuse;
            out vec4 fragment;
            {{SceneLighting.SkyGlsl}}
            void main()
            {
                vec3 albedo = baseColour;
                if (useTexture > 0.5)
                {
                    vec4 sample = texture(diffuse, texUv);
                    // Vegetation and fences are alpha cutouts.
                    if (sample.a < 0.5) { discard; }
                    albedo = sample.rgb;
                }
                vec3 n = normalize(worldNormal);
                // Many parts are single-sided shells; light whichever side faces the camera.
                if (!gl_FrontFacing) { n = -n; }
                float light = max(dot(n, sunDirection), 0.0);
                vec3 lit = albedo * (0.35 + 0.65 * light);
                fragment = vec4(
                    applyHaze(lit, distance(cameraPosition, worldPosition), worldPosition.z, haze), 1.0);
            }
            """);
        _uViewProjection = _program.UniformLocation("viewProjection");
        _uCameraPosition = _program.UniformLocation("cameraPosition");
        _uSunDirection = _program.UniformLocation("sunDirection");
        _uHaze = _program.UniformLocation("haze");
        _uUseTexture = _program.UniformLocation("useTexture");
        _program.Use();
        GL.Uniform1(_program.UniformLocation("diffuse"), 0);
    }

    /// <summary>
    /// Re-buckets the visible entities into per-mesh fine/coarse instance streams around the
    /// camera's sector and uploads them, returning the entities left for billboard markers: the
    /// mesh-less plus everything beyond the coarse ring. Fine instances pack forward from the
    /// front of each mesh's staging array, coarse ones backward from its end, so one pass fills
    /// both without counting first.
    /// </summary>
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

            Mesh mesh = _meshes[row.Model];
            int at;
            if (distance <= WorldModels.FineRadius)
            {
                at = mesh.FineFloats;
                mesh.FineFloats += InstanceStride;
                mesh.FineCount++;
            }
            else
            {
                mesh.CoarseFloats -= InstanceStride;
                at = mesh.CoarseFloats;
                mesh.CoarseCount++;
            }

            float[] staging = mesh.Staging;
            staging[at] = position.X;
            staging[at + 1] = position.Y;
            staging[at + 2] = position.Z;
            staging[at + 3] = entity.Angles.Z * MathF.PI / 180f;
            staging[at + 4] = row.R;
            staging[at + 5] = row.G;
            staging[at + 6] = row.B;
        }

        foreach (Mesh mesh in _meshes)
        {
            if (mesh.FineCount + mesh.CoarseCount == 0)
            {
                continue;
            }

            mesh.CoarseStart = mesh.CoarseFloats / InstanceStride;
            GL.BindBuffer(BufferTarget.ArrayBuffer, mesh.InstanceBuffer);
            if (mesh.FineCount > 0)
            {
                GL.BufferSubData(BufferTarget.ArrayBuffer, IntPtr.Zero,
                    mesh.FineCount * InstanceStrideBytes, mesh.Staging);
                RequestTextures(mesh);
            }
            if (mesh.CoarseCount > 0)
            {
                GL.BufferSubData(BufferTarget.ArrayBuffer, (IntPtr)(mesh.CoarseFloats * sizeof(float)),
                    mesh.CoarseCount * InstanceStrideBytes, ref mesh.Staging[mesh.CoarseFloats]);
            }
        }

        return leftover;
    }

    private void RequestTextures(Mesh mesh)
    {
        foreach (MaterialRange range in mesh.Ranges)
        {
            if (range.DiffuseTexturePath is not { } path || !_textures.TryAdd(path, -1))
            {
                continue;
            }

            _requests.Enqueue(path);
        }
        StartDecoders();
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
            _decoded.Enqueue((path, DecodeTexture(_readByPath(path))));
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
        GL.Uniform3(_uSunDirection, SceneLighting.SunDirection);
        GL.Uniform1(_uHaze, haze);
        GL.Enable(EnableCap.DepthTest);
        GL.ActiveTexture(TextureUnit.Texture0);

        float lastUseTexture = -1f;
        void SetUseTexture(float value)
        {
            if (lastUseTexture != value)
            {
                lastUseTexture = value;
                GL.Uniform1(_uUseTexture, value);
            }
        }

        foreach (Mesh mesh in _meshes)
        {
            if (mesh.FineCount + mesh.CoarseCount == 0)
            {
                continue;
            }

            GL.BindVertexArray(mesh.Vao);
            if (mesh.FineCount > 0)
            {
                PointInstanceAttribs(mesh, 0);
                if (mesh.Ranges.Count == 0)
                {
                    SetUseTexture(0f);
                    DrawRange(mesh.Fine, mesh.FineCount);
                }
                for (int i = 0; i < mesh.Ranges.Count; i++)
                {
                    int handle = mesh.RangeHandles[i];
                    SetUseTexture(handle > 0 ? 1f : 0f);
                    if (handle > 0)
                    {
                        GL.BindTexture(TextureTarget.Texture2D, handle);
                    }
                    DrawRange(new IndexRange(mesh.Ranges[i].Start, mesh.Ranges[i].Count), mesh.FineCount);
                }
            }
            if (mesh.CoarseCount > 0)
            {
                PointInstanceAttribs(mesh, mesh.CoarseStart);
                SetUseTexture(0f);
                DrawRange(mesh.Coarse, mesh.CoarseCount);
            }
        }
        GL.BindVertexArray(0);
    }

    private void RefreshRangeHandles()
    {
        foreach (Mesh mesh in _meshes)
        {
            for (int i = 0; i < mesh.Ranges.Count; i++)
            {
                mesh.RangeHandles[i] = mesh.Ranges[i].DiffuseTexturePath is { } path
                    && _textures.TryGetValue(path, out int handle) && handle > 0
                    ? handle
                    : 0;
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
        GL.VertexAttribPointer(3, 4, VertexAttribPointerType.Float, false, InstanceStrideBytes, baseOffset);
        GL.VertexAttribPointer(4, 3, VertexAttribPointerType.Float, false, InstanceStrideBytes, baseOffset + 16);
    }

    private static void DrawRange(IndexRange range, int instanceCount)
        => GL.DrawElementsInstanced(PrimitiveType.Triangles, range.Count, DrawElementsType.UnsignedInt,
            (IntPtr)(range.Start * sizeof(int)), instanceCount);

    /// <summary>Null when the bytes are missing or not a decodable .xbt; the range then keeps its
    /// tint for good.</summary>
    private static Decoded? DecodeTexture(byte[]? xbt)
    {
        if (xbt is null)
        {
            return null;
        }

        byte[] dds;
        try
        {
            (_, dds) = XbtTexture.Split(xbt);
        }
        catch (Exception)
        {
            return null;
        }

        if (DdsSurface.TryParse(dds) is { } surface)
        {
            return new Decoded(surface.Width, surface.Height, surface.FourCc, surface.Mips);
        }

        // Not a plain DXT payload - let the BCn decoder produce RGBA instead.
        return XbtImage.TryDecodeRgbaDds(dds) is { } rgba
            ? new Decoded(rgba.Width, rgba.Height, 0, [rgba.Rgba])
            : null;
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
        return handle;
    }

    public void Dispose()
    {
        _program.Dispose();
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
