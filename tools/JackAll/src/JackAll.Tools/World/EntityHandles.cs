using System.Numerics;
using JackAll.Core.Format.Fcb;

namespace JackAll.Tools.World;

public enum HandleKind
{
    /// <summary>One face of a proximity trigger's box.</summary>
    Face,

    /// <summary>A light's reach, as a ring round it.</summary>
    Radius,

    /// <summary>The rim of a spot light's outer or inner cone.</summary>
    OuterCone,
    InnerCone,
}

/// <summary>
/// One draggable handle on a component's shape. A face sits at <see cref="Centre"/> plus
/// <see cref="Direction"/> times <see cref="Extent"/>, its half size; a ring is centred on
/// <see cref="Centre"/> square to <see cref="Direction"/>, of radius <see cref="Extent"/>, and a cone's
/// ring sits <see cref="Reach"/> along the cone from its apex.
/// </summary>
public sealed record EntityHandle(HandleKind Kind, int Axis, Vector3 Centre, Vector3 Direction, float Extent, float Reach = 0f)
{
    public bool IsRing => Kind != HandleKind.Face;

    /// <summary>Where the handle is drawn and hit for a face; a ring's own middle.</summary>
    public Vector3 Point => IsRing ? Centre : Centre + (Direction * Extent);
}

/// <summary>A handle held, and how far off its edge the grab landed.</summary>
public readonly record struct HandleGrab(EntityHandle Handle, float Grip);

/// <summary>
/// The handles that resize what a trigger or light component describes, and what dragging each does
/// to the component's own fields. GL-free, like the gizmos.
/// </summary>
public static class EntityHandles
{
    /// <summary>How near a face handle or a ring the ray must pass, as a fraction of the screen-held size.</summary>
    public const float GrabRadius = 0.08f;

    public const float MinExtent = 0.1f;
    public const float MinAngle = 1f;
    public const float MaxAngle = 170f;

    /// <summary>How far along a spot its cone is drawn when it names no reach of its own.</summary>
    private const float DefaultReach = 2f;

    private static readonly uint OuterAngle = FcbClassDefinitions.Crc32Ascii("fOuterAngle");
    private static readonly uint InnerAngle = FcbClassDefinitions.Crc32Ascii("fInnerAngle");

    /// <summary>The handles <paramref name="node"/>'s components offer, placed where the entity stands.</summary>
    public static IReadOnlyList<EntityHandle> For(WorldEntity entity, FcbObject node)
    {
        if (entity.Position is not { } position)
        {
            return [];
        }

        var handles = new List<EntityHandle>();
        if (FcbEntityFields.FindComponent(node, WorldHashes.CProximityTriggerComponent) is { } trigger
            && FcbEntityFields.ReadVector3(trigger, WorldHashes.VectorSize) is { } size)
        {
            Vector3[] axes = TriggerVolume.Axes(entity.Angles.Z);
            for (int face = 0; face < 6; face++)
            {
                int axis = face / 2;
                handles.Add(new EntityHandle(HandleKind.Face, axis, position,
                    face % 2 == 0 ? axes[axis] : -axes[axis], size[axis] * 0.5f));
            }
        }

        if (FcbEntityFields.FindComponent(node, WorldHashes.CDynamicLightComponent) is { } light)
        {
            float radius = FcbEntityFields.ReadFloat(light, WorldHashes.FRadius) ?? 0f;
            handles.Add(new EntityHandle(HandleKind.Radius, 0, position, Vector3.UnitZ, radius));
            if (FcbEntityFields.ReadU32(light, WorldHashes.HidType) == WorldLights.SpotType)
            {
                Vector3 forward = SpotDirection(entity.Angles);
                float reach = radius > MinExtent ? radius : DefaultReach;
                handles.Add(Cone(HandleKind.OuterCone, position, forward, reach, FcbEntityFields.ReadFloat(light, OuterAngle) ?? 0f));
                handles.Add(Cone(HandleKind.InnerCone, position, forward, reach, FcbEntityFields.ReadFloat(light, InnerAngle) ?? 0f));
            }
        }
        return handles;
    }

    /// <summary>Which way a spot shines: its local +Y, which is why every ceiling spot is turned -90
    /// about X.</summary>
    public static Vector3 SpotDirection(Vector3 angles) => Vector3.TransformNormal(Vector3.UnitY, EulerZxy.ToMatrix(angles));

