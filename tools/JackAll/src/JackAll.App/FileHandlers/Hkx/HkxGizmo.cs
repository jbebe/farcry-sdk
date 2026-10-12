using System.Numerics;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Media3D;
using JackAll.Tools.World;

namespace JackAll.App.FileHandlers.Hkx;

/// <summary>What the HKX editor's gizmo does to the selected shape.</summary>
public enum HkxTool { Move, Turn }

/// <summary>
/// The HKX editor's handles, drawn in a viewport of their own over the scene so that nothing hides them: the move
/// arrows and squares or the turn rings at the selected shape, and a knob on each face of a selected box.
/// </summary>
internal sealed class HkxGizmo
{
    private const float ShaftWidth = 0.015f, HeadStart = 0.8f, HeadWidth = 0.05f, Tube = 0.015f;
    private const int RingSegments = 64, TubeSegments = 8;

    private static readonly MeshGeometry3D ArrowMesh = Arrow(), RingMesh = Ring(), SquareMesh = Square(), KnobMesh = Cuboid(new Vector3(-0.5f), new Vector3(0.5f));
    private static readonly Color Held = Color.FromRgb(0xF0, 0xD0, 0x40);
    private static readonly Dictionary<GizmoAxis, Color> Colours = new()
    {
        [GizmoAxis.X] = Color.FromRgb(0xE0, 0x50, 0x50),
        [GizmoAxis.Y] = Color.FromRgb(0x60, 0xC0, 0x60),
        [GizmoAxis.Z] = Color.FromRgb(0x50, 0x80, 0xE0),
        [GizmoAxis.PlaneYZ] = Color.FromArgb(0x90, 0xE0, 0x50, 0x50),
        [GizmoAxis.PlaneXZ] = Color.FromArgb(0x90, 0x60, 0xC0, 0x60),
        [GizmoAxis.PlaneXY] = Color.FromArgb(0x90, 0x50, 0x80, 0xE0),
    };

    private readonly ModelVisual3D _visual = new();

    public HkxGizmo(Viewport3D overlay)
    {
        overlay.Children.Add(new ModelVisual3D { Content = new AmbientLight(Colors.White) });
        overlay.Children.Add(_visual);
    }

    public void Hide() => _visual.Content = null;

    /// <summary>
    /// Draws the tool at <paramref name="origin"/>, <paramref name="scale"/> long, with the held handle lit, and a
    /// knob <paramref name="knob"/> across on each face handle.
    /// </summary>
    public void Show(HkxTool? tool, Vector3 origin, float scale, GizmoAxis held, IReadOnlyList<EntityHandle> faces, EntityHandle? heldFace, float knob)
    {
        var group = new Model3DGroup();
        if (tool == HkxTool.Move)
        {
            foreach (GizmoAxis axis in TranslateGizmo.Axes)
            {
                group.Children.Add(Part(ArrowMesh, Colour(axis, held), TranslateGizmo.Orientation(axis), scale, origin));
            }
            foreach (GizmoAxis plane in TranslateGizmo.Planes)
            {
                group.Children.Add(Part(SquareMesh, Colour(plane, held), TranslateGizmo.PlaneOrientation(plane), scale, origin));
            }
        }
        else if (tool == HkxTool.Turn)
        {
            foreach (GizmoAxis axis in TranslateGizmo.Axes)
            {
                group.Children.Add(Part(RingMesh, Colour(axis, held), TranslateGizmo.Orientation(axis), scale, origin));
            }
        }
        foreach (EntityHandle face in faces)
        {
            group.Children.Add(Part(KnobMesh, face == heldFace ? Held : Colors.White, Matrix4x4.Identity, knob, face.Point));
        }
        _visual.Content = group;
    }

    private static Color Colour(GizmoAxis axis, GizmoAxis held) => axis == held ? Held : Colours[axis];

    private static GeometryModel3D Part(MeshGeometry3D mesh, Color colour, Matrix4x4 orientation, float scale, Vector3 at)
    {
        var material = new DiffuseMaterial(new SolidColorBrush(colour));
        return new GeometryModel3D(mesh, material)
        {
            BackMaterial = material,
            Transform = new MatrixTransform3D(Scene3D.Matrix(orientation * Matrix4x4.CreateScale(scale) * Matrix4x4.CreateTranslation(at))),
        };
    }

