using System.Numerics;
using System.Runtime.InteropServices;
using SharpGen.Runtime;
using Vortice.D3DCompiler;
using Vortice.Direct3D;
using Vortice.Direct3D11;
using Vortice.Direct3D11.Shader;
using static JackAll.App.MapEditor.Gpu.GpuDevice;

namespace JackAll.App.MapEditor.Gpu;

/// <summary>
/// A compiled HLSL vertex+pixel pair, its input layout, and its <c>Params</c> constant buffer, which
/// it fills by variable name. Shader model 4.0, so feature level 10 hardware
/// runs it.
/// </summary>
public sealed class ShaderProgram : IDisposable
{
    /// <summary>
    /// Pasted ahead of every source: the shared samplers, the per-frame constants, and
    /// <c>toTarget</c>, which every pass into one of our own targets runs its position through. Those
    /// targets hold the image bottom-up, row 0 at the bottom, so <c>SV_Position</c> and every
    /// screen-space uv run from the bottom-left; only <see cref="PresentPass"/> turns it upright.
    /// </summary>
    private static readonly string Prelude =
        $$"""
        SamplerState pointClamp : register(s0);
        SamplerState linearClamp : register(s1);
        SamplerState anisoWrap : register(s2);
        SamplerComparisonState shadowCompare : register(s3);
        SamplerState anisoClamp : register(s4);

        cbuffer Frame : register(b1)
        {
            float4x4 shadowMatrices[{{ShadowCascades.Count}}];
            float4 shadowSplits;
            float3 fogSetup;
            float shadowStrength;
            float3 fogTint;
            float occlusionStrength;
            float2 viewportSize;
            float demo;
            float framePad;
        };

        float4 toTarget(float4 clip)
        {
            return float4(clip.x, -clip.y, clip.z, clip.w);
        }

        """;

    /// <summary>One oversized triangle covering the screen from <c>SV_VertexID</c> - draw 3 vertices
    /// and call <c>screenCorner(id)</c> for its [0,1] uv. Cheaper than a quad and, unlike two
    /// triangles, with no diagonal seam through the derivative quads.</summary>
    public const string FullScreenTriangleHlsl =
        """
        float2 screenCorner(uint id)
        {
            return float2((id << 1) & 2, id & 2);
        }
        """;

    /// <summary>The varyings every full-screen pass reads.</summary>
    public const string ScreenVaryingsHlsl =
        """
        struct ScreenVaryings
        {
            float4 position : SV_Position;
            float2 uv : TEXCOORD0;
        };
        """;

    /// <summary>The whole vertex stage for a pass that just wants the screen and its uv - which is
    /// every post pass.</summary>
    public const string FullScreenTriangleVertex =
        $$"""
        {{FullScreenTriangleHlsl}}
        {{ScreenVaryingsHlsl}}
        ScreenVaryings main(uint id : SV_VertexID)
        {
            ScreenVaryings o;
            o.uv = screenCorner(id);
            o.position = toTarget(float4(o.uv * 2.0 - 1.0, 0.0, 1.0));
            return o;
        }
        """;

    /// <summary>Interleaved gradient noise - one value per pixel with no transcendentals, in a
    /// pattern a blur can actually average out where white noise cannot. Used both to rotate a
    /// sampling kernel and to dither a gradient on its way to 8 bits.</summary>
    public const string NoiseHlsl =
        """
        float interleavedGradientNoise(float2 position)
        {
            return frac(52.9829189 * frac(dot(position, float2(0.06711056, 0.00583715))));
        }
        """;

    /// <summary>Unit-square corner from <c>SV_VertexID</c> for buffer-less quad draws - call
    /// <c>quadCorner(id)</c> (two triangles, 6 vertices, corners in [0,1]).</summary>
    public const string QuadCornerHlsl =
        """
        float2 quadCorner(uint id)
        {
            return float2((id == 1 || id == 4 || id == 5) ? 1.0 : 0.0,
                          (id == 2 || id == 3 || id == 5) ? 1.0 : 0.0);
        }
        """;

