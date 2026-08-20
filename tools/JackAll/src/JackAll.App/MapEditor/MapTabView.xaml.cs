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
        IReadOnlyList<WorldLight> Lights, IReadOnlyList<TriggerVolume> Triggers,
        ArchetypeIndex Archetypes, WorldModelSet Models);

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
    private EntityModelLayer? _modelLayer;
    private WorldEntity? _selectedEntity;
    private List<WorldEntity> _positionedEntities = [];
    private List<WorldEntity> _visibleEntities = [];
    private List<MissionLayerRow> _missionLayers = [];
    private bool _markersDirty;
    private (int X, int Y) _cameraSector = (int.MinValue, int.MinValue);

    /// <summary>Refilled per marker rebuild; sized for every positioned entity of the world.</summary>
    private float[] _markerStaging = [];

    /// <summary>Built once per world; the mission-layer and search filters only change visibility.</summary>
    private EntityTreeNode? _entityTree;

    /// <summary>The model stats line without its live texture-memory suffix.</summary>
    private string _modelStatusText = "";

    private ArchetypeIndex? _archetypes;

    /// <summary>Kept past the GL swap that clears the pending load, because picking and the
    /// selection outline need each entity's baked bounds every frame.</summary>
    private WorldModelSet? _modelSet;

    /// <summary>Raised for "Archetype in Library" - the host owns cross-tab navigation.</summary>
    public event Action<string, string>? ArchetypeRequested;

    /// <summary>Raised for "Open entity in XML editor", with the sector's game-relative path and the
    /// entity to open.</summary>
    public event Action<string, ulong>? SectorEditorRequested;

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

    /// <summary>One marker layer per mesh-less category, each with its own glyph and its own toggle
    /// in the layer list. Categories a dedicated layer already draws never reach these.</summary>
    private readonly Dictionary<EntityCategory, EntityMarkerLayer> _categoryLayers = [];
    private ShapeLayer? _triggerLayer;
    private SkyLayer? _sky;
    private SelectionBoxLayer? _selectionBox;

    private double _frameSeconds;
    private int _frames;

    private readonly Camera3D _camera = new();
    private readonly HashSet<Key> _flyKeys = [];
    /// <summary>How long the current movement has been held - what winds the fly speed up from a
    /// standing start. Cleared the moment nothing is pressed, so taps stay short.</summary>
    private float _flyHeldSeconds;
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
                // Groups the entity tree the way the Library tab groups archetypes, and is what the
                // "Archetype in Library" jump resolves against.
                ArchetypeIndex archetypes = ArchetypeIndex.Load(
                    map.Name, vm.ReadByPath, progress, LibraryProfile.Client,
                    ArchetypeIndex.DiscoverDlcLibraries(vm.AllKnownPaths));
                WorldModelSet models = WorldModels.Load(world.Entities, archetypes, vm.ReadByPath, progress);
                return new PendingLoad(
                    map, terrain, detail, table, world, shapes, splines, vegetation, navNodes, lights,
                    triggers, archetypes, models);
            });

            WorldTerrain terrain = loaded.Terrain;
            _pendingLoad = loaded;
            ShowSurfaceLegend(terrain, loaded.Table);
            _archetypes = loaded.Archetypes;
            ShowEntities(loaded.World, loaded.Archetypes);
            WorldModelSet models = loaded.Models;
            _modelStatusText =
                $"{models.ModelIndicesByEntity.Count:N0} of {_positionedEntities.Count:N0} entities " +
                $"have models ({models.Models.Count:N0} unique meshes" +
                (models.FailedPathCount > 0 ? $", {models.FailedPathCount:N0} meshes failed" : "") +
                (models.EntitiesWithoutMesh > 0 ? $", {models.EntitiesWithoutMesh:N0} named none" : "") + ")";
            ModelStatus.Text = _modelStatusText;
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
            // Sized for every positioned entity, because which category an entity lands in is not
            // known until the model layer has had its pass; the live counts do the real limiting.
            int categoryCapacity = pending.World.Entities.Count(e => e.Position is not null);
            foreach (EntityMarkerLayer stale in _categoryLayers.Values)
            {
                stale.Dispose();
            }
            _categoryLayers.Clear();
            foreach ((EntityCategory category, _, _, _, _, _) in DrawnCategories)
            {
                _categoryLayers[category] = new EntityMarkerLayer(categoryCapacity);
            }
            _triggerLayer?.Dispose();
            _triggerLayer = new ShapeLayer(BuildTriggerOutlines(pending.Triggers));
            TriggerStatus.Text = Describe(pending.Triggers);
            _modelLayer?.Dispose();
            _modelSet = pending.Models;
            _modelLayer = new EntityModelLayer(
                pending.Models, _vm is { } modelVm ? modelVm.ReadByPath : _ => null, MarkerColour);
            int positioned = pending.World.Entities.Count(e => e.Position is not null);
            _markerStaging = new float[positioned * EntityMarkerLayer.Stride];
            _markerLayer?.Dispose();
            _markerLayer = new EntityMarkerLayer(positioned);
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

        // The model tiers are keyed to the camera's sector, so crossing a boundary re-buckets the
        // instance streams the same way a mission-layer toggle does.
        var sector = ((int)MathF.Floor(_camera.Position.X / WorldModels.SectorMeters),
            (int)MathF.Floor(_camera.Position.Y / WorldModels.SectorMeters));
        if (sector != _cameraSector)
        {
            _cameraSector = sector;
            _markersDirty = true;
        }

        // Toggling a mission layer changes which markers exist, so the instance stream is rebuilt
        // here where a GL context is current rather than on the click.
        if (_markersDirty)
        {
            _markersDirty = false;
            if (_modelLayer is { } modelLayer && EntityDrawMode.SelectedIndex != ModeMarkers)
            {
                modelLayer.SetVisible(_visibleEntities, _camera.Position);
            }

            // The draw mode speaks only for entities that resolved to geometry: Models draws them as
            // meshes and nothing else, so the mode no longer smuggles the leftovers back in as
            // markers. The mesh-less ones belong to their category layers either way.
            List<WorldEntity> markerEntities = EntityDrawMode.SelectedIndex == ModeModels
                ? []
                : _visibleEntities;
            _markerLayer?.SetInstances(FillMarkers(markerEntities), markerEntities.Count);
            RebuildCategoryMarkers();
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

        // One switch behind the sky, the haze and the water shading: presentation on, or a plain
        // flat view of the data.
        float demo = DemoMode.IsChecked == true ? 1f : 0f;
        if (demo > 0f)
        {
            _sky ??= new SkyLayer();
            _sky.Draw(viewProjection, _camera.Position);
        }
        if (LayerCatalog.Heightmap.IsVisible)
        {
            _terrainMesh.Draw(viewProjection, _camera.Position, new TerrainDrawOptions(
                ShowTextures: LayerCatalog.Textures.IsVisible,
                TintBySurfaceType: LayerCatalog.SurfaceData.IsVisible,
                ShowShadow: LayerCatalog.Shadow.IsVisible,
                Brightness: (float)BrightnessSlider.Value,
                Haze: demo));
        }

        if (LayerCatalog.Water.IsVisible)
        {
            _waterLayer?.Draw(viewProjection, _camera.Position, demo);
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
            _vegetationLayer?.Draw(viewProjection, _camera.Position, Right(), Up(), flattenZ: false,
                MarkerStyle.World(2f));
        }

        if (LayerCatalog.Triggers.IsVisible)
        {
            _triggerLayer?.Draw(viewProjection);
        }

        if (LayerCatalog.Lights.IsVisible)
        {
            _lightLayer?.Draw(viewProjection, _camera.Position, Right(), Up(), flattenZ: false,
                MarkerStyle.World(4f));
        }

        if (LayerCatalog.NavMesh.IsVisible)
        {
            _navMeshLayer?.Draw(viewProjection, _camera.Position, Right(), Up(), flattenZ: false,
                MarkerStyle.World(1.5f));
        }

        if (LayerCatalog.Entities.IsVisible)
        {
            if (EntityDrawMode.SelectedIndex != ModeMarkers)
            {
                _modelLayer?.Draw(viewProjection, _camera.Position, demo);
            }

            // Markers blend, so they draw after the opaque models.
            _markerLayer?.Draw(viewProjection, _camera.Position, Right(), Up(), flattenZ: false,
                MarkerStyle.World(MarkerWorldSize));
        }

        foreach ((EntityCategory category, MarkerGlyph glyph, MapLayer layer, _, _, _) in DrawnCategories)
        {
            if (layer.IsVisible && _categoryLayers.TryGetValue(category, out EntityMarkerLayer? markers))
            {
                markers.Draw(viewProjection, _camera.Position, Right(), Up(), flattenZ: false,
                    MarkerStyle.Screen(glyph, GlyphPixels, (float)Viewport.ActualHeight,
                        Camera3D.VerticalFovRadians, GlyphMaxDistance));
            }
        }

        DrawSelectionBox(viewProjection);
    }

    /// <summary>The billboard axes for the 3D view: the camera's own right, and the up that squares
    /// with it, so every marker layer faces the viewer the same way.</summary>
    private OpenTK.Mathematics.Vector3 Right() => _camera.Right;

    private OpenTK.Mathematics.Vector3 Up()
        => OpenTK.Mathematics.Vector3.Cross(_camera.Right, _camera.Forward);

    /// <summary>Outlines the selected entity with the same box picking tests against, so what the
    /// click targets and what the highlight shows are always the one volume.</summary>
    private void DrawSelectionBox(OpenTK.Mathematics.Matrix4 viewProjection)
    {
        if (_selectedEntity is not { Position: { } position } entity)
        {
            return;
        }

        (System.Numerics.Vector3 min, System.Numerics.Vector3 max) = LocalBoundsOf(entity);
        System.Numerics.Vector3 size = max - min;
        System.Numerics.Vector3 centre = (min + max) * 0.5f;
        System.Numerics.Matrix4x4 model =
            System.Numerics.Matrix4x4.CreateScale(size)
            * System.Numerics.Matrix4x4.CreateTranslation(centre)
            * entity.Rotation
            * System.Numerics.Matrix4x4.CreateTranslation(position);

        _selectionBox ??= new SelectionBoxLayer();
        _selectionBox.Draw(viewProjection, ToGl(model), new OpenTK.Mathematics.Vector3(1f, 0.85f, 0.2f));
    }

    private static OpenTK.Mathematics.Matrix4 ToGl(System.Numerics.Matrix4x4 m) => new(
        m.M11, m.M12, m.M13, m.M14,
        m.M21, m.M22, m.M23, m.M24,
        m.M31, m.M32, m.M33, m.M34,
        m.M41, m.M42, m.M43, m.M44);

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

        string status = _modelStatusText + (_modelLayer is { TextureBytesResident: > 0 } layer
            ? $" · {layer.TextureBytesResident / 1048576.0:F0} MB textures"
            : "");
        if (_modelStatusText.Length > 0 && ModelStatus.Text != status)
        {
            ModelStatus.Text = status;
        }
    }

    /// <summary>Holding shift multiplies the fly speed.</summary>
    private const float SprintFactor = 10f;

    /// <summary>World size of a billboard marker, and so of the box that makes a mesh-less entity
    /// clickable.</summary>
    private const float MarkerWorldSize = 3f;

    /// <summary>Floor on each axis of a pick box, so a flat or tiny model is still a target.</summary>
    private const float MinPickExtent = 0.35f;

    /// <summary>How far above the terrain the camera is held when ground collision is on.</summary>
    private const float GroundClearance = 1f;

    private void ApplyFlyKeys(float dt)
    {
        if (_flyKeys.Count == 0)
        {
            _flyHeldSeconds = 0f;
            return;
        }

        float forward = (_flyKeys.Contains(Key.W) ? 1 : 0) - (_flyKeys.Contains(Key.S) ? 1 : 0);
        float strafe = (_flyKeys.Contains(Key.D) ? 1 : 0) - (_flyKeys.Contains(Key.A) ? 1 : 0);
        float lift = (_flyKeys.Contains(Key.E) ? 1 : 0) - (_flyKeys.Contains(Key.Q) ? 1 : 0);

        OpenTK.Mathematics.Vector3 direction = _camera.MoveDirection(forward, strafe, lift);
        if (direction == OpenTK.Mathematics.Vector3.Zero)
        {
            _flyHeldSeconds = 0f;
            return;
        }

        _flyHeldSeconds += dt;
        float speed = _camera.MoveSpeed * Camera3D.SpeedFactor(_flyHeldSeconds);
        if (Keyboard.IsKeyDown(Key.LeftShift) || Keyboard.IsKeyDown(Key.RightShift))
        {
            speed *= SprintFactor;
        }

        _camera.Move(direction, speed * Math.Min(dt, 0.1f));
        HoldAboveTerrain();
    }

    /// <summary>A step that ends up under the terrain is lifted back out, so the ground cannot be
    /// crossed at any framerate.</summary>
    private void HoldAboveTerrain()
    {
        if (GroundCollision.IsChecked != true || _terrain is null) return;

        OpenTK.Mathematics.Vector3 position = _camera.Position;
        float floor = _terrain.HeightMetersAt((int)MathF.Round(position.X), (int)MathF.Round(position.Y))
            + GroundClearance;
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

    private void ShowEntities(Fc2World world, ArchetypeIndex archetypes)
    {
        _world = world;
        _selectedEntity = null;
        _positionedEntities = [.. world.Entities.Where(e => e.Position is not null)];

        _missionLayers = [.. _positionedEntities
            .GroupBy(e => e.LayerPathId)
            .Select(g => new MissionLayerRow { PathId = g.Key, Count = g.Count() })
            .OrderByDescending(r => r.Count)];
        MissionLayerList.ItemsSource = _missionLayers;

        _entityTree = EntityTreeNode.Build(_positionedEntities, archetypes);
        EntityTree.ItemsSource = _entityTree.Children;

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
    private float[] FillMarkers(List<WorldEntity> entities)
    {
        float[] stream = _markerStaging;
        for (int i = 0; i < entities.Count; i++)
        {
            WorldEntity entity = entities[i];
            System.Numerics.Vector3 position = entity.Position!.Value;
            (byte r, byte g, byte b) = MarkerColour(entity);
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

    /// <summary>The glyph, layer toggle and colour each drawn category gets. Categories a dedicated
    /// layer already owns are absent, which is what keeps a light from being drawn twice.</summary>
    private static readonly (EntityCategory Category, MarkerGlyph Glyph, MapLayer Layer, float R, float G, float B)[]
        DrawnCategories =
        [
            (EntityCategory.Event, MarkerGlyph.Diamond, LayerCatalog.EventNodes, 0.55f, 0.60f, 0.70f),
            (EntityCategory.Ai, MarkerGlyph.Cone, LayerCatalog.AiPoints, 0.45f, 0.75f, 0.55f),
            (EntityCategory.Entrance, MarkerGlyph.Doorway, LayerCatalog.Entrances, 0.85f, 0.70f, 0.35f),
            (EntityCategory.Emitter, MarkerGlyph.Burst, LayerCatalog.Emitters, 0.80f, 0.50f, 0.75f),
        ];

    /// <summary>Roughly how many pixels of viewport height a category glyph holds, and how far out
    /// it is worth drawing one at all.</summary>
    private const float GlyphPixels = 13f;
    private const float GlyphMaxDistance = 220f;

    /// <summary>Refills each category layer from the entities the model layer could not draw. Runs
    /// only on a marker rebuild, so it costs nothing per frame.</summary>
    private void RebuildCategoryMarkers()
    {
        if (_modelSet is not { } models || _categoryLayers.Count == 0)
        {
            return;
        }

        var byCategory = new Dictionary<EntityCategory, List<WorldEntity>>();
        foreach (WorldEntity entity in _visibleEntities)
        {
            if (models.ModelIndicesByEntity.ContainsKey(entity))
            {
                continue;
            }

            EntityCategory category = WorldEntityCategories.Of(entity.Node);
            if (category.HasOwnLayer())
            {
                continue;
            }

            if (!byCategory.TryGetValue(category, out List<WorldEntity>? bucket))
            {
                byCategory[category] = bucket = [];
            }
            bucket.Add(entity);
        }

        foreach ((EntityCategory category, _, _, float r, float g, float b) in DrawnCategories)
        {
            if (!_categoryLayers.TryGetValue(category, out EntityMarkerLayer? layer))
            {
                continue;
            }

            List<WorldEntity> entities = byCategory.GetValueOrDefault(category) ?? [];
            var stream = new float[Math.Max(1, entities.Count) * EntityMarkerLayer.Stride];
            for (int i = 0; i < entities.Count; i++)
            {
                System.Numerics.Vector3 position = entities[i].Position!.Value;
                int at = i * EntityMarkerLayer.Stride;
                stream[at] = position.X;
                stream[at + 1] = position.Y;
                stream[at + 2] = position.Z;
                stream[at + 3] = r;
                stream[at + 4] = g;
                stream[at + 5] = b;
            }
            layer.SetInstances(stream, entities.Count);
        }
    }

    /// <summary>Colour keyed to the archetype, shared by markers and model tints so the same kind
    /// of object reads the same in both forms.</summary>
    private static (byte R, byte G, byte B) MarkerColour(WorldEntity entity)
        => SurfaceTypeTexture.ColourFor((byte)(StableHash(entity.ArchetypeName) & 0x7F));

    private const int ModeModels = 0;
    private const int ModeMarkers = 1;

    private void EntityDrawMode_Changed(object sender, SelectionChangedEventArgs e) => _markersDirty = true;

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
        if (_entityTree is null)
        {
            return;
        }

        HashSet<string> layers = [.. _missionLayers.Where(r => r.IsVisible).Select(r => r.PathId)];
        int shown = EntityTreeNode.ApplyFilter(_entityTree, EntitySearch.Text.Trim(), layers);

        EntityCount.Text = shown == _positionedEntities.Count
            ? $"{_positionedEntities.Count:N0} entities"
            : $"{shown:N0} of {_positionedEntities.Count:N0} entities";
    }

    private void EntityTree_SelectedItemChanged(
        object sender, System.Windows.RoutedPropertyChangedEventArgs<object> e)
    {
        if (e.NewValue is EntityTreeNode { Entity: { } entity })
        {
            Select(entity);
        }
    }

    private void Select(WorldEntity entity)
    {
        _selectedEntity = entity;
        EntityHeading.Text = $"{entity.Name}  ({entity.LayerPathId})";

        // A standalone entity names no archetype, and three quarters of a world is standalone.
        ShowArchetypeButton.IsEnabled =
            entity.ArchetypeName.Length > 0 && _archetypes?.Winner(entity.ArchetypeName) is not null;
        OpenSectorButton.IsEnabled = true;

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

    /// <summary>Local-space extent of what an entity draws: the union of its models, or a box the
    /// size of the billboard for the mesh-less ones so they stay clickable.</summary>
    private (System.Numerics.Vector3 Min, System.Numerics.Vector3 Max) LocalBoundsOf(WorldEntity entity)
    {
        var half = new System.Numerics.Vector3(MarkerWorldSize * 0.5f);
        if (_modelSet is not { } set || !set.ModelIndicesByEntity.TryGetValue(entity, out int[]? models))
        {
            return (-half, half);
        }

        var min = new System.Numerics.Vector3(float.MaxValue);
        var max = new System.Numerics.Vector3(float.MinValue);
        foreach (int index in models)
        {
            min = System.Numerics.Vector3.Min(min, set.Models[index].LocalMin);
            max = System.Numerics.Vector3.Max(max, set.Models[index].LocalMax);
        }

        // A model whose bake produced nothing, and any axis too thin to hit, still needs a target.
        if (min.X > max.X)
        {
            return (-half, half);
        }

        for (int axis = 0; axis < 3; axis++)
        {
            if (max[axis] - min[axis] < MinPickExtent)
            {
                float centre = (min[axis] + max[axis]) * 0.5f;
                min[axis] = centre - MinPickExtent * 0.5f;
                max[axis] = centre + MinPickExtent * 0.5f;
            }
        }

        return (min, max);
    }

    /// <summary>Slab test; returns the near hit distance along the ray, or null when it misses. A
    /// ray starting inside the box counts as a hit at zero.</summary>
    private static float? RayHitsBox(
        System.Numerics.Vector3 origin, System.Numerics.Vector3 direction,
        System.Numerics.Vector3 min, System.Numerics.Vector3 max)
    {
        float near = 0f, far = float.MaxValue;
        for (int axis = 0; axis < 3; axis++)
        {
            float o = origin[axis], d = direction[axis];
            float lo = axis == 0 ? min.X : axis == 1 ? min.Y : min.Z;
            float hi = axis == 0 ? max.X : axis == 1 ? max.Y : max.Z;
            if (MathF.Abs(d) < 1e-6f)
            {
                if (o < lo || o > hi)
                {
                    return null;
                }
                continue;
            }

            float t1 = (lo - o) / d, t2 = (hi - o) / d;
            if (t1 > t2)
            {
                (t1, t2) = (t2, t1);
            }

            near = MathF.Max(near, t1);
            far = MathF.Min(far, t2);
            if (near > far)
            {
                return null;
            }
        }

        return near;
    }

    /// <summary>
    /// Picks the entity whose drawn volume the click ray enters first. Testing the real bounds rather
    /// than the projected origin is what makes a model clickable anywhere on its body, and taking the
    /// nearest hit along the ray means the thing in front wins instead of whatever happens to project
    /// closest to the cursor.
    /// </summary>
    private void PickEntityAt(System.Windows.Point point)
    {
        if (_visibleEntities.Count == 0)
        {
            return;
        }

        (OpenTK.Mathematics.Vector3 glOrigin, OpenTK.Mathematics.Vector3 glDirection) = _camera.Ray(
            point.X, point.Y, Viewport.ActualWidth, Math.Max(Viewport.ActualHeight, 1));
        var origin = new System.Numerics.Vector3(glOrigin.X, glOrigin.Y, glOrigin.Z);
        var direction = new System.Numerics.Vector3(glDirection.X, glDirection.Y, glDirection.Z);

        WorldEntity? best = null;
        float bestDistance = float.MaxValue;
        foreach (WorldEntity entity in _visibleEntities)
        {
            if (entity.Position is not { } position)
            {
                continue;
            }

            // Into the entity's own space rather than growing its box to fit the world axes, so a
            // rotated building is no easier to miss than an unrotated one.
            System.Numerics.Matrix4x4 inverse = System.Numerics.Matrix4x4.Transpose(entity.Rotation);
            System.Numerics.Vector3 localOrigin =
                System.Numerics.Vector3.Transform(origin - position, inverse);
            System.Numerics.Vector3 localDirection =
                System.Numerics.Vector3.Transform(direction, inverse);

            (System.Numerics.Vector3 min, System.Numerics.Vector3 max) = LocalBoundsOf(entity);
            if (RayHitsBox(localOrigin, localDirection, min, max) is { } distance && distance < bestDistance)
            {
                bestDistance = distance;
                best = entity;
            }
        }

        if (best is not null)
        {
            Select(best);
            if (_entityTree is not null)
            {
                EntityTreeNode.Reveal(_entityTree, best);
            }
        }
    }

    private void ShowArchetype_Click(object sender, System.Windows.RoutedEventArgs e)
    {
        if (_selectedEntity is { ArchetypeName.Length: > 0 } entity && _pendingLoad is { } loaded)
        {
            ArchetypeRequested?.Invoke(loaded.Map.Name, entity.ArchetypeName);
        }
    }

    private void OpenSector_Click(object sender, System.Windows.RoutedEventArgs e)
    {
        if (_selectedEntity is { } entity)
        {
            SectorEditorRequested?.Invoke(entity.HomeSector.SourcePath, entity.Id);
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
