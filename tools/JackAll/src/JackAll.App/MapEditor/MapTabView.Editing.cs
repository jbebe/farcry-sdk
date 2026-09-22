using System.Numerics;
using System.Windows;
using JackAll.App.MapEditor.Gl;
using JackAll.Tools.World;

namespace JackAll.App.MapEditor;

/// <summary>The viewport's editing: picking, the move and rotate gizmos over the selection, and
/// adding and removing entities.</summary>
public partial class MapTabView
{
    /// <summary>World size of the box that makes a mesh-less entity clickable.</summary>
    private const float MeshlessPickSize = 3f;

    /// <summary>Floor on each axis of a pick box, so a flat or tiny model is still a target.</summary>
    private const float MinPickExtent = 0.35f;

    /// <summary>Viewport height the gizmo holds, whatever it is standing on and however far away - a
    /// gizmo that shrinks with its entity stops being grabbable well before it stops being visible.</summary>
    private const float GizmoPixels = 110f;

    /// <summary>How far, and in what steps, a placement looks along the cursor ray for the ground.</summary>
    private const float PlaceReach = 2000f;
    private const float PlaceStep = 0.25f;

    private SelectionBoxLayer? _selectionBox;
    private GizmoLayer? _translateGizmo;
    private GizmoLayer? _rotateGizmo;

    /// <summary>The handle being dragged, one of the two; null when nothing is held.</summary>
    private GizmoGrab? _moveGrab;
    private RotateGrab? _rotateGrab;

    /// <summary>The handle under the cursor when nothing is held.</summary>
    private GizmoAxis _hovered = GizmoAxis.None;

    /// <summary>Where each dragged entity stood and faced when the drag began: every step is solved
    /// from here, and Escape returns here.</summary>
    private readonly Dictionary<WorldEntity, Placement> _dragStart = [];

    private bool IsDragging => _moveGrab is not null || _rotateGrab is not null || _handleGrab is not null;

    private bool Rotating => RotateMode.IsChecked == true;

    /// <summary>Outlines every selected entity with the same box picking tests against, and puts the
    /// gizmo on the primary one.</summary>
    private void DrawSelection(OpenTK.Mathematics.Matrix4 viewProjection)
    {
        _selectionBox ??= new SelectionBoxLayer();
        foreach (WorldEntity entity in _selection.Items)
        {
            if (entity.Position is not { } position)
            {
                continue;
            }

            // A trigger is outlined by its volume, as the Triggers layer draws it. Only the outline: as
            // a click target, a trigger spanning a town would take every click on the buildings inside it.
            (Vector3 min, Vector3 max) = _edits is { } edits && WorldTriggers.SizeOf(edits.CurrentNode(entity)) is { } volume
                ? (volume * -0.5f, volume * 0.5f)
                : LocalBoundsOf(entity);
            Matrix4x4 model = Matrix4x4.CreateScale(max - min)
                * Matrix4x4.CreateTranslation((min + max) * 0.5f)
                * entity.Rotation
                * Matrix4x4.CreateTranslation(position);
            bool primary = ReferenceEquals(entity, _selection.Primary);
            _selectionBox.Draw(viewProjection, GlMatrix.From(model),
                primary ? new OpenTK.Mathematics.Vector3(1f, 0.85f, 0.2f) : new OpenTK.Mathematics.Vector3(0.9f, 0.6f, 0.1f));
        }

        if (_selection.Primary is not { Position: { } pivot })
        {
            return;
        }
        GizmoLayer gizmo = Rotating ? (_rotateGizmo ??= GizmoLayer.Rings()) : (_translateGizmo ??= GizmoLayer.Arrows());
        gizmo.Draw(viewProjection, pivot, GizmoScale(pivot), _moveGrab?.Axis ?? _rotateGrab?.Axis ?? _hovered);
    }

    /// <summary>The gizmo's world size where it stands, holding it at a constant on-screen size. The
    /// one definition - hit testing and drawing both go through it.</summary>
    private float GizmoScale(Vector3 origin)
        => TranslateGizmo.Scale(
            origin, (Vector3)_camera.Position, Camera3D.VerticalFovRadians,
            (float)Viewport.ActualHeight, GizmoPixels);

