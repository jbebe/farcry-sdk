using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using JackAll.App.MapEditor.Gl;
using JackAll.Tools.World;
using OpenTK.Graphics.OpenGL4;
using OpenTK.Wpf;

namespace JackAll.App.MapEditor;

/// <summary>
/// The map editor tab: layer list, per-layer context panel, 3D viewport. Currently read-only
/// terrain (L1) - loading a world streams its 6400 sdat sectors into one heightfield rendered by a
/// fly camera; each editing phase replaces one layer's mock context with its real panel. GL objects
/// are created and destroyed only inside <see cref="Viewport_Render"/>, the one place a context is
/// current.
/// </summary>
public partial class MapTabView : UserControl
{
    private MainViewModel? _vm;

    private WorldTerrain? _pendingTerrain;
    private WorldTerrain? _terrain;
    private HeightTexture? _heightTexture;
    private TerrainMesh3D? _terrainMesh;

    private readonly Camera3D _camera = new();
    private readonly HashSet<Key> _flyKeys = [];
    private bool _looking;
    private System.Windows.Point _lastDragPoint;

    public MapTabView()
    {
        InitializeComponent();

        var view = new ListCollectionView(LayerCatalog.Layers);
        view.GroupDescriptions.Add(new PropertyGroupDescription(nameof(MapLayer.Group)));
        LayerList.ItemsSource = view;
        LayerList.SelectedItem = LayerCatalog.Layers[0];

        WorldPicker.ItemsSource = Fc2WorldGrid.SpWorldNames;
        WorldPicker.SelectedIndex = 0;
        Viewport.Start(new GLWpfControlSettings { MajorVersion = 3, MinorVersion = 3 });
    }

    /// <summary>Called by MainWindow once the VFS is loaded and worlds become readable.</summary>
    public void Initialize(MainViewModel vm)
    {
        _vm = vm;
        WorldPicker.IsEnabled = true;
        LoadButton.IsEnabled = true;
        StatusText.Text = "Pick a world and Load";
    }

    private async void Load_Click(object sender, System.Windows.RoutedEventArgs e)
    {
        if (_vm is null) return;

        string worldName = (string)WorldPicker.SelectedItem;
        LoadButton.IsEnabled = false;
        try
        {
            var progress = new Progress<string>(s => StatusText.Text = s);
            MainViewModel vm = _vm;
            WorldTerrain terrain = await Task.Run(() =>
                WorldTerrain.Load(WorldPaths.ForWorld(worldName, vm.AllKnownPaths), vm.ReadByPath, progress));

            _pendingTerrain = terrain;
            int center = WorldTerrain.Side / 2;
            _camera.Position = new OpenTK.Mathematics.Vector3(
                center, center, terrain.HeightMetersAt(center, center) + 150);
            ViewportHint.Visibility = System.Windows.Visibility.Collapsed;
            StatusText.Text = $"{worldName}: {terrain.MinHeight / 128f:F0}-{terrain.MaxHeight / 128f:F0} m terrain";
            Viewport.Focus();
        }
        finally
        {
            LoadButton.IsEnabled = true;
        }
    }

    private void Viewport_Render(TimeSpan delta)
    {
        if (_pendingTerrain is { } pending)
        {
            // Swap inside the render callback so GL resources live and die with a context.
            _pendingTerrain = null;
            _terrainMesh?.Dispose();
            _heightTexture?.Dispose();
            _terrain = pending;
            _heightTexture = new HeightTexture(pending);
            _terrainMesh = new TerrainMesh3D(_heightTexture);
        }

        GL.Viewport(0, 0, Viewport.FrameBufferWidth, Viewport.FrameBufferHeight);
        GL.ClearColor(0.13f, 0.15f, 0.17f, 1f);
        GL.Clear(ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit);
        if (_terrain is null || _terrainMesh is null)
        {
            return;
        }

        ApplyFlyKeys((float)delta.TotalSeconds);
        OpenTK.Mathematics.Matrix4 viewProjection = _camera.View()
            * _camera.Projection((float)(Viewport.ActualWidth / Math.Max(Viewport.ActualHeight, 1)));
        _terrainMesh.Draw(viewProjection, _camera.Position);
    }

    private void ApplyFlyKeys(float dt)
    {
        if (_flyKeys.Count == 0) return;
        float forward = (_flyKeys.Contains(Key.W) ? 1 : 0) - (_flyKeys.Contains(Key.S) ? 1 : 0);
        float strafe = (_flyKeys.Contains(Key.D) ? 1 : 0) - (_flyKeys.Contains(Key.A) ? 1 : 0);
        float lift = (_flyKeys.Contains(Key.E) ? 1 : 0) - (_flyKeys.Contains(Key.Q) ? 1 : 0);
        _camera.Move(forward, strafe, lift, Math.Min(dt, 0.1f));
    }

    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        if (Viewport.IsKeyboardFocused && e.Key is Key.W or Key.A or Key.S or Key.D or Key.Q or Key.E)
        {
            _flyKeys.Add(e.Key);
            e.Handled = true;
        }
        base.OnPreviewKeyDown(e);
    }

    protected override void OnPreviewKeyUp(KeyEventArgs e)
    {
        _flyKeys.Remove(e.Key);
        base.OnPreviewKeyUp(e);
    }

    private void Viewport_MouseDown(object sender, MouseButtonEventArgs e)
    {
        Viewport.Focus();
        if (e.ChangedButton == MouseButton.Right)
        {
            _looking = true;
            _lastDragPoint = e.GetPosition(Viewport);
            Viewport.CaptureMouse();
        }
    }

    private void Viewport_MouseUp(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Right)
        {
            _looking = false;
            Viewport.ReleaseMouseCapture();
        }
    }

    private void Viewport_MouseMove(object sender, MouseEventArgs e)
    {
        if (!_looking) return;
        System.Windows.Point p = e.GetPosition(Viewport);
        _camera.Look((float)(p.X - _lastDragPoint.X), (float)(p.Y - _lastDragPoint.Y));
        _lastDragPoint = p;
    }

    private void Viewport_MouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (_terrain is null) return;
        _camera.MoveSpeed = Math.Clamp(_camera.MoveSpeed * (e.Delta > 0 ? 1.3f : 1 / 1.3f), 5f, 600f);
        StatusText.Text = $"fly speed {_camera.MoveSpeed:F0} m/s";
    }

    private void LayerList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (LayerList.SelectedItem is not MapLayer layer)
            return;
        ContextTitle.Text = layer.Name;
        ContextReadiness.Text = layer.Readiness.ToUpperInvariant();
        ContextSummary.Text = layer.Summary;
        ContextControls.ItemsSource = layer.Controls;
    }
}
