using System.Numerics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using JackAll.Tools.Hkx;
using JackAll.Tools.World;

namespace JackAll.App.FileHandlers.Hkx;

/// <summary>The HKX editor's gizmo: moving and turning the selected convex shape, and dragging a box's faces.</summary>
public partial class HkxTabView
{
    private const float GizmoPixels = 110, KnobPixels = 9;

    private readonly HkxGizmo _gizmo;
    private HkxTool _tool = HkxTool.Move;
    private (HkxLink Link, HkxPrimitive Primitive)? _target;
    private Drag? _drag;

    // A drag in progress: the file before it, where the shape and its parent landed in the model then, and what is
    // held: a GizmoGrab, a RotateGrab or a BoxFace.
    private sealed record Drag(byte[] Before, Matrix4x4 ToModel, Matrix4x4 ParentToModel, object Held);

    // A box's face held, numbered as EntityHandles.BoxFaces numbers them, and the box's size when it was taken.
    private sealed record BoxFace(HandleGrab Grab, int Face, Vector3 Size);

    private void StartGizmo()
    {
        _orbit.Overlay(HkxGizmos);
        _orbit.Moved += ShowGizmo;
        HkxFrame.PreviewMouseLeftButtonDown += Frame_PreviewMouseLeftButtonDown;
        HkxFrame.PreviewMouseMove += Frame_PreviewMouseMove;
        HkxFrame.PreviewMouseLeftButtonUp += Frame_PreviewMouseLeftButtonUp;
        HkxFrame.LostMouseCapture += (_, _) => EndDrag(keep: true);
    }

    private void Target(HkxTreeNode? node)
    {
        _target = node?.Node is { } n ? HkxShapeEdit.Placed(n) : null;
        ShowGizmo();
    }

    private void ShowGizmo()
    {
        if (_target is not { } target || !IsLoaded)
        {
            _gizmo.Hide();
            return;
        }
        Vector3 origin = target.Primitive.ToModel.Translation;
        GizmoAxis held = _drag?.Held switch { GizmoGrab move => move.Axis, RotateGrab turn => turn.Axis, _ => GizmoAxis.None };
        _gizmo.Show(_tool, origin, _orbit.Scale(origin, GizmoPixels), held, Faces(target), (_drag?.Held as BoxFace)?.Face ?? -1,
            _orbit.Scale(origin, KnobPixels));
    }

    private static IReadOnlyList<EntityHandle> Faces((HkxLink Link, HkxPrimitive Primitive) target)
        => HkxShapeEdit.Leaf(target.Link) is { Class.Name: "hkpBoxShape" } box ? HkxShapeEdit.Faces(box, target.Primitive.ToModel) : [];

    private void SetTool(HkxTool tool)
    {
        _tool = tool;
        HkxMove.IsChecked = tool == HkxTool.Move;
        HkxTurn.IsChecked = tool == HkxTool.Turn;
        ShowGizmo();
    }

    private void Move_Click(object sender, RoutedEventArgs e) => SetTool(HkxTool.Move);

    private void Turn_Click(object sender, RoutedEventArgs e) => SetTool(HkxTool.Turn);

    private void Frame_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (_session is null || _target is not { } target)
        {
            return;
        }
        (Vector3 origin, Vector3 direction) = _orbit.Ray(e.GetPosition(HkxViewport));
        Vector3 at = target.Primitive.ToModel.Translation;
        float scale = _orbit.Scale(at, GizmoPixels);
        List<EntityHandle> faces = [.. Faces(target)];
        object? held = EntityHandles.Grab(faces, origin, direction, p => _orbit.Scale(p, KnobPixels) / EntityHandles.GrabRadius) is { } face
            ? new BoxFace(face, faces.IndexOf(face.Handle), HkxShapeEdit.Size(HkxShapeEdit.Leaf(target.Link)))
            : _tool == HkxTool.Move ? (object?)TranslateGizmo.Grab(origin, direction, at, scale) : RotateGizmo.Grab(origin, direction, at, scale);
        if (held is null)
        {
            return;
        }

        _drag = new Drag(_session.Bytes, target.Primitive.ToModel, HkxShapeEdit.ParentToModel(target.Link, target.Primitive.ToModel), held);
        HkxFrame.CaptureMouse();
        ShowGizmo();
        e.Handled = true;
    }

    private void Frame_PreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (_drag is not { } drag || _target is not { } target)
        {
            return;
        }
        (Vector3 origin, Vector3 direction) = _orbit.Ray(e.GetPosition(HkxViewport));
        switch (drag.Held)
        {
            case GizmoGrab move when TranslateGizmo.Follow(move, origin, direction) is { } position:
                HkxShapeEdit.PlaceInModel(target.Link, drag.ParentToModel, drag.ToModel * Matrix4x4.CreateTranslation(position - move.Origin));
                break;
            case RotateGrab turn when RotateGizmo.Follow(turn, origin, direction) is { } degrees:
                HkxShapeEdit.PlaceInModel(target.Link, drag.ParentToModel, RotateGizmo.About(drag.ToModel, turn.Origin, turn.Axis, degrees));
                break;
            case BoxFace face when TranslateGizmo.ClosestApproach(origin, direction, face.Grab.Handle.Centre, face.Grab.Handle.Direction) is { } approach:
                HkxShapeEdit.Stretch(target.Link, drag.ParentToModel, drag.ToModel, face.Size, face.Face, face.Grab.Handle.Extent + approach.Arm - face.Grab.Grip);
                break;
            default:
                return;
        }

        HkxObject leaf = HkxShapeEdit.Leaf(target.Link);
        HkxPrimitive moved = HkxShapes.Leaf(leaf, HkxShapeEdit.Local(target.Link.Target!) * drag.ParentToModel)!;
        _scene.Show(Shown().Select(p => p.Shape == leaf ? moved : p));
        _scene.Highlight(new HashSet<HkxPrimitive> { moved });
        _target = (target.Link, moved);
        ShowGizmo();
        e.Handled = true;
    }

    private void Frame_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (_drag is not null)
        {
            EndDrag(keep: true);
            e.Handled = true;
        }
    }

    // Records the drag as one step, or with keep false puts the file back as it was before it.
    private void EndDrag(bool keep)
    {
        if (_drag is not { } drag)
        {
            return;
        }
        _drag = null;
        if (HkxFrame.IsMouseCaptured)
        {
            HkxFrame.ReleaseMouseCapture();
        }
        if (keep)
        {
            _session!.Commit(drag.Held switch { BoxFace => "resize a box", RotateGrab => "turn a shape", _ => "move a shape" }, drag.Before);
        }
        else
        {
            _session!.Revert(drag.Before);
        }
    }

    // Esc drops a drag; T and R pick moving and turning while the focus is not in a text box.
    private bool GizmoKey(Key key)
    {
        if (key == Key.Escape && _drag is not null)
        {
            EndDrag(keep: false);
            return true;
        }
        if (Keyboard.Modifiers != ModifierKeys.None || Keyboard.FocusedElement is TextBox || key is not (Key.T or Key.R))
        {
            return false;
        }
        SetTool(key == Key.T ? HkxTool.Move : HkxTool.Turn);
        return true;
    }
}
