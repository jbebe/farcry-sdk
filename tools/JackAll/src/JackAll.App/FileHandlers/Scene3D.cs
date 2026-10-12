using System.Numerics;
using System.Windows.Media;
using System.Windows.Media.Media3D;
using JackAll.Tools.Xbg;

namespace JackAll.App.FileHandlers;

/// <summary>The WPF 3D pieces the model previews share. Z is up, as in the mesh data.</summary>
public static class Scene3D
{
    public static Point3D Point(Vector3 v) => new(v.X, v.Y, v.Z);

    /// <summary>Ambient, a key light shining down, and a dim fill from below.</summary>
    public static Model3D[] Lights() =>
    [
        new AmbientLight(Color.FromRgb(0x60, 0x60, 0x60)),
        new DirectionalLight(Color.FromRgb(0xB0, 0xB0, 0xB0), new Vector3D(-0.5, -0.3, -0.8)),
        new DirectionalLight(Color.FromRgb(0x40, 0x40, 0x40), new Vector3D(0.6, 0.7, 0.2)),
    ];

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
