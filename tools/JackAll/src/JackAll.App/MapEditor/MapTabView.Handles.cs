using System.Numerics;
using System.Windows;
using JackAll.App.MapEditor.Gl;
using JackAll.Core.Format.Fcb;
using JackAll.Tools.World;
using OpenTK.Graphics.OpenGL4;

namespace JackAll.App.MapEditor;

/// <summary>The primary selection's component handles: a trigger box's faces, a light's radius and a
/// spot's cone, each dragged straight into the component's fields.</summary>
public partial class MapTabView
{
    /// <summary>Viewport height of a face handle's cube.</summary>
    private const float FaceHandlePixels = 12f;

    private HandleGrab? _handleGrab;
    private EntityHandle? _hoveredHandle;

    /// <summary>The held entity's node as the drag found it: what Escape restores and undo returns to.</summary>
    private FcbObject? _handleBefore;

    /// <summary>The rings and cone lines, rebuilt when <see cref="_handlesDirty"/> says they moved.</summary>
    private ShapeLayer? _handleLines;
    private bool _handlesDirty = true;

    /// <summary>Set when a trigger or light changed, so both layers are rebuilt from the edited nodes.</summary>
    private bool _overlaysDirty;

    private IReadOnlyList<EntityHandle> CurrentHandles()
        => _edits is { } edits && _selection.Count == 1 && _selection.Primary is { } entity && _hierarchy.CanEdit(entity)
            ? EntityHandles.For(entity, edits.CurrentNode(entity))
            : [];

    private HandleGrab? HandleUnder(Point point)
    {
        if (!LayerCatalog.Entities.IsVisible)
        {
            return null;
        }
        (Vector3 origin, Vector3 direction) = RayAt(point);
        return EntityHandles.Grab(CurrentHandles(), origin, direction, GizmoScale);
    }

    /// <summary>Whether the handle under the cursor changed, so its highlight needs a redraw.</summary>
    private bool HoverHandle(Point point)
    {
        EntityHandle? hovered = HandleUnder(point)?.Handle;
        if (hovered == _hoveredHandle)
        {
            return false;
        }
        _hoveredHandle = hovered;
        _handlesDirty = true;
        return true;
    }

    private bool BeginHandleDrag(Point point)
    {
        if (_edits is not { } edits || HandleUnder(point) is not { } grab)
        {
            return false;
        }
        _handleGrab = grab;
        _handleBefore = edits.EditableNode(_selection.Primary!).Clone();
        return true;
    }

    private void DragHandle(Point point)
    {
        if (_edits is not { } edits || _handleGrab is not { } grab || _selection.Primary is not { } entity)
        {
            return;
        }
        (Vector3 origin, Vector3 direction) = RayAt(point);
        if (EntityHandles.Follow(grab, origin, direction) is not { } value)
        {
            return;
        }

        EntityHandles.Apply(edits.EditableNode(entity), grab.Handle, value);
        edits.Edited(entity);
        StatusText.Text = grab.Handle.Kind switch
        {
            HandleKind.Face => $"size {"XYZ"[grab.Handle.Axis]} {value:0.00} m",
            HandleKind.Radius => $"radius {value:0.00} m",
            _ => $"cone {value:0.0}°",
        };
        _handlesDirty = true;
        Viewport.InvalidateVisual();
    }

    private void EndHandleDrag(bool revert)
    {
        if (_edits is not { } edits || _selection.Primary is not { } entity || _handleBefore is not { } before)
        {
            return;
        }

        FcbObject node = edits.EditableNode(entity);
        if (revert)
        {
            node.Overwrite(before);
        }
        else
        {
            _history.Push(new NodeEditStep(edits, entity, before, node.Clone(), $"Resize {entity.Name}"));
            _hierarchy.RefreshModified([entity]);
            RefreshSaveButton();
        }
        _inspector.Reload();
        OverlaysChanged();
    }

    /// <summary>Asks the next frame to redraw the trigger, light and handle geometry.</summary>
    private void OverlaysChanged()
    {
        _overlaysDirty = true;
        _handlesDirty = true;
        Viewport.InvalidateVisual();
    }

    /// <summary>Rebuilds the trigger, light and link layers from the edited nodes, on load and after
    /// any edit that could move them. Needs the GL context.</summary>
    private void RebuildOverlays()
    {
        if (!_overlaysDirty || _edits is not { } edits)
        {
            return;
        }
        _overlaysDirty = false;

        IReadOnlyList<TriggerVolume> triggers = WorldTriggers.Load(edits.World.Entities, edits.CurrentNode);
        _triggerLayer?.Dispose();
        _triggerLayer = new ShapeLayer(WorldMarkers.BuildTriggerOutlines(triggers));
        LayerCatalog.Triggers.Status = WorldMarkers.Describe(triggers);
        IReadOnlyList<WorldLight> lights = WorldLights.Load(edits.World.Entities, edits.CurrentNode);
        _lightLayer?.Dispose();
        _lightLayer = new EntityMarkerLayer(WorldMarkers.BuildLightMarkers(lights), lights.Count);
        LayerCatalog.Lights.Status = WorldMarkers.Describe(lights);
        RebuildLinkLayer(edits);
    }

    /// <summary>Draws the handles over everything, as the gizmo is, so one behind a wall stays grabbable.</summary>
    private void DrawHandles(OpenTK.Mathematics.Matrix4 viewProjection)
    {
        IReadOnlyList<EntityHandle> handles = CurrentHandles();
        EntityHandle? active = _handleGrab?.Handle ?? _hoveredHandle;
        if (_handlesDirty)
        {
            _handlesDirty = false;
            _handleLines?.Dispose();
            _handleLines = new ShapeLayer([.. handles.Where(h => h.IsRing).SelectMany(h => LinesOf(h, h == active))]);
        }
        if (handles.Count == 0)
        {
            return;
        }

        using var state = new GlState();
        GL.Disable(EnableCap.DepthTest);
        _handleLines?.Draw(viewProjection);
        _selectionBox ??= new SelectionBoxLayer();
        foreach (EntityHandle face in handles.Where(h => !h.IsRing))
        {
            float size = GizmoScale(face.Point) * (FaceHandlePixels / GizmoPixels);
            Matrix4x4 model = Matrix4x4.CreateScale(size) * Matrix4x4.CreateTranslation(face.Point);
            (float r, float g, float b) = ShapeLayer.TintFor(KindOf(face == active));
            _selectionBox.Draw(viewProjection, GlMatrix.From(model), new OpenTK.Mathematics.Vector3(r, g, b));
        }
    }

    private static string KindOf(bool active) => active ? "handle-active" : "handle";

    /// <summary>A ring, and for a cone the four lines from the light to its rim.</summary>
    private static IEnumerable<WorldShape> LinesOf(EntityHandle ring, bool active)
    {
        string kind = KindOf(active);
        IReadOnlyList<Vector3> rim = EntityHandles.RingPoints(ring);
        yield return new WorldShape(kind, "", "", rim);
        if (ring.Kind is HandleKind.OuterCone or HandleKind.InnerCone)
        {
            Vector3 apex = ring.Centre - (ring.Direction * ring.Reach);
            for (int i = 0; i < 4; i++)
            {
                yield return new WorldShape(kind, "", "", [apex, rim[i * (rim.Count - 1) / 4]]);
            }
        }
    }
}
