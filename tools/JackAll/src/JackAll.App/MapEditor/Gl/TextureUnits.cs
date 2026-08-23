using OpenTK.Graphics.OpenGL4;

namespace JackAll.App.MapEditor.Gl;

/// <summary>
/// The texture units the shared GLSL blocks bind on. Which sampler sits on which unit is something
/// every program has to agree about, and it was being tracked by comments in one file reasoning
/// about the bindings in another - the terrain claims 0-8 and the model shader 0-2, so anything
/// shared has to start above both.
/// </summary>
public static class TextureUnits
{
    /// <summary>First unit no layer binds for itself.</summary>
    private const int Shared = 9;

    /// <summary>The cascade array, for <see cref="SceneLighting.ShadowGlsl"/>.</summary>
    public const int ShadowMap = Shared;

    /// <summary>The occlusion buffer, for the lookup in <see cref="SceneLighting.SurfaceGlsl"/>.</summary>
    public const int Occlusion = Shared + 1;

    private static int _shadowStandIn;
    private static int _occlusionStandIn;

    /// <summary>
    /// Puts a 1x1 stand-in on both shared units, for the frames that bind no real one. A sampler
    /// reading an empty unit is an incomplete texture, and a driver answers that with a message per
    /// draw call - which, with debug output synchronous and thousands of draws a frame, is the
    /// whole frame.
    /// </summary>
    public static void BindStandIns()
    {
        if (_shadowStandIn == 0)
        {
            Create();
        }

        GL.ActiveTexture(TextureUnit.Texture0 + ShadowMap);
        GL.BindTexture(TextureTarget.Texture2DArray, _shadowStandIn);
        GL.ActiveTexture(TextureUnit.Texture0 + Occlusion);
        GL.BindTexture(TextureTarget.Texture2D, _occlusionStandIn);
        GL.ActiveTexture(TextureUnit.Texture0);
    }

    /// <summary>Both carry the value that reads as "nothing here": full depth is a fragment in
    /// front of every caster, and full white is a fragment nothing occludes.</summary>
    private static void Create()
    {
        _shadowStandIn = GL.GenTexture();
        GL.BindTexture(TextureTarget.Texture2DArray, _shadowStandIn);
        GL.TexImage3D(TextureTarget.Texture2DArray, 0, PixelInternalFormat.DepthComponent32f,
            1, 1, 1, 0, PixelFormat.DepthComponent, PixelType.Float, new[] { 1f });
        Nearest(TextureTarget.Texture2DArray);

        // Compared rather than sampled, to match the sampler2DArrayShadow that reads it - a shadow
        // sampler over a texture with comparison off is the same undefined state as an empty unit.
        GL.TexParameter(TextureTarget.Texture2DArray, TextureParameterName.TextureCompareMode,
            (int)TextureCompareMode.CompareRefToTexture);
        GL.TexParameter(TextureTarget.Texture2DArray, TextureParameterName.TextureCompareFunc,
            (int)DepthFunction.Lequal);

        _occlusionStandIn = GL.GenTexture();
        GL.BindTexture(TextureTarget.Texture2D, _occlusionStandIn);
        GL.TexImage2D(TextureTarget.Texture2D, 0, PixelInternalFormat.R8, 1, 1, 0,
            PixelFormat.Red, PixelType.UnsignedByte, new byte[] { 255 });
        Nearest(TextureTarget.Texture2D);

        static void Nearest(TextureTarget target)
        {
            GL.TexParameter(target, TextureParameterName.TextureMinFilter,
                (int)TextureMinFilter.Nearest);
            GL.TexParameter(target, TextureParameterName.TextureMagFilter,
                (int)TextureMagFilter.Nearest);
        }
    }
}
