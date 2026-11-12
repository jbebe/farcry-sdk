using Vortice.Direct3D11;
using Vortice.Mathematics;
using static JackAll.App.MapEditor.Gpu.GpuDevice;

namespace JackAll.App.MapEditor.Gpu;

public enum DepthMode
{
    /// <summary>Tested and written, less-or-equal: the frame's default.</summary>
    TestWrite,

    /// <summary>Tested, never written - blended surfaces and overlays.</summary>
    TestOnly,

    Off,
}

/// <summary>
/// Puts back, on dispose, the pipeline state a pass is allowed to disturb: depth, blending, the
/// rasterizer, and the textures on a layer's own slots, so nothing is still bound as a texture when
/// a later pass makes it a target. Set to known values, never queried.
/// </summary>
public readonly struct GpuState : IDisposable
{
    /// <summary>The first slot no layer binds for itself; the shadow cascades and the occlusion sit
    /// here for the whole of the scene pass.</summary>
    public const int SharedSlot = 9;

    public const int ShadowSlot = SharedSlot;

    public const int OcclusionSlot = SharedSlot + ShadowCascades.Count;

    private const int Slots = 16;

    /// <summary>Nulls for every slot, to unbind any run of them.</summary>
    internal static readonly ID3D11ShaderResourceView[] NoTextures = new ID3D11ShaderResourceView[Slots];

    private static ID3D11DepthStencilState? _testWrite, _testOnly, _off;
    private static ID3D11BlendState? _alpha;
    private static ID3D11RasterizerState? _solid, _biased;
    private static ID3D11SamplerState[]? _samplers;

    /// <summary>The state objects, made once per device.</summary>
    public static void Create()
    {
        _testWrite = Device.CreateDepthStencilState(new DepthStencilDescription(true, DepthWriteMask.All, ComparisonFunction.LessEqual));
        _testOnly = Device.CreateDepthStencilState(new DepthStencilDescription(true, DepthWriteMask.Zero, ComparisonFunction.LessEqual));
        _off = Device.CreateDepthStencilState(new DepthStencilDescription(false, DepthWriteMask.Zero, ComparisonFunction.Always));

        // The same factors on alpha as on colour.
        var alpha = new BlendDescription(Blend.SourceAlpha, Blend.InverseSourceAlpha, Blend.SourceAlpha, Blend.InverseSourceAlpha);
        _alpha = Device.CreateBlendState(alpha);

        _solid = Device.CreateRasterizerState(new RasterizerDescription(CullMode.None, FillMode.Solid) { DepthClipEnable = true });
        // For the coarse terrain patch, so the fine ring wins where the two overlap.
        _biased = Device.CreateRasterizerState(new RasterizerDescription(CullMode.None, FillMode.Solid)
        {
            DepthClipEnable = true,
            DepthBias = 1,
            SlopeScaledDepthBias = 1f,
        });

        // In the order ShaderProgram's prelude declares them, s0 onwards.
        _samplers =
        [
            Sampler(Filter.MinMagMipPoint, TextureAddressMode.Clamp),
            Sampler(Filter.MinMagMipLinear, TextureAddressMode.Clamp),
            Sampler(Filter.Anisotropic, TextureAddressMode.Wrap),
            Sampler(Filter.ComparisonMinMagLinearMipPoint, TextureAddressMode.Border, ComparisonFunction.LessEqual),
            Sampler(Filter.Anisotropic, TextureAddressMode.Clamp),
        ];
    }

    /// <summary>Anisotropy at 16 wherever a filter asks for it: every feature level allows it. A
    /// border reads 1, which for the shadow compare is a fragment in front of every caster.</summary>
    private static ID3D11SamplerState Sampler(
        Filter filter, TextureAddressMode address, ComparisonFunction compare = ComparisonFunction.Never)
        => Device.CreateSamplerState(new SamplerDescription
        {
            Filter = filter,
            AddressU = address,
            AddressV = address,
            AddressW = address,
            MaxAnisotropy = 16,
            ComparisonFunc = compare,
            BorderColor = new Color4(1f, 1f, 1f, 1f),
            MinLOD = 0f,
            MaxLOD = float.MaxValue,
        });

    public static void Release()
    {
        foreach (IDisposable? state in new IDisposable?[] { _testWrite, _testOnly, _off, _alpha, _solid, _biased })
        {
            state?.Dispose();
        }
        foreach (ID3D11SamplerState sampler in _samplers ?? [])
        {
            sampler.Dispose();
        }
        _samplers = null;
    }

    /// <summary>The state every layer is entitled to assume on entry, set once at the top of the
    /// frame, with the samplers every program declares and no texture left bound from the last.</summary>
    public static void BeginFrame()
    {
        Defaults();
        ShaderProgram.Forget();
        Context.VSSetSamplers(0, _samplers!);
        Context.PSSetSamplers(0, _samplers!);
        Context.VSSetShaderResources(0, Slots, NoTextures);
        Context.PSSetShaderResources(0, Slots, NoTextures);
    }

    public static void Depth(DepthMode mode) => Context.OMSetDepthStencilState(mode switch
    {
        DepthMode.TestWrite => _testWrite,
        DepthMode.TestOnly => _testOnly,
        _ => _off,
    });

    public static void AlphaBlend(bool on) => Context.OMSetBlendState(on ? _alpha : null);

    public static void DepthBias(bool on) => Context.RSSetState(on ? _biased : _solid);

    public void Dispose()
    {
        Defaults();
        Context.VSSetShaderResources(0, SharedSlot, NoTextures);
        Context.PSSetShaderResources(0, SharedSlot, NoTextures);
    }

    private static void Defaults()
    {
        Depth(DepthMode.TestWrite);
        AlphaBlend(false);
        DepthBias(false);
    }
}
