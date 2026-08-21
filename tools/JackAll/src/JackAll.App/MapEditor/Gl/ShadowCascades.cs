using OpenTK.Graphics.OpenGL4;
using OpenTK.Mathematics;

namespace JackAll.App.MapEditor.Gl;

/// <summary>
/// The sun's depth of the scene, in four slices of the view frustum. One depth texture array, one
/// orthographic fit per slice, snapped to its own texel grid so the shadow edges do not crawl as
/// the camera pans.
/// </summary>
public sealed class ShadowCascades : IDisposable
{
    public const int Count = 4;

    /// <summary>How far shadows reach. Past this the scene is haze, and the models and scatter that
    /// cast them only exist within four sectors of the camera anyway.</summary>
    private const float Distance = 420f;

    internal const int Size = 2048;

    /// <summary>How far behind a slice the near plane is pulled, so a hill or a tower standing
    /// outside the slice still casts into it.</summary>
    private const float CasterDepth = 400f;

    private readonly int _framebuffer;

    public int Handle { get; }

    /// <summary>World to light clip, per cascade.</summary>
    public Matrix4[] Matrices { get; } = new Matrix4[Count];

    /// <summary>
    /// Where each cascade starts and stops, from the near plane out to <see cref="Distance"/>.
    /// Between a logarithmic and a uniform split: logarithmic alone spends most of the resolution on
    /// the first few metres, uniform alone starves them. Only the constants above feed it, so it is
    /// the same five numbers for the life of the process.
    /// </summary>
    private static readonly float[] SplitDistances = BuildSplits();

    /// <summary>The far edge of each cascade, for the lookup to pick one.</summary>
    public static Vector4 Splits { get; } =
        new(SplitDistances[1], SplitDistances[2], SplitDistances[3], SplitDistances[4]);

    private static float[] BuildSplits()
    {
        const float near = 1f;
        var splits = new float[Count + 1];
        splits[0] = near;
        for (int i = 1; i <= Count; i++)
        {
            float p = i / (float)Count;
            splits[i] = 0.6f * (near * MathF.Pow(Distance / near, p))
                      + 0.4f * (near + (Distance - near) * p);
        }
        return splits;
    }

    public ShadowCascades()
    {
        Handle = GL.GenTexture();
        GL.BindTexture(TextureTarget.Texture2DArray, Handle);
        GL.TexImage3D(TextureTarget.Texture2DArray, 0, PixelInternalFormat.DepthComponent32f,
            Size, Size, Count, 0, PixelFormat.DepthComponent, PixelType.Float, IntPtr.Zero);
        GL.TexParameter(TextureTarget.Texture2DArray, TextureParameterName.TextureMinFilter,
            (int)TextureMinFilter.Linear);
        GL.TexParameter(TextureTarget.Texture2DArray, TextureParameterName.TextureMagFilter,
            (int)TextureMagFilter.Linear);
        GL.TexParameter(TextureTarget.Texture2DArray, TextureParameterName.TextureWrapS,
            (int)TextureWrapMode.ClampToBorder);
        GL.TexParameter(TextureTarget.Texture2DArray, TextureParameterName.TextureWrapT,
            (int)TextureWrapMode.ClampToBorder);
        GL.TexParameter(TextureTarget.Texture2DArray, TextureParameterName.TextureBorderColor,
            new[] { 1f, 1f, 1f, 1f });

        // Compared rather than sampled, which is what makes the hardware do the first 2x2 of the
        // filtering for free.
        GL.TexParameter(TextureTarget.Texture2DArray, TextureParameterName.TextureCompareMode,
            (int)TextureCompareMode.CompareRefToTexture);
        GL.TexParameter(TextureTarget.Texture2DArray, TextureParameterName.TextureCompareFunc,
            (int)DepthFunction.Lequal);

        _framebuffer = GL.GenFramebuffer();
        GL.BindFramebuffer(FramebufferTarget.Framebuffer, _framebuffer);
        GL.DrawBuffer(DrawBufferMode.None);
        GL.ReadBuffer(ReadBufferMode.None);
        GL.BindFramebuffer(FramebufferTarget.Framebuffer, 0);
    }