    private readonly ID3D11VertexShader _vertex;
    private readonly ID3D11PixelShader _pixel;
    private readonly ID3D11InputLayout? _layout;
    private readonly ID3D11Buffer? _constants;
    private readonly byte[] _shadow;
    private readonly Dictionary<string, int> _offsets = [];
    private bool _dirty = true;

    /// <param name="layout">The vertex streams; null for a program that builds its vertices from
    /// <c>SV_VertexID</c> alone.</param>
    public ShaderProgram(string vertexSource, string pixelSource, InputElementDescription[]? layout = null)
    {
        ReadOnlyMemory<byte> vertex = Compile(vertexSource, "vs_4_0");
        ReadOnlyMemory<byte> pixel = Compile(pixelSource, "ps_4_0");
        _vertex = Device.CreateVertexShader(vertex.Span);
        _pixel = Device.CreatePixelShader(pixel.Span);
        if (layout is not null)
        {
            _layout = Device.CreateInputLayout(layout, vertex.Span);
        }

        int size = Math.Max(Reflect(vertex), Reflect(pixel));
        _shadow = new byte[size];
        if (size > 0)
        {
            _constants = Device.CreateBuffer((uint)size, BindFlags.ConstantBuffer, ResourceUsage.Dynamic, CpuAccessFlags.Write);
        }
    }

    /// <summary>A float as an HLSL literal, the same in every locale: interpolated as it is, a comma
    /// decimal turns one constant into two and the source stops compiling.</summary>
    public static string Literal(float value)
        => value.ToString("0.0#####", System.Globalization.CultureInfo.InvariantCulture);

    /// <summary>Bytecode by source and profile; it does not depend on the device, so layers that
    /// share a program, and a world or a device made again, compile it once.</summary>
    private static readonly Dictionary<(string Source, string Profile), byte[]> Compiled = [];

    /// <summary>Compiles one stage; a failure quotes the source line each error points at, since the
    /// source exists only as this string.</summary>
    private static ReadOnlyMemory<byte> Compile(string source, string profile)
    {
        if (Compiled.TryGetValue((source, profile), out byte[]? cached))
        {
            return cached;
        }
        string full = Prelude + source;
        Result result = Compiler.Compile(full, null!, null!, "main", profile, profile,
            ShaderFlags.OptimizationLevel3, out Blob? blob, out Blob? errors);
        if (result.Failure || blob is null)
        {
            string log = errors?.AsString() ?? result.ToString();
            errors?.Dispose();
            string[] lines = full.Split('\n');
            IEnumerable<string> quoted = System.Text.RegularExpressions.Regex.Matches(log, @"\((\d+),\d+")
                .Select(m => int.Parse(m.Groups[1].Value)).Distinct().Where(n => n >= 1 && n <= lines.Length)
                .Select(n => $"{n}: {lines[n - 1].Trim()}");
            throw new InvalidOperationException($"HLSL {profile} compile failed: {log}\n{string.Join("\n", quoted)}");
        }
        errors?.Dispose();
        byte[] bytes = blob.AsBytes();
        blob.Dispose();
        Compiled[(source, profile)] = bytes;
        return bytes;
    }

    /// <summary>Records where each variable of <c>Params</c> sits, and returns the buffer's size. The
    /// two stages share one declaration, so a name found in both must sit at the same offset.</summary>
    private int Reflect(ReadOnlyMemory<byte> bytecode)
    {
        using ID3D11ShaderReflection reflection = Compiler.Reflect<ID3D11ShaderReflection>(bytecode.Span);
        for (uint i = 0; i < reflection.Description.ConstantBuffers; i++)
        {
            ID3D11ShaderReflectionConstantBuffer buffer = reflection.GetConstantBufferByIndex(i);
            if (buffer.Description.Name != "Params")
            {
                continue;
            }
            foreach (ID3D11ShaderReflectionVariable variable in buffer.Variables)
            {
                ShaderVariableDescription description = variable.Description;
                if (_offsets.TryGetValue(description.Name, out int seen) && seen != (int)description.StartOffset)
                {
                    throw new InvalidOperationException($"Params.{description.Name} differs between the stages.");
                }
                _offsets[description.Name] = (int)description.StartOffset;
            }
            return (int)buffer.Description.Size;
        }
        return 0;
    }

