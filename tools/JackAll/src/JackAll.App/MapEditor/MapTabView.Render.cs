using System.Numerics;
using JackAll.App.MapEditor.Gpu;
using JackAll.Tools.World;
using Vortice.Direct3D11;
using Vortice.Mathematics;
using static JackAll.App.MapEditor.Gpu.GpuDevice;

namespace JackAll.App.MapEditor;

/// <summary>The viewport's frame: the world swap, the passes, and the indicators drawn over them.</summary>
public partial class MapTabView
{
    private void Viewport_Render(TimeSpan delta)
    {
        FrameStats stats = _stats ??= new FrameStats();

        // Still the previous frame's decision, so it says whether this frame was asked for as part of a run.
        bool continuous = Viewport.RenderContinuously;
        stats.BeginFrame();
        try
        {
            DrawFrame(delta, stats);
        }
        finally
        {
            if (stats.EndFrame(delta, continuous) is { } line)
            {
                ShowStats(line);
            }
        }
    }

    private void DrawFrame(TimeSpan delta, FrameStats stats)
    {
        // A viewport that only redraws on demand hands out the whole idle gap as its delta, and
        // advancing the camera or the clock by seconds at once flings both.
        float step = MathF.Min((float)delta.TotalSeconds, MaxStep);
        SceneLighting.Time += step;

        GpuState.BeginFrame();
        SyncPresentation();

        if (_pendingLoad is { } pending)
        {
            // Swapped inside the frame, where everything the layers own is made and released.
            _pendingLoad = null;
            ReleaseWorld();
            _terrain = pending.Terrain;
            _waterLayer = new WaterLayer(pending.Terrain);
            _waterLayer.SetVisible(_camera.Position);
            _shapeLayer = new ShapeLayer(pending.Shapes);
            _splineLayer = new ShapeLayer(pending.Splines);
            _vegetationLayer = new EntityMarkerLayer(
                WorldMarkers.BuildVegetationMarkers(pending.Vegetation), pending.Vegetation.Count);
            _vegetationModelLayer = new EntityModelLayer(
                pending.VegetationModels.Models, _vm is { } vegVm ? vegVm.ReadByPath : _ => null,
                ScatterCapacity(pending.VegetationModels))
            {
                Scatter = pending.VegetationBySector,
            };
            _vegetationDirty = true;
            _loadedNavMesh = null;
            LayerCatalog.NavMesh.Status = "Read the first time it is shown.";
            // Sized for every positioned entity, because which category an entity lands in is not
            // known until the model layer has had its pass; the live counts do the real limiting.
            int categoryCapacity = pending.World.Entities.Count(e => e.Position is not null);
            foreach ((EntityCategory category, _, _, _, _, _) in WorldMarkers.DrawnCategories)
            {
                _categoryLayers[category] = new EntityMarkerLayer(categoryCapacity);
            }
            _modelSet = pending.Models;
            _modelLayer = new EntityModelLayer(
                pending.Models, _vm is { } modelVm ? modelVm.ReadByPath : _ => null, WorldMarkers.MarkerColour);
            _visibleDirty = true;
            _heightTexture = new HeightTexture(pending.Terrain);
            _surfaceTexture = new SurfaceTypeTexture(pending.Terrain);
            _terrainTextures = _vm is { } vm
                ? new TerrainTextureSet(pending.Map, pending.DetailLayers, pending.Table, vm.ReadByPath)
                : null;
            _terrainMesh = new TerrainMesh3D(_heightTexture, _surfaceTexture, _terrainTextures);
            SceneLighting.Fog = pending.Environment;
            LayerCatalog.Textures.Status = _terrainTextures is { } set
                ? $"{set.LayersLoaded} of {pending.Table.Layers.Count} layer textures loaded " +
                  $"({set.DetailBytes / (1024 * 1024)} MB); blend mask {set.WeightSide}x{set.WeightSide}." +
                  (set.FailedLayers.Count > 0 ? $" Missing: {string.Join(", ", set.FailedLayers)}." : "")
                : "No terrain textures loaded.";
        }

        long rebuildStart = System.Diagnostics.Stopwatch.GetTimestamp();

        // The model tiers are keyed to the camera's sector, so crossing a boundary refills the
        // instance streams from the ring around the new one.
        var sector = WorldModels.SectorOf(_camera.Position);
        if (sector != _cameraSector)
        {
            _cameraSector = sector;
            _modelsDirty = true;
            _vegetationDirty = true;
            _waterLayer?.SetVisible(_camera.Position);
        }

        // Grass is cut off by distance, so it follows the camera inside a sector as well.
        if ((_camera.Position - _grassStreamedFrom).LengthSquared() > GrassStep * GrassStep)
        {
            _vegetationDirty = true;
        }

        if (_vegetationDirty && _vegetationModelLayer is { } vegetationModels)
        {
            _vegetationDirty = false;
            _grassStreamedFrom = _camera.Position;
            vegetationModels.Stream(_camera.Position, DrawRing);
        }

        RebuildOverlays();

        if (LayerCatalog.NavMesh.IsVisible && _navMeshLayer is null)
        {
            if (_loadedNavMesh is { } navMesh)
            {
                _navMeshLayer = new NavMeshLayer(navMesh);
                _loadedNavMesh = null;
            }
            else
            {
                LoadNavMesh();
            }
        }

        // Rebuilt here, in the frame, rather than on the click that caused it.
        bool markersMoved = _visibleDirty || _moved.Count > 0;
        if (_visibleDirty)
        {
            _visibleDirty = false;
            _modelLayer?.SetVisible(_hierarchy.VisibleEntities);
            _markerGroups = _modelSet is { } models ? WorldMarkers.GroupByCategory(_hierarchy.VisibleEntities, models) : [];
            _modelsDirty = true;
        }
        if (_moved.Count > 0)
        {
            _modelsDirty |= _modelLayer?.Moved(_moved) == true;
            _moved.Clear();
        }
        if (_modelsDirty)
        {
            _modelsDirty = false;
            _modelLayer?.Stream(_camera.Position, DrawRing);
        }
        if (markersMoved)
        {
            WorldMarkers.FillCategoryMarkers(_markerGroups, _categoryLayers);
        }
        stats.Rebuilt(System.Diagnostics.Stopwatch.GetElapsedTime(rebuildStart));

        int width = Viewport.PixelWidth;
        int height = Viewport.PixelHeight;
        ID3D11RenderTargetView backBuffer = Viewport.BackBuffer!;

        // Nothing loaded yet, so there is no scene to resolve - clear the back buffer and leave. It
        // is the only clear that reaches an 8-bit buffer directly, so it takes the colour as authored.
        if (_terrain is null || _terrainMesh is null)
        {
            Context.ClearRenderTargetView(backBuffer, new Color4(BackgroundColour, 1f));
            return;
        }

        _targets ??= new RenderTargets();
        _targets.Resize(width, height, SceneLighting.Demo);
        _post ??= new PostProcess();
        _present ??= new PresentPass();

        ApplyFlyKeys(step);
        float aspect = (float)(Viewport.ActualWidth / Math.Max(Viewport.ActualHeight, 1));
        Matrix4x4 viewProjection = _camera.View() * _camera.Projection(aspect);

        SceneLighting.Exposure = (float)ExposureSlider.Value;
        bool drawEntityModels = LayerCatalog.Entities.IsVisible;

        SceneLighting.Shadows = null;
        SceneLighting.OcclusionMap = null;
        if (SceneLighting.Demo)
        {
            SceneLighting.Shadows = DrawShadowMaps(drawEntityModels, aspect);
            SceneLighting.OcclusionMap = DrawOcclusion(drawEntityModels, viewProjection, aspect);
        }

        _targets.BindScene();
        FrameConstants.Upload(width, height);
        Context.ClearRenderTargetView(_targets.Scene.Target, new Color4(BackgroundLinear, 1f));

        // The occlusion pass leaves behind the depth its prepass laid down, which is exactly what
        // the opaque pass tests against - so only the colour needs clearing when it ran.
        if (SceneLighting.OcclusionMap is null)
        {
            Context.ClearDepthStencilView(_targets.Depth.Stencil, DepthStencilClearFlags.Depth, 1f, 0);
        }

        if (SceneLighting.Demo)
        {
            _sky ??= new SkyLayer();
            _sky.Draw(viewProjection, _camera.Position);
        }
        if (LayerCatalog.Heightmap.IsVisible)
        {
            _terrainMesh.Draw(viewProjection, _camera.Position, new TerrainDrawOptions(
                ShowTextures: LayerCatalog.Textures.IsVisible,
                TintBySurfaceType: LayerCatalog.SurfaceData.IsVisible,
                ShowShadow: LayerCatalog.Shadow.IsVisible));
        }

        // Every opaque surface goes in before the water. The water blends and never writes depth,
        // so it can only occlude what is already in the depth buffer: drawn first, a boat below
        // the surface paints straight over it and reads as floating in a hole.
        if (LayerCatalog.Vegetation.IsVisible)
        {
            _vegetationModelLayer?.Draw(viewProjection, _camera.Position);
        }
        if (drawEntityModels)
        {
            _modelLayer?.Draw(viewProjection, _camera.Position);
        }

        if (LayerCatalog.Water.IsVisible && _waterLayer is { HasVisibleWater: true })
        {
            // The water reads both what is behind it and how far away that is, and a pass cannot
            // sample a texture it is drawing into. Depth as well as colour, because the water is
            // depth-testing against the live buffer while it reads this one. The flat pane reads
            // neither, so the copy is part of the presentation.
            if (SceneLighting.Demo)
            {
                Context.OMSetRenderTargets((ID3D11RenderTargetView)null!);
                _targets.CopyScene();
                _targets.BindScene();
            }

            _waterLayer.Draw(viewProjection, _camera.Position, _targets);
        }

        // The scene is complete, so resolve it to a displayable image here. Everything after this
        // point is an indicator drawn at exactly the colour it was authored in - run a selection box
        // or a category glyph through the tonemap and it comes back muted.
        _targets.BindPresent();
        _post.Composite(_targets.Scene.View);

        // Indicators from here on: lines and markers that are meant to read through the water, not
        // sit under it.
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
            _vegetationLayer?.Draw(viewProjection, _camera.Position, Right(), Up(),
                MarkerStyle.World(2f));
        }

