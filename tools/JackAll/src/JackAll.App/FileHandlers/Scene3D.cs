using System.Numerics;
using System.Windows.Media;
using System.Windows.Media.Media3D;
using JackAll.Tools.Xbg;

namespace JackAll.App.FileHandlers;

/// <summary>The WPF 3D pieces the model previews share. Z is up, as in the mesh data.</summary>
public static class Scene3D
{
    /// <summary>Colours that tell parts apart, since a preview draws no textures.</summary>
    public static readonly Color[] Palette =
    [
        Color.FromRgb(0xC9, 0x6A, 0x4A), Color.FromRgb(0x4A, 0x8F, 0xC9), Color.FromRgb(0x6A, 0xC9, 0x4A),
        Color.FromRgb(0xC9, 0xB0, 0x4A), Color.FromRgb(0x9A, 0x4A, 0xC9), Color.FromRgb(0x4A, 0xC9, 0xB0),
        Color.FromRgb(0xC9, 0x4A, 0x8F), Color.FromRgb(0x8F, 0xC9, 0x4A),
    ];

    public static Point3D Point(Vector3 v) => new(v.X, v.Y, v.Z);

    public static Vector3 Vec(Point3D p) => new((float)p.X, (float)p.Y, (float)p.Z);

    public static Vector3 Vec(Vector3D v) => new((float)v.X, (float)v.Y, (float)v.Z);

    public static Matrix3D Matrix(Matrix4x4 m)
        => new(m.M11, m.M12, m.M13, m.M14, m.M21, m.M22, m.M23, m.M24, m.M31, m.M32, m.M33, m.M34, m.M41, m.M42, m.M43, m.M44);

    /// <summary>A group lit by ambient light, a key light shining down and a dim fill from below.</summary>
    public static Model3DGroup Lit() => new()
    {
        Children =
        {
            new AmbientLight(Color.FromRgb(0x60, 0x60, 0x60)),
            new DirectionalLight(Color.FromRgb(0xB0, 0xB0, 0xB0), new Vector3D(-0.5, -0.3, -0.8)),
            new DirectionalLight(Color.FromRgb(0x40, 0x40, 0x40), new Vector3D(0.6, 0.7, 0.2)),
        },
    };

    /// <summary>A submesh placed in model space, with smooth normals where the file stores none.</summary>
    public static MeshGeometry3D Mesh(XbgSubmesh submesh)
    {
        var mesh = new MeshGeometry3D { Positions = new Point3DCollection(submesh.Positions.Length) };
        foreach (Vector3 p in submesh.Positions)
        {
            mesh.Positions.Add(Point(submesh.Place(p)));
        }

        Vector3[] normals = submesh.Normals ?? XbgModel.ComputeSmoothNormals(submesh.Positions, submesh.Indices);
        mesh.Normals = new Vector3DCollection(normals.Length);
        foreach (Vector3 n in normals)
        {
            Vector3 placed = submesh.PlaceNormal(n);
            mesh.Normals.Add(new Vector3D(placed.X, placed.Y, placed.Z));
        }

        mesh.TriangleIndices = new Int32Collection(submesh.Indices);
        return mesh;
    }
}
