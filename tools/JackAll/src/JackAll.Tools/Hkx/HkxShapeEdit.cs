using System.Numerics;
using JackAll.Tools.World;

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
    public static Matrix4x4 Local(HkxObject shape) => HkxShapes.Unwrap(shape)?.Local ?? Matrix4x4.Identity;

    /// <summary>The convex shape a link places: the one its translate or transform shape wraps, or the linked shape itself.</summary>
    public static HkxObject Leaf(HkxLink link) => link.Target is { } target && HkxShapes.IsWrapper(target) ? HkxShapes.Unwrap(target)!.Value.Child! : link.Target!;

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
                target.SetReals("transform", HkxShapes.TransformReals(local));
                return;
            case "hkpConvexTranslateShape" when !turns:
                target.SetReals("translation", HkxShapes.Lanes(local.Translation, 0));
                return;
            default:
                if (target == leaf && local.IsIdentity)
                {
                    return;
                }
                HkxObject wrapper = HkxEdit.Create(turns ? "hkpConvexTransformShape" : "hkpConvexTranslateShape");
                wrapper.SetRaw("userData", leaf.GetRaw("userData"));
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
    public static Vector3 Size(HkxObject box) => (HkxShapes.Vec(box.GetReals("halfExtents")) + new Vector3(box.GetReals("radius")[0])) * 2;

    /// <summary>Sets a box's size from surface to surface, about its centre; no side goes below twice its convex radius.</summary>
    public static void Resize(HkxObject box, Vector3 size)
    {
        Vector3 half = Vector3.Max(size / 2 - new Vector3(box.GetReals("radius")[0]), Vector3.Zero);
        box.SetReals("halfExtents", HkxShapes.Lanes(half, box.GetReals("halfExtents")[3]));
    }

    /// <summary>A handle on each face of a box landing at <paramref name="toModel"/>, numbered as <see cref="EntityHandles.BoxFaces"/> numbers them.</summary>
    public static IReadOnlyList<EntityHandle> Faces(HkxObject box, Matrix4x4 toModel)
    {
        Vector3[] axes = [new(toModel.M11, toModel.M12, toModel.M13), new(toModel.M21, toModel.M22, toModel.M23), new(toModel.M31, toModel.M32, toModel.M33)];
        return EntityHandles.BoxFaces(toModel.Translation, [.. axes.Select(Vector3.Normalize)], Size(box) / 2);
    }

    /// <summary>
    /// Makes the box behind a link <paramref name="across"/> wide through face <paramref name="face"/> (numbered as
    /// <see cref="EntityHandles.BoxFaces"/> numbers them), starting from <paramref name="size"/> where it lands at
    /// <paramref name="toModel"/>, so that the opposite face stays where it was.
    /// </summary>
    public static void Stretch(HkxLink link, Matrix4x4 parentToModel, Matrix4x4 toModel, Vector3 size, int face, float across)
    {
        HkxObject box = Leaf(link);
        int axis = face / 2;
        Vector3 next = size;
        next[axis] = across;
        Resize(box, next);
        Vector3 shift = Vector3.Zero;
        shift[axis] = (face % 2 == 0 ? 1 : -1) * (Size(box)[axis] - size[axis]) / 2;
        PlaceInModel(link, parentToModel, Matrix4x4.CreateTranslation(shift) * toModel);
    }

    private static bool IsTranslation(Matrix4x4 m)
        => Matrix4x4.CreateTranslation(m.Translation) is var t && Enumerable.Range(0, 16).All(i => MathF.Abs(m[i / 4, i % 4] - t[i / 4, i % 4]) < Tolerance);
}