        if (LayerCatalog.Triggers.IsVisible)
        {
            _triggerLayer?.Draw(viewProjection);
        }

        if (LayerCatalog.Links.IsVisible)
        {
            _linkLayer?.Draw(viewProjection);
        }

        if (LayerCatalog.Lights.IsVisible)
        {
            _lightLayer?.Draw(viewProjection, _camera.Position, Right(), Up(),
                MarkerStyle.World(4f));
        }

        if (LayerCatalog.NavMesh.IsVisible)
        {
            _navMeshLayer?.Draw(viewProjection, _camera.Position, NavMeshSurface.IsChecked == true,
                NavMeshEdges.IsChecked == true, NavMeshLinks.IsChecked == true);
        }

        foreach ((EntityCategory category, MarkerGlyph glyph, MapLayer layer, _, _, _) in WorldMarkers.DrawnCategories)
        {
            if (layer.IsVisible && _categoryLayers.TryGetValue(category, out EntityMarkerLayer? markers))
            {
                markers.Draw(viewProjection, _camera.Position, Right(), Up(),
                    MarkerStyle.Screen(glyph, GlyphPixels, (float)Viewport.ActualHeight,
                        Camera3D.VerticalFovRadians, GlyphMaxDistance));
            }
        }

        DrawSelection(viewProjection);
        DrawHandles(viewProjection);

