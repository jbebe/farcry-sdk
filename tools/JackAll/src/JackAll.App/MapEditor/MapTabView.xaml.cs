using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using JackAll.App.FileHandlers.Fcb;
using JackAll.App.MapEditor.Gl;
using JackAll.Tools.World;
using OpenTK.Wpf;

namespace JackAll.App.MapEditor;

/// <summary>
/// The map editor tab: hierarchy, 3D viewport, inspector and entity library. The panels are
/// view-models over the loaded world; this class composes them and owns the viewport. GL objects are
/// created and destroyed only inside <see cref="Viewport_Render"/>, the one place a context is current.
/// </summary>
public partial class MapTabView : UserControl
{
    private MainViewModel? _vm;

    private sealed record PendingLoad(
        TerrainMap Map, WorldTerrain Terrain, SectorDetailLayers DetailLayers, TerrainLayerTable Table,
        Fc2World World, IReadOnlyList<WorldShape> Shapes, IReadOnlyList<WorldShape> Splines,
        IReadOnlyList<VegetationInstance> Vegetation, ScatterSet VegetationModels,
        NavMesh NavMesh,
        IReadOnlyList<WorldLight> Lights, IReadOnlyList<TriggerVolume> Triggers,
        ArchetypeIndex Archetypes, WorldModelSet Models, WorldEnvironment Environment);

    private readonly SelectionSet _selection = new();
    private readonly HierarchyViewModel _hierarchy;
    private readonly InspectorViewModel _inspector;
    private readonly EntityLibraryViewModel _library = new();

    /// <summary>The loaded world's unsaved additions, moves, edits and deletes.</summary>
    private WorldEditSession? _edits;

    /// <summary>What Ctrl+V places, and the entity it was copied from so a paste can draw with its
    /// meshes. Survives loading another world, which is how an entity moves between worlds.</summary>
    private (CopiedEntity Copy, WorldEntity Original)? _clipboard;

    private EntityModelLayer? _modelLayer;
    private List<WorldEntity> _positionedEntities = [];
    private bool _markersDirty;

    private RenderTargets? _targets;
    private PostProcess? _post;
    private ShadowCascades? _cascades;
    private AmbientOcclusion? _occlusion;
    private (int X, int Y) _cameraSector = (int.MinValue, int.MinValue);

    /// <summary>The viewport's own background, and so what shows in place of the sky.</summary>
    private static readonly OpenTK.Mathematics.Vector3 BackgroundColour = new(0.13f, 0.15f, 0.17f);

    /// <summary>The same colour for the scene buffer, which holds linear radiance and is sRGB-encoded
    /// by the composite - clear it with the authored value and the background comes back far lighter.
    /// </summary>
    private static readonly OpenTK.Mathematics.Vector3 BackgroundLinear =
        SceneLighting.Linear(BackgroundColour);

    /// <summary>The model stats line without its live texture-memory suffix.</summary>
    private string _modelStatusText = "";

    private ArchetypeIndex? _archetypes;

    /// <summary>Kept past the GL swap that clears the pending load, because picking and the
    /// selection outline need each entity's baked bounds every frame.</summary>
    private WorldModelSet? _modelSet;

    /// <summary>Raised for "Archetype in Library" - the host owns cross-tab navigation.</summary>
    public event Action<string, string>? ArchetypeRequested;

    /// <summary>Raised for "Open in XML editor", with the sector's game-relative path and the entity
    /// to open.</summary>
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

    /// <summary>The scatter that resolved to real meshes - rocks, grasses, facing bushes - drawn by
    /// the same layer the entities use, so they get its culling, detail tiers and materials.</summary>
    private EntityModelLayer? _vegetationModelLayer;

    /// <summary>Every drawable scatter instance, as the model layer's visibility pass wants them.
    /// Rebuilt into the layer whenever the camera crosses a sector.</summary>
    private ScatterInstance[] _vegetationInstances = [];
    private bool _vegetationDirty;
    private NavMeshLayer? _navMeshLayer;
    private EntityMarkerLayer? _lightLayer;

