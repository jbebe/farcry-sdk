using System.Numerics;
using JackAll.Tools.World;

namespace JackAll.Tools.Hkx;

/// <summary>The member of an object that points at a shape.</summary>
public readonly record struct HkxLink(HkxObject Owner, string Path)
{
    public HkxObject? Target => Owner.GetPointer(Path);
}

/// <summary>Edits to collision shapes: where a convex shape sits in its parent, a box's size, and adding or taking out shapes.</summary>
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

    /// <summary>
    /// Adds a box after the last shape of the list or extended mesh group holding the shape <paramref name="like"/>
    /// places, <paramref name="size"/> from surface to surface and landing at <paramref name="toModel"/> in the model,
    /// with that shape's convex radius, user data and collision filter.
    /// </summary>
    public static HkxObject AddBox(HkxLink like, Matrix4x4 parentToModel, Vector3 size, Matrix4x4 toModel)
    {
        HkxObject sibling = Leaf(like), owner = like.Owner;
        float radius = sibling.GetReals("radius")[0];
        HkxObject box = HkxEdit.Create("hkpBoxShape");
        box.SetRaw("userData", sibling.GetRaw("userData"));
        box.SetReals("radius", radius);
        box.SetReals("halfExtents", 0, 0, 0, radius);
        Resize(box, size);

        (string array, int index) = Slot(like);
        int end = owner.Count(array);
        HkxLink added;
        if (owner.Class.Name == "hkpListShape")
        {
            owner.InsertItem(array, end);
            owner.SetRaw($"{array}[{end}].collisionFilterInfo", owner.GetRaw($"{array}[{index}].collisionFilterInfo"));
            added = new HkxLink(owner, $"{array}[{end}].shape");
            owner.SetPointer(added.Path, box);
        }
        else
        {
            owner.Append(array, box);
            added = new HkxLink(owner, $"{array}[{end}]");
        }
        PlaceInModel(added, parentToModel, toModel);
        return box;
    }

    /// <summary>Takes the shape a link places out of its list or extended mesh group.</summary>
    /// <exception cref="InvalidOperationException">It is the only shape there.</exception>
    public static void Remove(HkxLink link)
    {
        (string array, int index) = Slot(link);
        if (link.Owner.Count(array) == 1)
        {
            throw new InvalidOperationException("This is the only shape its list or group holds; Havok needs at least one there.");
        }
        link.Owner.RemoveAt(array, index);
    }

    // The array a list's child or an extended mesh group's shape sits in, and its place there.
    private static (string Array, int Index) Slot(HkxLink link)
    {
        int open = link.Path.LastIndexOf('['), close = link.Path.LastIndexOf(']');
        string array = open < 0 ? "" : link.Path[..open];
        bool held = link.Owner.Class.Name switch
        {
            "hkpListShape" => array == "childInfo",
            "hkpStorageExtendedMeshShape" => array.EndsWith(".childShapes", StringComparison.Ordinal),
            _ => false,
        };
        return held
            ? (array, int.Parse(link.Path[(open + 1)..close], System.Globalization.CultureInfo.InvariantCulture))
            : throw new NotSupportedException($"Only a list's children and an extended mesh's shapes can be added to or taken out, not a shape a {link.Owner.Class.Name} holds.");
    }

    private static bool IsTranslation(Matrix4x4 m)
        => Matrix4x4.CreateTranslation(m.Translation) is var t && Enumerable.Range(0, 16).All(i => MathF.Abs(m[i / 4, i % 4] - t[i / 4, i % 4]) < Tolerance);
}
