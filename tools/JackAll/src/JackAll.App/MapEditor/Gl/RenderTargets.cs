using OpenTK.Graphics.OpenGL4;

namespace JackAll.App.MapEditor.Gl;

/// <summary>
/// The offscreen surfaces the frame is built on: a float colour buffer for the scene, a depth
/// texture the passes after it can sample, and an 8-bit buffer the tonemapped result and the
/// overlays share before one blit reaches the control.
/// </summary>
/// <remarks>
/// The present buffer exists because the two framebuffers have to agree on depth and cannot. The
/// control's own depth is a <c>DEPTH24_STENCIL8</c> renderbuffer, so blitting a 32-bit float depth
/// into it is an error, and a fullscreen pass cannot write depth with the depth test off whatever it
/// assigns to <c>gl_FragDepth</c>. Attaching the one depth texture to both framebuffers sidesteps
/// both: the overlays test against the depth the scene already wrote, and the final blit is
/// colour-only, which is legal between any two formats.
/// </remarks>
public sealed class RenderTargets : IDisposable
{
    public int Width { get; private set; }

    public int Height { get; private set; }

    public int SceneFramebuffer { get; private set; }

    /// <summary>Linear radiance, unclamped - the sun glint and the sky run well above 1.</summary>
    public int Colour { get; private set; }

    public int Depth { get; private set; }

    public int PresentFramebuffer { get; private set; }

    private int _presentColour;

    /// <summary>The opaque scene as it stood before the water went in, so the water can refract
    /// what is behind it. A pass cannot sample the target it is drawing into.</summary>
    public int ColourCopy { get; private set; }

    /// <summary>The depth beside it. The water needs the scene depth to know how much water the
    /// view crosses, but it is depth-testing against the live buffer at the same time, and sampling
    /// the image a draw is bound to is a feedback loop with no defined result.</summary>
    public int DepthCopy { get; private set; }

    public int ColourCopyFramebuffer { get; private set; }

    /// <summary>Ambient occlusion and the buffer its separable blur ping-pongs through.</summary>
    public int Occlusion { get; private set; }

    public int OcclusionBlur { get; private set; }

    public int OcclusionFramebuffer { get; private set; }

    public int OcclusionBlurFramebuffer { get; private set; }

    /// <summary>Rebuilds every surface when the viewport size changes, and does nothing when it has
    /// not. Sized in device pixels, never in WPF units - a screen-space effect measured against the
    /// latter is wrong on any display that is not at 100%.</summary>
    public void Resize(int width, int height)
    {
        if (width == Width && height == Height && SceneFramebuffer != 0)
        {
            return;
        }

        Release();
        Width = Math.Max(width, 1);
        Height = Math.Max(height, 1);

        Colour = NewTexture(PixelInternalFormat.Rgba16f, PixelFormat.Rgba, PixelType.Float);
        _presentColour = NewTexture(PixelInternalFormat.Rgba8, PixelFormat.Rgba, PixelType.UnsignedByte);
        Depth = NewTexture(
            PixelInternalFormat.DepthComponent32f, PixelFormat.DepthComponent, PixelType.Float,
            TextureMinFilter.Nearest);

        SceneFramebuffer = NewFramebuffer(Colour, Depth);
        PresentFramebuffer = NewFramebuffer(_presentColour, Depth);

        ColourCopy = NewTexture(PixelInternalFormat.Rgba16f, PixelFormat.Rgba, PixelType.Float);
        DepthCopy = NewTexture(
            PixelInternalFormat.DepthComponent32f, PixelFormat.DepthComponent, PixelType.Float,
            TextureMinFilter.Nearest);
        ColourCopyFramebuffer = NewFramebuffer(ColourCopy, DepthCopy);

        Occlusion = NewTexture(PixelInternalFormat.R8, PixelFormat.Red, PixelType.UnsignedByte);
        OcclusionBlur = NewTexture(PixelInternalFormat.R8, PixelFormat.Red, PixelType.UnsignedByte);
        OcclusionFramebuffer = NewFramebuffer(Occlusion, depthTexture: 0);
        OcclusionBlurFramebuffer = NewFramebuffer(OcclusionBlur, depthTexture: 0);
    }

    private int NewTexture(PixelInternalFormat internalFormat, PixelFormat format, PixelType type,
        TextureMinFilter filter = TextureMinFilter.Linear)
    {
        int handle = GL.GenTexture();
        GL.BindTexture(TextureTarget.Texture2D, handle);
        GL.TexImage2D(TextureTarget.Texture2D, 0, internalFormat, Width, Height, 0, format, type,
            IntPtr.Zero);
        GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter,
            (int)filter);
        GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter,
            (int)(filter == TextureMinFilter.Nearest ? TextureMagFilter.Nearest : TextureMagFilter.Linear));
        GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapS,
            (int)TextureWrapMode.ClampToEdge);
        GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapT,
            (int)TextureWrapMode.ClampToEdge);
        GL.BindTexture(TextureTarget.Texture2D, 0);
        return handle;
    }

    /// <summary>A colour target and the depth to attach beside it, or 0 for none - which is what
    /// the buffers that <em>read</em> depth pass, since a framebuffer cannot sample its own
    /// attachment.</summary>
    private int NewFramebuffer(int colour, int depthTexture)
    {
        int handle = GL.GenFramebuffer();
        GL.BindFramebuffer(FramebufferTarget.Framebuffer, handle);
        GL.FramebufferTexture2D(FramebufferTarget.Framebuffer, FramebufferAttachment.ColorAttachment0,
            TextureTarget.Texture2D, colour, 0);
        if (depthTexture != 0)
        {
            GL.FramebufferTexture2D(FramebufferTarget.Framebuffer,
                FramebufferAttachment.DepthAttachment, TextureTarget.Texture2D, depthTexture, 0);
        }

        FramebufferErrorCode status = GL.CheckFramebufferStatus(FramebufferTarget.Framebuffer);
        if (status != FramebufferErrorCode.FramebufferComplete)
        {
            throw new InvalidOperationException($"Framebuffer incomplete: {status}");
        }
        return handle;
    }

    private void Release()
    {
        if (SceneFramebuffer == 0)
        {
            return;
        }

        int[] framebuffers =
            [SceneFramebuffer, PresentFramebuffer, OcclusionFramebuffer, OcclusionBlurFramebuffer,
             ColourCopyFramebuffer];
        int[] textures =
            [Colour, _presentColour, Depth, Occlusion, OcclusionBlur, ColourCopy, DepthCopy];
        GL.DeleteFramebuffers(framebuffers.Length, framebuffers);
        GL.DeleteTextures(textures.Length, textures);
        SceneFramebuffer = 0;
        PresentFramebuffer = 0;
        OcclusionFramebuffer = 0;
        OcclusionBlurFramebuffer = 0;
        ColourCopyFramebuffer = 0;
    }

    public void Dispose() => Release();
}
