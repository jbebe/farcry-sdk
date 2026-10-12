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
internal sealed class HkxScene
{
    private static readonly Dictionary<HkxShapeKind, Material> Solid = new()
    {
        [HkxShapeKind.Box] = Flat(Scene3D.Palette[0]),
        [HkxShapeKind.Hull] = Flat(Scene3D.Palette[1]),
        [HkxShapeKind.Mesh] = Flat(Scene3D.Palette[2]),
        [HkxShapeKind.Capsule] = Flat(Scene3D.Palette[4]),
        [HkxShapeKind.Sphere] = Flat(Scene3D.Palette[4]),
    };

    private static readonly Material Selected = new MaterialGroup
    {
        Children = { new DiffuseMaterial(new SolidColorBrush(Color.FromRgb(0xF0, 0xD0, 0x40))), new EmissiveMaterial(new SolidColorBrush(Color.FromRgb(0x60, 0x50, 0x10))) },
    };

    private readonly Viewport3D _viewport;
    private readonly ModelVisual3D _shapes = new();
    private readonly ModelVisual3D _ghost = new();
    private readonly SolidColorBrush _ghostBrush = new(Color.FromRgb(0xD8, 0xD8, 0xD8));
    private Dictionary<HkxPrimitive, GeometryModel3D> _built = [];
    private IReadOnlySet<HkxPrimitive> _selected = new HashSet<HkxPrimitive>();

    public HkxScene(Viewport3D viewport, IReadOnlyList<XbgSubmesh>? model)
    {
        _viewport = viewport;
        var material = new DiffuseMaterial(_ghostBrush);
        var ghost = new Model3DGroup();
        foreach (XbgSubmesh submesh in model ?? [])
        {
            ghost.Children.Add(new GeometryModel3D(Scene3D.Mesh(submesh), material) { BackMaterial = material });
        }
        _ghost.Content = ghost;
        viewport.Children.Clear();
        viewport.Children.Add(_shapes);
        // Last, so the translucent ghost blends over the shapes already drawn.
        viewport.Children.Add(_ghost);
    }

    /// <summary>How solid the model is drawn over the collision, from 0 (hidden) to 1.</summary>
    public double ModelOpacity
    {
        get => _ghostBrush.Opacity;
        set => _ghostBrush.Opacity = value;
    }

    public void Show(IEnumerable<HkxPrimitive> primitives)
    {
        Model3DGroup shapes = Scene3D.Lit();
        Dictionary<HkxPrimitive, GeometryModel3D> built = [];
        foreach (HkxPrimitive p in primitives)
        {
            built[p] = _built.GetValueOrDefault(p) ?? Model(p);
            shapes.Children.Add(built[p]);
        }
        _built = built;
        _shapes.Content = shapes;
    }

    public void Highlight(IReadOnlySet<HkxPrimitive> selected)
    {
        _selected = selected;
        foreach ((HkxPrimitive p, GeometryModel3D shape) in _built)
        {
            shape.Material = shape.BackMaterial = MaterialOf(p);
        }
    }

    /// <summary>The nearest collision shape under a point of the viewport, looking through the model.</summary>
    public HkxPrimitive? PrimitiveAt(Point at)
    {
        HkxPrimitive? found = null;
        VisualTreeHelper.HitTest(_viewport,
            o => o == _ghost ? HitTestFilterBehavior.ContinueSkipSelfAndChildren : HitTestFilterBehavior.Continue,
            result =>
            {
                if (result is RayMeshGeometry3DHitTestResult { ModelHit: GeometryModel3D hit })
                {
                    found = _built.FirstOrDefault(b => b.Value == hit).Key;
                }
                return found is null ? HitTestResultBehavior.Continue : HitTestResultBehavior.Stop;
            },
            new PointHitTestParameters(at));
        return found;
    }

    // Each triangle's own three corners, placed in the model, so WPF lights every face flat.
    private GeometryModel3D Model(HkxPrimitive p)
    {
        System.Numerics.Vector3[] placed = [.. p.ModelVertices()];
        var mesh = new MeshGeometry3D { Positions = new Point3DCollection(p.Triangles.Length) };
        foreach (int i in p.Triangles)
        {
            mesh.Positions.Add(Scene3D.Point(placed[i]));
        }
        mesh.Freeze();
        return new GeometryModel3D(mesh, MaterialOf(p)) { BackMaterial = MaterialOf(p) };
    }

    private Material MaterialOf(HkxPrimitive p) => _selected.Contains(p) ? Selected : Solid[p.Kind];

    private static Material Flat(Color colour)
    {
        var material = new DiffuseMaterial(new SolidColorBrush(colour));
        material.Freeze();
        return material;
    }
}
