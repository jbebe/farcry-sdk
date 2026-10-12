using System.Numerics;

namespace JackAll.Tools.Hkx;

/// <summary>
/// The values Havok works out from a body's shapes and saves beside them: the MOPP's code, the list's or extended
/// mesh's bounds, and the body's object radius. The game reads them as saved, so an edited shape leaves them stale.
/// Shipped bounds and radii often differ from what the shapes give, so a refresh moves each saved value by how much
/// the shapes' own value changed since the file it is given as the original.
/// </summary>
public static class HkxCaches
{
    /// <summary>
    /// Refreshes the caches of every body whose shapes changed since <paramref name="original"/>, matching bodies by
    /// name; the members written, as <c>body: path</c>. Bodies over shapes this cannot bound keep theirs.
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
            if (min != oldMin || max != oldMax)
            {
                float[] half = collection.GetReals("aabbHalfExtents"), centre = collection.GetReals("aabbCenter");
                Vector3 low = Vec(centre) - Vec(half) + min - oldMin, high = Vec(centre) + Vec(half) + max - oldMax;
                collection.SetReals("aabbHalfExtents", [.. Lanes((high - low) / 2), half[3]]);
                collection.SetReals("aabbCenter", [.. Lanes((low + high) / 2), centre[3]]);
                written.AddRange(["aabbHalfExtents", "aabbCenter"]);
            }
            if (shape.Class.Name == "hkpMoppBvTreeShape" && !HkxMopp.Items(shape).SequenceEqual(HkxMopp.Items(oldShape)))
            {
                if (modified)
                {
                    throw new InvalidOperationException("This file's part modifiers point into the MOPP code of a body whose shapes moved, so the code cannot be rebuilt.");
                }
                HkxMopp.Rebuild(shape);
                written.Add("collidable.shape (MOPP code)");
            }
        }

        float radius = Radius(body, shape) - Radius(oldBody, oldShape);
        if (radius != 0)
        {
            body.SetReals("motion.motionState.objectRadius", body.GetReals("motion.motionState.objectRadius")[0] + radius);
            written.Add("motion.motionState.objectRadius");
        }
        return written;
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

    private static Vector3 Vec(float[] f) => new(f[0], f[1], f[2]);

    private static float[] Lanes(Vector3 v) => [v.X, v.Y, v.Z];
}