    /// <summary>The world ray under a viewport point, in the numerics types the world model uses.</summary>
    private (Vector3 Origin, Vector3 Direction) RayAt(Point point)
    {
        (OpenTK.Mathematics.Vector3 origin, OpenTK.Mathematics.Vector3 direction) = _camera.Ray(
            point.X, point.Y, Viewport.ActualWidth, Math.Max(Viewport.ActualHeight, 1));
        return ((Vector3)origin, (Vector3)direction);
    }

    /// <summary>The handle of the current gizmo under a point, or None.</summary>
    private GizmoAxis HandleAt(Point point, out GizmoGrab? move, out RotateGrab? rotate)
    {
        move = null;
        rotate = null;
        if (!LayerCatalog.Entities.IsVisible || _selection.Primary is not { Position: { } pivot })
        {
            return GizmoAxis.None;
        }

        (Vector3 origin, Vector3 direction) = RayAt(point);
        if (Rotating)
        {
            rotate = RotateGizmo.Grab(origin, direction, pivot, GizmoScale(pivot));
            return rotate?.Axis ?? GizmoAxis.None;
        }
        move = TranslateGizmo.Grab(origin, direction, pivot, GizmoScale(pivot));
        return move?.Axis ?? GizmoAxis.None;
    }

    /// <summary>Whether the handle under the cursor changed, so the highlight needs a redraw.</summary>
    private bool HoverGizmo(Point point)
    {
        GizmoAxis hovered = HandleAt(point, out _, out _);
        bool component = HoverHandle(point);
        if (hovered == _hovered)
        {
            return component;
        }
        _hovered = hovered;
        return true;
    }

    /// <summary>Takes hold of the gizmo handle under a point, if there is one, and records where the
    /// selection stood. Locked and hidden entities stay out of the drag.</summary>
    private bool BeginDrag(Point point)
    {
        if (HandleAt(point, out GizmoGrab? move, out RotateGrab? rotate) == GizmoAxis.None)
        {
            return false;
        }

        _moveGrab = move;
        _rotateGrab = rotate;
        _dragStart.Clear();
        foreach (WorldEntity entity in _selection.Items)
        {
            if (entity.Position is { } position && _hierarchy.CanEdit(entity))
            {
                _dragStart[entity] = new Placement(position, entity.Angles);
            }
        }
        return true;
    }

    /// <summary>Moves or turns the held selection to where the cursor has dragged the handle.</summary>
    private void DragTo(Point point)
    {
        if (_handleGrab is not null)
        {
            DragHandle(point);
            return;
        }

        (Vector3 origin, Vector3 direction) = RayAt(point);
        if (_moveGrab is { } move && TranslateGizmo.Follow(move, origin, direction) is { } moved)
        {
            Vector3 delta = moved - move.Origin;
            foreach ((WorldEntity entity, (Vector3 position, _)) in _dragStart)
            {
                entity.Position = position + delta;
            }
            StatusText.Text = $"moved {delta.X:0.00}, {delta.Y:0.00}, {delta.Z:0.00}";
        }
        else if (_rotateGrab is { } rotate && RotateGizmo.Follow(rotate, origin, direction) is { } degrees)
        {
            foreach ((WorldEntity entity, (_, Vector3 angles)) in _dragStart)
            {
                entity.Angles = RotateGizmo.Apply(angles, rotate.Axis, degrees);
            }
            StatusText.Text = $"turned {degrees:0.0}° about {rotate.Axis}";
        }
        else
        {
            return;
        }

        // The instance streams are uploaded from entity placements, so the change shows up by asking
        // for the same rebuild a mission-layer toggle does.
        _markersDirty = true;
    }