    /// <summary>The handle the ray takes, nearest first, or null when it misses them all.</summary>
    /// <param name="scaleAt">The world size that holds a fixed screen size at a point.</param>
    public static HandleGrab? Grab(
        IReadOnlyList<EntityHandle> handles, Vector3 rayOrigin, Vector3 rayDirection, Func<Vector3, float> scaleAt)
    {
        HandleGrab? grabbed = null;
        float nearest = float.MaxValue;
        foreach (EntityHandle handle in handles)
        {
            if (handle.IsRing)
            {
                if (TranslateGizmo.Crossing(rayOrigin, rayDirection, handle.Centre, handle.Direction) is { } t && t < nearest)
                {
                    Vector3 hit = rayOrigin + (rayDirection * t);
                    float off = Vector3.Distance(hit, handle.Centre) - handle.Extent;
                    if (MathF.Abs(off) <= GrabRadius * scaleAt(hit))
                    {
                        nearest = t;
                        grabbed = new HandleGrab(handle, off);
                    }
                }
                continue;
            }

            Vector3 point = handle.Point;
            float along = Vector3.Dot(point - rayOrigin, rayDirection);
            if (along > 0f && along < nearest
                && Vector3.Distance(rayOrigin + (rayDirection * along), point) <= GrabRadius * scaleAt(point)
                && TranslateGizmo.ClosestApproach(rayOrigin, rayDirection, handle.Centre, handle.Direction) is { } approach)
            {
                nearest = along;
                grabbed = new HandleGrab(handle, approach.Arm - handle.Extent);
            }
        }
        return grabbed;
    }

    /// <summary>The field value the drag now makes - a face's full size along its axis, a radius, or a
    /// cone's full angle in degrees - or null while the handle is edge-on.</summary>
    public static float? Follow(HandleGrab grab, Vector3 rayOrigin, Vector3 rayDirection)
    {
        EntityHandle handle = grab.Handle;
        if (!handle.IsRing)
        {
            return TranslateGizmo.ClosestApproach(rayOrigin, rayDirection, handle.Centre, handle.Direction) is { } approach
                ? 2f * MathF.Max(approach.Arm - grab.Grip, MinExtent * 0.5f)
                : null;
        }

        if (TranslateGizmo.Crossing(rayOrigin, rayDirection, handle.Centre, handle.Direction) is not { } t)
        {
            return null;
        }
        float rim = MathF.Max(Vector3.Distance(rayOrigin + (rayDirection * t), handle.Centre) - grab.Grip, MinExtent);
        return handle.Kind == HandleKind.Radius
            ? rim
            : Math.Clamp(2f * MathF.Atan(rim / handle.Reach) * (180f / MathF.PI), MinAngle, MaxAngle);
    }

    /// <summary>Writes a dragged value into the component, keeping a spot's inner cone inside its outer.</summary>
    public static void Apply(FcbObject node, EntityHandle handle, float value)
    {
        if (handle.Kind == HandleKind.Face)
        {
            FcbObject trigger = FcbEntityFields.FindComponent(node, WorldHashes.CProximityTriggerComponent)!;
            Vector3 size = FcbEntityFields.ReadVector3(trigger, WorldHashes.VectorSize) ?? Vector3.One;
            size[handle.Axis] = value;
            trigger.Values[WorldHashes.VectorSize] = FcbEntityFields.Vector3Bytes(size);
            return;
        }

        FcbObject light = FcbEntityFields.FindComponent(node, WorldHashes.CDynamicLightComponent)!;
        switch (handle.Kind)
        {
            case HandleKind.Radius:
                light.Values[WorldHashes.FRadius] = BitConverter.GetBytes(value);
                break;
            case HandleKind.OuterCone:
                light.Values[OuterAngle] = BitConverter.GetBytes(value);
                if ((FcbEntityFields.ReadFloat(light, InnerAngle) ?? 0f) > value)
                {
                    light.Values[InnerAngle] = BitConverter.GetBytes(value);
                }
                break;
            case HandleKind.InnerCone:
                light.Values[InnerAngle] = BitConverter.GetBytes(MathF.Min(value, FcbEntityFields.ReadFloat(light, OuterAngle) ?? value));
                break;
        }
    }

    /// <summary>A ring handle as a closed polyline.</summary>
    public static IReadOnlyList<Vector3> RingPoints(EntityHandle ring, int segments = 48)
    {
        Vector3 aside = MathF.Abs(ring.Direction.Z) < 0.9f ? Vector3.UnitZ : Vector3.UnitX;
        Vector3 u = Vector3.Normalize(Vector3.Cross(aside, ring.Direction));
        Vector3 v = Vector3.Cross(ring.Direction, u);
        return [.. Enumerable.Range(0, segments + 1).Select(i =>
        {
            (float sin, float cos) = MathF.SinCos(MathF.Tau * i / segments);
            return ring.Centre + (((u * cos) + (v * sin)) * ring.Extent);
        })];
    }

    private static EntityHandle Cone(HandleKind kind, Vector3 apex, Vector3 forward, float reach, float degrees)
        => new(kind, 0, apex + (forward * reach), forward,
            reach * MathF.Tan(Math.Clamp(degrees, MinAngle, MaxAngle) * 0.5f * (MathF.PI / 180f)), reach);
}
