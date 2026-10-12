using System.Numerics;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Media3D;
using JackAll.Tools.Hkx;
using JackAll.Tools.World;

namespace JackAll.App.FileHandlers.Hkx;

/// <summary>What the HKX editor's gizmo does to the selected shape.</summary>
public enum HkxTool { Move, Turn }

/// <summary>
/// The HKX editor's handles, in the Map tab's shapes and colours, drawn in a viewport of their own over the scene so
/// that nothing hides them: the move arrows and squares or the turn rings at the selected shape, and a knob on each
/// face of a selected box.
/// </summary>
internal sealed class HkxGizmo
{
    private static readonly MeshGeometry3D ArrowMesh = Soup(GizmoGeometry.Arrow()), SquareMesh = Soup(GizmoGeometry.Square()),
        RingMesh = Soup(GizmoGeometry.Ring()), KnobMesh = Knob();
    private static readonly Dictionary<(Vector3 Colour, float Alpha), Material> Materials = [];

    private readonly ModelVisual3D _visual = new();

    public HkxGizmo(Viewport3D overlay)
    {
        overlay.Children.Add(new ModelVisual3D { Content = new AmbientLight(Colors.White) });
        overlay.Children.Add(_visual);
    }

    public void Hide() => _visual.Content = null;

    /// <summary>
    /// Draws the tool at <paramref name="origin"/>, <paramref name="scale"/> long, with the held arm, square, ring or
    /// face lit, and a knob <paramref name="knob"/> across on each face handle.
    /// </summary>
    public void Show(HkxTool tool, Vector3 origin, float scale, GizmoAxis held, IReadOnlyList<EntityHandle> faces, int heldFace, float knob)
    {
        var group = new Model3DGroup();
        foreach (GizmoAxis axis in TranslateGizmo.Axes)
        {
            group.Children.Add(Part(tool == HkxTool.Move ? ArrowMesh : RingMesh, Material(axis, held, 1f, 1f), TranslateGizmo.Orientation(axis), scale, origin));
        }
        foreach (GizmoAxis plane in tool == HkxTool.Move ? TranslateGizmo.Planes : [])
        {
            group.Children.Add(Part(SquareMesh, Material(plane, held, GizmoGeometry.PlaneAlpha, GizmoGeometry.HeldPlaneAlpha),
                TranslateGizmo.PlaneOrientation(plane), scale, origin));
        }
        for (int i = 0; i < faces.Count; i++)
        {
            group.Children.Add(Part(KnobMesh, Material(i == heldFace ? GizmoGeometry.Highlight : Vector3.One, 1f), Matrix4x4.Identity, knob, faces[i].Point));
        }
        _visual.Content = group;
    }

    private static GeometryModel3D Part(MeshGeometry3D mesh, Material material, Matrix4x4 orientation, float scale, Vector3 at) => new(mesh, material)
    {
        BackMaterial = material,
        Transform = new MatrixTransform3D(Scene3D.Matrix(orientation * Matrix4x4.CreateScale(scale) * Matrix4x4.CreateTranslation(at))),
    };

    private static Material Material(GizmoAxis axis, GizmoAxis held, float alpha, float heldAlpha)
        => axis == held ? Material(GizmoGeometry.Highlight, heldAlpha) : Material(GizmoGeometry.Tint(axis), alpha);

    private static Material Material(Vector3 colour, float alpha)
    {
        if (!Materials.TryGetValue((colour, alpha), out Material? material))
        {
            material = new DiffuseMaterial(new SolidColorBrush(Color.FromArgb(Byte(alpha), Byte(colour.X), Byte(colour.Y), Byte(colour.Z))));
            material.Freeze();
            Materials[(colour, alpha)] = material;
        }
        return material;
    }

    private static byte Byte(float unit) => (byte)Math.Round(Math.Clamp(unit, 0, 1) * 255);

    // A list of triangle corners as a mesh.
    private static MeshGeometry3D Soup(Vector3[] corners)
    {
        var mesh = new MeshGeometry3D { Positions = [.. corners.Select(Scene3D.Point)], TriangleIndices = [.. Enumerable.Range(0, corners.Length)] };
        mesh.Freeze();
        return mesh;
    }

    // A unit cube about the origin.
    private static MeshGeometry3D Knob()
    {
        (Vector3[] corners, int[] triangles) = HkxShapes.BoxMesh(new Vector3(0.5f));
        var mesh = new MeshGeometry3D { Positions = [.. corners.Select(Scene3D.Point)], TriangleIndices = [.. triangles] };
        mesh.Freeze();
        return mesh;
    }
}
