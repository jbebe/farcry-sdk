using System.Numerics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Media3D;

namespace JackAll.App.FileHandlers;

/// <summary>
/// A horizontal orbit around whatever a preview viewport frames: dragging turns it, the wheel zooms.
/// </summary>
/// <remarks>
/// Z is up, matching the mesh data - the map editor's world is X east, Y north, Z up and vertices
/// go into it unswapped, so anything treating Y as up lays the model on its side.
/// </remarks>
public sealed class OrbitViewport
{
    /// <summary>How far above the target the camera sits, in radians. Fixed: the orbit is
    /// horizontal-only, so nothing changes this.</summary>
    private const double Elevation = 0.35;

    private readonly Viewport3D _viewport;
    private readonly PerspectiveCamera _camera;
    private double _yaw = -0.7, _distance = 5, _near = 0.01, _far = 100;
    private Point3D _target;
    private Point _lastMouse;
    private bool _dragging;
    private (Vector3 Min, Vector3 Max)? _pending;

    public OrbitViewport(Viewport3D viewport)
    {
        _viewport = viewport;
        _camera = (PerspectiveCamera)viewport.Camera;
        viewport.MouseDown += OnMouseDown;
        viewport.MouseUp += OnMouseUp;
        viewport.MouseMove += OnMouseMove;
        viewport.MouseWheel += OnMouseWheel;
        viewport.SizeChanged += (_, _) =>
        {
            if (_pending is { } box)
            {
                Frame(box.Min, box.Max);
            }
        };
    }

    /// <summary>
    /// Puts the whole of a bounding box in view, from the default angle. Before the viewport has a
    /// size the fit waits for one, because the tighter of its two angles decides the distance.
    /// </summary>
    public void Frame(Vector3 min, Vector3 max)
    {
        if (_viewport.ActualHeight <= 0)
        {
            _pending = (min, max);
            return;
        }
        _pending = null;

        Vector3 center = (min + max) / 2f;
        float radius = Math.Max(0.01f, (max - min).Length() / 2f);
        // FieldOfView is the horizontal angle; the vertical one is narrower on a wide viewport.
        double halfWidth = Math.Tan(_camera.FieldOfView * Math.PI / 360);
        double halfHeight = halfWidth * _viewport.ActualHeight / _viewport.ActualWidth;

        _target = new Point3D(center.X, center.Y, center.Z);
        _distance = radius * 1.1 / Math.Min(halfWidth, halfHeight);
        _near = radius * 0.01;
        _far = radius * 20;
        _yaw = -0.7;
        Update();
    }

    private void Update()
    {
        double cy = Math.Cos(_yaw), sy = Math.Sin(_yaw);
        double cp = Math.Cos(Elevation), sp = Math.Sin(Elevation);
        var dir = new Vector3D(cy * cp, sy * cp, sp);
        _camera.Position = _target + dir * _distance;
        _camera.LookDirection = -dir;
        _camera.UpDirection = new Vector3D(0, 0, 1);
        _camera.NearPlaneDistance = _near;
        _camera.FarPlaneDistance = _far;
    }

    private void OnMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left)
        {
            return;
        }

        _dragging = true;
        _lastMouse = e.GetPosition(_viewport);
        _viewport.CaptureMouse();
    }

    private void OnMouseUp(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left)
        {
            return;
        }

        _dragging = false;
        _viewport.ReleaseMouseCapture();
    }

    private void OnMouseMove(object sender, MouseEventArgs e)
    {
        if (!_dragging)
        {
            return;
        }

        Point pos = e.GetPosition(_viewport);
        // Horizontal only - the model turns on the spot, and there is no elevation to lose track of.
        _yaw += (pos.X - _lastMouse.X) * 0.01;
        _lastMouse = pos;
        Update();
    }

    private void OnMouseWheel(object sender, MouseWheelEventArgs e)
    {
        double factor = Math.Pow(0.9, e.Delta / 120.0);
        _distance = Math.Clamp(_distance * factor, _near * 2, _far / 2);
        Update();
    }
}
