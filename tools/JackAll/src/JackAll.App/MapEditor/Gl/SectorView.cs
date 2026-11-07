using JackAll.Tools.World;
using OpenTK.Mathematics;

namespace JackAll.App.MapEditor.Gl;

/// <summary>
/// Which sectors the camera can see: each sector's column of the world, widened so a model that
/// overhangs its own sector still counts, tested against the six planes of the view.
/// </summary>
public sealed class SectorView
{
    private readonly Vector4[] _planes;
    private readonly float _floor;
    private readonly float _ceiling;
    private readonly float _margin;

    /// <param name="floor">The lowest anything stands.</param>
    /// <param name="ceiling">The highest anything reaches.</param>
    /// <param name="margin">How far past its own sector what a sector holds may reach.</param>
    public SectorView(Matrix4 viewProjection, float floor, float ceiling, float margin)
    {
        // Row vectors, so each clip coordinate is a column: inside is -w <= x, y, z <= w.
        Vector4 x = viewProjection.Column0, y = viewProjection.Column1, z = viewProjection.Column2;
        Vector4 w = viewProjection.Column3;
        _planes = [w + x, w - x, w + y, w - y, w + z, w - z];
        _floor = floor;
        _ceiling = ceiling;
        _margin = margin;
    }

    public bool Sees((int X, int Y) sector)
    {
        float minX = sector.X * WorldModels.SectorMeters - _margin;
        float minY = sector.Y * WorldModels.SectorMeters - _margin;
        float maxX = minX + WorldModels.SectorMeters + 2 * _margin;
        float maxY = minY + WorldModels.SectorMeters + 2 * _margin;
        foreach (Vector4 p in _planes)
        {
            // The box corner furthest along the plane's normal: if even that is outside, all of it is.
            float reach = p.X * (p.X > 0 ? maxX : minX) + p.Y * (p.Y > 0 ? maxY : minY)
                + p.Z * (p.Z > 0 ? _ceiling : _floor) + p.W;
            if (reach < 0)
            {
                return false;
            }
        }
        return true;
    }

    /// <summary>One bit per sector of the ring the view reaches, for comparing frames.</summary>
    public UInt128 Mask((int X, int Y) centre, int ring)
    {
        UInt128 mask = 0;
        int bit = 0;
        for (int y = centre.Y - ring; y <= centre.Y + ring; y++)
        {
            for (int x = centre.X - ring; x <= centre.X + ring; x++, bit++)
            {
                if (Sees((x, y)))
                {
                    mask |= UInt128.One << bit;
                }
            }
        }
        return mask;
    }
}
