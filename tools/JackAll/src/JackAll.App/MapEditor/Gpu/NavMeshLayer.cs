using System.Numerics;
using JackAll.Tools.World;
using Vortice.Direct3D;
using Vortice.Direct3D11;
using Vortice.DXGI;
using static JackAll.App.MapEditor.Gpu.GpuDevice;

namespace JackAll.App.MapEditor.Gpu;

/// <summary>
/// Draws a map's navmesh: its triangles tinted by slope, their edges by kind, and optionally a line
/// between the centres of every two neighbouring triangles.
/// </summary>
public sealed class NavMeshLayer : IDisposable
{
    private static readonly Vector4 InteriorTint = new(0.9f, 0.9f, 0.9f, 0.35f);
    private static readonly Vector4 BoundaryTint = new(1f, 1f, 1f, 1f);
    private static readonly Vector4 SeamTint = new(0.95f, 0.35f, 0.95f, 1f);
    private static readonly Vector4 LinkTint = new(1f, 0.85f, 0.3f, 0.6f);

    /// <summary>Share of the camera distance each pass is pulled toward the camera, lines further
    /// than the surface, so both win against the ground they lie on.</summary>
    private const float SurfaceLift = 0.003f;
    private const float LineLift = 0.005f;

    private const string Shared =
        """
        cbuffer Params : register(b0)
        {
            float4x4 viewProjection;
            float3 origin;
            float lift;
        };

        struct Varyings
        {
            float4 position : SV_Position;
            float3 local : TEXCOORD0;
        };
        """;

    private readonly ShaderProgram _fill;
    private readonly int _uViewProjection;
    private readonly int _uOrigin;
    private readonly int _uLift;

    private readonly ID3D11Buffer _vertices;
    private readonly ID3D11ShaderResourceView _vertexView;
    private readonly ID3D11Buffer _indices;
    private readonly ID3D11ShaderResourceView _indexView;

    /// <summary>Where the centroids start in the vertex buffer, which link indices are relative to.</summary>
    private readonly int _centroidBase;

    /// <summary>Index counts of the triangles, the three edge kinds and the links, stored in that
    /// order in the index buffer.</summary>
    private readonly int[] _counts;

    public NavMeshLayer(NavMesh mesh)
    {
        _fill = new ShaderProgram(
            $$"""
            {{Shared}}
            Varyings main(float3 position : POSITION)
            {
                Varyings o;
                o.local = position - origin;
                o.position = toTarget(mul(viewProjection, float4(position - o.local * lift, 1.0)));
                return o;
            }
            """,
            $$"""
            {{Shared}}
            float4 main(Varyings v) : SV_Target
            {
                float3 normal = normalize(cross(ddx(v.local), ddy(v.local)));
                float level = abs(normal.z);
                return float4(1.0 - level, level, 0.25, 0.35);
            }
            """,
            [new InputElementDescription("POSITION", 0, Format.R32G32B32_Float, 0, 0)]);
        _uViewProjection = _fill.Offset("viewProjection");
        _uOrigin = _fill.Offset("origin");
        _uLift = _fill.Offset("lift");

        _centroidBase = mesh.Vertices.Length;
        float[] positions = [.. mesh.Vertices.Concat(mesh.Centroids).SelectMany(v => new[] { v.X, v.Y, v.Z })];
        (_vertices, _vertexView) = WideLines.Readable(positions, Format.R32_Float, BindFlags.VertexBuffer);

        int[][] ranges = [mesh.Triangles, mesh.InteriorEdges, mesh.BoundaryEdges, mesh.SeamEdges, mesh.Links];
        _counts = [.. ranges.Select(r => r.Length)];
        int[] indices = [.. ranges.SelectMany(r => r)];
        (_indices, _indexView) = WideLines.Readable(indices.Length > 0 ? indices : [0], Format.R32_UInt, BindFlags.IndexBuffer);
    }

    public void Draw(Matrix4x4 viewProjection, Vector3 cameraPosition, bool surface, bool edges, bool links)
    {
        if (_counts[0] == 0)
        {
            return;
        }

        using GpuState pass = new();
        GpuState.AlphaBlend(true);

        if (surface)
        {
            _fill.Use();
            _fill.Set(_uViewProjection, viewProjection);
            _fill.Set(_uOrigin, cameraPosition);
            _fill.Set(_uLift, SurfaceLift);
            Context.IASetVertexBuffer(0, _vertices, 3 * sizeof(float));
            Context.IASetIndexBuffer(_indices, Format.R32_UInt, 0);
            GpuState.Depth(DepthMode.TestOnly);
            _fill.DrawIndexed(_counts[0]);
            FrameStats.Drawn(_counts[0] / 3);
            GpuState.Depth(DepthMode.TestWrite);
        }

        if (edges)
        {
            DrawLines(viewProjection, cameraPosition, InteriorTint, 1f, 1, 0);
            DrawLines(viewProjection, cameraPosition, BoundaryTint, 2f, 2, 0);
            DrawLines(viewProjection, cameraPosition, SeamTint, 2f, 3, 0);
        }
        if (links)
        {
            DrawLines(viewProjection, cameraPosition, LinkTint, 1f, 4, _centroidBase);
        }
    }

    /// <summary>Draws one range of the index buffer as lines, its indices offset by
    /// <paramref name="baseVertex"/>.</summary>
    private void DrawLines(Matrix4x4 viewProjection, Vector3 cameraPosition, Vector4 tint, float width, int range,
        int baseVertex)
    {
        if (_counts[range] == 0)
        {
            return;
        }

        WideLines.Draw(viewProjection, new WideLines.Segments(
            _vertexView, Stride: 3, _counts[range] / 2, tint, width, Indices: _indexView,
            FirstIndex: _counts.Take(range).Sum(), BaseVertex: baseVertex, Origin: cameraPosition, Lift: LineLift));
        FrameStats.Drawn(0);
    }

    public void Dispose()
    {
        _fill.Dispose();
        _vertexView.Dispose();
        _vertices.Dispose();
        _indexView.Dispose();
        _indices.Dispose();
    }
}
