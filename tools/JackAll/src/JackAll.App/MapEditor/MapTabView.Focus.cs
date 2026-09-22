using System.Numerics;
using JackAll.App.MapEditor.Gl;
using JackAll.Tools.World;

namespace JackAll.App.MapEditor;

/// <summary>Bringing an entity into view: the camera turns to it and closes in until it fills a good
/// part of the screen.</summary>
public partial class MapTabView
{
    /// <summary>How much of the view's height the framed entity takes up.</summary>
    private const float FrameFill = 0.4f;

    /// <summary>The nearest the camera comes, so a small prop is not framed from inside it.</summary>
    private const float MinFrameDistance = 4f;

    /// <summary>Turns the camera to <paramref name="entity"/> and moves it close enough to fit it,
    /// approaching from where the camera already is so the view keeps its bearings.</summary>
    private void Focus(WorldEntity entity)
    {
        if (entity.Position is not { } position || _terrain is null)
        {
            return;
        }

        (Vector3 min, Vector3 max) = OutlineBoundsOf(entity);

        Vector3 centre = position + Vector3.Transform((min + max) * 0.5f, entity.Rotation);
        float radius = Vector3.Distance(min, max) * 0.5f;
        float distance = MathF.Max(radius / (MathF.Tan(Camera3D.VerticalFovRadians * 0.5f) * FrameFill), MinFrameDistance);

        Vector3 eye = (Vector3)_camera.Position;
        Vector3 towards = centre - eye;
        Vector3 direction = towards.LengthSquared() > 1e-4f ? Vector3.Normalize(towards) : (Vector3)_camera.Forward;
        _camera.Pitch = Math.Clamp(MathF.Asin(direction.Z), -1.55f, 1.55f);
        _camera.Yaw = MathF.Atan2(direction.X, direction.Y);
        _camera.Position = (OpenTK.Mathematics.Vector3)(centre - ((Vector3)_camera.Forward * distance));
        HoldAboveTerrain();
        Viewport.InvalidateVisual();
    }
}