    /// <summary>One marker layer per mesh-less category, each with its own glyph and its own toggle
    /// in the layer list. Categories a dedicated layer already draws never reach these.</summary>
    private readonly Dictionary<EntityCategory, EntityMarkerLayer> _categoryLayers = [];
    private ShapeLayer? _triggerLayer;
    private SkyLayer? _sky;

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

        // Layer visibility is what the frame actually reads, so the redraw hangs off the property
        // rather than off the checkbox - code that hides a layer without a click still redraws.
        foreach (MapLayer layer in LayerCatalog.Layers)
        {
            layer.PropertyChanged += (_, _) => Viewport.InvalidateVisual();
        }

        // The toolbar's other controls have no such state to watch, so they are caught as routed
        // events on it. Checked and Unchecked are not redundant with Click: a box toggled by
        // anything other than a click - a binding, an automation peer - raises only the former, and
        // with the viewport idle there is no next frame to notice the change on its own.
        Toolbar.AddHandler(ButtonBase.ClickEvent,
            new System.Windows.RoutedEventHandler(Redraw), handledEventsToo: true);
        Toolbar.AddHandler(ToggleButton.CheckedEvent,
            new System.Windows.RoutedEventHandler(Redraw), handledEventsToo: true);
        Toolbar.AddHandler(ToggleButton.UncheckedEvent,
            new System.Windows.RoutedEventHandler(Redraw), handledEventsToo: true);
        Toolbar.AddHandler(Slider.ValueChangedEvent,
            new System.Windows.RoutedPropertyChangedEventHandler<double>(Redraw), handledEventsToo: true);

        _hierarchy = new HierarchyViewModel(_selection, () => (System.Numerics.Vector3)_camera.Position);
        _inspector = new InspectorViewModel(_selection);
        Hierarchy.DataContext = _hierarchy;
        Inspector.DataContext = _inspector;
        Library.DataContext = _library;

        _selection.Changed += Viewport.InvalidateVisual;
        _hierarchy.VisibleSetChanged += () =>
        {
            _markersDirty = true;
            Viewport.InvalidateVisual();
        };
        _inspector.Edited += (entity, moved) =>
        {
            _markersDirty |= moved;
            _hierarchy.RefreshModified([entity]);
            RefreshSaveButton();
            Viewport.InvalidateVisual();
        };
        Hierarchy.DeleteRequested += DeleteSelected;
        Hierarchy.PlaceRequested += PlaceAtViewCentre;
        Inspector.ShowArchetypeRequested += ShowArchetype;
        Inspector.OpenSectorRequested += OpenSector;
        Inspector.CopyRequested += CopySelected;
        Inspector.DeleteRequested += DeleteSelected;

