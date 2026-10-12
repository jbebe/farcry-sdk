using System.Numerics;

namespace JackAll.Tools.Hkx;

/// <summary>
/// The values Havok works out from a body's shapes and saves beside them: the MOPP's code, the list's or extended
/// mesh's bounds, and the body's object radius. The game reads them as saved, so an edited shape leaves them stale.
/// Shipped bounds and radii often differ from what the shapes give, so each is set to the original file's value,
/// moved by how much the shapes' own value changed since then.
/// </summary>
public static class HkxCaches
{
    /// <summary>
    /// Sets the caches of every body, matched by name to <paramref name="original"/>, from how its shapes differ from
    /// the original's; the members written, as <c>body: path</c>. Bodies over shapes this cannot bound keep theirs.
    /// </summary>
    /// <exception cref="InvalidOperationException">A MOPP whose children moved belongs to a file whose part modifiers point into its code.</exception>
    public static List<string> Refresh(HkxFile file, HkxFile original)
    {
        Dictionary<string, HkxObject> before = Bodies(original);
        bool modified = file.Objects().Any(o => o.Class.Name == "nomadPartModifiersGroup");
        var written = new List<string>();
        foreach ((string name, HkxObject body) in Bodies(file))
        {
            if (before.GetValueOrDefault(name)?.GetPointer("collidable.shape") is not { } was || body.GetPointer("collidable.shape") is not { } shape)
            {
                continue;
            }
            try
            {
                written.AddRange(Refresh(body, shape, before[name], was, modified).Select(path => $"{name}: {path}"));
            }
            catch (NotSupportedException)
            {
            }
        }
        return written;
    }

    private static IEnumerable<string> Refresh(HkxObject body, HkxObject shape, HkxObject oldBody, HkxObject oldShape, bool modified)
    {
        var written = new List<string>();
        if (Collection(shape) is { } collection && Collection(oldShape) is { } oldCollection && collection.Class == oldCollection.Class)
        {
            (Vector3 min, Vector3 max) = HkxShapes.Bounds(collection);
            (Vector3 oldMin, Vector3 oldMax) = HkxShapes.Bounds(oldCollection);
            float[] half = oldCollection.GetReals("aabbHalfExtents"), centre = oldCollection.GetReals("aabbCenter");
            if (min != oldMin || max != oldMax)
            {
                Vector3 low = Vec(centre) - Vec(half) + min - oldMin, high = Vec(centre) + Vec(half) + max - oldMax;
                (half, centre) = ([.. Lanes((high - low) / 2), half[3]], [.. Lanes((low + high) / 2), centre[3]]);
            }
            written.AddRange(Set(collection, "aabbHalfExtents", half).Concat(Set(collection, "aabbCenter", centre)));
            if (shape.Class.Name == "hkpMoppBvTreeShape")
            {
                written.AddRange(Mopp(shape, oldShape, modified));
            }
        }

        float radius = oldBody.GetReals("motion.motionState.objectRadius")[0], moved = Radius(body, shape) - Radius(oldBody, oldShape);
        written.AddRange(Set(body, "motion.motionState.objectRadius", [moved == 0 ? radius : radius + moved]));
        return written;
    }

    // The original code while the MOPP's children stand where they did, code built for where they are otherwise.
    private static IEnumerable<string> Mopp(HkxObject mopp, HkxObject original, bool modified)
    {
        List<HkxChild> children = HkxMopp.Items(mopp);
        (Vector4 info, byte[] data) = children.SequenceEqual(HkxMopp.Items(original)) ? HkxMopp.Code(original) : HkxMopp.Build(children);
        (Vector4 savedInfo, byte[] saved) = HkxMopp.Code(mopp);
        if (info == savedInfo && data.AsSpan().SequenceEqual(saved))
        {
            return [];
        }
        if (modified)
        {
            throw new InvalidOperationException("This file's part modifiers point into the MOPP code of a body whose shapes moved, so the code cannot be rebuilt.");
        }
        HkxMopp.Write(mopp, info, data);
        return ["MOPP code"];
    }

    // Havok's object radius: from the centre of mass to the centre of the shape's bounds, plus half their diagonal.
    private static float Radius(HkxObject body, HkxObject shape)
    {
        (Vector3 min, Vector3 max) = HkxShapes.Bounds(shape);
        return Vector3.Distance((min + max) / 2, Vec(body.GetReals("motion.motionState.sweptTransform.centerOfMassLocal")))
            + Vector3.Distance(min, max) / 2;
    }

    // The list or extended mesh a body's shape is, or that its MOPP wraps.
    private static HkxObject? Collection(HkxObject shape) => shape.Class.Name switch
    {
        "hkpMoppBvTreeShape" => shape.GetPointer("child.childShape"),
        "hkpListShape" or "hkpStorageExtendedMeshShape" => shape,
        _ => null,
    };

    // Each rigid body by its name, with an occurrence number after a repeated one.
    private static Dictionary<string, HkxObject> Bodies(HkxFile file)
    {
        var bodies = new Dictionary<string, HkxObject>();
        foreach (HkxObject body in file.Objects().Where(o => o.Class.Name == "hkpRigidBody"))
        {
            string name = body.GetString("name") ?? "";
            string key = name;
            for (int k = 2; bodies.ContainsKey(key); k++)
            {
                key = $"{name} ({k})";
            }
            bodies[key] = body;
        }
        return bodies;
    }

    private static IEnumerable<string> Set(HkxObject o, string path, float[] value)
    {
        if (o.GetReals(path).AsSpan().SequenceEqual(value))
        {
            return [];
        }
        o.SetReals(path, value);
        return [path];
    }

    private static Vector3 Vec(float[] f) => new(f[0], f[1], f[2]);

    private static float[] Lanes(Vector3 v) => [v.X, v.Y, v.Z];
}
