using System.Numerics;

namespace JackAll.Tools.Hkx;

/// <summary>
/// The values Havok works out from a body's shapes and saves beside them: the MOPP's code, the list's or extended
/// mesh's bounds, the body's object radius, and the copies a file keeps of a wrapped shape's radius, a list's child
/// count and a storage mesh's shapes. The game reads them as saved, so an edited shape leaves them stale.
/// Shipped bounds and radii often differ from what the shapes give, so where a body's shapes changed each value
/// moves by as much as the shapes' own value did; where they did not, it is left alone.
/// </summary>
public static class HkxCaches
{
    /// <summary>
    /// Brings the caches of every body, matched by name to <paramref name="original"/>, in line with how its shapes
    /// changed since then; a body the original lacks gets them worked out afresh. Returns the members written, as
    /// <c>body: path</c>. Bodies over shapes this cannot bound keep theirs.
    /// </summary>
    /// <exception cref="InvalidOperationException">A MOPP that has to be rebuilt belongs to a file whose part modifiers point into its code.</exception>
    public static List<string> Refresh(HkxFile file, HkxFile original)
    {
        Dictionary<string, HkxObject> before = Bodies(original);
        bool modified = file.Objects().Any(o => o.Class.Name == "nomadPartModifiersGroup");
        var written = new List<string>();
        foreach ((HkxObject owner, string member, Action fix) in StaleCopies(file).ToList())
        {
            fix();
            written.Add($"{owner.Class.Name}: {member}");
        }
        foreach ((string name, HkxObject body) in Bodies(file))
        {
            if (body.GetPointer("collidable.shape") is not { } shape)
            {
                continue;
            }
            try
            {
                IEnumerable<string> paths = before.GetValueOrDefault(name) is { } oldBody && oldBody.GetPointer("collidable.shape") is { } was
                    ? Moved(body, shape, oldBody, was, modified)
                    : Afresh(body, shape, modified);
                written.AddRange(paths.Select(path => $"{name}: {path}"));
            }
            catch (NotSupportedException)
            {
            }
        }
        return written;
    }

    /// <summary>
    /// The members every shipped file holds as an exact copy of another, where this file's no longer do: a wrapper's
    /// radius, a list's child count in each child, a storage mesh's shapes. Each comes with what sets it right.
    /// </summary>
    internal static IEnumerable<(HkxObject Owner, string Member, Action Fix)> StaleCopies(HkxFile file)
    {
        foreach (HkxObject o in file.Objects())
        {
            if (HkxShapes.IsWrapper(o) && o.GetPointer("childShape.childShape") is { } child && !child.GetReals("radius").AsSpan().SequenceEqual(o.GetReals("radius")))
            {
                yield return (o, "radius", () => o.SetReals("radius", child.GetReals("radius")));
            }
            else if (o.Class.Name == "hkpListShape" && o.Count("childInfo") is var count
                && Enumerable.Range(0, count).Any(i => o.GetInt($"childInfo[{i}].numChildShapes") != count))
            {
                yield return (o, "childInfo[*].numChildShapes", () =>
                {
                    for (int i = 0; i < count; i++)
                    {
                        o.SetRaw($"childInfo[{i}].numChildShapes", BitConverter.GetBytes(count));
                    }
                });
            }
            else if (o.Class.Name == "hkpStorageExtendedMeshShape")
            {
                List<HkxObject?> storage = o.GetPointers("shapestorage");
                for (int k = 0; k < storage.Count; k++)
                {
                    List<HkxObject?> shapes = o.GetPointers($"shapesSubparts[{k}].childShapes");
                    if (storage[k] is { } copy && !copy.GetPointers("shapes").SequenceEqual(shapes))
                    {
                        yield return (o, $"shapestorage[{k}].shapes", () => copy.SetPointers("shapes", shapes));
                    }
                }
            }
        }
    }

