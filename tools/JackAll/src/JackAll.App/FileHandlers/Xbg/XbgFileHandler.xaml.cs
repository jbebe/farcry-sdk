using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Media3D;
using JackAll.Core.Format;

namespace JackAll.App.FileHandlers.Xbg;

/// <summary>
/// The file handler for .xbg meshes - a read-only 3D geometry preview. Parses the Far Cry 2
/// chunk layout via <see cref="XbgModel"/> (positions/normals/triangles per LOD, no skinning or
/// textures - see that class's remarks) and renders the selected LOD in an orbitable Viewport3D, one
/// flat-coloured material per submesh so distinct parts/materials are visually distinguishable without
/// needing the game's actual textures.
/// </summary>
public partial class XbgFileHandler : UserControl
{
    // A small fixed palette rather than per-material texture lookups - this preview has no access to
    // the game's actual material colours/textures, just enough visual separation to tell parts apart.
    private static readonly Color[] Palette =
    [
        Color.FromRgb(0xC9, 0x6A, 0x4A), Color.FromRgb(0x4A, 0x8F, 0xC9), Color.FromRgb(0x6A, 0xC9, 0x4A),
        Color.FromRgb(0xC9, 0xB0, 0x4A), Color.FromRgb(0x9A, 0x4A, 0xC9), Color.FromRgb(0x4A, 0xC9, 0xB0),
        Color.FromRgb(0xC9, 0x4A, 0x8F), Color.FromRgb(0x8F, 0xC9, 0x4A),
    ];

    private XbgModel? _model;
    private int _selectedLod;

    private double _yaw = -0.7, _pitch = 0.35, _distance = 5;
    private Point3D _target;
    private double _near = 0.01, _far = 100;
    private Point _lastMouse;
    private bool _dragging;

    public XbgFileHandler(string fileName, byte[] content)
    {
        InitializeComponent();
        Load(fileName, content);
    }

    private void Load(string fileName, byte[] content)
    {
        try
        {
            XbgModel model = XbgModel.Parse(content);
            _model = model;

            if (model.Submeshes.Count == 0)
            {
                StatusText.Text = $"{fileName}\n\nParsed the header but found no renderable geometry " +
                                   "(the DNKS submesh table didn't match this file's layout, or the mesh is empty).";
                Toolbar.Visibility = Visibility.Collapsed;
                Viewport.Children.Clear();
                return;
            }

            LodCombo.ItemsSource = model.LodLevels;
            LodCombo.SelectedItem = model.LodLevels[0];
            Toolbar.Visibility = Visibility.Visible;
            // SelectionChanged (below) does the rest: status text + scene build + camera frame.
        }
        catch (Exception ex)
        {
            _model = null;
            StatusText.Text = $"Couldn't read this file: {ex.Message}";
            Toolbar.Visibility = Visibility.Collapsed;
            Viewport.Children.Clear();
        }
    }

