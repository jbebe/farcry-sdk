using JackAll.App.MapEditor.Gl;
using JackAll.Tools.World;
using OpenTK.Graphics.OpenGL4;

namespace JackAll.App.MapEditor;

/// <summary>The viewport's frame: the world swap, the passes, and the indicators drawn over them.</summary>
public partial class MapTabView
{
    private void Viewport_Render(TimeSpan delta)
    {
        ShowFrameRate(delta);

        // A viewport that only redraws on demand hands out the whole idle gap as its delta, and
        // advancing the camera or the clock by seconds at once flings both.
        float step = MathF.Min((float)delta.TotalSeconds, MaxStep);
        SceneLighting.Time += step;

        GlDebug.Install();

        // Scoped for the throwing paths below - a shader typo in a lazy layer, a failed world swap.
        // Leaving a pass framebuffer bound blacks the viewport for the rest of the session.
        using GlState frame = new();
        GlState.BeginFrame();

        SyncPresentation();

        if (_pendingLoad is { } pending)
        {
            // Swap inside the render callback so GL resources live and die with a context.
            _pendingLoad = null;
            _terrainMesh?.Dispose();
            _heightTexture?.Dispose();
            _surfaceTexture?.Dispose();
            _terrainTextures?.Dispose();
            _waterLayer?.Dispose();
            _terrain = pending.Terrain;
            _shapeLayer?.Dispose();
            _splineLayer?.Dispose();
            _waterLayer = new WaterLayer(pending.Terrain);
            _waterLayer.SetVisible(_camera.Position);
            _shapeLayer = new ShapeLayer(pending.Shapes);
            _splineLayer = new ShapeLayer(pending.Splines);
            _vegetationLayer?.Dispose();
            _vegetationLayer = new EntityMarkerLayer(
                WorldMarkers.BuildVegetationMarkers(pending.Vegetation), pending.Vegetation.Count);
            _vegetationModelLayer?.Dispose();
            _vegetationModelLayer = new EntityModelLayer(
                pending.VegetationModels.Models, _vm is { } vegVm ? vegVm.ReadByPath : _ => null,
                ScatterCapacity(pending.VegetationModels));
            _vegetationInstances = pending.VegetationModels.Instances;
            _vegetationDirty = true;
            _navMeshLayer?.Dispose();
            _navMeshLayer = new NavMeshLayer(pending.NavMesh);
            LayerCatalog.NavMesh.Status = WorldMarkers.Describe(pending.NavMesh);
            // Sized for every positioned entity, because which category an entity lands in is not
            // known until the model layer has had its pass; the live counts do the real limiting.
            int categoryCapacity = pending.World.Entities.Count(e => e.Position is not null);
            foreach (EntityMarkerLayer stale in _categoryLayers.Values)
            {
                stale.Dispose();
            }
            _categoryLayers.Clear();
            foreach ((EntityCategory category, _, _, _, _, _) in WorldMarkers.DrawnCategories)
            {
                _categoryLayers[category] = new EntityMarkerLayer(categoryCapacity);
            }
            _modelLayer?.Dispose();
            _modelSet = pending.Models;
            _modelLayer = new EntityModelLayer(
                pending.Models, _vm is { } modelVm ? modelVm.ReadByPath : _ => null, WorldMarkers.MarkerColour);
            _markersDirty = true;
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

        // The model tiers are keyed to the camera's sector, so crossing a boundary re-buckets the
        // instance streams the same way a mission-layer toggle does.
        var sector = ((int)MathF.Floor(_camera.Position.X / WorldModels.SectorMeters),
            (int)MathF.Floor(_camera.Position.Y / WorldModels.SectorMeters));
        if (sector != _cameraSector)
        {
            _cameraSector = sector;
            _markersDirty = true;
            _vegetationDirty = true;
            _waterLayer?.SetVisible(_camera.Position);
        }

        if (_vegetationDirty && _vegetationModelLayer is { } vegetationModels)
        {
            _vegetationDirty = false;
            vegetationModels.SetVisible(_vegetationInstances, _camera.Position, DrawRing);
        }

        RebuildOverlays();

        // Toggling a mission layer changes which markers exist, so the instance stream is rebuilt
        // here where a GL context is current rather than on the click.
        if (_markersDirty)
        {
            _markersDirty = false;
            _modelLayer?.SetVisible(_hierarchy.VisibleEntities, _camera.Position, DrawRing);
            if (_modelSet is { } models && _categoryLayers.Count > 0)
            {
                WorldMarkers.RebuildCategoryMarkers(_hierarchy.VisibleEntities, models, _categoryLayers);
            }
        }

        int width = Viewport.FrameBufferWidth;
        int height = Viewport.FrameBufferHeight;

        // Collapsed or mid-layout: there is no surface to size the offscreen targets against.
        if (width <= 0 || height <= 0)
        {
            return;
        }

        GL.Viewport(0, 0, width, height);

        // Nothing loaded yet, so there is no scene to resolve - clear the control's own buffer and
        // leave, rather than clearing an offscreen one nothing will read. This one is the only clear
        // that reaches an 8-bit buffer directly, so it takes the colour as authored.
        if (_terrain is null || _terrainMesh is null)
        {
            GL.ClearColor(BackgroundColour.X, BackgroundColour.Y, BackgroundColour.Z, 1f);
            GL.Clear(ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit);
            return;
        }

        _targets ??= new RenderTargets();
        _targets.Resize(width, height, SceneLighting.Demo);
        _post ??= new PostProcess();

        ApplyFlyKeys(step);
        float aspect = (float)(Viewport.ActualWidth / Math.Max(Viewport.ActualHeight, 1));
        OpenTK.Mathematics.Matrix4 viewProjection = _camera.View() * _camera.Projection(aspect);

        SceneLighting.Exposure = (float)ExposureSlider.Value;
        bool drawEntityModels = LayerCatalog.Entities.IsVisible;

        SceneLighting.Shadows = null;
        SceneLighting.OcclusionMap = 0;
        if (SceneLighting.Demo)
        {
            SceneLighting.Shadows = DrawShadowMaps(drawEntityModels, aspect);
            SceneLighting.OcclusionMap = DrawOcclusion(drawEntityModels, viewProjection, aspect);
        }

        GL.BindFramebuffer(FramebufferTarget.Framebuffer, _targets.SceneFramebuffer);
        GL.Viewport(0, 0, width, height);

        GL.ClearColor(BackgroundLinear.X, BackgroundLinear.Y, BackgroundLinear.Z, 1f);

        // The occlusion pass leaves behind the depth its prepass laid down, which is exactly what
        // the opaque pass tests against - so only the colour needs clearing when it ran.
        GL.Clear(SceneLighting.OcclusionMap == 0
            ? ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit
            : ClearBufferMask.ColorBufferBit);

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
            // sample an image the draw it is part of is bound to. Depth as well as colour, because
            // the water is depth-testing against the live buffer while it reads this one. The flat
            // pane reads neither, so the copy is part of the presentation.
            if (SceneLighting.Demo)
            {
                GL.BindFramebuffer(FramebufferTarget.ReadFramebuffer, _targets.SceneFramebuffer);
                GL.BindFramebuffer(FramebufferTarget.DrawFramebuffer, _targets.ColourCopyFramebuffer);
                GL.BlitFramebuffer(0, 0, width, height, 0, 0, width, height,
                    ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit,
                    BlitFramebufferFilter.Nearest);
                GL.BindFramebuffer(FramebufferTarget.Framebuffer, _targets.SceneFramebuffer);
            }

            _waterLayer.Draw(viewProjection, _camera.Position, _targets);
        }

        // The scene is complete, so resolve it to a displayable image here. Everything after this
        // point is an indicator drawn at exactly the colour it was authored in - run a selection box
        // or a category glyph through the tonemap and it comes back muted.
        GL.BindFramebuffer(FramebufferTarget.Framebuffer, _targets.PresentFramebuffer);
        _post.Composite(_targets.Colour);

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
            _vegetationLayer?.Draw(viewProjection, _camera.Position, Right(), Up(), flattenZ: false,
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
            _lightLayer?.Draw(viewProjection, _camera.Position, Right(), Up(), flattenZ: false,
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
                markers.Draw(viewProjection, _camera.Position, Right(), Up(), flattenZ: false,
                    MarkerStyle.Screen(glyph, GlyphPixels, (float)Viewport.ActualHeight,
                        Camera3D.VerticalFovRadians, GlyphMaxDistance));
            }
        }

        DrawSelection(viewProjection);
        DrawHandles(viewProjection);

        // Colour only: the control's depth is a 24-bit renderbuffer and the scene's is a 32-bit
        // float texture, and a depth blit between two formats is an error.
        GL.BindFramebuffer(FramebufferTarget.ReadFramebuffer, _targets.PresentFramebuffer);
        GL.BindFramebuffer(FramebufferTarget.DrawFramebuffer, Viewport.Framebuffer);
        GL.BlitFramebuffer(0, 0, width, height, 0, 0, width, height,
            ClearBufferMask.ColorBufferBit, BlitFramebufferFilter.Nearest);

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

    /// <summary>The longest step the camera and the water clock will take from one frame. An idle
    /// viewport can be seconds between frames, and neither is meant to cover that in one go.</summary>
    private const float MaxStep = 0.1f;

    /// <summary>How far out, in sectors, a model or a scatter placement still draws as geometry.
    /// The far tier is the presentation's; off it, only the near ring draws.</summary>
    private static int DrawRing
        => SceneLighting.Demo ? WorldModels.CoarseRadius : WorldModels.FineRadius;

    /// <summary>
    /// Takes the Demo checkbox and, when it has moved, everything that follows from it: the draw
    /// ring changes, so both instance streams are rebuilt, and switching off frees the 64 MB cascade
    /// array. Read here rather than on the click because that freeing needs a current GL context.
    /// </summary>
    private void SyncPresentation()
    {
        bool demo = DemoMode.IsChecked == true;
        if (demo == SceneLighting.Demo)
        {
            return;
        }

        SceneLighting.Demo = demo;
        _markersDirty = true;
        _vegetationDirty = true;

        // The occlusion and sky layers own nothing but shader programs, so they are kept: disposing
        // them would reclaim a few KB and cost three compiles the next time Demo comes back on.
        if (!demo)
        {
            _cascades?.Dispose();
            _cascades = null;
        }
    }

    /// <summary>
    /// A depth-only pass from the camera, and the occlusion computed off it. The prepass is the same
    /// <c>DrawDepth</c> the cascades use, pointed at the eye instead of the sun - occlusion has to be
    /// ready before the surfaces that consume it are shaded, and a forward pass cannot sample a
    /// depth buffer it is still writing.
    /// </summary>
    private int DrawOcclusion(
        bool drawEntityModels, OpenTK.Mathematics.Matrix4 viewProjection, float aspect)
    {
        _occlusion ??= new AmbientOcclusion();

        GL.BindFramebuffer(FramebufferTarget.Framebuffer, _targets!.SceneFramebuffer);
        GL.Viewport(0, 0, _targets.Width, _targets.Height);
        GL.Clear(ClearBufferMask.DepthBufferBit);
        DrawCasters(viewProjection, drawEntityModels);

        return _occlusion.Render(_targets, _camera.Projection(aspect));
    }

    /// <summary>Everything that writes depth, from one viewpoint. The cascades and the prepass want
    /// the same set - the only difference between them is where it is seen from.</summary>
    private void DrawCasters(OpenTK.Mathematics.Matrix4 viewProjection, bool drawEntityModels)
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
    /// <remarks>The bias is entirely in the lookup rather than a polygon offset here, because the
    /// terrain's own draw sets and clears <c>PolygonOffsetFill</c> for its coarse patch and would
    /// take an outer one with it.</remarks>
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
    private OpenTK.Mathematics.Vector3 Right() => _camera.Right;

    private OpenTK.Mathematics.Vector3 Up()
        => OpenTK.Mathematics.Vector3.Cross(_camera.Right, _camera.Forward);

    /// <summary>Averages over a quarter second: a per-frame number is unreadable, and the average is
    /// the one that matters while judging whether a layer costs anything. Counted only while the
    /// previous frame had already asked for another - the gap after an idle viewport is how long
    /// nothing wanted a frame, and averaging it in reports seconds per frame that nobody waited.
    /// </summary>
    private void ShowFrameRate(TimeSpan delta)
    {
        if (!Viewport.RenderContinuously)
        {
            return;
        }

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
        if (_modelStatusText.Length > 0 && LayerCatalog.Entities.Status != status)
        {
            LayerCatalog.Entities.Status = status;
        }
    }

}
