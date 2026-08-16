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

    private sealed record PendingLoad(
        TerrainMap Map, WorldTerrain Terrain, SectorDetailLayers DetailLayers, TerrainLayerTable Table);

    private PendingLoad? _pendingLoad;
    private WorldTerrain? _terrain;
    private HeightTexture? _heightTexture;
    private SurfaceTypeTexture? _surfaceTexture;
    private TerrainTextureSet? _terrainTextures;
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

        Viewport.Start(new GLWpfControlSettings { MajorVersion = 3, MinorVersion = 3 });
    }

    /// <summary>Called by MainWindow once the VFS is loaded and its maps become discoverable.</summary>
    public async Task InitializeAsync(MainViewModel vm)
    {
        _vm = vm;
        IReadOnlyList<TerrainMap> maps = await Task.Run(() => TerrainMap.Discover(vm.AllKnownPaths));

        MapPicker.ItemsSource = maps;
        MapPicker.SelectedIndex = 0;
        MapPicker.IsEnabled = maps.Count > 0;
        LoadButton.IsEnabled = maps.Count > 0;
        StatusText.Text = maps.Count > 0 ? $"{maps.Count} maps - pick one and Load" : "No map terrain found";
    }

    private async void Load_Click(object sender, System.Windows.RoutedEventArgs e)
    {
        if (_vm is null || MapPicker.SelectedItem is not TerrainMap map) return;

        LoadButton.IsEnabled = false;
        try
        {
            IProgress<string> progress = new Progress<string>(s => StatusText.Text = s);
            MainViewModel vm = _vm;
            PendingLoad loaded = await Task.Run(() =>
            {
                WorldTerrain terrain = WorldTerrain.Load(map, vm.ReadByPath, progress);
                progress.Report($"Loading {map.Name} sector descriptors");
                SectorDetailLayers detail = SectorDetailLayers.Load(map, vm.ReadByPath);
                return new PendingLoad(map, terrain, detail, TerrainLayerTable.Load(map.Name, vm.ReadByPath));
            });

            WorldTerrain terrain = loaded.Terrain;
            _pendingLoad = loaded;
            ShowSurfaceLegend(terrain, loaded.Table);
            int center = terrain.Side / 2;
            _camera.Position = new OpenTK.Mathematics.Vector3(
                center, center, terrain.HeightMetersAt(center, center) + 150);
            ViewportHint.Visibility = System.Windows.Visibility.Collapsed;
            StatusText.Text = $"{map.Name}: {map.SectorsPerSide}x{map.SectorsPerSide} sectors, " +
                              $"{terrain.MinHeight / 128f:F0}-{terrain.MaxHeight / 128f:F0} m";
            Viewport.Focus();
        }
        finally
        {
            LoadButton.IsEnabled = true;
        }
    }

    private void Viewport_Render(TimeSpan delta)
    {
        if (_pendingLoad is { } pending)
        {
            // Swap inside the render callback so GL resources live and die with a context.
            _pendingLoad = null;
            _terrainMesh?.Dispose();
            _heightTexture?.Dispose();
            _surfaceTexture?.Dispose();
            _terrainTextures?.Dispose();
            _terrain = pending.Terrain;
            _heightTexture = new HeightTexture(pending.Terrain);
            _surfaceTexture = new SurfaceTypeTexture(pending.Terrain);
            _terrainTextures = _vm is { } vm
                ? new TerrainTextureSet(pending.Map, pending.DetailLayers, pending.Table, vm.ReadByPath)
                : null;
            _terrainMesh = new TerrainMesh3D(_heightTexture, _surfaceTexture, _terrainTextures);
            TextureStatus.Text = _terrainTextures is { } set
                ? $"{set.LayersLoaded} of {pending.Table.Layers.Count} layer textures loaded; blend mask {set.WeightSide}x{set.WeightSide}." +
                  (set.FailedLayers.Count > 0 ? $" Missing: {string.Join(", ", set.FailedLayers)}." : "")
                : "No terrain textures loaded.";
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
        if (LayerCatalog.Heightmap.IsVisible)
        {
            _terrainMesh.Draw(viewProjection, _camera.Position, new TerrainDrawOptions(
                ShowTextures: LayerCatalog.Textures.IsVisible,
                TintBySurfaceType: LayerCatalog.SurfaceData.IsVisible));
        }
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
        UpdateSurfaceLegendVisibility();
    }

    private sealed record SurfaceLegendRow(System.Windows.Media.Brush Swatch, string Label, string Coverage);

    /// <summary>
    /// Lists the surface types the loaded map actually uses, biggest first, with the same colours the
    /// terrain is tinted with. Ids are resolved to layer names where the world's table names them.
    /// </summary>
    private void ShowSurfaceLegend(WorldTerrain terrain, TerrainLayerTable layers)
    {
        long total = terrain.SurfaceTypeCoverage.Sum(entry => entry.Samples);
        SurfaceLegendRows.ItemsSource = terrain.SurfaceTypeCoverage
            .Select(entry =>
            {
                (byte r, byte g, byte b) = SurfaceTypeTexture.ColourFor(entry.SurfaceType);
                string name = entry.SurfaceType == 0xFF
                    ? "hole / no terrain"
                    : layers.Label(entry.SurfaceType) ?? "unnamed";
                var brush = new System.Windows.Media.SolidColorBrush(
                    System.Windows.Media.Color.FromRgb(r, g, b));
                brush.Freeze();
                return new SurfaceLegendRow(brush, $"{entry.SurfaceType} · {name}",
                    $"{100.0 * entry.Samples / total:F1}%");
            })
            .ToList();
        UpdateSurfaceLegendVisibility();
    }

    private void UpdateSurfaceLegendVisibility()
    {
        SurfaceLegend.Visibility =
            ReferenceEquals(LayerList.SelectedItem, LayerCatalog.SurfaceData) && SurfaceLegendRows.ItemsSource is not null
                ? System.Windows.Visibility.Visible
                : System.Windows.Visibility.Collapsed;
        TexturePanel.Visibility = ReferenceEquals(LayerList.SelectedItem, LayerCatalog.Textures)
            ? System.Windows.Visibility.Visible
            : System.Windows.Visibility.Collapsed;
    }
}