        _present.Draw(backBuffer, _targets.Present.View, width, height);

        // Keep drawing only while something is still moving. The water is the one thing in the scene
        // that animates, and only under the presentation. A texture reaches the GPU inside the very
        // Draw this frame may have skipped, so a layer only counts as streaming while it is drawn -
        // untick Entities mid-load and waiting on it would spin forever, making no progress.
        // Otherwise this frame is the one the viewport holds until an input or a control asks again.
        Viewport.RenderContinuously = _flyKeys.Count > 0 || _looking
            || (SceneLighting.Demo && LayerCatalog.Water.IsVisible
                && _waterLayer is { HasVisibleWater: true })
            || (drawEntityModels && _modelLayer is { Streaming: true })
            || (LayerCatalog.Vegetation.IsVisible && _vegetationModelLayer is { Streaming: true });
    }

    /// <summary>
    /// The device went away under the viewport - a driver reset or update. Everything the old one
    /// held is let go, a new one is made, and the last world is swapped in again on the next frame;
    /// the edits live in the session, not on the GPU, so they survive.
    /// </summary>
    private void Viewport_DeviceLost()
    {
        ReleaseGpu();
        Viewport.ReleaseSwapChain();
        GpuDevice.Destroy();
        try
        {
            GpuDevice.Create();
        }
        catch (Exception ex) when (GpuDevice.IsFailure(ex))
        {
            ShowViewportFailure(ex);
            return;
        }
        _pendingLoad = _lastLoad;
        _overlaysDirty = true;
        _handlesDirty = true;
        Viewport.RequestFrame();
    }

    /// <summary>What a world swap replaces: every layer made from the loaded world.</summary>
    private void ReleaseWorld()
    {
        Release(ref _terrainMesh);
        Release(ref _heightTexture);
        Release(ref _surfaceTexture);
        Release(ref _terrainTextures);
        Release(ref _waterLayer);
        Release(ref _shapeLayer);
        Release(ref _splineLayer);
        Release(ref _vegetationLayer);
        Release(ref _vegetationModelLayer);
        Release(ref _navMeshLayer);
        Release(ref _modelLayer);
        foreach (EntityMarkerLayer layer in _categoryLayers.Values)
        {
            layer.Dispose();
        }
        _categoryLayers.Clear();
    }

    /// <summary>Every GPU object the tab holds, for a device that is going away: the world's, and
    /// what the frame and the overlays make for themselves.</summary>
    private void ReleaseGpu()
    {
        ReleaseWorld();
        Release(ref _lightLayer);
        Release(ref _triggerLayer);
        Release(ref _linkLayer);
        Release(ref _handleLines);
        Release(ref _selectionBox);
        Release(ref _translateGizmo);
        Release(ref _rotateGizmo);
        Release(ref _sky);
        Release(ref _occlusion);
        Release(ref _cascades);
        Release(ref _post);
        Release(ref _present);
        Release(ref _targets);
        Release(ref _stats);
        _terrain = null;
        _overlaySources = null;
    }

    /// <summary>Released and forgotten, so the next frame's <c>??=</c> makes it again.</summary>
    private static void Release<T>(ref T? field) where T : class, IDisposable
    {
        field?.Dispose();
        field = null;
    }

    /// <summary>The longest step the camera and the water clock will take from one frame. An idle
    /// viewport can be seconds between frames, and neither is meant to cover that in one go.</summary>
    private const float MaxStep = 0.1f;


    /// <summary>How far the camera moves before the grass is cut off again around where it now is.</summary>
    private const float GrassStep = 4f;

    /// <summary>How far out, in sectors, a model or a scatter placement still draws as geometry.
    /// The far tier is the presentation's; off it, only the near ring draws.</summary>
    private static int DrawRing
        => SceneLighting.Demo ? WorldModels.CoarseRadius : WorldModels.FineRadius;

    /// <summary>
    /// Takes the Demo checkbox and, when it has moved, everything that follows from it: the draw
    /// ring changes, so both instance streams are rebuilt, and switching off frees the 64 MB cascade
    /// array. Read in the frame rather than on the click, beside everything else the GPU holds.
    /// </summary>
    private void SyncPresentation()
    {
        bool demo = DemoMode.IsChecked == true;
        if (demo == SceneLighting.Demo)
        {
            return;
        }

        SceneLighting.Demo = demo;
        _modelsDirty = true;
        _vegetationDirty = true;

        // The occlusion and sky layers own nothing but shader programs, so they are kept: disposing
        // them would reclaim a few KB and cost three compiles the next time Demo comes back on.
        if (!demo)
        {
            Release(ref _cascades);
        }
    }

    /// <summary>
    /// A depth-only pass from the camera, and the occlusion computed off it. The prepass is the same
    /// <c>DrawDepth</c> the cascades use, pointed at the eye instead of the sun - occlusion has to be
    /// ready before the surfaces that consume it are shaded, and a forward pass cannot sample a
    /// depth buffer it is still writing.
    /// </summary>
    private ID3D11ShaderResourceView DrawOcclusion(bool drawEntityModels, Matrix4x4 viewProjection, float aspect)
    {
        _occlusion ??= new AmbientOcclusion();

        _targets!.BindDepth();
        Context.ClearDepthStencilView(_targets.Depth.Stencil, DepthStencilClearFlags.Depth, 1f, 0);
        DrawCasters(viewProjection, drawEntityModels);

        return _occlusion.Render(_targets, _camera.Projection(aspect));
    }

    /// <summary>Everything that writes depth, from one viewpoint. The cascades and the prepass want
    /// the same set - the only difference between them is where it is seen from.</summary>
    private void DrawCasters(Matrix4x4 viewProjection, bool drawEntityModels)
    {
        if (LayerCatalog.Heightmap.IsVisible)
        {
            _terrainMesh?.DrawDepth(viewProjection, _camera.Position);
        }
        if (LayerCatalog.Vegetation.IsVisible)
        {
            _vegetationModelLayer?.DrawDepth(viewProjection, _camera.Position);
        }
        if (drawEntityModels)
        {
            _modelLayer?.DrawDepth(viewProjection, _camera.Position);
        }
    }

    /// <summary>
    /// The sun's depth of the scene, one pass per cascade. The terrain casts as well as receives:
    /// the lightmap baked into it covers the ground and nothing standing on it, so a hut in a hill's
    /// shade would stay lit without this.
    /// </summary>
    /// <remarks>The bias is entirely in the lookup rather than in the rasterizer here, because the
    /// terrain's own draw sets and clears a depth bias for its coarse patch and would take an outer
    /// one with it.</remarks>
    private ShadowCascades DrawShadowMaps(bool drawEntityModels, float aspect)
    {
        _cascades ??= new ShadowCascades();
        _cascades.Fit(_camera, aspect);

        for (int cascade = 0; cascade < ShadowCascades.Count; cascade++)
        {
            _cascades.BeginCascade(cascade);
            DrawCasters(_cascades.Matrices[cascade], drawEntityModels);
        }
        return _cascades;
    }

    /// <summary>
    /// How many placements of one scatter mesh a frame may hold. Sizing to the world's own count is
    /// what the entity layer does, but the scatter counts differently: world 1 places 794,000 tufts
    /// of one grass alone, and a buffer for all of them would be 29 MB of the ~1% ever inside the
    /// draw radius. Anything past the cap in a single ring simply does not draw.
    /// </summary>
    private const int MaxScatterPerMesh = 40_000;

    private static int[] ScatterCapacity(ScatterSet scatter)
    {
        var capacity = new int[scatter.Models.Count];
        foreach (ScatterInstance instance in scatter.Instances)
        {
            capacity[instance.Model]++;
        }

        for (int i = 0; i < capacity.Length; i++)
        {
            capacity[i] = Math.Min(capacity[i], MaxScatterPerMesh);
        }

        return capacity;
    }

    /// <summary>The billboard axes for the 3D view: the camera's own right, and the up that squares
    /// with it, so every marker layer faces the viewer the same way.</summary>
    private Vector3 Right() => _camera.Right;

    private Vector3 Up() => Vector3.Cross(_camera.Right, _camera.Forward);

    /// <summary>Reads the loaded map's navmesh off the UI thread; the frame after it arrives uploads it.</summary>
    private async void LoadNavMesh()
    {
        if (_navMeshLoading || _vm is not { } vm || _loadedMap is not { } map)
        {
            return;
        }

        _navMeshLoading = true;
        LayerCatalog.NavMesh.Status = $"Reading {map.Name} navmesh";
        NavMesh mesh = await Task.Run(() => WorldNavMesh.Load(map, vm.ReadByPath));
        _navMeshLoading = false;
        if (map == _loadedMap)
        {
            _loadedNavMesh = mesh;
            LayerCatalog.NavMesh.Status = WorldMarkers.Describe(mesh);
        }
        Viewport.RequestFrame();
    }

    private void ShowStats(string line)
    {
        FpsText.Text = line;
        string status = _modelStatusText + (_modelLayer is { TextureBytesResident: > 0 } layer
            ? $" · {layer.TextureBytesResident / 1048576.0:F0} MB textures"
            : "");
        if (_modelStatusText.Length > 0 && LayerCatalog.Entities.Status != status)
        {
            LayerCatalog.Entities.Status = status;
        }
    }

}
