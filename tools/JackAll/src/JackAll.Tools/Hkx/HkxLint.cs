using System.Numerics;
using JackAll.Tools.World;

namespace JackAll.Tools.Hkx;

public sealed record HkxProblem(LintSeverity Severity, string Message);

/// <summary>What would break an <c>.hkx</c> in game: rules the engine relies on, and caches that no longer match the shapes.</summary>
public static class HkxLint
{
    // Cached bounds this far inside what the shapes give are taken as rounding.
    private const float Tolerance = 1e-3f;
    private const string RefreshAdvice = "Refresh it against the original: hkx refresh, or hkx encode --original.";

    public static IReadOnlyList<HkxProblem> Check(HkxFile file)
    {
        var problems = new List<HkxProblem>();
        void Add(LintSeverity severity, string message) => problems.Add(new HkxProblem(severity, message));

        foreach (HkxObject system in file.Objects().Where(o => o.Class.Name == "hkpPhysicsSystem"))
        {
            List<HkxObject> bodies = [.. system.GetPointers("rigidBodies").OfType<HkxObject>()];
            int twins = bodies.FindIndex(b => IsTwin(Name(b)));
            foreach (HkxObject body in twins < 0 ? [] : bodies.Skip(twins).Where(b => !IsTwin(Name(b))))
            {
                Add(LintSeverity.Error, $"Body '{Name(body)}' comes after '{Name(bodies[twins])}'. The game keeps a position, rotation and "
                    + "mass only for the bodies before the first HIGH_ or NAV_ one, so this body reads past them; move it before.");
            }
            // A constraint's second body may be missing, which holds the first to the world.
            foreach (HkxObject constraint in system.GetPointers("constraints").OfType<HkxObject>())
            {
                if (constraint.GetPointer("entities[0]") is not { } a || !bodies.Contains(a)
                    || (constraint.GetPointer("entities[1]") is { } b && !bodies.Contains(b)))
                {
                    Add(LintSeverity.Error, $"Constraint '{constraint.GetString("name")}' joins a body that is not in its own system.");
                }
            }
        }

        foreach (IGrouping<string, HkxObject> twice in file.Objects().Where(o => o.Class.Name == "hkpRigidBody").GroupBy(Name).Where(g => g.Count() > 1))
        {
            Add(LintSeverity.Warning, $"{twice.Count()} bodies are named '{twice.Key}'.");
        }

        foreach ((HkxObject owner, string member, _) in HkxCaches.StaleCopies(file))
        {
            Add(LintSeverity.Warning, $"A {owner.Class.Name}'s {member} no longer copies what it does in every shipped file. {RefreshAdvice}");
        }

        foreach ((string name, HkxObject body) in HkxCaches.Bodies(file))
        {
            if (body.GetPointer("collidable.shape") is not { } shape)
            {
                continue;
            }
            try
            {
                if (HkxShapes.Collection(shape) is { Class.Name: "hkpListShape" } list && !Covers(list))
                {
                    Add(LintSeverity.Error, $"Body '{name}': its list's cached bounds do not cover its children, so the game misses "
                        + $"collisions outside them. {RefreshAdvice}");
                }
                if (shape.Class.Name == "hkpMoppBvTreeShape" && HkxMopp.Lost(shape) is { Count: > 0 } lost)
                {
                    Add(LintSeverity.Error, $"Body '{name}': its MOPP does not find {lost.Count} of its shapes where they now are, so the "
                        + $"game never collides with them there. {RefreshAdvice}");
                }
            }
            catch (NotSupportedException)
            {
            }
        }
        return problems;
    }

    // Whether a list's cached bounds hold its children's.
    private static bool Covers(HkxObject list)
    {
        (Vector3 min, Vector3 max) = HkxShapes.Bounds(list);
        Vector3 centre = HkxShapes.Vec(list.GetReals("aabbCenter")), half = HkxShapes.Vec(list.GetReals("aabbHalfExtents"));
        Vector3 gap = Vector3.Max(centre - half - min, max - centre - half);
        return MathF.Max(gap.X, MathF.Max(gap.Y, gap.Z)) <= Tolerance;
    }

    private static string Name(HkxObject body) => body.GetString("name") ?? "";

    private static bool IsTwin(string name) => name.Contains("HIGH_", StringComparison.Ordinal) || name.Contains("NAV_", StringComparison.Ordinal);
}
