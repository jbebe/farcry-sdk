using System.Numerics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Media3D;
using JackAll.Tools.World;

namespace JackAll.App.FileHandlers;

/// <summary>
/// An orbit around whatever a preview viewport frames: dragging turns it, the wheel zooms, and a click that
/// turned nothing is reported as <see cref="Clicked"/>. With tilt, dragging up and down raises and lowers the
/// camera as well.
/// </summary>
/// <remarks>
/// Z is up, matching the mesh data - the map editor's world is X east, Y north, Z up and vertices
/// go into it unswapped, so anything treating Y as up lays the model on its side.
/// </remarks>
public sealed class OrbitViewport
{
    /// <summary>How far above the target the camera starts, in radians.</summary>
    private const double DefaultElevation = 0.35;

    /// <summary>Short of straight up or down, where the up direction would flip.</summary>
    private const double MaxElevation = 1.5;

    private readonly Viewport3D _viewport;
    private readonly UIElement _input;
    private readonly PerspectiveCamera _camera;
    private readonly bool _tilt;
    private readonly List<PerspectiveCamera> _overlays = [];
    private double _yaw = -0.7, _elevation = DefaultElevation, _distance = 5, _near = 0.01, _far = 100;
    private Point3D _target;
    private Point _lastMouse, _pressedAt;
    private bool _dragging;
    private (Vector3 Min, Vector3 Max)? _pending;

    public OrbitViewport(Viewport3D viewport, bool tilt = false)
    {
        _viewport = viewport;
        _tilt = tilt;
        _camera = (PerspectiveCamera)viewport.Camera;
        // A Viewport3D only sees the mouse over what it draws; the frame around it sees it everywhere.
        _input = viewport.Parent as UIElement ?? viewport;
        _input.MouseDown += OnMouseDown;
        _input.MouseUp += OnMouseUp;
        _input.MouseMove += OnMouseMove;
        _input.MouseWheel += OnMouseWheel;
        viewport.SizeChanged += (_, _) =>
        {
            if (_pending is { } box)
            {
                Frame(box.Min, box.Max);
            }
        };
    }

    /// <summary>A left click that did not turn the view, at its position in the viewport.</summary>
    public event Action<Point>? Clicked;

    /// <summary>Raised whenever the camera moves.</summary>
    public event Action? Moved;

    /// <summary>Has <paramref name="overlay"/> look through a camera that follows this one, so what it draws lies over the view.</summary>
    public void Overlay(Viewport3D overlay)
    {
        var camera = new PerspectiveCamera();
        overlay.Camera = camera;
        _overlays.Add(camera);
        Update();
    }

    /// <summary>The ray from the camera through a point of the viewport.</summary>
    public (Vector3 Origin, Vector3 Direction) Ray(Point at)
    {
        Vector3 forward = Vector3.Normalize(Scene3D.Vec(_camera.LookDirection));
        Vector3 right = Vector3.Normalize(Vector3.Cross(forward, Scene3D.Vec(_camera.UpDirection)));
        Vector3 up = Vector3.Cross(right, forward);
        (double halfWidth, double halfHeight) = HalfAngles();
        float x = (float)((2 * at.X / _viewport.ActualWidth - 1) * halfWidth), y = (float)((1 - 2 * at.Y / _viewport.ActualHeight) * halfHeight);
        return (Scene3D.Vec(_camera.Position), Vector3.Normalize(forward + right * x + up * y));
    }

    /// <summary>The length that spans <paramref name="pixels"/> of the viewport's height at <paramref name="at"/>.</summary>
    public float Scale(Vector3 at, float pixels)
        => TranslateGizmo.Scale(at, Scene3D.Vec(_camera.Position), (float)(2 * Math.Atan(HalfAngles().HalfHeight)), (float)_viewport.ActualHeight, pixels);

    // The tangents of half the view's horizontal and vertical angles; FieldOfView is the horizontal one.
    private (double HalfWidth, double HalfHeight) HalfAngles()
    {
        double halfWidth = Math.Tan(_camera.FieldOfView * Math.PI / 360);
        return (halfWidth, halfWidth * _viewport.ActualHeight / Math.Max(_viewport.ActualWidth, 1));
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
        (double halfWidth, double halfHeight) = HalfAngles();

        _target = Scene3D.Point(center);
        _distance = radius * 1.1 / Math.Min(halfWidth, halfHeight);
        _near = radius * 0.01;
        _far = radius * 20;
        _yaw = -0.7;
        _elevation = DefaultElevation;
        Update();
    }

    private void Update()
    {
        double cy = Math.Cos(_yaw), sy = Math.Sin(_yaw);
        double cp = Math.Cos(_elevation), sp = Math.Sin(_elevation);
        var dir = new Vector3D(cy * cp, sy * cp, sp);
        _camera.Position = _target + dir * _distance;
        _camera.LookDirection = -dir;
        _camera.UpDirection = new Vector3D(0, 0, 1);
        _camera.NearPlaneDistance = _near;
        _camera.FarPlaneDistance = _far;
        foreach (PerspectiveCamera overlay in _overlays)
        {
            (overlay.Position, overlay.LookDirection, overlay.UpDirection) = (_camera.Position, _camera.LookDirection, _camera.UpDirection);
            (overlay.FieldOfView, overlay.NearPlaneDistance, overlay.FarPlaneDistance) = (_camera.FieldOfView, _near, _far);
        }
        Moved?.Invoke();
    }

    private void OnMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left)
        {
            return;
        }

        _dragging = true;
        _lastMouse = _pressedAt = e.GetPosition(_viewport);
        _input.CaptureMouse();
    }

    private void OnMouseUp(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left || !_dragging)
        {
            return;
        }

        _dragging = false;
        _input.ReleaseMouseCapture();
        Point at = e.GetPosition(_viewport);
        if (Math.Abs(at.X - _pressedAt.X) < SystemParameters.MinimumHorizontalDragDistance
            && Math.Abs(at.Y - _pressedAt.Y) < SystemParameters.MinimumVerticalDragDistance)
        {
            Clicked?.Invoke(at);
        }
    }

    private void OnMouseMove(object sender, MouseEventArgs e)
    {
        if (!_dragging)
        {
            return;
        }

        Point pos = e.GetPosition(_viewport);
        _yaw += (pos.X - _lastMouse.X) * 0.01;
        if (_tilt)
        {
            _elevation = Math.Clamp(_elevation + (pos.Y - _lastMouse.Y) * 0.01, -MaxElevation, MaxElevation);
        }
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