    private void LodCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_model is null || LodCombo.SelectedItem is not int lod)
        {
            return;
        }

        _selectedLod = lod;
        List<XbgSubmesh> submeshes = _model.Submeshes.Where(s => s.LodLevel == lod).ToList();

        var sb = new StringBuilder();
        int totalVerts = submeshes.Select(s => s.Positions).Distinct().Sum(p => p.Length);
        int totalTris = submeshes.Sum(s => s.Indices.Length / 3);
        int parts = submeshes.Select(s => s.PartNumber).Distinct().Count();
        sb.AppendLine($"LOD {lod}: {parts} part(s), {submeshes.Count} submesh(es), " +
                       $"{totalVerts:N0} vertices, {totalTris:N0} triangles");
        foreach (string mat in submeshes.Select(s => s.MaterialName).Distinct().OrderBy(m => m))
        {
            sb.AppendLine($"  - {mat}");
        }

        StatusText.Text = sb.ToString().TrimEnd();

        BuildScene(submeshes);
        FrameCamera(submeshes);
    }

    private void ResetViewButton_Click(object sender, RoutedEventArgs e)
    {
        if (_model is null)
        {
            return;
        }

        FrameCamera(_model.Submeshes.Where(s => s.LodLevel == _selectedLod).ToList());
    }

    private void BuildScene(List<XbgSubmesh> submeshes)
    {
        var root = new Model3DGroup();
        root.Children.Add(new AmbientLight(Color.FromRgb(0x60, 0x60, 0x60)));
        root.Children.Add(new DirectionalLight(Color.FromRgb(0xB0, 0xB0, 0xB0), new Vector3D(-0.5, -0.8, -0.3)));
        root.Children.Add(new DirectionalLight(Color.FromRgb(0x40, 0x40, 0x40), new Vector3D(0.6, 0.2, 0.7)));

        foreach (XbgSubmesh sm in submeshes)
        {
            var mesh = new MeshGeometry3D { Positions = new Point3DCollection(sm.Positions.Length) };
            foreach (System.Numerics.Vector3 p in sm.Positions)
            {
                mesh.Positions.Add(new Point3D(p.X, p.Y, p.Z));
            }

            System.Numerics.Vector3[] normals = sm.Normals ?? ComputeSmoothNormals(sm.Positions, sm.Indices);
            mesh.Normals = new Vector3DCollection(normals.Length);
            foreach (System.Numerics.Vector3 n in normals)
            {
                mesh.Normals.Add(new Vector3D(n.X, n.Y, n.Z));
            }

            mesh.TriangleIndices = new Int32Collection(sm.Indices);

            Color color = Palette[((sm.MaterialIndex % Palette.Length) + Palette.Length) % Palette.Length];
            var material = new MaterialGroup();
            material.Children.Add(new DiffuseMaterial(new SolidColorBrush(color)));
            material.Children.Add(new SpecularMaterial(new SolidColorBrush(Color.FromArgb(0x30, 0xFF, 0xFF, 0xFF)), 24));

            root.Children.Add(new GeometryModel3D(mesh, material) { BackMaterial = material });
        }

        Viewport.Children.Clear();
        Viewport.Children.Add(new ModelVisual3D { Content = root });
    }

    /// <summary>Used when the file has no NORMAL vertex component: accumulate each triangle's face
    /// normal into its three vertices and normalise, so shading still reads as a solid rather than flat
    /// per-face facets.</summary>
    private static System.Numerics.Vector3[] ComputeSmoothNormals(System.Numerics.Vector3[] positions, int[] indices)
    {
        var normals = new System.Numerics.Vector3[positions.Length];
        for (int i = 0; i + 2 < indices.Length; i += 3)
        {
            int a = indices[i], b = indices[i + 1], c = indices[i + 2];
            System.Numerics.Vector3 faceNormal = System.Numerics.Vector3.Cross(
                positions[b] - positions[a], positions[c] - positions[a]);
            normals[a] += faceNormal;
            normals[b] += faceNormal;
            normals[c] += faceNormal;
        }

        for (int i = 0; i < normals.Length; i++)
        {
            normals[i] = normals[i] == System.Numerics.Vector3.Zero
                ? System.Numerics.Vector3.UnitY
                : System.Numerics.Vector3.Normalize(normals[i]);
        }

        return normals;
    }

    private void FrameCamera(List<XbgSubmesh> submeshes)
    {
        var min = new System.Numerics.Vector3(float.MaxValue);
        var max = new System.Numerics.Vector3(float.MinValue);
        foreach (XbgSubmesh sm in submeshes)
        {
            foreach (System.Numerics.Vector3 p in sm.Positions)
            {
                min = System.Numerics.Vector3.Min(min, p);
                max = System.Numerics.Vector3.Max(max, p);
            }
        }

        if (min.X > max.X)
        {
            min = max = System.Numerics.Vector3.Zero;
        }

        System.Numerics.Vector3 center = (min + max) / 2f;
        float radius = Math.Max(0.01f, (max - min).Length() / 2f);

        _target = new Point3D(center.X, center.Y, center.Z);
        _distance = radius * 2.5;
        _near = radius * 0.01;
        _far = radius * 20;
        _yaw = -0.7;
        _pitch = 0.35;
        UpdateCamera();
    }

    private void UpdateCamera()
    {
        double cy = Math.Cos(_yaw), sy = Math.Sin(_yaw);
        double cp = Math.Cos(_pitch), sp = Math.Sin(_pitch);
        var dir = new Vector3D(cy * cp, sp, sy * cp);
        Camera.Position = _target + dir * _distance;
        Camera.LookDirection = -dir;
        Camera.UpDirection = new Vector3D(0, 1, 0);
        Camera.NearPlaneDistance = _near;
        Camera.FarPlaneDistance = _far;
    }

    private void Viewport_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left)
        {
            return;
        }

        _dragging = true;
        _lastMouse = e.GetPosition(Viewport);
        Viewport.CaptureMouse();
    }

    private void Viewport_MouseUp(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left)
        {
            return;
        }

        _dragging = false;
        Viewport.ReleaseMouseCapture();
    }

    private void Viewport_MouseMove(object sender, MouseEventArgs e)
    {
        if (!_dragging || _model is null)
        {
            return;
        }

        Point pos = e.GetPosition(Viewport);
        Vector delta = pos - _lastMouse;
        _lastMouse = pos;

        _yaw += delta.X * 0.01;
        _pitch = Math.Clamp(_pitch - delta.Y * 0.01, -1.5, 1.5);
        UpdateCamera();
    }

    private void Viewport_MouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (_model is null)
        {
            return;
        }

        double factor = Math.Pow(0.9, e.Delta / 120.0);
        _distance = Math.Clamp(_distance * factor, _near * 2, _far / 2);
        UpdateCamera();
    }
}
