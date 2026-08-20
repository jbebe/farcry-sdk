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
    /// <summary>Floats per instance: x y z, then the three Euler angles in radians, then r g b.</summary>
    private const int InstanceStride = 9;

    private const int VertexStrideBytes = WorldModel.FloatsPerVertex * sizeof(float);
    private const int InstanceStrideBytes = InstanceStride * sizeof(float);
    private const int MaxDecoders = 2;
    private const long UploadBudgetPerFrame = 8 << 20;

    private sealed class Mesh
    {
        public int Vao, VertexBuffer, IndexBuffer, InstanceBuffer;
        public IndexRange Fine, Coarse;
        public required IReadOnlyList<MaterialRange> Ranges;
        /// <summary>Indices into <see cref="Ranges"/> per pass, so neither pass walks the other's.
        /// </summary>
        public required int[] OpaqueRanges;
        public required int[] BlendedRanges;
        public required float[] Staging;
        public required int[] RangeHandles;
        public int FineCount, CoarseCount;
        public int FineFloats, CoarseFloats;
        public int CoarseStart;
        /// <summary>Instance index the attrib pointers target; -1 forces the first bind.</summary>
        public int PointedAt = -1;
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
    /// <summary>Just the meshes carrying a blended range, so the second pass skips the rest.</summary>
    private readonly Mesh[] _blendedMeshes;

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
        foreach (int[] indices in set.ModelIndicesByEntity.Values)
        {
            foreach (int index in indices)
            {
                capacity[index]++;
            }
        }

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

        _meshes = new Mesh[set.Models.Count];
        var blended = new List<Mesh>();
        for (int i = 0; i < set.Models.Count; i++)
        {
            WorldModel model = set.Models[i];
            IReadOnlyList<MaterialRange> ranges = model.MaterialRanges;
            var mesh = new Mesh
            {
                Fine = model.Fine,
                Coarse = model.Coarse,
                Ranges = ranges,
                OpaqueRanges = [.. Enumerable.Range(0, ranges.Count).Where(r => ranges[r].Alpha != MaterialAlpha.Blend)],
                BlendedRanges = [.. Enumerable.Range(0, ranges.Count).Where(r => ranges[r].Alpha == MaterialAlpha.Blend)],
                Staging = new float[capacity[i] * InstanceStride],
                RangeHandles = new int[ranges.Count],
            };
            if (mesh.BlendedRanges.Length > 0)
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
            GL.VertexAttribPointer(6, 1, VertexAttribPointerType.Float, false, VertexStrideBytes, 32);
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

        _program = new ShaderProgram(
            """
            #version 330 core
            layout(location = 0) in vec3 position;
            layout(location = 1) in vec3 normal;
            layout(location = 2) in vec2 uv;
            layout(location = 3) in vec3 instancePosition;
            layout(location = 4) in vec3 instanceAngles;
            layout(location = 5) in vec3 tint;
            layout(location = 6) in float vertexMask;
            uniform mat4 viewProjection;
            out vec3 worldNormal;
            out vec3 worldPosition;
            out vec3 baseColour;
            out vec2 texUv;
            out float maskBlue;
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
            void main()
            {
                mat3 rotation = spin(instanceAngles);
                worldPosition = rotation * position + instancePosition;
                // Pure rotation, so the normal rotates the same way - no inverse-transpose needed.
                worldNormal = rotation * normal;
                baseColour = tint;
                texUv = uv;
                maskBlue = vertexMask;
                gl_Position = viewProjection * vec4(worldPosition, 1.0);
            }
            """,
            $$"""
            #version 330 core
            in vec3 worldNormal;
            in vec3 worldPosition;
            in vec3 baseColour;
            in vec2 texUv;
            in float maskBlue;
            uniform vec3 cameraPosition;
            uniform vec3 sunDirection;
            uniform float haze;
            uniform float useTexture;
            uniform int alphaMode;
            uniform vec3 tintBase;
            uniform vec3 tintColour;
            uniform sampler2D diffuse;
            out vec4 fragment;
            {{SceneLighting.SkyGlsl}}
            void main()
            {
                vec3 albedo = baseColour;
                float coverage = 1.0;
                if (useTexture > 0.5)
                {
                    vec4 texel = texture(diffuse, texUv);
                    // Only materials that asked for it read alpha as coverage; on the rest it is a
                    // gloss or spec mask and would erase the surface.
                    if (alphaMode == 1 && texel.a < 0.5) { discard; }
                    if (alphaMode == 2) { coverage = texel.a; }
                    // The engine's diffuse: the map is a base and the colour comes from the two
                    // material tints, blended by the vertex mask's blue channel.
                    albedo = texel.rgb * mix(tintBase, tintColour, clamp(maskBlue, 0.0, 1.0));
                }
                vec3 n = normalize(worldNormal);
                // Many parts are single-sided shells, so light whichever side is showing. The test
                // is against the eye, not gl_FrontFacing: the meshes are wound clockwise the way
                // D3D wants, which GL reads as back-facing on every outward triangle, and taking
                // that at its word flips every normal into the surface and kills the sun term.
                if (dot(n, cameraPosition - worldPosition) < 0.0) { n = -n; }
                float light = max(dot(n, sunDirection), 0.0);
                vec3 lit = albedo * (0.35 + 0.65 * light);
                fragment = vec4(
                    applyHaze(lit, distance(cameraPosition, worldPosition), worldPosition.z, haze), coverage);
            }
            """);
        _uViewProjection = _program.UniformLocation("viewProjection");
        _uCameraPosition = _program.UniformLocation("cameraPosition");
        _uSunDirection = _program.UniformLocation("sunDirection");
        _uHaze = _program.UniformLocation("haze");
        _uUseTexture = _program.UniformLocation("useTexture");
        _uAlphaMode = _program.UniformLocation("alphaMode");
        _uTintBase = _program.UniformLocation("tintBase");
        _uTintColour = _program.UniformLocation("tintColour");
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

            bool fine = distance <= WorldModels.FineRadius;
            System.Numerics.Vector3 angles = entity.Angles * (MathF.PI / 180f);
            foreach (int model in row.Models)
            {
                Mesh mesh = _meshes[model];
                int at;
                if (fine)
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
                staging[at + 3] = angles.X;
                staging[at + 4] = angles.Y;
                staging[at + 5] = angles.Z;
                staging[at + 6] = row.R;
                staging[at + 7] = row.G;
                staging[at + 8] = row.B;
            }
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
        int lastAlphaMode = -1;
        MaterialSurface lastSurface = default;
        void SetSurface(int handle, MaterialSurface surface)
        {
            float useTexture = handle > 0 ? 1f : 0f;
            if (lastUseTexture != useTexture)
            {
                GL.Uniform1(_uUseTexture, lastUseTexture = useTexture);
            }
            if (lastAlphaMode != (int)surface.Alpha)
            {
                GL.Uniform1(_uAlphaMode, lastAlphaMode = (int)surface.Alpha);
            }
            if (lastSurface.TintBase != surface.TintBase || lastSurface.Tint != surface.Tint)
            {
                GL.Uniform3(_uTintBase, surface.TintBase.X, surface.TintBase.Y, surface.TintBase.Z);
                GL.Uniform3(_uTintColour, surface.Tint.X, surface.Tint.Y, surface.Tint.Z);
            }
            lastSurface = surface;
        }

        void DrawRanges(Mesh mesh, int[] which)
        {
            PointInstanceAttribs(mesh, 0);
            foreach (int i in which)
            {
                MaterialRange range = mesh.Ranges[i];
                int handle = mesh.RangeHandles[i];
                SetSurface(handle, range.Surface);
                if (handle > 0)
                {
                    GL.BindTexture(TextureTarget.Texture2D, handle);
                }
                DrawRange(new IndexRange(range.Start, range.Count), mesh.FineCount);
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
                DrawRanges(mesh, mesh.OpaqueRanges);
            }
            if (mesh.CoarseCount > 0)
            {
                PointInstanceAttribs(mesh, mesh.CoarseStart);
                SetSurface(0, MaterialSurface.None);
                DrawRange(mesh.Coarse, mesh.CoarseCount);
            }
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
                if (mesh.FineCount > 0)
                {
                    GL.BindVertexArray(mesh.Vao);
                    DrawRanges(mesh, mesh.BlendedRanges);
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
        GL.VertexAttribPointer(3, 3, VertexAttribPointerType.Float, false, InstanceStrideBytes, baseOffset);
        GL.VertexAttribPointer(4, 3, VertexAttribPointerType.Float, false, InstanceStrideBytes, baseOffset + 12);
        GL.VertexAttribPointer(5, 3, VertexAttribPointerType.Float, false, InstanceStrideBytes, baseOffset + 24);
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