    /// <summary>A variable's byte offset in <c>Params</c>, or -1 when the program has none of that
    /// name, which setting ignores.</summary>
    public int Offset(string name) => _offsets.GetValueOrDefault(name, -1);

    public void Set(int offset, float value) => Write(offset, value);

    public void Set(int offset, int value) => Write(offset, value);

    public void Set(int offset, Vector2 value) => Write(offset, value);

    public void Set(int offset, Vector3 value) => Write(offset, value);

    public void Set(int offset, Vector4 value) => Write(offset, value);

    /// <summary>Stored as it is: HLSL reads it column-major, so <c>mul(m, v)</c> applies it the way
    /// System.Numerics' row-vector <c>v * m</c> does.</summary>
    public void Set(int offset, Matrix4x4 value) => Write(offset, value);

    /// <summary>A <c>float[]</c> in a constant buffer, whose every element starts a new 16 bytes.</summary>
    public void Set(int offset, float[] values)
    {
        for (int i = 0; i < values.Length && offset >= 0; i++)
        {
            Write(offset + i * 16, values[i]);
        }
    }

    private void Write<T>(int offset, T value) where T : unmanaged
    {
        if (offset < 0)
        {
            return;
        }
        ReadOnlySpan<byte> bytes = MemoryMarshal.AsBytes(new ReadOnlySpan<T>(in value));
        Span<byte> slot = _shadow.AsSpan(offset, bytes.Length);
        if (!slot.SequenceEqual(bytes))
        {
            bytes.CopyTo(slot);
            _dirty = true;
        }
    }

    /// <summary>The program whose stages and <c>Params</c> are bound; null after <see cref="Forget"/>.</summary>
    private static ShaderProgram? _current;

    /// <summary>
    /// Starts a frame with nothing taken as bound, and triangle lists, the only kind any layer draws.
    /// Everything that binds a program goes through <see cref="Use"/>, so this is the one reset.
    /// </summary>
    public static void Forget()
    {
        _current = null;
        Context.IASetPrimitiveTopology(PrimitiveTopology.TriangleList);
    }

    public void Use()
    {
        if (ReferenceEquals(_current, this))
        {
            return;
        }
        _current = this;
        Context.VSSetShader(_vertex);
        Context.PSSetShader(_pixel);
        Context.IASetInputLayout(_layout);
        Context.VSSetConstantBuffer(0, _constants);
        Context.PSSetConstantBuffer(0, _constants);
    }

    /// <summary>Uploads what changed since the last draw; every draw below runs it first.</summary>
    private void Commit()
    {
        if (!_dirty || _constants is null)
        {
            return;
        }
        _dirty = false;
        MappedSubresource mapped = Context.Map(_constants, MapMode.WriteDiscard);
        Marshal.Copy(_shadow, 0, mapped.DataPointer, _shadow.Length);
        Context.Unmap(_constants);
    }

    public void Draw(int vertexCount, int firstVertex = 0)
    {
        Commit();
        Context.Draw((uint)vertexCount, (uint)firstVertex);
    }

    public void DrawInstanced(int vertexCount, int instanceCount)
    {
        Commit();
        Context.DrawInstanced((uint)vertexCount, (uint)instanceCount, 0, 0);
    }

    public void DrawIndexed(int indexCount)
    {
        Commit();
        Context.DrawIndexed((uint)indexCount, 0, 0);
    }

    public void DrawIndexedInstanced(int indexCount, int instanceCount, int firstIndex = 0, int firstInstance = 0)
    {
        Commit();
        Context.DrawIndexedInstanced((uint)indexCount, (uint)instanceCount, (uint)firstIndex, 0, (uint)firstInstance);
    }

    public void Dispose()
    {
        if (ReferenceEquals(_current, this))
        {
            _current = null;
        }
        _constants?.Dispose();
        _layout?.Dispose();
        _pixel.Dispose();
        _vertex.Dispose();
    }
}