    /// <summary>Ends a drag, either recording the change for the next save or putting the selection
    /// back where it was grabbed.</summary>
    private void EndDrag(bool revert)
    {
        if (_handleGrab is not null)
        {
            EndHandleDrag(revert);
        }

        List<WorldEntity> changed = [.. _dragStart
            .Where(p => p.Key.Position != p.Value.Position || p.Key.Angles != p.Value.Angles)
            .Select(p => p.Key)];
        foreach (WorldEntity entity in changed)
        {
            if (revert)
            {
                (entity.Position, entity.Angles) = (_dragStart[entity].Position, _dragStart[entity].Angles);
            }
            else
            {
                _edits?.Moved(entity);
            }
        }
        _markersDirty |= changed.Count > 0;
        if (changed.Count > 0)
        {
            OverlaysChanged();
        }
        if (!revert && changed.Count > 0 && _edits is { } edits)
        {
            _history.Push(new MoveStep(edits, changed.ToDictionary(
                e => e, e => (_dragStart[e], Placement.Of(e)))));
            _hierarchy.RefreshModified(changed);
            RefreshSaveButton();
        }
        _inspector.RefreshTransform();
        CancelDrag();

        // Not while the right button still holds it for looking, or the camera loses the mouse
        // halfway through a turn.
        if (!_looking)
        {
            Viewport.ReleaseMouseCapture();
        }
    }

    private void CancelDrag()
    {
        _moveGrab = null;
        _rotateGrab = null;
        _handleGrab = null;
        _handleBefore = null;
        _hovered = GizmoAxis.None;
        _dragStart.Clear();
    }

    /// <summary>Selects what a click lands on: Ctrl adds or removes it, a plain click replaces the
    /// selection, and a plain click on nothing clears it.</summary>
    private void PickAt(Point point, bool add)
    {
        (Vector3 origin, Vector3 direction) = RayAt(point);
        WorldEntity? hit = EntityPicking.Pick(
            origin, direction, _hierarchy.VisibleEntities.Where(_hierarchy.IsPickable), LocalBoundsOf);
        if (CompleteLinkPick(hit))
        {
            return;
        }
        if (hit is null)
        {
            if (!add)
            {
                _selection.Clear();
            }
            return;
        }

        if (add)
        {
            _selection.Toggle(hit);
        }
        else
        {
            _selection.Replace([hit]);
        }
        Reveal(hit);
    }

    private void Reveal(WorldEntity entity)
    {
        if (_hierarchy.Reveal(entity) is { } row)
        {
            Hierarchy.BringIntoView(row);
        }
    }

