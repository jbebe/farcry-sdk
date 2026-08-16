using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using JackAll.App.FileHandlers.Fcb;
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
        TerrainMap Map, WorldTerrain Terrain, SectorDetailLayers DetailLayers, TerrainLayerTable Table,
        Fc2World World);

    private sealed record FieldRow(string Name, string Value);

    private Fc2World? _world;
    private EntityMarkerLayer? _markerLayer;
    private WorldEntity? _selectedEntity;
    private List<WorldEntity> _positionedEntities = [];

    private PendingLoad? _pendingLoad;
    private WorldTerrain? _terrain;
    private HeightTexture? _heightTexture;
    private SurfaceTypeTexture? _surfaceTexture;
    private TerrainTextureSet? _terrainTextures;
    private TerrainMesh3D? _terrainMesh;
    private WaterLayer? _waterLayer;

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
                TerrainLayerTable table = TerrainLayerTable.Load(map.Name, vm.ReadByPath);
                Fc2World world = WorldLoader.Load(map, vm.ReadByPath, progress);
                return new PendingLoad(map, terrain, detail, table, world);
            });

            WorldTerrain terrain = loaded.Terrain;
            _pendingLoad = loaded;
            ShowSurfaceLegend(terrain, loaded.Table);
            ShowEntities(loaded.World);
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
            _waterLayer?.Dispose();
            _markerLayer?.Dispose();
            _terrain = pending.Terrain;
            _waterLayer = new WaterLayer(pending.Terrain);
            _markerLayer = new EntityMarkerLayer(BuildMarkers(_positionedEntities), _positionedEntities.Count);
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
                TintBySurfaceType: LayerCatalog.SurfaceData.IsVisible,
                ShowShadow: LayerCatalog.Shadow.IsVisible));
        }

        if (LayerCatalog.Water.IsVisible)
        {
            _waterLayer?.Draw(viewProjection);
        }

        if (LayerCatalog.Entities.IsVisible)
        {
            OpenTK.Mathematics.Vector3 right = _camera.Right;
            _markerLayer?.Draw(viewProjection, 3f, right,
                OpenTK.Mathematics.Vector3.Cross(right, _camera.Forward), flattenZ: false, _selectedEntity);
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
        else if (e.ChangedButton == MouseButton.Left && LayerCatalog.Entities.IsVisible)
        {
            PickEntityAt(e.GetPosition(Viewport));
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

    private void ShowEntities(Fc2World world)
    {
        _world = world;
        _selectedEntity = null;
        _positionedEntities = [.. world.Entities.Where(e => e.Position is not null)];
        ApplyEntityFilter();
    }

    /// <summary>Markers are one instance each: position plus a colour keyed to the archetype, so the
    /// same kind of object reads the same everywhere.</summary>
    private static float[] BuildMarkers(List<WorldEntity> entities)
    {
        var stream = new float[entities.Count * EntityMarkerLayer.Stride];
        for (int i = 0; i < entities.Count; i++)
        {
            WorldEntity entity = entities[i];
            System.Numerics.Vector3 position = entity.Position!.Value;
            (byte r, byte g, byte b) = SurfaceTypeTexture.ColourFor(
                (byte)(StableHash(entity.ArchetypeName) & 0x7F));
            int at = i * EntityMarkerLayer.Stride;
            stream[at] = position.X;
            stream[at + 1] = position.Y;
            stream[at + 2] = position.Z;
            stream[at + 3] = r / 255f;
            stream[at + 4] = g / 255f;
            stream[at + 5] = b / 255f;
        }
        return stream;
    }

    private static uint StableHash(string text)
    {
        uint hash = 2166136261;
        foreach (char c in text)
        {
            hash = (hash ^ c) * 16777619;
        }
        return hash;
    }

    private void EntitySearch_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e) =>
        ApplyEntityFilter();

    private void ApplyEntityFilter()
    {
        if (_world is null)
        {
            return;
        }

        string term = EntitySearch.Text.Trim();
        List<WorldEntity> shown = term.Length == 0
            ? _positionedEntities
            : [.. _positionedEntities.Where(e =>
                e.Name.Contains(term, StringComparison.OrdinalIgnoreCase) ||
                e.ArchetypeName.Contains(term, StringComparison.OrdinalIgnoreCase))];

        EntityList.ItemsSource = shown;
        EntityCount.Text = shown.Count == _positionedEntities.Count
            ? $"{_positionedEntities.Count:N0} entities"
            : $"{shown.Count:N0} of {_positionedEntities.Count:N0} entities";
    }

    private void EntityList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (EntityList.SelectedItem is not WorldEntity entity)
        {
            return;
        }
        Select(entity, moveCamera: true);
    }

    private void Select(WorldEntity entity, bool moveCamera)
    {
        _selectedEntity = entity;
        EntityHeading.Text = $"{entity.Name}  ({entity.LayerPathId})";

        JackAll.Core.Format.Fcb.FcbClassDefinitions defs = FcbDefinitionsProvider.Value.Value;
        JackAll.Core.Format.Fcb.FcbClass entityClass = defs.GetClass(entity.Node.TypeHash);
        var rows = new List<FieldRow>
        {
            new("sector", entity.HomeSector.SectorId.ToString()),
            new("disEntityId", entity.Id.ToString()),
        };
        foreach ((uint hash, byte[] value) in entity.Node.Values)
        {
            rows.Add(new FieldRow(entityClass.FindMember(hash)?.Name ?? $"{hash:X8}", Describe(value)));
        }
        foreach (IGrouping<uint, JackAll.Core.Format.Fcb.FcbObject> group in
            entity.Node.Children.GroupBy(c => c.TypeHash))
        {
            string name = defs.GetClass(group.Key).Name ?? $"{group.Key:X8}";
            rows.Add(new FieldRow("component", group.Count() > 1 ? $"{name} x{group.Count()}" : name));
        }
        EntityFields.ItemsSource = rows;

        if (moveCamera && entity.Position is { } p)
        {
            _camera.Position = new OpenTK.Mathematics.Vector3(p.X, p.Y - 25f, p.Z + 15f);
        }
    }

    /// <summary>Best-effort decode for display: the shapes that actually occur on entity fields.</summary>
    private static string Describe(byte[] value)
    {
        if (value.Length > 1 && value[^1] == 0 &&
            value.Take(value.Length - 1).All(b => b >= 32 && b < 127))
        {
            return System.Text.Encoding.ASCII.GetString(value, 0, value.Length - 1);
        }
        return value.Length switch
        {
            1 => value[0].ToString(),
            4 => $"{BitConverter.ToInt32(value)}  ({BitConverter.ToSingle(value):0.###})",
            8 => BitConverter.ToUInt64(value).ToString(),
            12 => $"{BitConverter.ToSingle(value, 0):0.##}, {BitConverter.ToSingle(value, 4):0.##}, {BitConverter.ToSingle(value, 8):0.##}",
            _ => Convert.ToHexString(value.Take(16).ToArray()) + (value.Length > 16 ? "..." : ""),
        };
    }

    /// <summary>
    /// Picks the entity nearest the click. Projecting the whole pool costs a pass over ~90k points on
    /// one click, which is far simpler than maintaining a spatial index and quick enough not to notice.
    /// </summary>
    private void PickEntityAt(System.Windows.Point point)
    {
        if (_positionedEntities.Count == 0)
        {
            return;
        }

        OpenTK.Mathematics.Matrix4 viewProjection = _camera.View()
            * _camera.Projection((float)(Viewport.ActualWidth / Math.Max(Viewport.ActualHeight, 1)));
        double width = Viewport.ActualWidth, height = Viewport.ActualHeight;
        WorldEntity? best = null;
        double bestDistance = 20 * 20;

        foreach (WorldEntity entity in _positionedEntities)
        {
            System.Numerics.Vector3 p = entity.Position!.Value;
            var clip = new OpenTK.Mathematics.Vector4(p.X, p.Y, p.Z, 1f) * viewProjection;
            if (clip.W <= 0.01f)
            {
                continue;
            }

            double sx = (clip.X / clip.W * 0.5 + 0.5) * width;
            double sy = (1 - (clip.Y / clip.W * 0.5 + 0.5)) * height;
            double distance = (sx - point.X) * (sx - point.X) + (sy - point.Y) * (sy - point.Y);
            if (distance < bestDistance)
            {
                bestDistance = distance;
                best = entity;
            }
        }

        if (best is not null)
        {
            Select(best, moveCamera: false);
            EntityList.SelectedItem = best;
            EntityList.ScrollIntoView(best);
        }
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

        // Entities take over the whole context column rather than sitting under the mock text.
        bool entities = ReferenceEquals(LayerList.SelectedItem, LayerCatalog.Entities);
        EntityPanel.Visibility = entities ? System.Windows.Visibility.Visible : System.Windows.Visibility.Collapsed;
        ContextInfo.Visibility = entities ? System.Windows.Visibility.Collapsed : System.Windows.Visibility.Visible;
    }
}
