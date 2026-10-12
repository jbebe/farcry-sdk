using System.Numerics;

namespace JackAll.Tools.Hkx;

public enum HkxProblemSeverity { Note, Warning, Error }

public sealed record HkxProblem(HkxProblemSeverity Severity, string Message);

/// <summary>What would break an <c>.hkx</c> in game: rules the engine relies on, and caches that no longer match the shapes.</summary>
public static class HkxLint
{
    // Cached bounds and radii this far inside what the shapes give are taken as rounding.
    private const float Tolerance = 1e-3f;
    private const uint ShapesSubpartKey = 0x80000000;

    public static IReadOnlyList<HkxProblem> Check(HkxFile file)
    {
        var problems = new List<HkxProblem>();
        void Add(HkxProblemSeverity severity, string message) => problems.Add(new HkxProblem(severity, message));

        foreach (HkxObject system in file.Objects().Where(o => o.Class.Name == "hkpPhysicsSystem"))
        {
            List<HkxObject> bodies = [.. system.GetPointers("rigidBodies").OfType<HkxObject>()];
            // The game keeps a position, rotation and mass only for the bodies before the first HIGH_ or NAV_ one.
            int twins = bodies.FindIndex(b => IsTwin(Name(b)));
            foreach (HkxObject body in twins < 0 ? [] : bodies.Skip(twins).Where(b => !IsTwin(Name(b))))
            {
                Add(HkxProblemSeverity.Error, $"Body '{Name(body)}' comes after '{Name(bodies[twins])}'. The game keeps a position, rotation and "
                    + "mass only for the bodies before the first HIGH_ or NAV_ one, so this body reads past them; move it before.");
            }
            // A constraint's second body may be missing, which holds the first to the world.
            foreach (HkxObject constraint in system.GetPointers("constraints").OfType<HkxObject>())
            {
                if (constraint.GetPointer("entities[0]") is not { } a || !bodies.Contains(a)
                    || (constraint.GetPointer("entities[1]") is { } b && !bodies.Contains(b)))
                {
                    Add(HkxProblemSeverity.Error, $"Constraint '{constraint.GetString("name")}' joins a body that is not in its own system.");
                }
            }
        }

        foreach (IGrouping<string, HkxObject> twice in file.Objects().Where(o => o.Class.Name == "hkpRigidBody").GroupBy(Name).Where(g => g.Count() > 1))
        {
            Add(HkxProblemSeverity.Warning, $"{twice.Count()} bodies are named '{twice.Key}'.");
        }

        foreach (HkxObject wrapper in file.Objects().Where(HkxShapes.IsWrapper))
        {
            if (wrapper.GetPointer("childShape.childShape") is { } child && wrapper.GetReals("radius")[0] != child.GetReals("radius")[0])
            {
                Add(HkxProblemSeverity.Warning, $"A {wrapper.Class.Name} has a radius other than the shape it wraps; every shipped one carries its child's.");
            }
        }

        foreach ((string name, HkxObject body) in HkxCaches.Bodies(file))
        {
            if (body.GetPointer("collidable.shape") is { } shape && HkxShapes.Collection(shape) is { } collection)
            {
                try
                {
                    Collection(name, shape, collection, Add);
                }
                catch (NotSupportedException)
                {
                }
            }
        }
        return problems;
    }

    // A list's cached bounds and a MOPP's code against the children. Extended meshes keep only the shapes they hold to
    // it: some shipped breakable pieces cache bounds, and build code, for triangles other than the ones they store.
    private static void Collection(string name, HkxObject shape, HkxObject collection, Action<HkxProblemSeverity, string> add)
    {
        bool mesh = collection.Class.Name == "hkpStorageExtendedMeshShape";
        if (!mesh)
        {
            (Vector3 min, Vector3 max) = HkxShapes.Bounds(collection);
            Vector3 centre = HkxShapes.Vec(collection.GetReals("aabbCenter")), half = HkxShapes.Vec(collection.GetReals("aabbHalfExtents"));
            Vector3 below = centre - half - min, above = max - centre - half;
            if (MathF.Max(MathF.Max(below.X, below.Y), MathF.Max(below.Z, MathF.Max(MathF.Max(above.X, above.Y), above.Z))) > Tolerance)
            {
                add(HkxProblemSeverity.Error, $"Body '{name}': its list's cached bounds do not cover its children, so the game misses "
                    + "collisions outside them. Run hkx refresh.");
            }
        }
        if (shape.Class.Name == "hkpMoppBvTreeShape")
        {
            (Vector4 info, byte[] code) = HkxMopp.Code(shape);
            IEnumerable<HkxChild> children = HkxMopp.Items(shape).Where(c => !mesh || (c.Key & ShapesSubpartKey) != 0);
            if (HkxMopp.Misses(children, info, code) is { Count: > 0 } lost)
            {
                add(HkxProblemSeverity.Error, $"Body '{name}': its MOPP does not find {lost.Count} of its shapes where they now are, so the "
                    + "game never collides with them there. Run hkx refresh.");
            }
        }
    }

    private static string Name(HkxObject body) => body.GetString("name") ?? "";

    private static bool IsTwin(string name) => name.Contains("HIGH_", StringComparison.Ordinal) || name.Contains("NAV_", StringComparison.Ordinal);
}