    /// <summary>Local-space extent of what an entity draws: the union of its models, or a box the
    /// size of <see cref="MeshlessPickSize"/> for the mesh-less ones so they stay clickable.</summary>
    private (Vector3 Min, Vector3 Max) LocalBoundsOf(WorldEntity entity)
    {
        var half = new Vector3(MeshlessPickSize * 0.5f);
        if (_modelSet is not { } set || !set.ModelIndicesByEntity.TryGetValue(entity, out int[]? models))
        {
            return (-half, half);
        }

        var min = new Vector3(float.MaxValue);
        var max = new Vector3(float.MinValue);
        foreach (int index in models)
        {
            min = Vector3.Min(min, set.Models[index].LocalMin);
            max = Vector3.Max(max, set.Models[index].LocalMax);
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

    /// <summary>Where the ray under a viewport point first reaches the ground, or null when it
    /// leaves the map without doing so.</summary>
    private Vector3? TerrainUnder(Point point)
    {
        if (_terrain is not { } terrain)
        {
            return null;
        }

        (Vector3 origin, Vector3 direction) = RayAt(point);
        for (float t = 0f; t < PlaceReach; t += PlaceStep)
        {
            Vector3 at = origin + direction * t;
            int x = (int)MathF.Round(at.X), y = (int)MathF.Round(at.Y);
            if (x < 0 || y < 0 || x >= terrain.Side || y >= terrain.Side)
            {
                continue;
            }

            float ground = terrain.HeightMetersAt(x, y);
            if (at.Z <= ground)
            {
                return new Vector3(at.X, at.Y, ground);
            }
        }
        return null;
    }

    private void ShowArchetype()
    {
        if (_selection.Primary is { ArchetypeName.Length: > 0 } entity && _edits is { } edits)
        {
            ArchetypeRequested?.Invoke(edits.World.Name, entity.ArchetypeName);
        }
    }

    private void OpenSector()
    {
        if (_selection.Primary is { } entity)
        {
            SectorEditorRequested?.Invoke(entity.HomeSector.SourcePath, entity.Id);
        }
    }

    private void CopySelected()
    {
        if (_selection.Primary is not { } entity || _edits is null)
        {
            return;
        }

        try
        {
            _clipboard = (CopiedEntity.Of(entity, _edits.World.Name), entity);
            StatusText.Text = $"Copied {entity.Name} - Ctrl+V in the viewport places it under the cursor";
        }
        catch (InvalidOperationException ex)
        {
            StatusText.Text = ex.Message;
        }
    }

    private void PasteAt(Point point)
    {
        if (_clipboard is not { } clip || _edits is not { } edits)
        {
            return;
        }
        if (TerrainUnder(point) is not { } ground)
        {
            StatusText.Text = "Point the cursor at the ground to paste";
            return;
        }
        if (Add(() => edits.Paste(clip.Copy, ground), clip.Original) is { } pasted)
        {
            StatusText.Text = $"Pasted {pasted.Name} into sector {pasted.HomeSector.SectorId}";
        }
    }

    private void DeleteSelected()
    {
        if (_edits is null || _selection.Count == 0)
        {
            return;
        }

        List<WorldEntity> doomed = [.. _selection.Items];
        _history.Push(PresenceStep.Deleted(_edits, [.. doomed.Select(_edits.Delete)]));
        var gone = new HashSet<WorldEntity>(doomed);
        _positionedEntities.RemoveAll(gone.Contains);
        _selection.Clear();
        EntitySetChanged();
        StatusText.Text = doomed.Count == 1 ? $"Deleted {doomed[0].Name}" : $"Deleted {doomed.Count} entities";
    }

    /// <summary>Places an archetype dropped on a hierarchy layer at the ground in the middle of the view.</summary>
    private void PlaceAtViewCentre(string archetype, string layer)
        => Place(archetype, layer, new Point(Viewport.ActualWidth / 2, Viewport.ActualHeight / 2));

    private void Place(string archetypeName, string layer, Point point)
    {
        if (_edits is not { } edits || _archetypes?.Winner(archetypeName) is not { } archetype)
        {
            return;
        }
        if (TerrainUnder(point) is not { } ground)
        {
            StatusText.Text = "There is no ground at that spot to place on";
            return;
        }

        // Any loaded instance of the same archetype lends its meshes; otherwise the new one is a
        // marker until a save and reload bakes it.
        WorldEntity? lookalike = _positionedEntities.FirstOrDefault(e =>
            e.ArchetypeName.Equals(archetypeName, StringComparison.OrdinalIgnoreCase)
            && _modelSet?.ModelIndicesByEntity.ContainsKey(e) == true);
        if (Add(() => edits.Place(archetype, ground, layer), lookalike) is { } placed)
        {
            StatusText.Text = $"Placed {placed.Name} in {layer}, sector {placed.HomeSector.SectorId}";
        }
    }

    /// <summary>Adds an entity through the session, draws it with <paramref name="drawLike"/>'s meshes
    /// when there is one, and selects it.</summary>
    private WorldEntity? Add(Func<WorldEntity> add, WorldEntity? drawLike)
    {
        WorldEntity added;
        try
        {
            added = add();
        }
        catch (InvalidOperationException ex)
        {
            StatusText.Text = ex.Message;
            return null;
        }

        if (drawLike is not null && _modelLayer?.AddCopy(added, drawLike) is { } models)
        {
            _modelSet!.ModelIndicesByEntity[added] = models;
        }
        _positionedEntities.Add(added);
        _history.Push(PresenceStep.Added(_edits!, added));
        EntitySetChanged();
        _selection.Replace([added]);
        Reveal(added);
        return added;
    }

    private void Viewport_DragOver(object sender, DragEventArgs e)
    {
        Vector3? ground = e.Data.GetDataPresent(EntityLibraryViewModel.DragFormat)
            ? TerrainUnder(e.GetPosition(Viewport))
            : null;
        e.Effects = ground is null ? DragDropEffects.None : DragDropEffects.Copy;
        if (ground is { } at)
        {
            StatusText.Text = $"place at {at.X:0.0}, {at.Y:0.0}, {at.Z:0.0}";
        }
        e.Handled = true;
    }

    private void Viewport_Drop(object sender, DragEventArgs e)
    {
        if (e.Data.GetData(EntityLibraryViewModel.DragFormat) is string archetype)
        {
            Place(archetype, Core.Format.Fcb.MissionLayers.MainName, e.GetPosition(Viewport));
            Viewport.Focus();
            e.Handled = true;
        }
    }
}
