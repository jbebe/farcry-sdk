using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Media3D;
using JackAll.Tools.Hkx;
using JackAll.Tools.Xbg;

namespace JackAll.App.FileHandlers.Hkx;

/// <summary>
/// The HKX editor's 3D view: each collision shape a solid in its kind's colour, the model a see-through
/// ghost drawn over them, and the selected shapes lit up.
/// </summary>
internal sealed class HkxScene(Viewport3D viewport)
{
    private static readonly Dictionary<HkxShapeKind, Material> Solid = new()
    {
        [HkxShapeKind.Box] = Flat(Color.FromRgb(0xC9, 0x6A, 0x4A)),
        [HkxShapeKind.Hull] = Flat(Color.FromRgb(0x4A, 0x8F, 0xC9)),
        [HkxShapeKind.Mesh] = Flat(Color.FromRgb(0x6A, 0xC9, 0x4A)),
        [HkxShapeKind.Capsule] = Flat(Color.FromRgb(0x9A, 0x4A, 0xC9)),
    };

    private static readonly Material Selected = new MaterialGroup
    {
        Children = { new DiffuseMaterial(new SolidColorBrush(Color.FromRgb(0xF0, 0xD0, 0x40))), new EmissiveMaterial(new SolidColorBrush(Color.FromRgb(0x60, 0x50, 0x10))) },
    };

    private readonly Dictionary<GeometryModel3D, HkxPrimitive> _shapes = [];
    private readonly SolidColorBrush _ghost = new(Color.FromRgb(0xD8, 0xD8, 0xD8)) { Opacity = 0.35 };

    /// <summary>How solid the model is drawn over the collision, from 0 (hidden) to 1.</summary>
    public double ModelOpacity
    {
        get => _ghost.Opacity;
        set => _ghost.Opacity = value;
    }

    public void Show(IReadOnlyList<XbgSubmesh>? model, IEnumerable<HkxPrimitive> primitives)
    {
        _shapes.Clear();
        var shapes = new Model3DGroup();
        foreach (Model3D light in Scene3D.Lights())
        {
            shapes.Children.Add(light);
        }
        foreach (HkxPrimitive p in primitives)
        {
            var shape = new GeometryModel3D(Faces(p), Solid[p.Kind]) { BackMaterial = Solid[p.Kind], Transform = new MatrixTransform3D(Matrix(p.ToModel)) };
            _shapes[shape] = p;
            shapes.Children.Add(shape);
        }

        viewport.Children.Clear();
        viewport.Children.Add(new ModelVisual3D { Content = shapes });
        if (model is not null)
        {
            // Last, so the translucent ghost blends over the shapes already drawn.
            var ghost = new Model3DGroup();
            var material = new DiffuseMaterial(_ghost);
            foreach (XbgSubmesh submesh in model)
            {
                ghost.Children.Add(new GeometryModel3D(Scene3D.Mesh(submesh), material) { BackMaterial = material });
            }
            viewport.Children.Add(new ModelVisual3D { Content = ghost });
        }
    }

    public void Highlight(IReadOnlySet<HkxPrimitive> selected)
    {
        foreach ((GeometryModel3D shape, HkxPrimitive p) in _shapes)
        {
            shape.Material = shape.BackMaterial = selected.Contains(p) ? Selected : Solid[p.Kind];
        }
    }

    /// <summary>The nearest collision shape under a point of the viewport, looking through the model.</summary>
    public HkxPrimitive? PrimitiveAt(Point at)
    {
        HkxPrimitive? found = null;
        VisualTreeHelper.HitTest(viewport, null, result =>
        {
            if (result is RayMeshGeometry3DHitTestResult { ModelHit: GeometryModel3D model } && _shapes.TryGetValue(model, out HkxPrimitive? p))
            {
                found = p;
                return HitTestResultBehavior.Stop;
            }
            return HitTestResultBehavior.Continue;
        }, new PointHitTestParameters(at));
        return found;
    }

    // Each triangle's own three corners, so WPF lights every face flat.
    private static MeshGeometry3D Faces(HkxPrimitive p)
    {
        var mesh = new MeshGeometry3D { Positions = new Point3DCollection(p.Triangles.Length) };
        foreach (int i in p.Triangles)
        {
            mesh.Positions.Add(Scene3D.Point(p.Vertices[i]));
        }
        return mesh;
    }

    private static Matrix3D Matrix(System.Numerics.Matrix4x4 m)
        => new(m.M11, m.M12, m.M13, m.M14, m.M21, m.M22, m.M23, m.M24, m.M31, m.M32, m.M33, m.M34, m.M41, m.M42, m.M43, m.M44);

    private static Material Flat(Color colour)
    {
        var material = new DiffuseMaterial(new SolidColorBrush(colour));
        material.Freeze();
        return material;
    }
}
