using OpenTK.Graphics.OpenGL4;

namespace JackAll.App.MapEditor.Gl;

/// <summary>
/// Puts back, on dispose, the pipeline state a pass is allowed to disturb: the frame's defaults, and
/// for the frame as a whole the control's own framebuffer. Known values rather than queried ones - a
/// glGet makes the caller wait for the driver to catch up with everything issued before it.
/// </summary>
public readonly struct GlState : IDisposable
{
    private readonly int _framebuffer = -1;
    private readonly int _width;
    private readonly int _height;

    /// <summary>Restores the frame's defaults only.</summary>
    public GlState()
    {
    }

    /// <summary>Also rebinds <paramref name="framebuffer"/> at the given size: a pass target left
    /// bound by a throwing path blacks the viewport for the rest of the session.</summary>
    public GlState(int framebuffer, int width, int height)
    {
        _framebuffer = framebuffer;
        _width = width;
        _height = height;
    }

    /// <summary>The state every layer is entitled to assume on entry, set once at the top of the frame.</summary>
    public static void BeginFrame()
    {
        Defaults();

        // The shared units carry something readable from here on. Whatever produces a real cascade
        // array or occlusion buffer this frame binds over the top of these.
        TextureUnits.BindStandIns();
    }

    public void Dispose()
    {
        if (_framebuffer >= 0)
        {
            GL.BindFramebuffer(FramebufferTarget.Framebuffer, _framebuffer);
            GL.Viewport(0, 0, _width, _height);
        }
        Defaults();
    }

    private static void Defaults()
    {
        GL.Enable(EnableCap.DepthTest);
        GL.DepthFunc(DepthFunction.Lequal);
        GL.DepthMask(true);
        GL.Disable(EnableCap.Blend);
        GL.Disable(EnableCap.CullFace);
    }
}
