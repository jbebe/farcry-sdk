using System.Numerics;
using Vortice.Direct3D;
using Vortice.Direct3D11;
using Vortice.DXGI;
using static JackAll.App.MapEditor.Gpu.GpuDevice;

namespace JackAll.App.MapEditor.Gpu;

/// <summary>
/// Line segments as quads some pixels wide, which Direct3D 11 does not rasterize. A quad is widened
/// along the screen's minor axis, the way OpenGL widens a line, so a diagonal keeps its weight.
/// </summary>
/// <remarks>The vertices are read from a buffer of floats rather than streamed, so one program serves
/// every layout the line layers keep: positions alone or with a colour, in pairs or by index.</remarks>
public static class WideLines
{
    private const string Shared =
        """
        cbuffer Params : register(b0)
        {
            float4x4 viewProjection;
            // The colour, or with perVertexColour set, just the alpha beside each vertex's own rgb.
            float4 tint;
            // Pulls each vertex toward this point by this share of the way, so lines win against
            // the surface they lie on.
            float3 origin;
            float lift;
            float width;
            uint stride;
            uint indexed;
            int baseVertex;
            uint firstIndex;
            uint perVertexColour;
        };

        struct Varyings
        {
            float4 position : SV_Position;
            float4 colour : COLOR;
        };
        """;

    private static ShaderProgram? _program;
    private static int _uViewProjection, _uTint, _uOrigin, _uLift, _uWidth, _uStride;
    private static int _uIndexed, _uBaseVertex, _uFirstIndex, _uPerVertexColour;

    private static ShaderProgram Program()
    {
        if (_program is not null)
        {
            return _program;
        }

        _program = new ShaderProgram(
            $$"""
            {{Shared}}
            {{ShaderProgram.QuadCornerHlsl}}
            Buffer<float> vertices : register(t0);
            Buffer<uint> indices : register(t1);

            float3 at(uint v)
            {
                float3 p = float3(vertices[v * stride], vertices[v * stride + 1], vertices[v * stride + 2]);
                return p - (p - origin) * lift;
            }

            Varyings main(uint id : SV_VertexID)
            {
                uint segment = id / 6;
                uint i0 = segment * 2;
                uint i1 = i0 + 1;
                if (indexed != 0)
                {
                    i0 = indices[firstIndex + segment * 2] + baseVertex;
                    i1 = indices[firstIndex + segment * 2 + 1] + baseVertex;
                }

                float4 a = mul(viewProjection, float4(at(i0), 1.0));
                float4 b = mul(viewProjection, float4(at(i1), 1.0));

                // Cut at the near plane first: an end behind the camera has no screen position to widen at.
                Varyings o;
                if (a.z < 0.0 && b.z < 0.0)
                {
                    o.position = float4(0.0, 0.0, -1.0, 1.0);
                    o.colour = 0.0;
                    return o;
                }
                if (a.z < 0.0) { a = lerp(a, b, a.z / (a.z - b.z)); }
                if (b.z < 0.0) { b = lerp(b, a, b.z / (b.z - a.z)); }

                float2 corner = quadCorner(id % 6);
                float4 p = corner.x < 0.5 ? a : b;
                float2 along = (b.xy / b.w - a.xy / a.w) * viewportSize;
                float2 minor = abs(along.x) >= abs(along.y) ? float2(0.0, 1.0) : float2(1.0, 0.0);
                p.xy += minor * (corner.y - 0.5) * width * 2.0 / viewportSize * p.w;
                o.position = toTarget(p);

                o.colour = tint;
                if (perVertexColour != 0)
                {
                    uint c = i0 * stride + 3;
                    o.colour.rgb = float3(vertices[c], vertices[c + 1], vertices[c + 2]);
                }
                return o;
            }
            """,
            $$"""
            {{Shared}}
            float4 main(Varyings v) : SV_Target
            {
                return v.colour;
            }
            """);
        _uViewProjection = _program.Offset("viewProjection");
        _uTint = _program.Offset("tint");
        _uOrigin = _program.Offset("origin");
        _uLift = _program.Offset("lift");
        _uWidth = _program.Offset("width");
        _uStride = _program.Offset("stride");
        _uIndexed = _program.Offset("indexed");
        _uBaseVertex = _program.Offset("baseVertex");
        _uFirstIndex = _program.Offset("firstIndex");
        _uPerVertexColour = _program.Offset("perVertexColour");
        return _program;
    }

    public static void Release()
    {
        _program?.Dispose();
        _program = null;
    }

    /// <summary>A buffer of floats or indices the line program reads by element.</summary>
    public static (ID3D11Buffer Buffer, ID3D11ShaderResourceView View) Readable<T>(T[] data, Format format,
        BindFlags also = BindFlags.None) where T : unmanaged
    {
        ID3D11Buffer buffer = Device.CreateBuffer(data, BindFlags.ShaderResource | also, ResourceUsage.Immutable);
        ID3D11ShaderResourceView view = Device.CreateShaderResourceView(buffer,
            new ShaderResourceViewDescription(buffer, format, 0, (uint)data.Length));
        return (buffer, view);
    }

    /// <summary>What one draw of segments reads and how it is placed and coloured.</summary>
    public readonly record struct Segments(
        ID3D11ShaderResourceView Vertices, int Stride, int Count, Vector4 Tint, float Width,
        bool PerVertexColour = false, ID3D11ShaderResourceView? Indices = null, int FirstIndex = 0,
        int BaseVertex = 0, Vector3 Origin = default, float Lift = 0f);

    /// <summary>Draws <see cref="Segments.Count"/> segments with whatever depth and blending the
    /// caller has set.</summary>
    public static void Draw(Matrix4x4 viewProjection, in Segments lines)
    {
        if (lines.Count == 0)
        {
            return;
        }

        ShaderProgram program = Program();
        program.Use();
        program.Set(_uViewProjection, viewProjection);
        program.Set(_uTint, lines.Tint);
        program.Set(_uOrigin, lines.Origin);
        program.Set(_uLift, lines.Lift);
        program.Set(_uWidth, lines.Width);
        program.Set(_uStride, lines.Stride);
        program.Set(_uIndexed, lines.Indices is null ? 0 : 1);
        program.Set(_uBaseVertex, lines.BaseVertex);
        program.Set(_uFirstIndex, lines.FirstIndex);
        program.Set(_uPerVertexColour, lines.PerVertexColour ? 1 : 0);
        Context.VSSetShaderResource(0, lines.Vertices);
        Context.VSSetShaderResource(1, lines.Indices!);
        program.Draw(lines.Count * 6);
    }
}
