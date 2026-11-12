using System.Numerics;
using JackAll.Tools.World;
using Vortice.Direct3D11;
using Vortice.DXGI;

namespace JackAll.App.MapEditor.Gpu;

/// <summary>
/// Draws the world's authored polylines - zone outlines, paths and sound lines - as segments two
/// pixels wide. Sound lines are tinted apart from the rest so the two kinds are distinguishable.
/// </summary>
public sealed class ShapeLayer : IDisposable
{
    private ID3D11Buffer? _buffer;
    private ID3D11ShaderResourceView? _view;
    private int _segmentCount;

    public ShapeLayer(IReadOnlyList<WorldShape> shapes) => SetShapes(shapes);

    /// <summary>Replaces what the layer draws.</summary>
    public void SetShapes(IReadOnlyList<WorldShape> shapes)
    {
        // Each polyline becomes its own set of segments, so one buffer covers them all without
        // needing a draw call per shape.
        var vertices = new List<float>(shapes.Sum(s => s.Points.Count) * 12);
        foreach (WorldShape shape in shapes)
        {
            (float r, float g, float b) = TintFor(shape.Kind);
            for (int i = 0; i + 1 < shape.Points.Count; i++)
            {
                Append(vertices, shape.Points[i], r, g, b);
                Append(vertices, shape.Points[i + 1], r, g, b);
            }
        }
        _segmentCount = vertices.Count / 12;

        Release();
        if (_segmentCount > 0)
        {
            (_buffer, _view) = WideLines.Readable(vertices.ToArray(), Format.R32_Float);
        }
    }

    internal static (float R, float G, float B) TintFor(string kind) => kind switch
    {
        "road" => (0.85f, 0.65f, 0.30f),
        "river" => (0.30f, 0.55f, 0.95f),
        "path" => (0.70f, 0.45f, 0.85f),
        "sound" => (0.95f, 0.75f, 0.25f),
        "trigger" => (0.95f, 0.90f, 0.20f),
        "trigger-off" => (0.45f, 0.42f, 0.15f),
        "link" => (0.95f, 0.45f, 0.75f),
        "handle" => (0.35f, 0.95f, 0.85f),
        "handle-active" => (1f, 0.92f, 0.4f),
        _ => (0.35f, 0.85f, 0.95f),
    };

    private static void Append(List<float> into, Vector3 point, float r, float g, float b)
    {
        into.Add(point.X);
        into.Add(point.Y);
        into.Add(point.Z);
        into.Add(r);
        into.Add(g);
        into.Add(b);
    }

    public void Draw(Matrix4x4 viewProjection)
    {
        if (_view is null)
        {
            return;
        }

        using GpuState pass = new();
        GpuState.AlphaBlend(true);
        WideLines.Draw(viewProjection, new WideLines.Segments(
            _view, Stride: 6, _segmentCount, new Vector4(1f, 1f, 1f, 0.9f), Width: 2f, PerVertexColour: true));
    }

    private void Release()
    {
        _view?.Dispose();
        _buffer?.Dispose();
        _view = null;
        _buffer = null;
    }

    public void Dispose() => Release();
}