    /// <summary>Refits every cascade to the camera's frustum for this frame.</summary>
    public void Fit(Camera3D camera, float aspect)
    {
        Vector3 forward = camera.Forward;
        Vector3 right = camera.Right;
        Vector3 up = Vector3.Cross(right, forward);
        float tanV = MathF.Tan(Camera3D.VerticalFovRadians * 0.5f);
        float tanH = tanV * aspect;

        // Light travels along -SunDirection; the basis is built off that, not off the world axes.
        Vector3 lightDir = -SceneLighting.SunDirection;
        Vector3 lightUp = MathF.Abs(lightDir.Z) > 0.99f ? Vector3.UnitY : Vector3.UnitZ;
        Vector3 lightRight = Vector3.Normalize(Vector3.Cross(lightUp, lightDir));
        lightUp = Vector3.Cross(lightDir, lightRight);

        for (int i = 0; i < Count; i++)
        {
            (Vector3 centre, float radius) = SliceBounds(
                camera.Position, forward, right, up, tanV, tanH, SplitDistances[i], SplitDistances[i + 1]);

            // Snapped to whole texels along the light's own axes, so panning slides the map by
            // whole texels instead of resampling the scene every frame.
            float texel = radius * 2f / Size;
            centre = lightRight * (MathF.Round(Vector3.Dot(centre, lightRight) / texel) * texel)
                   + lightUp * (MathF.Round(Vector3.Dot(centre, lightUp) / texel) * texel)
                   + lightDir * Vector3.Dot(centre, lightDir);

            Matrix4 view = Matrix4.LookAt(centre - lightDir * (radius + CasterDepth), centre, lightUp);
            Matrix4 projection = Matrix4.CreateOrthographicOffCenter(
                -radius, radius, -radius, radius, 0f, radius * 2f + CasterDepth);
            Matrices[i] = view * projection;
        }
    }

    /// <summary>The bounding sphere of one frustum slice. A sphere rather than a box because it is
    /// the same size whichever way the camera turns, which is half of what keeps the map stable.
    /// </summary>
    private static (Vector3 Centre, float Radius) SliceBounds(
        Vector3 eye, Vector3 forward, Vector3 right, Vector3 up,
        float tanV, float tanH, float near, float far)
    {
        Span<Vector3> corners = stackalloc Vector3[8];
        int at = 0;
        foreach (float distance in stackalloc[] { near, far })
        {
            Vector3 middle = eye + forward * distance;
            Vector3 h = up * (tanV * distance);
            Vector3 w = right * (tanH * distance);
            corners[at++] = middle - w - h;
            corners[at++] = middle + w - h;
            corners[at++] = middle - w + h;
            corners[at++] = middle + w + h;
        }

        var centre = Vector3.Zero;
        foreach (Vector3 corner in corners)
        {
            centre += corner;
        }
        centre /= 8f;

        float radius = 0f;
        foreach (Vector3 corner in corners)
        {
            radius = MathF.Max(radius, (corner - centre).Length);
        }
        return (centre, radius);
    }

    /// <summary>Binds one cascade's layer as the depth target and clears it.</summary>
    public void BeginCascade(int cascade)
    {
        GL.BindFramebuffer(FramebufferTarget.Framebuffer, _framebuffer);
        GL.FramebufferTextureLayer(
            FramebufferTarget.Framebuffer, FramebufferAttachment.DepthAttachment, Handle, 0, cascade);
        GL.Viewport(0, 0, Size, Size);
        GL.Clear(ClearBufferMask.DepthBufferBit);
    }

    public void Bind(TextureUnit unit)
    {
        GL.ActiveTexture(unit);
        GL.BindTexture(TextureTarget.Texture2DArray, Handle);
    }

    public void Dispose()
    {
        GL.DeleteFramebuffer(_framebuffer);
        GL.DeleteTexture(Handle);
    }
}

/// <summary>
/// One program's shadow uniforms, resolved once at link and uploaded per draw. A program that
/// pastes <see cref="SceneLighting.ShadowGlsl"/> needs one of these, or its cascade matrices stay
/// at zero and every surface reads as shadowed.
/// </summary>
public sealed class ShadowBinding
{
    private readonly int _map;
    private readonly int[] _matrices = new int[ShadowCascades.Count];
    private readonly int _splits;
    private readonly int _strength;

    public ShadowBinding(ShaderProgram program)
    {
        _map = program.UniformLocation("shadowMap");
        for (int i = 0; i < _matrices.Length; i++)
        {
            _matrices[i] = program.UniformLocation($"shadowMatrices[{i}]");
        }
        _splits = program.UniformLocation("shadowSplits");
        _strength = program.UniformLocation("shadowStrength");
    }

    /// <summary>Uploads the cascades to whichever program is currently in use. A null set switches
    /// the lookup off rather than leaving last frame's matrices behind it.</summary>
    public void Apply()
    {
        if (SceneLighting.Shadows is not { } cascades)
        {
            GL.Uniform1(_strength, 0f);
            return;
        }

        cascades.Bind(TextureUnit.Texture0 + TextureUnits.ShadowMap);
        GL.Uniform1(_map, TextureUnits.ShadowMap);
        GL.Uniform1(_strength, 1f);
        Vector4 splits = ShadowCascades.Splits;
        GL.Uniform4(_splits, splits);

        for (int i = 0; i < _matrices.Length; i++)
        {
            Matrix4 matrix = cascades.Matrices[i];
            GL.UniformMatrix4(_matrices[i], false, ref matrix);
        }

        // Back to where every other layer expects to find it; the shadow map lives on its own
        // unit precisely so nothing has to think about this, but the active one is global.
        GL.ActiveTexture(TextureUnit.Texture0);
    }
}
