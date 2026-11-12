using System.Numerics;
using JackAll.Tools.World;
using Vortice.Direct3D;
using Vortice.Direct3D11;
using Vortice.DXGI;
using static JackAll.App.MapEditor.Gpu.GpuDevice;

namespace JackAll.App.MapEditor.Gpu;

/// <summary>
/// Draws a gizmo's handles: the move gizmo's arrows and plane squares, or the rotate gizmo's rings.
/// One handle built around +Z lives in the buffer and each axis is a rotation of it, so the whole
/// gizmo is a few draws of the same geometry.
/// </summary>
public sealed class GizmoLayer : IDisposable
{
    private const string Params =
        """
        cbuffer Params : register(b0)
        {
            float4x4 viewProjection;
            float4x4 model;
            float4 tint;
        };
        """;

    private readonly ShaderProgram _program;
    private readonly ID3D11Buffer _buffer;
    private readonly int _handleVertices;
    private readonly int _planeVertices;
    private readonly int _uViewProjection;
    private readonly int _uModel;
    private readonly int _uTint;

    /// <summary><paramref name="plane"/> is the square drawn between two arms, empty for a gizmo
    /// without one; it follows the handle in the same buffer.</summary>
    private GizmoLayer(Vector3[] handle, Vector3[] plane)
    {
        _handleVertices = handle.Length;
        _planeVertices = plane.Length;
        Vector3[] vertices = [.. handle.Concat(plane)];

        _program = new ShaderProgram(
            $$"""
            {{Params}}
            float4 main(float3 corner : POSITION) : SV_Position
            {
                return toTarget(mul(viewProjection, mul(model, float4(corner, 1.0))));
            }
            """,
            $$"""
            {{Params}}
            float4 main() : SV_Target { return tint; }
            """,
            [new InputElementDescription("POSITION", 0, Format.R32G32B32_Float, 0, 0)]);
        _uViewProjection = _program.Offset("viewProjection");
        _uModel = _program.Offset("model");
        _uTint = _program.Offset("tint");

        _buffer = Device.CreateBuffer(vertices, BindFlags.VertexBuffer, ResourceUsage.Immutable);
    }

    /// <summary>The move gizmo: its arm, and the square each pair of arms frames.</summary>
    public static GizmoLayer Arrows() => new(GizmoGeometry.Arrow(), GizmoGeometry.Square());

    /// <summary>The rotate gizmo: its ring.</summary>
    public static GizmoLayer Rings() => new(GizmoGeometry.Ring(), []);

    /// <summary>
    /// The handles at <paramref name="origin"/>, sized so the gizmo holds its screen size, with
    /// <paramref name="active"/> lit up.
    /// </summary>
    /// <remarks>
    /// Depth testing stays off while this draws, and nothing is culled: a handle you cannot see
    /// because the entity's own model swallows it is a handle you cannot grab, and the geometry is
    /// not wound for culling.
    /// </remarks>
    public void Draw(Matrix4x4 viewProjection, Vector3 origin, float scale, GizmoAxis active)
    {
        using GpuState pass = new();
        _program.Use();
        _program.Set(_uViewProjection, viewProjection);
        Context.IASetVertexBuffer(0, _buffer, 3 * sizeof(float));
        GpuState.Depth(DepthMode.Off);
        Matrix4x4 place = Matrix4x4.CreateScale(scale) * Matrix4x4.CreateTranslation(origin);

        foreach (GizmoAxis axis in TranslateGizmo.Axes)
        {
            _program.Set(_uModel, TranslateGizmo.Orientation(axis) * place);
            _program.Set(_uTint, Tint(axis, active, 1f, 1f));
            _program.Draw(_handleVertices);
        }

        GpuState.AlphaBlend(true);
        foreach (GizmoAxis plane in _planeVertices > 0 ? TranslateGizmo.Planes : [])
        {
            _program.Set(_uModel, TranslateGizmo.PlaneOrientation(plane) * place);
            _program.Set(_uTint, Tint(plane, active, GizmoGeometry.PlaneAlpha, GizmoGeometry.HeldPlaneAlpha));
            _program.Draw(_planeVertices, _handleVertices);
        }
    }

    private static Vector4 Tint(GizmoAxis axis, GizmoAxis active, float alpha, float heldAlpha)
    {
        Vector3 c = axis == active ? GizmoGeometry.Highlight : GizmoGeometry.Tint(axis);
        return new Vector4(c, axis == active ? heldAlpha : alpha);
    }

    public void Dispose()
    {
        _program.Dispose();
        _buffer.Dispose();
    }
}
