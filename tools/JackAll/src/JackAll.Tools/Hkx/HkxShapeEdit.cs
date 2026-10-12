using System.Numerics;

namespace JackAll.Tools.Hkx;

/// <summary>The member of an object that points at a shape.</summary>
public readonly record struct HkxLink(HkxObject Owner, string Path)
{
    public HkxObject? Target => Owner.GetPointer(Path);
}

/// <summary>Edits to collision shapes: where a convex shape sits in its parent, and a box's size.</summary>
public static class HkxShapeEdit
{
    private const float Tolerance = 1e-6f;

    /// <summary>The matrix placing a shape in its parent's space: a translate or transform shape's own, identity for any other.</summary>
    public static Matrix4x4 Local(HkxObject shape) => IsWrapper(shape) ? HkxShapes.Unwrap(shape)!.Value.Local : Matrix4x4.Identity;

    /// <summary>The convex shape a link places: the one its translate or transform shape wraps, or the linked shape itself.</summary>
    public static HkxObject Leaf(HkxLink link) => link.Target is { } target && IsWrapper(target) ? target.GetPointer("childShape.childShape")! : link.Target!;

    /// <summary>
    /// Places the convex shape behind a link at <paramref name="local"/> in the link owner's space. A translate shape
    /// that has to turn becomes a transform shape, and an unwrapped shape gets a wrapper; the link then points at it.
    /// </summary>
    public static void Place(HkxLink link, Matrix4x4 local)
    {
        HkxObject target = link.Target!, leaf = Leaf(link);
        bool turns = !IsTranslation(local);
        switch (target.Class.Name)
        {
            case "hkpConvexTransformShape":
                target.SetReals("transform", local.M11, local.M12, local.M13, 0, local.M21, local.M22, local.M23, 0,
                    local.M31, local.M32, local.M33, 0, local.M41, local.M42, local.M43, 0);
                return;
            case "hkpConvexTranslateShape" when !turns:
                target.SetReals("translation", local.M41, local.M42, local.M43, 0);
                return;
            default:
                if (target == leaf && local.IsIdentity)
                {
                    return;
                }
                HkxObject wrapper = HkxEdit.Create(turns ? "hkpConvexTransformShape" : "hkpConvexTranslateShape");
                wrapper.SetInt("userData", leaf.GetInt("userData"));
                wrapper.SetReals("radius", leaf.GetReals("radius")[0]);
                wrapper.SetPointer("childShape.childShape", leaf);
                link.Owner.SetPointer(link.Path, wrapper);
                Place(link, local);
                return;
        }
    }

    /// <summary>Where the space a link's owner places its shape in sits in the model, from where the shape itself lands there.</summary>
    public static Matrix4x4 ParentToModel(HkxLink link, Matrix4x4 toModel)
    {
        Matrix4x4.Invert(Local(link.Target!), out Matrix4x4 inverse);
        return inverse * toModel;
    }

    /// <summary>Places the convex shape behind a link so that it lands at <paramref name="toModel"/> in the model.</summary>
    public static void PlaceInModel(HkxLink link, Matrix4x4 parentToModel, Matrix4x4 toModel)
    {
        Matrix4x4.Invert(parentToModel, out Matrix4x4 toParent);
        Place(link, toModel * toParent);
    }

    /// <summary>A box's size from surface to surface.</summary>
    public static Vector3 Size(HkxObject box)
    {
        float[] half = box.GetReals("halfExtents");
        return (new Vector3(half[0], half[1], half[2]) + new Vector3(box.GetReals("radius")[0])) * 2;
    }

    /// <summary>Sets a box's size from surface to surface, about its centre; no side goes below twice its convex radius.</summary>
    public static void Resize(HkxObject box, Vector3 size)
    {
        float radius = box.GetReals("radius")[0];
        Vector3 half = Vector3.Max(size / 2 - new Vector3(radius), Vector3.Zero);
        box.SetReals("halfExtents", half.X, half.Y, half.Z, box.GetReals("halfExtents")[3]);
    }

    /// <summary>
    /// Moves one face of the box behind a link, across its own <paramref name="axis"/> on the <paramref name="side"/>
    /// (1 or -1), so that the box is <paramref name="size"/> across and the opposite face stays where it was.
    /// </summary>
    public static void Stretch(HkxLink link, int axis, int side, float size)
    {
        HkxObject box = Leaf(link);
        Vector3 was = Size(box), next = was;
        next[axis] = size;
        Resize(box, next);
        Vector3 shift = Vector3.Zero;
        shift[axis] = side * (Size(box)[axis] - was[axis]) / 2;
        Place(link, Matrix4x4.CreateTranslation(shift) * Local(link.Target!));
    }

    private static bool IsWrapper(HkxObject shape) => shape.Class.Name is "hkpConvexTranslateShape" or "hkpConvexTransformShape";

    private static bool IsTranslation(Matrix4x4 m)
        => Matrix4x4.CreateTranslation(m.Translation) is var t && Enumerable.Range(0, 16).All(i => MathF.Abs(m[i / 4, i % 4] - t[i / 4, i % 4]) < Tolerance);
}
