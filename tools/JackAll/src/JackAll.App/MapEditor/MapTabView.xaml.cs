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
        Fc2World World, IReadOnlyList<WorldShape> Shapes, IReadOnlyList<WorldShape> Splines,
        IReadOnlyList<VegetationInstance> Vegetation, IReadOnlyList<NavMeshNode> NavNodes,
        IReadOnlyList<WorldLight> Lights, IReadOnlyList<TriggerVolume> Triggers);

    private sealed record FieldRow(string Name, string Value);

    /// <summary>One mission layer in the filter list. <see cref="IsVisible"/> is written by its
    /// checkbox, so it is a settable property rather than a record positional.</summary>
    private sealed class MissionLayerRow
    {
        public required string PathId { get; init; }
        public required int Count { get; init; }
        public bool IsVisible { get; set; } = true;
        public string Display => PathId.Length == 0 ? "(unnamed)" : PathId;
    }

    private Fc2World? _world;
    private EntityMarkerLayer? _markerLayer;
    private WorldEntity? _selectedEntity;
    private List<WorldEntity> _positionedEntities = [];
    private List<WorldEntity> _visibleEntities = [];
    private List<MissionLayerRow> _missionLayers = [];
    private bool _markersDirty;

    private PendingLoad? _pendingLoad;
    private WorldTerrain? _terrain;
    private HeightTexture? _heightTexture;
    private SurfaceTypeTexture? _surfaceTexture;
    private TerrainTextureSet? _terrainTextures;
    private TerrainMesh3D? _terrainMesh;
    private WaterLayer? _waterLayer;
    private ShapeLayer? _shapeLayer;
    private ShapeLayer? _splineLayer;
    private EntityMarkerLayer? _vegetationLayer;
    private EntityMarkerLayer? _navMeshLayer;
    private EntityMarkerLayer? _lightLayer;
    private ShapeLayer? _triggerLayer;
    private SkyLayer? _sky;

    private double _frameSeconds;
    private int _frames;

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
                IReadOnlyList<WorldShape> shapes =
                    WorldShapes.Load(map.Name, vm.ReadByPath, FcbDefinitionsProvider.Value.Value);
                IReadOnlyList<WorldShape> splines = WorldSplines.Load(map.Name, vm.ReadByPath);
                IReadOnlyList<VegetationInstance> vegetation =
                    WorldVegetation.Load(map, vm.ReadByPath, FcbDefinitionsProvider.Value.Value, progress);
                IReadOnlyList<NavMeshNode> navNodes = WorldNavMesh.Load(map, vm.ReadByPath, progress);
                IReadOnlyList<WorldLight> lights = WorldLights.Load(world.Entities);
                IReadOnlyList<TriggerVolume> triggers = WorldTriggers.Load(world.Entities);
                return new PendingLoad(
                    map, terrain, detail, table, world, shapes, splines, vegetation, navNodes, lights,
                    triggers);
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
        ShowFrameRate(delta);

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
            _shapeLayer?.Dispose();
            _splineLayer?.Dispose();
            _waterLayer = new WaterLayer(pending.Terrain);
            _shapeLayer = new ShapeLayer(pending.Shapes);
            _splineLayer = new ShapeLayer(pending.Splines);
            _vegetationLayer?.Dispose();
            _vegetationLayer = new EntityMarkerLayer(BuildVegetationMarkers(pending.Vegetation), pending.Vegetation.Count);
            _navMeshLayer?.Dispose();
            _navMeshLayer = new EntityMarkerLayer(BuildNavMeshMarkers(pending.NavNodes), pending.NavNodes.Count);
            _lightLayer?.Dispose();
            _lightLayer = new EntityMarkerLayer(BuildLightMarkers(pending.Lights), pending.Lights.Count);
            LightStatus.Text = Describe(pending.Lights);
            _triggerLayer?.Dispose();
            _triggerLayer = new ShapeLayer(BuildTriggerOutlines(pending.Triggers));
            TriggerStatus.Text = Describe(pending.Triggers);
            _markersDirty = true;
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

        // Toggling a mission layer changes which markers exist, so the instance stream is rebuilt
        // here where a GL context is current rather than on the click.
        if (_markersDirty)
        {
            _markersDirty = false;
            _markerLayer?.Dispose();
            _markerLayer = new EntityMarkerLayer(BuildMarkers(_visibleEntities), _visibleEntities.Count);
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

        _sky ??= new SkyLayer();
        _sky.Draw(viewProjection, _camera.Position);
        if (LayerCatalog.Heightmap.IsVisible)
        {
            _terrainMesh.Draw(viewProjection, _camera.Position, new TerrainDrawOptions(
                ShowTextures: LayerCatalog.Textures.IsVisible,
                TintBySurfaceType: LayerCatalog.SurfaceData.IsVisible,
                ShowShadow: LayerCatalog.Shadow.IsVisible,
                Brightness: (float)BrightnessSlider.Value,
                Haze: Atmosphere.IsChecked == true ? 1f : 0f));
        }

        if (LayerCatalog.Water.IsVisible)
        {
            _waterLayer?.Draw(viewProjection, _camera.Position, Atmosphere.IsChecked == true ? 1f : 0f);
        }

        if (LayerCatalog.Shapes.IsVisible)
        {
            _shapeLayer?.Draw(viewProjection);
        }

        if (LayerCatalog.Roads.IsVisible)
        {
            _splineLayer?.Draw(viewProjection);
        }

        if (LayerCatalog.Vegetation.IsVisible)
        {
            OpenTK.Mathematics.Vector3 vegRight = _camera.Right;
            _vegetationLayer?.Draw(viewProjection, 2f, vegRight,
                OpenTK.Mathematics.Vector3.Cross(vegRight, _camera.Forward), flattenZ: false, null);
        }

        if (LayerCatalog.Triggers.IsVisible)
        {
            _triggerLayer?.Draw(viewProjection);
        }

        if (LayerCatalog.Lights.IsVisible)
        {
            OpenTK.Mathematics.Vector3 lightRight = _camera.Right;
            _lightLayer?.Draw(viewProjection, 4f, lightRight,
                OpenTK.Mathematics.Vector3.Cross(lightRight, _camera.Forward), flattenZ: false, null);
        }

        if (LayerCatalog.NavMesh.IsVisible)
        {
            OpenTK.Mathematics.Vector3 navRight = _camera.Right;
            _navMeshLayer?.Draw(viewProjection, 1.5f, navRight,
                OpenTK.Mathematics.Vector3.Cross(navRight, _camera.Forward), flattenZ: false, null);
        }

        if (LayerCatalog.Entities.IsVisible)
        {
            OpenTK.Mathematics.Vector3 right = _camera.Right;
            _markerLayer?.Draw(viewProjection, 3f, right,
                OpenTK.Mathematics.Vector3.Cross(right, _camera.Forward), flattenZ: false, _selectedEntity);
        }
    }

    /// <summary>Averages over a quarter second: a per-frame number is unreadable, and the average is
    /// the one that matters while judging whether a layer costs anything.</summary>
    private void ShowFrameRate(TimeSpan delta)
    {
        _frameSeconds += delta.TotalSeconds;
        _frames++;
        if (_frameSeconds < 0.25)
        {
            return;
        }

        FpsText.Text = $"{_frames / _frameSeconds:0} fps";
        _frameSeconds = 0;
        _frames = 0;
    }

    /// <summary>Holding shift multiplies the fly speed.</summary>
    private const float SprintFactor = 5f;

    /// <summary>Where the camera starts braking, and where it stops dead - metres of clear travel
    /// ahead along the direction of movement.</summary>
    private const float BrakeFrom = 50f;
    private const float BrakeTo = 1f;

    private void ApplyFlyKeys(float dt)
    {
        if (_flyKeys.Count == 0) return;
        float forward = (_flyKeys.Contains(Key.W) ? 1 : 0) - (_flyKeys.Contains(Key.S) ? 1 : 0);
        float strafe = (_flyKeys.Contains(Key.D) ? 1 : 0) - (_flyKeys.Contains(Key.A) ? 1 : 0);
        float lift = (_flyKeys.Contains(Key.E) ? 1 : 0) - (_flyKeys.Contains(Key.Q) ? 1 : 0);

        OpenTK.Mathematics.Vector3 direction = _camera.MoveDirection(forward, strafe, lift);
        if (direction == OpenTK.Mathematics.Vector3.Zero) return;

        float speed = _camera.MoveSpeed;
        if (Keyboard.IsKeyDown(Key.LeftShift) || Keyboard.IsKeyDown(Key.RightShift))
        {
            speed *= SprintFactor;
        }

        float step = speed * Math.Min(dt, 0.1f);
        if (GroundCollision.IsChecked == true && _terrain is not null)
        {
            step *= GroundBrake(direction);
        }
        _camera.Move(direction, step);
        HoldAboveTerrain();
    }

    /// <summary>
    /// How much of the requested step survives, from full at <see cref="BrakeFrom"/> metres of clear
    /// travel down to nothing at <see cref="BrakeTo"/>. Marching the movement direction rather than
    /// looking straight down is what lets the camera skim low over flat ground at full speed and
    /// still stop dead against a hillside it is pointed at.
    /// </summary>
    private float GroundBrake(OpenTK.Mathematics.Vector3 direction)
    {
        for (float ahead = 0; ahead <= BrakeFrom; ahead += 1f)
        {
            OpenTK.Mathematics.Vector3 at = _camera.Position + direction * ahead;
            if (at.Z <= _terrain!.HeightMetersAt((int)MathF.Round(at.X), (int)MathF.Round(at.Y)))
            {
                return Math.Clamp((ahead - BrakeTo) / (BrakeFrom - BrakeTo), 0f, 1f);
            }
        }
        return 1f;
    }

    /// <summary>The backstop behind the braking ramp: a step that still ends up under the terrain is
    /// lifted back out, so the ground cannot be crossed at any framerate.</summary>
    private void HoldAboveTerrain()
    {
        if (GroundCollision.IsChecked != true || _terrain is null) return;

        OpenTK.Mathematics.Vector3 position = _camera.Position;
        float floor = _terrain.HeightMetersAt((int)MathF.Round(position.X), (int)MathF.Round(position.Y)) + BrakeTo;
        if (position.Z < floor)
        {
            _camera.Position = new OpenTK.Mathematics.Vector3(position.X, position.Y, floor);
        }
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

    /// <summary>Hides every layer but the heightmap, which is left exactly as it was - wanting the
    /// ground gone is the rare case, and this button should not be the thing that decides it.</summary>
    private void UncheckAll_Click(object sender, System.Windows.RoutedEventArgs e)
    {
        foreach (MapLayer layer in LayerCatalog.Layers)
        {
            if (!ReferenceEquals(layer, LayerCatalog.Heightmap))
            {
                layer.IsVisible = false;
            }
        }
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

        _missionLayers = [.. _positionedEntities
            .GroupBy(e => e.LayerPathId)
            .Select(g => new MissionLayerRow { PathId = g.Key, Count = g.Count() })
            .OrderByDescending(r => r.Count)];
        MissionLayerList.ItemsSource = _missionLayers;

        ApplyMissionLayerFilter();
    }

    /// <summary>Rebuilds what the viewport and list show from the ticked mission layers.</summary>
    private void ApplyMissionLayerFilter()
    {
        HashSet<string> visible = [.. _missionLayers.Where(r => r.IsVisible).Select(r => r.PathId)];
        _visibleEntities = [.. _positionedEntities.Where(e => visible.Contains(e.LayerPathId))];
        _markersDirty = true;
        ApplyEntityFilter();
    }

    private void MissionLayerToggle_Click(object sender, System.Windows.RoutedEventArgs e) =>
        ApplyMissionLayerFilter();

    private void MissionLayersAll_Click(object sender, System.Windows.RoutedEventArgs e) =>
        SetAllMissionLayers(_ => true);

    private void MissionLayersMain_Click(object sender, System.Windows.RoutedEventArgs e) =>
        SetAllMissionLayers(row => row.PathId.Equals("main", StringComparison.OrdinalIgnoreCase));

    private void SetAllMissionLayers(Func<MissionLayerRow, bool> visible)
    {
        foreach (MissionLayerRow row in _missionLayers)
        {
            row.IsVisible = visible(row);
        }
        MissionLayerList.ItemsSource = null;
        MissionLayerList.ItemsSource = _missionLayers;
        ApplyMissionLayerFilter();
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

    /// <summary>One marker per plant, coloured by the resource it instantiates so a species reads
    /// the same across the map.</summary>
    private static float[] BuildVegetationMarkers(IReadOnlyList<VegetationInstance> vegetation)
    {
        var stream = new float[vegetation.Count * EntityMarkerLayer.Stride];
        for (int i = 0; i < vegetation.Count; i++)
        {
            VegetationInstance plant = vegetation[i];
            (byte r, byte g, byte b) = SurfaceTypeTexture.ColourFor((byte)(plant.ResourceId & 0x7F));
            int at = i * EntityMarkerLayer.Stride;
            stream[at] = plant.Position.X;
            stream[at + 1] = plant.Position.Y;
            stream[at + 2] = plant.Position.Z;
            stream[at + 3] = r / 255f;
            stream[at + 4] = g / 255f;
            stream[at + 5] = b / 255f;
        }
        return stream;
    }

    /// <summary>One marker per walkable node, shading from green to red as the node's normal tips
    /// away from vertical - the same measure the engine's slope limit tests.</summary>
    private static float[] BuildNavMeshMarkers(IReadOnlyList<NavMeshNode> nodes)
    {
        var stream = new float[nodes.Count * EntityMarkerLayer.Stride];
        for (int i = 0; i < nodes.Count; i++)
        {
            NavMeshNode node = nodes[i];
            float flat = Math.Clamp(node.Normal.Z, 0f, 1f);
            int at = i * EntityMarkerLayer.Stride;
            stream[at] = node.Position.X;
            stream[at + 1] = node.Position.Y;
            stream[at + 2] = node.Position.Z;
            stream[at + 3] = 1f - flat;
            stream[at + 4] = flat;
            stream[at + 5] = 0.25f;
        }
        return stream;
    }

    /// <summary>Each trigger box as its twelve edges: the two rectangles plus the four uprights.
    /// Reusing the polyline layer keeps this to line data rather than a renderer of its own.</summary>
    private static List<WorldShape> BuildTriggerOutlines(IReadOnlyList<TriggerVolume> triggers)
    {
        var outlines = new List<WorldShape>(triggers.Count * 6);
        foreach (TriggerVolume trigger in triggers)
        {
            System.Numerics.Vector3[] c = trigger.Corners();
            string kind = trigger.Enabled ? "trigger" : "trigger-off";

            outlines.Add(new WorldShape(kind, trigger.Name, "box", [c[0], c[1], c[3], c[2], c[0]]));
            outlines.Add(new WorldShape(kind, trigger.Name, "box", [c[4], c[5], c[7], c[6], c[4]]));
            for (int i = 0; i < 4; i++)
            {
                outlines.Add(new WorldShape(kind, trigger.Name, "box", [c[i], c[i + 4]]));
            }
        }
        return outlines;
    }

    private static string Describe(IReadOnlyList<TriggerVolume> triggers)
    {
        if (triggers.Count == 0)
        {
            return "No proximity triggers in this map.";
        }

        int off = triggers.Count(t => !t.Enabled);
        int rotated = triggers.Count(t => t.Yaw != 0);
        return $"{triggers.Count:N0} proximity triggers; {rotated:N0} rotated" +
               (off > 0 ? $", {off:N0} start disabled." : ".") +
               " vectorSize is drawn as the box's full extent, centred on the entity - neither is " +
               "confirmed from the engine.";
    }

    /// <summary>One marker per light in its own emitted colour, dimmed hard when the light ships
    /// disabled so the two read apart at a glance.</summary>
    private static float[] BuildLightMarkers(IReadOnlyList<WorldLight> lights)
    {
        var stream = new float[lights.Count * EntityMarkerLayer.Stride];
        for (int i = 0; i < lights.Count; i++)
        {
            WorldLight light = lights[i];
            float dim = light.Enabled ? 1f : 0.2f;
            int at = i * EntityMarkerLayer.Stride;
            stream[at] = light.Position.X;
            stream[at + 1] = light.Position.Y;
            stream[at + 2] = light.Position.Z;
            stream[at + 3] = light.Colour.X * dim;
            stream[at + 4] = light.Colour.Y * dim;
            stream[at + 5] = light.Colour.Z * dim;
        }
        return stream;
    }

    private static string Describe(IReadOnlyList<WorldLight> lights)
    {
        if (lights.Count == 0)
        {
            return "No lights in this map.";
        }

        int spots = lights.Count(l => l.IsSpot);
        int off = lights.Count(l => !l.Enabled);
        return $"{lights.Count:N0} lights: {lights.Count - spots:N0} omni, {spots:N0} spot" +
               (off > 0 ? $"; {off:N0} start disabled." : ".");
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
            ? _visibleEntities
            : [.. _visibleEntities.Where(e =>
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
        if (_visibleEntities.Count == 0)
        {
            return;
        }

        OpenTK.Mathematics.Matrix4 viewProjection = _camera.View()
            * _camera.Projection((float)(Viewport.ActualWidth / Math.Max(Viewport.ActualHeight, 1)));
        double width = Viewport.ActualWidth, height = Viewport.ActualHeight;
        WorldEntity? best = null;
        double bestDistance = 20 * 20;

        foreach (WorldEntity entity in _visibleEntities)
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
        LightPanel.Visibility = ReferenceEquals(LayerList.SelectedItem, LayerCatalog.Lights)
            ? System.Windows.Visibility.Visible
            : System.Windows.Visibility.Collapsed;
        TriggerPanel.Visibility = ReferenceEquals(LayerList.SelectedItem, LayerCatalog.Triggers)
            ? System.Windows.Visibility.Visible
            : System.Windows.Visibility.Collapsed;

        // Entities take over the whole context column rather than sitting under the mock text.
        bool entities = ReferenceEquals(LayerList.SelectedItem, LayerCatalog.Entities);
        bool missions = ReferenceEquals(LayerList.SelectedItem, LayerCatalog.MissionLayers);
        EntityPanel.Visibility = entities ? System.Windows.Visibility.Visible : System.Windows.Visibility.Collapsed;
        MissionLayerPanel.Visibility = missions ? System.Windows.Visibility.Visible : System.Windows.Visibility.Collapsed;
        ContextInfo.Visibility = entities || missions
            ? System.Windows.Visibility.Collapsed
            : System.Windows.Visibility.Visible;
    }
}
