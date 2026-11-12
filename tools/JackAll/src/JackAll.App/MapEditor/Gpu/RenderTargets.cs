using Vortice.Direct3D;
using Vortice.Direct3D11;
using Vortice.DXGI;
using static JackAll.App.MapEditor.Gpu.GpuDevice;

namespace JackAll.App.MapEditor.Gpu;

/// <summary>A texture a pass draws into and a later one reads.</summary>
public sealed class ColourTarget : IDisposable
{
    private readonly ID3D11Texture2D _texture;

    public ID3D11RenderTargetView Target { get; }

    public ID3D11ShaderResourceView View { get; }

    public ColourTarget(int width, int height, Format format)
    {
        _texture = Device.CreateTexture2D(new Texture2DDescription(format, (uint)width, (uint)height, 1, 1,
            BindFlags.RenderTarget | BindFlags.ShaderResource));
        Target = Device.CreateRenderTargetView(_texture);
        View = Device.CreateShaderResourceView(_texture);
    }

    public ID3D11Resource Resource => _texture;

    public void Dispose()
    {
        View.Dispose();
        Target.Dispose();
        _texture.Dispose();
    }
}

/// <summary>A 32-bit float depth buffer that later passes can also sample.</summary>
public sealed class DepthTarget : IDisposable
{
    private readonly ID3D11Texture2D _texture;

    public ID3D11DepthStencilView Stencil { get; }

    public ID3D11ShaderResourceView View { get; }

    public DepthTarget(int width, int height)
    {
        _texture = Device.CreateTexture2D(new Texture2DDescription(Format.R32_Typeless, (uint)width, (uint)height, 1, 1,
            BindFlags.DepthStencil | BindFlags.ShaderResource));
        Stencil = Device.CreateDepthStencilView(_texture,
            new DepthStencilViewDescription(_texture, DepthStencilViewDimension.Texture2D, Format.D32_Float));
        View = Device.CreateShaderResourceView(_texture,
            new ShaderResourceViewDescription(_texture, ShaderResourceViewDimension.Texture2D, Format.R32_Float));
    }

    public ID3D11Resource Resource => _texture;

    public void Dispose()
    {
        View.Dispose();
        Stencil.Dispose();
        _texture.Dispose();
    }
}

/// <summary>The four surfaces only the presentation reads, made and let go together.</summary>
public sealed class Presentation(int width, int height) : IDisposable
{
    /// <summary>The opaque scene as it stood before the water went in, so the water can refract
    /// what is behind it. A pass cannot sample the target it is drawing into.</summary>
    public ColourTarget ColourCopy { get; } = new(width, height, Format.R16G16B16A16_Float);

    /// <summary>The depth beside it. The water needs the scene depth to know how much water the
    /// view crosses, but it is depth-testing against the live buffer at the same time.</summary>
    public DepthTarget DepthCopy { get; } = new(width, height);

    /// <summary>Ambient occlusion and the buffer its separable blur ping-pongs through.</summary>
    public ColourTarget Occlusion { get; } = new(width, height, Format.R8_UNorm);

    public ColourTarget OcclusionBlur { get; } = new(width, height, Format.R8_UNorm);

    public void Dispose()
    {
        ColourCopy.Dispose();
        DepthCopy.Dispose();
        Occlusion.Dispose();
        OcclusionBlur.Dispose();
    }
}

/// <summary>
/// The offscreen surfaces the frame is built on: a float colour buffer for the scene, a depth
/// buffer the passes after it can sample, and an 8-bit buffer the tonemapped result and the
/// overlays share before <see cref="PresentPass"/> puts it on screen.
/// </summary>
/// <remarks>The present buffer shares the scene's depth, so the overlays test against the depth the
/// scene already wrote; and it is where the frame still lies bottom-up, as every pass draws it.</remarks>
public sealed class RenderTargets : IDisposable
{
    public int Width { get; private set; }

    public int Height { get; private set; }

    /// <summary>Linear radiance, unclamped - the sun glint and the sky run well above 1.</summary>
    public ColourTarget Scene { get; private set; } = null!;

    public DepthTarget Depth { get; private set; } = null!;

    public ColourTarget Present { get; private set; } = null!;

    /// <summary>Allocated only while the presentation is on, so a layer reading it with the
    /// presentation off finds nothing rather than a released view.</summary>
    public Presentation? Presentation { get; private set; }

    /// <summary>
    /// Brings the surfaces in line with the size and the mode the frame is about to draw at, and
    /// does nothing when neither has moved. Sized in device pixels, never in WPF units - a
    /// screen-space effect measured against the latter is wrong on any display that is not at 100%.
    /// </summary>
    public void Resize(int width, int height, bool presentation)
    {
        if (width != Width || height != Height || Scene is null)
        {
            Release();
            Width = Math.Max(width, 1);
            Height = Math.Max(height, 1);
            Scene = new ColourTarget(Width, Height, Format.R16G16B16A16_Float);
            Present = new ColourTarget(Width, Height, Format.R8G8B8A8_UNorm);
            Depth = new DepthTarget(Width, Height);
        }

        if (presentation != (Presentation is not null))
        {
            Presentation?.Dispose();
            Presentation = presentation ? new Presentation(Width, Height) : null;
        }
    }

    public void BindScene() => Bind(Scene.Target);

    /// <summary>Depth alone, for the prepass the occlusion reads.</summary>
    public void BindDepth() => Bind(null);

    public void BindPresent() => Bind(Present.Target);

    private void Bind(ID3D11RenderTargetView? colour)
    {
        Context.OMSetRenderTargets(colour!, Depth.Stencil);
        Context.RSSetViewport(0, 0, Width, Height);
    }

    /// <summary>Copies the opaque scene and its depth for the water to read.</summary>
    public void CopyScene()
    {
        Context.CopyResource(Presentation!.ColourCopy.Resource, Scene.Resource);
        Context.CopyResource(Presentation.DepthCopy.Resource, Depth.Resource);
    }

    private void Release()
    {
        Presentation?.Dispose();
        Scene?.Dispose();
        Present?.Dispose();
        Depth?.Dispose();
        (Presentation, Scene, Present, Depth) = (null, null!, null!, null!);
    }

    public void Dispose() => Release();
}