    // A shaft up +Z to a four-sided head whose tip is at 1.
    private static MeshGeometry3D Arrow()
    {
        MeshGeometry3D mesh = Cuboid(new Vector3(-ShaftWidth, -ShaftWidth, 0), new Vector3(ShaftWidth, ShaftWidth, HeadStart), freeze: false);
        int tip = mesh.Positions.Count;
        mesh.Positions.Add(new Point3D(0, 0, 1));
        foreach ((float x, float y) in new[] { (-1f, -1f), (1f, -1f), (1f, 1f), (-1f, 1f) })
        {
            mesh.Positions.Add(new Point3D(x * HeadWidth, y * HeadWidth, HeadStart));
        }
        for (int i = 0; i < 4; i++)
        {
            mesh.TriangleIndices.Add(tip);
            mesh.TriangleIndices.Add(tip + 1 + i);
            mesh.TriangleIndices.Add(tip + 1 + (i + 1) % 4);
        }
        mesh.TriangleIndices.Add(tip + 1);
        mesh.TriangleIndices.Add(tip + 3);
        mesh.TriangleIndices.Add(tip + 2);
        mesh.TriangleIndices.Add(tip + 1);
        mesh.TriangleIndices.Add(tip + 4);
        mesh.TriangleIndices.Add(tip + 3);
        mesh.Freeze();
        return mesh;
    }

    // A thin torus of radius 1 round +Z.
    private static MeshGeometry3D Ring()
    {
        var mesh = new MeshGeometry3D();
        for (int i = 0; i < RingSegments; i++)
        {
            double around = 2 * Math.PI * i / RingSegments;
            for (int j = 0; j < TubeSegments; j++)
            {
                double tube = 2 * Math.PI * j / TubeSegments;
                double r = 1 + Tube * Math.Cos(tube);
                mesh.Positions.Add(new Point3D(r * Math.Cos(around), r * Math.Sin(around), Tube * Math.Sin(tube)));
            }
        }
        for (int i = 0; i < RingSegments; i++)
        {
            for (int j = 0; j < TubeSegments; j++)
            {
                int a = i * TubeSegments + j, b = (i + 1) % RingSegments * TubeSegments + j;
                int c = (i + 1) % RingSegments * TubeSegments + (j + 1) % TubeSegments, d = i * TubeSegments + (j + 1) % TubeSegments;
                foreach (int k in new[] { a, b, c, a, c, d })
                {
                    mesh.TriangleIndices.Add(k);
                }
            }
        }
        mesh.Freeze();
        return mesh;
    }

    // The move gizmo's square between two arms, in the XY plane.
    private static MeshGeometry3D Square()
    {
        float lo = TranslateGizmo.PlaneStart, hi = TranslateGizmo.PlaneEnd;
        var mesh = new MeshGeometry3D
        {
            Positions = [new Point3D(lo, lo, 0), new Point3D(hi, lo, 0), new Point3D(hi, hi, 0), new Point3D(lo, hi, 0)],
            TriangleIndices = [0, 1, 2, 0, 2, 3],
        };
        mesh.Freeze();
        return mesh;
    }

    private static MeshGeometry3D Cuboid(Vector3 min, Vector3 max, bool freeze = true)
    {
        var mesh = new MeshGeometry3D();
        for (int i = 0; i < 8; i++)
        {
            mesh.Positions.Add(new Point3D((i & 1) != 0 ? max.X : min.X, (i & 2) != 0 ? max.Y : min.Y, (i & 4) != 0 ? max.Z : min.Z));
        }
        foreach (int k in new[] { 4, 5, 7, 4, 7, 6, 0, 2, 3, 0, 3, 1, 1, 3, 7, 1, 7, 5, 0, 4, 6, 0, 6, 2, 2, 6, 7, 2, 7, 3, 0, 1, 5, 0, 5, 4 })
        {
            mesh.TriangleIndices.Add(k);
        }
        if (freeze)
        {
            mesh.Freeze();
        }
        return mesh;
    }
}