    // A body's caches, each moved by as much as its shapes' own value changed since the original.
    private static List<string> Moved(HkxObject body, HkxObject shape, HkxObject oldBody, HkxObject oldShape, bool modified)
    {
        var written = new List<string>();
        if (HkxShapes.Collection(shape) is { } collection && HkxShapes.Collection(oldShape) is { } oldCollection && collection.Class == oldCollection.Class)
        {
            (Vector3 min, Vector3 max) = HkxShapes.Bounds(collection);
            (Vector3 oldMin, Vector3 oldMax) = HkxShapes.Bounds(oldCollection);
            if (min != oldMin || max != oldMax)
            {
                float[] half = oldCollection.GetReals("aabbHalfExtents"), centre = oldCollection.GetReals("aabbCenter");
                Vector3 low = HkxShapes.Vec(centre) - HkxShapes.Vec(half) + min - oldMin, high = HkxShapes.Vec(centre) + HkxShapes.Vec(half) + max - oldMax;
                written.AddRange(Bounds(collection, low, high));
            }
            if (shape.Class.Name == "hkpMoppBvTreeShape" && !HkxMopp.Items(shape).SequenceEqual(HkxMopp.Items(oldShape)))
            {
                written.AddRange(Rebuild(shape, modified));
            }
        }

        float moved = Radius(body, shape) - Radius(oldBody, oldShape);
        if (moved != 0)
        {
            written.AddRange(Set(body, "motion.motionState.objectRadius", [oldBody.GetReals("motion.motionState.objectRadius")[0] + moved]));
        }
        return written;
    }

    // A new body's caches, by Havok's own rules; its MOPP is rebuilt only where its code has lost a child.
    private static List<string> Afresh(HkxObject body, HkxObject shape, bool modified)
    {
        var written = new List<string>();
        if (HkxShapes.Collection(shape) is { } collection)
        {
            (Vector3 min, Vector3 max) = HkxShapes.Bounds(collection);
            written.AddRange(Bounds(collection, min, max));
            if (shape.Class.Name == "hkpMoppBvTreeShape" && HkxMopp.Code(shape) is var (info, code) && HkxMopp.Misses(HkxMopp.Items(shape), info, code).Count > 0)
            {
                written.AddRange(Rebuild(shape, modified));
            }
        }
        written.AddRange(Set(body, "motion.motionState.objectRadius", [Radius(body, shape)]));
        return written;
    }

    // Writes a collection's cached box, keeping the w lanes.
    private static IEnumerable<string> Bounds(HkxObject collection, Vector3 low, Vector3 high)
        => Set(collection, "aabbHalfExtents", HkxShapes.Lanes((high - low) / 2, collection.GetReals("aabbHalfExtents")[3]))
            .Concat(Set(collection, "aabbCenter", HkxShapes.Lanes((low + high) / 2, collection.GetReals("aabbCenter")[3])));

    private static IEnumerable<string> Rebuild(HkxObject mopp, bool modified)
    {
        if (modified)
        {
            throw new InvalidOperationException("This file's part modifiers point into the MOPP code of a body whose shapes moved, so the code cannot be rebuilt.");
        }
        HkxMopp.Rebuild(mopp);
        return ["MOPP code"];
    }

    /// <summary>Havok's object radius: from the centre of mass to the centre of the shape's bounds, plus half their diagonal.</summary>
    internal static float Radius(HkxObject body, HkxObject shape)
    {
        (Vector3 min, Vector3 max) = HkxShapes.Bounds(shape);
        return Vector3.Distance((min + max) / 2, HkxShapes.Vec(body.GetReals("motion.motionState.sweptTransform.centerOfMassLocal")))
            + Vector3.Distance(min, max) / 2;
    }

    /// <summary>Each rigid body by its name, with an occurrence number after a repeated one.</summary>
    internal static Dictionary<string, HkxObject> Bodies(HkxFile file)
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

}
