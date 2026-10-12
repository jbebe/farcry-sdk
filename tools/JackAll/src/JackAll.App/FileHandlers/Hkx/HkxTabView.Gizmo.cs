using System.Numerics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Media3D;
using JackAll.Tools.Hkx;
using JackAll.Tools.World;

namespace JackAll.App.FileHandlers.Hkx;

/// <summary>The HKX editor's gizmo: moving and turning the selected convex shape, and dragging a box's faces.</summary>
public partial class HkxTabView
{
    private const float GizmoPixels = 110, KnobPixels = 9;

    private HkxGizmo _gizmo = null!;
    private HkxTool _tool = HkxTool.Move;
    private (HkxLink Link, HkxPrimitive Primitive)? _target;
    private Drag? _drag;

    // A drag in progress: the file before it, where the shape and its parent sat in the model then, the box's size,
    // and the handle held.
    private sealed record Drag(byte[] Before, Matrix4x4 ToModel, Matrix4x4 ParentToModel, Vector3 Size, GizmoGrab? Move, RotateGrab? Turn,
        HandleGrab? Face, int FaceIndex);

    private void StartGizmo()
    {
        _gizmo = new HkxGizmo(HkxGizmos);
        _orbit.Moved += ShowGizmo;
        HkxFrame.PreviewMouseLeftButtonDown += Frame_PreviewMouseLeftButtonDown;
        HkxFrame.PreviewMouseMove += Frame_PreviewMouseMove;
        HkxFrame.PreviewMouseLeftButtonUp += Frame_PreviewMouseLeftButtonUp;
        HkxFrame.LostMouseCapture += (_, _) => EndDrag(keep: true);
    }

    // The selected node's shape when the gizmo can place it: a convex shape hanging from a member of its own.
    private void Target(HkxTreeNode? node)
    {
        _target = node?.Node is { Link: { } link } n && n.Primitives().ToList() is [{ Kind: not HkxShapeKind.Mesh } primitive]
            ? (link, primitive)
            : null;
        ShowGizmo();
    }

    private void ShowGizmo()
    {
        // A camera can't be shared, so the overlay's copies the scene's.
        HkxGizmos.Camera = ((PerspectiveCamera)HkxViewport.Camera).Clone();
        if (_target is not { } target || !IsLoaded)
        {
            _gizmo.Hide();
            return;
        }
        Vector3 origin = target.Primitive.ToModel.Translation;
        _gizmo.Show(_tool, origin, _orbit.Scale(origin, GizmoPixels), _drag?.Move?.Axis ?? _drag?.Turn?.Axis ?? GizmoAxis.None,
            Faces(target), _drag?.Face?.Handle, _orbit.Scale(origin, KnobPixels));
    }

    // A box's faces in the model, as handles in the order face 2k + (0 for the + side, 1 for the - side) is axis k.
    private static List<EntityHandle> Faces((HkxLink Link, HkxPrimitive Primitive) target)
    {
        if (HkxShapeEdit.Leaf(target.Link) is not { Class.Name: "hkpBoxShape" } box)
        {
            return [];
        }
        Matrix4x4 m = target.Primitive.ToModel;
        Vector3 half = HkxShapeEdit.Size(box) / 2;
        Vector3[] axes = [new(m.M11, m.M12, m.M13), new(m.M21, m.M22, m.M23), new(m.M31, m.M32, m.M33)];
        return [.. Enumerable.Range(0, 6).Select(f => new EntityHandle(HandleKind.Face, f / 2, m.Translation,
            Vector3.Normalize(f % 2 == 0 ? axes[f / 2] : -axes[f / 2]), half[f / 2]))];
    }

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
        List<EntityHandle> faces = Faces(target);
        HandleGrab? face = EntityHandles.Grab(faces, origin, direction, p => _orbit.Scale(p, KnobPixels) / EntityHandles.GrabRadius);
        GizmoGrab? move = face is null && _tool == HkxTool.Move ? TranslateGizmo.Grab(origin, direction, at, _orbit.Scale(at, GizmoPixels)) : null;
        RotateGrab? turn = face is null && _tool == HkxTool.Turn ? RotateGizmo.Grab(origin, direction, at, _orbit.Scale(at, GizmoPixels)) : null;
        if (face is null && move is null && turn is null)
        {
            return;
        }

        HkxObject leaf = HkxShapeEdit.Leaf(target.Link);
        _drag = new Drag(_session.File.Write(), target.Primitive.ToModel, HkxShapeEdit.ParentToModel(target.Link, target.Primitive.ToModel),
            leaf.Class.Name == "hkpBoxShape" ? HkxShapeEdit.Size(leaf) : default, move, turn, face, face is { } held ? faces.IndexOf(held.Handle) : -1);
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
        HkxObject leaf = HkxShapeEdit.Leaf(target.Link);
        if (drag.Move is { } move && TranslateGizmo.Follow(move, origin, direction) is { } position)
        {
            HkxShapeEdit.PlaceInModel(target.Link, drag.ParentToModel, drag.ToModel * Matrix4x4.CreateTranslation(position - move.Origin));
        }
        else if (drag.Turn is { } turn && RotateGizmo.Follow(turn, origin, direction) is { } degrees)
        {
            Matrix4x4 about = Matrix4x4.CreateTranslation(-turn.Origin)
                * Matrix4x4.CreateFromAxisAngle(TranslateGizmo.Direction(turn.Axis), degrees * MathF.PI / 180)
                * Matrix4x4.CreateTranslation(turn.Origin);
            HkxShapeEdit.PlaceInModel(target.Link, drag.ParentToModel, drag.ToModel * about);
        }
        else if (drag.Face is { } face
            && TranslateGizmo.ClosestApproach(origin, direction, face.Handle.Centre, face.Handle.Direction) is { } approach)
        {
            // From where the box started, so that each step moves its face from the opposite one rather than adding up.
            HkxShapeEdit.Resize(leaf, drag.Size);
            HkxShapeEdit.PlaceInModel(target.Link, drag.ParentToModel, drag.ToModel);
            HkxShapeEdit.Stretch(target.Link, face.Handle.Axis, drag.FaceIndex % 2 == 0 ? 1 : -1, face.Handle.Extent + approach.Arm - face.Grip);
        }
        else
        {
            return;
        }

        List<HkxPrimitive> live = [.. HkxShapes.Read(_session!.File).SelectMany(BodiesOf).Where(b => IsShownBody(b.Label)).SelectMany(b => b.Primitives())];
        _scene.Show(live);
        HkxPrimitive moved = live.FirstOrDefault(p => p.Shape == leaf) ?? target.Primitive;
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
        string label = drag.Face is not null ? "resize a box" : drag.Turn is not null ? "turn a shape" : "move a shape";
        if (keep)
        {
            _session!.Commit(label, drag.Before);
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

    private static IEnumerable<HkxNode> BodiesOf(HkxNode node)
        => node.Kind == HkxNodeKind.Body ? [node] : node.Children.SelectMany(BodiesOf);

    private bool IsShownBody(string label) => _roots.SelectMany(r => r.Bodies()).Any(b => b.IsShown && b.Node.Label == label);
}