        Viewport.Start(new GLWpfControlSettings { MajorVersion = 3, MinorVersion = 3 });
        Viewport.SizeChanged += Redraw;
    }

    private void Redraw(object? sender, System.Windows.RoutedEventArgs e) => Viewport.InvalidateVisual();

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
        if (_edits is { IsDirty: true } && System.Windows.MessageBox.Show(
                System.Windows.Window.GetWindow(this), "Loading discards the entity edits you have not saved.",
                "JackAll", System.Windows.MessageBoxButton.OKCancel) != System.Windows.MessageBoxResult.OK)
        {
            return;
        }

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
                WorldEnvironment environment = WorldEnvironment.Load(map.Name, vm.ReadByPath);
                Fc2World world = WorldLoader.Load(map, vm.ReadByPath, progress);
                IReadOnlyList<WorldShape> shapes =
                    WorldShapes.Load(map.Name, vm.ReadByPath, FcbDefinitionsProvider.Value.Value);
                IReadOnlyList<WorldShape> splines = WorldSplines.Load(map.Name, vm.ReadByPath);
                IReadOnlyList<VegetationInstance> scatter =
                    WorldVegetation.Load(map, vm.ReadByPath, FcbDefinitionsProvider.Value.Value, progress);
                // A scatter resource id is its own path's hash, so the ones naming geometry draw as
                // geometry; a RealTree has no parser and borrows an impostor card at its size.
                ScatterSet vegetationModels = WorldVegetation.Split(
                    scatter, WorldVegetation.ResourcesById(vm.AllKnownPaths), vm.ReadByPath, progress);
                IReadOnlyList<VegetationInstance> vegetation = vegetationModels.Markers;
                NavMesh navMesh = WorldNavMesh.Load(map, vm.ReadByPath, progress);
                IReadOnlyList<WorldLight> lights = WorldLights.Load(world.Entities);
                IReadOnlyList<TriggerVolume> triggers = WorldTriggers.Load(world.Entities);
                // The library single-player reads, and the one a save stages into - see
                // docs/docs/engine-internals/entity-instancing.md.
                ArchetypeIndex archetypes = vm.ArchetypesOf(map.Name, progress: progress).GetAwaiter().GetResult();
                WorldModelSet models = WorldModels.Load(world.Entities, archetypes, vm.ReadByPath, progress);
                return new PendingLoad(
                    map, terrain, detail, table, world, shapes, splines, vegetation, vegetationModels,
                    navMesh, lights, triggers, archetypes, models, environment);
            });

            WorldTerrain terrain = loaded.Terrain;
            _pendingLoad = loaded;
            ShowSurfaceLegend(terrain, loaded.Table);
            _archetypes = loaded.Archetypes;
            _library.Load(loaded.Archetypes);
            ShowEntities(loaded.World, map.SectorsPerSide);
            WorldModelSet models = loaded.Models;
            _modelStatusText =
                $"{models.ModelIndicesByEntity.Count:N0} of {_positionedEntities.Count:N0} entities " +
                $"have models ({models.Models.Count:N0} unique meshes" +
                (models.FailedPathCount > 0 ? $", {models.FailedPathCount:N0} meshes failed" : "") +
                (models.EntitiesWithoutMesh > 0 ? $", {models.EntitiesWithoutMesh:N0} named none" : "") + ")";
            LayerCatalog.Entities.Status = _modelStatusText;
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
            Viewport.InvalidateVisual();
        }
    }

    /// <summary>Holding shift multiplies the fly speed.</summary>
    private const float SprintFactor = 10f;

    /// <summary>How far above the terrain the camera is held when ground collision is on.</summary>
    private const float GroundClearance = 1f;

    /// <summary>Roughly how many pixels of viewport height a category glyph holds, and how far out
    /// it is worth drawing one at all.</summary>
    private const float GlyphPixels = 13f;
    private const float GlyphMaxDistance = 220f;

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
        bool viewport = Viewport.IsKeyboardFocused;
        if (e.Key == Key.Escape && IsDragging)
        {
            EndDrag(revert: true);
            e.Handled = true;
        }
        else if ((viewport || Hierarchy.IsKeyboardFocusWithin) && IsUndoKey(e.Key, out bool redo))
        {
            if (redo)
            {
                Redo();
            }
            else
            {
                Undo();
            }
            e.Handled = true;
        }
        else if (viewport && Keyboard.Modifiers == ModifierKeys.Control && e.Key == Key.C)
        {
            CopySelected();
            e.Handled = true;
        }
        else if (viewport && Keyboard.Modifiers == ModifierKeys.Control && e.Key == Key.V)
        {
            PasteAt(Mouse.GetPosition(Viewport));
            e.Handled = true;
        }
        else if (viewport && e.Key == Key.Delete)
        {
            DeleteSelected();
            e.Handled = true;
        }
        else if (viewport && Keyboard.Modifiers == ModifierKeys.None && e.Key is Key.T or Key.R)
        {
            (e.Key == Key.T ? MoveMode : RotateMode).IsChecked = true;
            e.Handled = true;
        }
        else if (viewport && e.Key is Key.W or Key.A or Key.S or Key.D or Key.Q or Key.E)
        {
            // Auto-repeat fires this for as long as the key is held, by which point the frame has
            // already asked for continuous redraws - only the first press has to wake it.
            if (_flyKeys.Add(e.Key))
            {
                Viewport.InvalidateVisual();
            }
            e.Handled = true;
        }
        base.OnPreviewKeyDown(e);
    }

    protected override void OnPreviewKeyUp(KeyEventArgs e)
    {
        if (_flyKeys.Remove(e.Key))
        {
            Viewport.InvalidateVisual();
        }
        base.OnPreviewKeyUp(e);
    }

    private void Viewport_MouseDown(object sender, MouseButtonEventArgs e)
    {
        Viewport.InvalidateVisual();
        Viewport.Focus();
        if (e.ChangedButton == MouseButton.Right)
        {
            _looking = true;
            _lastDragPoint = e.GetPosition(Viewport);
            Viewport.CaptureMouse();
        }
        else if (e.ChangedButton == MouseButton.Left && LayerCatalog.Entities.IsVisible)
        {
            System.Windows.Point point = e.GetPosition(Viewport);

            // The gizmo gets first refusal on the click: it stands in front of the entities it
            // moves, so a click that lands on it must not fall through and reselect what is behind.
            if (BeginDrag(point))
            {
                Viewport.CaptureMouse();
            }
            else
            {
                PickAt(point, Keyboard.Modifiers.HasFlag(ModifierKeys.Control));
            }
        }
    }

    private void Viewport_MouseUp(object sender, MouseButtonEventArgs e)
    {
        Viewport.InvalidateVisual();
        if (e.ChangedButton == MouseButton.Right)
        {
            _looking = false;
            Viewport.ReleaseMouseCapture();
        }
        else if (e.ChangedButton == MouseButton.Left && IsDragging)
        {
            EndDrag(revert: false);
        }
    }

    private void Viewport_MouseMove(object sender, MouseEventArgs e)
    {
        System.Windows.Point p = e.GetPosition(Viewport);
        if (IsDragging)
        {
            DragTo(p);
            Viewport.InvalidateVisual();
            return;
        }

        if (_looking)
        {
            _camera.Look((float)(p.X - _lastDragPoint.X), (float)(p.Y - _lastDragPoint.Y));
            _lastDragPoint = p;
            Viewport.InvalidateVisual();
            return;
        }

        // A plain hover changes nothing unless it moves onto or off a gizmo handle, and redrawing the
        // scene for every mouse move across the viewport is the cost this mode exists to avoid.
        if (HoverGizmo(p))
        {
            Viewport.InvalidateVisual();
        }
    }

    private void Viewport_MouseWheel(object sender, MouseWheelEventArgs e)
    {
        Viewport.InvalidateVisual();
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
    }

    private void ShowEntities(Fc2World world, int sectorsPerSide)
    {
        _edits = new WorldEditSession(world, sectorsPerSide);
        _inspector.Session = _edits;
        _inspector.Archetypes = _archetypes;
        StartHistory();
        CancelDrag();
        _selection.Clear();
        _positionedEntities = [.. world.Entities.Where(e => e.Position is not null)];
        EntitySetChanged();
    }

    private void RefreshSaveButton() => SaveButton.IsEnabled = _edits is { IsDirty: true };

    /// <summary>Refreshes everything listing the world's entities after one is added or removed.</summary>
    private void EntitySetChanged()
    {
        if (_edits is null || _archetypes is null)
        {
            return;
        }
        _hierarchy.Rebuild(_positionedEntities, _edits.Deleted, _archetypes, _edits.IsModified);
        RefreshSaveButton();
        Viewport.InvalidateVisual();
    }

    private async void Save_Click(object sender, System.Windows.RoutedEventArgs e)
    {
        if (_vm is null || _edits is not { IsDirty: true } edits)
        {
            return;
        }

        SaveButton.IsEnabled = false;
        try
        {
            (int staged, IReadOnlyList<string> report) = await _vm.SaveWorldEdits(edits);
            StatusText.Text = $"Staged {staged} files into the workspace";
            EntitySetChanged();
            System.Windows.MessageBox.Show(System.Windows.Window.GetWindow(this),
                string.Join('\n', report), "Map edits saved");
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.IO.InvalidDataException or System.IO.IOException)
        {
            RefreshSaveButton();
            System.Windows.MessageBox.Show(System.Windows.Window.GetWindow(this), ex.Message, "JackAll",
                System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Warning);
        }
    }
}
