using OpenTK.Graphics.OpenGL4;

namespace JackAll.App.MapEditor.Gl;

/// <summary>
/// The pipeline state a pass is allowed to disturb, put back on dispose. The frame is a chain of
/// layers that each set what they need and mostly do not restore it - workable while everything
/// draws to one framebuffer at one size, and not workable once passes bring their own.
/// </summary>
public readonly struct GlState : IDisposable
{
    /// <summary>Reused rather than allocated per capture; the struct copies the four values straight
    /// out, and nothing here nests or runs off the render thread.</summary>
    private static readonly int[] ViewportScratch = new int[4];

    private readonly int _framebuffer;
    private readonly int _x;
    private readonly int _y;
    private readonly int _width;
    private readonly int _height;
    private readonly bool _depthTest;
    private readonly int _depthFunc;
    private readonly bool _depthMask;
    private readonly bool _blend;
    private readonly bool _cullFace;

    public GlState()
    {
        GL.GetInteger(GetPName.FramebufferBinding, out _framebuffer);
        GL.GetInteger(GetPName.Viewport, ViewportScratch);
        (_x, _y, _width, _height) =
            (ViewportScratch[0], ViewportScratch[1], ViewportScratch[2], ViewportScratch[3]);
        _depthTest = GL.IsEnabled(EnableCap.DepthTest);
        GL.GetInteger(GetPName.DepthFunc, out _depthFunc);
        GL.GetBoolean(GetPName.DepthWritemask, out _depthMask);
        _blend = GL.IsEnabled(EnableCap.Blend);
        _cullFace = GL.IsEnabled(EnableCap.CullFace);
    }

    /// <summary>The state every layer is entitled to assume on entry. Set once at the top of the
    /// frame: depth testing has never been enabled by the layers that rely on it, and the depth
    /// function has never been set at all, so both are inherited from whatever ran last.</summary>
    public static void BeginFrame()
    {
        GL.Enable(EnableCap.DepthTest);
        GL.DepthFunc(DepthFunction.Lequal);
        GL.DepthMask(true);
        GL.Disable(EnableCap.Blend);
        GL.Disable(EnableCap.CullFace);
    }

    public void Dispose()
    {
        GL.BindFramebuffer(FramebufferTarget.Framebuffer, _framebuffer);
        GL.Viewport(_x, _y, _width, _height);
        Toggle(EnableCap.DepthTest, _depthTest);
        GL.DepthFunc((DepthFunction)_depthFunc);
        GL.DepthMask(_depthMask);
        Toggle(EnableCap.Blend, _blend);
        Toggle(EnableCap.CullFace, _cullFace);
    }

    private static void Toggle(EnableCap cap, bool enabled)
    {
        if (enabled)
        {
            GL.Enable(cap);
        }
        else
        {
            GL.Disable(cap);
        }
    }
}
