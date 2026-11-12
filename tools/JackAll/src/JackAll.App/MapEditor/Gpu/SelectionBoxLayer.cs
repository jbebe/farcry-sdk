using System.Numerics;
using Vortice.Direct3D11;
using Vortice.DXGI;

namespace JackAll.App.MapEditor.Gpu;

/// <summary>
/// Draws the wireframe box around the selected entity. The cube's twelve edges live in the buffer
/// once and every selection is a model matrix over them, so switching selection uploads nothing.
/// </summary>
public sealed class SelectionBoxLayer : IDisposable
{
    private readonly ID3D11Buffer _buffer;
    private readonly ID3D11ShaderResourceView _view;

    public SelectionBoxLayer() => (_buffer, _view) = WideLines.Readable(UnitCubeEdges(), Format.R32_Float);

    /// <summary>Twelve edges as line pairs over the corners of a unit cube centred on the origin.</summary>
    private static float[] UnitCubeEdges()
    {
        var corner = new Vector3[8];
        for (int i = 0; i < 8; i++)
        {
            corner[i] = new Vector3(
                (i & 1) == 0 ? -0.5f : 0.5f,
                (i & 2) == 0 ? -0.5f : 0.5f,
                (i & 4) == 0 ? -0.5f : 0.5f);
        }

        int[] edges =
        [
            0, 1, 2, 3, 4, 5, 6, 7,
            0, 2, 1, 3, 4, 6, 5, 7,
            0, 4, 1, 5, 2, 6, 3, 7,
        ];

        var vertices = new float[edges.Length * 3];
        for (int i = 0; i < edges.Length; i++)
        {
            Vector3 v = corner[edges[i]];
            vertices[i * 3] = v.X;
            vertices[i * 3 + 1] = v.Y;
            vertices[i * 3 + 2] = v.Z;
        }

        return vertices;
    }

    /// <summary>Depth testing stays off so the box reads through whatever it encloses - a selection
    /// you cannot see inside a wall is the thing this exists to fix.</summary>
    public void Draw(Matrix4x4 viewProjection, Matrix4x4 model, Vector3 tint)
    {
        using GpuState pass = new();
        GpuState.Depth(DepthMode.Off);
        WideLines.Draw(model * viewProjection, new WideLines.Segments(
            _view, Stride: 3, Count: 12, new Vector4(tint, 1f), Width: 2f));
    }

    public void Dispose()
    {
        _view.Dispose();
        _buffer.Dispose();
    }
}
