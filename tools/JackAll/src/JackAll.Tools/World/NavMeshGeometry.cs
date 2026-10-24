using System.Collections.Concurrent;
using System.Numerics;
using System.Xml.Linq;
using JackAll.Core.Format.Fcb;
using JackAll.Tools.Hkx;
using JackAll.Tools.Rtx;
using JackAll.Tools.Xbg;

namespace JackAll.Tools.World;

/// <summary>A placed entity's collision as triangles in world space, Z up.</summary>
public sealed class NavMeshSolid
{
    public Vector3[] Vertices { get; }
    public int[] Triangles { get; }
    public Vector3 Min { get; }
    public Vector3 Max { get; }

    public NavMeshSolid(Vector3[] vertices, int[] triangles)
    {
        Vertices = vertices;
        Triangles = triangles;
        (Min, Max) = XbgModel.Bounds(vertices);
    }

    public IEnumerable<(Vector3 A, Vector3 B, Vector3 C)> Faces()
    {
        for (int t = 0; t < Triangles.Length; t += 3)
        {
            yield return (Vertices[Triangles[t]], Vertices[Triangles[t + 1]], Vertices[Triangles[t + 2]]);
        }
    }

    /// <summary>A box on the ground, for tests and quick blockers.</summary>
    public static NavMeshSolid Box(Vector3 centre, Vector3 half, Matrix4x4 rotation)
    {
        (Vector3[] vertices, int[] triangles) = HkxShapes.BoxMesh(half);
        return new([.. vertices.Select(v => Vector3.Transform(v, rotation) + centre)], triangles);
    }

    /// <summary>
    /// The collision of every placed entity with a static, compound or rigid physics component: each
    /// body's <c>NAV_</c> twin where it has one, its own shapes otherwise, and of a breakable object only
    /// the bodies of its intact state.
    /// </summary>
    public static List<NavMeshSolid> Of(
        IEnumerable<WorldEntity> entities, ArchetypeIndex archetypes, Func<string, byte[]?> readByPath,
        IProgress<string>? progress = null)
    {
        var models = new ConcurrentDictionary<(string, string), (Vector3[], int[])?>();
        var missing = new ConcurrentDictionary<string, byte>(StringComparer.OrdinalIgnoreCase);
        var solids = new ConcurrentBag<NavMeshSolid>();
        Parallel.ForEach(entities, entity =>
        {
            XElement? descriptor = FcbEntityFields.ReadDescriptor(entity.Node)
                ?? (archetypes.Winner(entity.ArchetypeName) is { } archetype
                    ? FcbEntityFields.ReadDescriptor(archetype.Node)
                    : null);
            if (entity.Position is not { } position || descriptor is null)
            {
                return;
            }
            foreach (XElement component in descriptor.Elements("component")
                .Where(c => (string?)c.Attribute("class") is "StaticPhysComponent" or "CompoundPhysComponent" or "RigidPhysComponent"))
            {
                string? path = component.Elements("resource").Select(r => (string?)r.Attribute("fileName"))
                    .FirstOrDefault(f => f?.EndsWith(".hkx", StringComparison.OrdinalIgnoreCase) == true);
                if (path is null)
                {
                    continue;
                }
                string[] broken = [.. component.Descendants("state").Where(s => (string?)s.Attribute("index") != "0")
                    .Elements("body").Select(b => (string?)b.Attribute("name") ?? "")];
                string key = string.Join("|", broken);
                if (models.GetOrAdd((path.ToLowerInvariant(), key), _ => readByPath(path) is { } bytes ? Model(bytes, broken) : Missing(path)) is not var (vertices, triangles))
                {
                    continue;
                }
                Matrix4x4 placement = entity.Rotation * Matrix4x4.CreateTranslation(position);
                solids.Add(new([.. vertices.Select(v => Vector3.Transform(v, placement))], triangles));
            }
        });
        if (!missing.IsEmpty)
        {
            progress?.Report($"{missing.Count} collision files were not found, so what uses them is left out of the navmesh: "
                + string.Join(", ", missing.Keys.Order().Take(3)) + (missing.Count > 3 ? ", ..." : ""));
        }
        return [.. solids];

        (Vector3[], int[])? Missing(string path)
        {
            missing.TryAdd(path, 0);
            return null;
        }
    }

    /// <summary>
    /// The trunks of the trees a map scatters, each an upright block as thick as its base and taller than
    /// an agent. Retail's navmesh has a hole at every tree, palm and cactus and none at a bush, and the
    /// line between them is the trunk: none of the bushes' is thicker than <see cref="TrunkRadius"/>.
    /// </summary>
    /// <param name="resources">The path of each scatter resource id; see <see cref="WorldVegetation.ResourcesById"/>.</param>
    public static List<NavMeshSolid> OfVegetation(
        IEnumerable<VegetationInstance> plants, IReadOnlyDictionary<uint, string> resources, Func<string, byte[]?> readByPath)
    {
        var trunks = new Dictionary<uint, (Vector3[], int[])?>();
        var solids = new List<NavMeshSolid>();
        foreach (VegetationInstance plant in plants)
        {
            if (!trunks.TryGetValue(plant.ResourceId, out var trunk))
            {
                trunk = trunks[plant.ResourceId] = resources.TryGetValue(plant.ResourceId, out string? path)
                    && path.EndsWith(".rtx", StringComparison.OrdinalIgnoreCase) && readByPath(path) is { } bytes
                    ? Trunk(RtxModel.Parse(bytes))
                    : null;
            }
            if (trunk is var (vertices, triangles))
            {
                solids.Add(new([.. vertices.Select(v => v + plant.Position)], triangles));
            }
        }
        return solids;
    }

    /// <summary>The thinnest trunk that is an obstacle, in metres.</summary>
    public const float TrunkRadius = 0.12f;

    /// <summary>A tree's trunk as an upright octagon from below its foot to above an agent's head, or null for a bush.</summary>
    private static (Vector3[], int[])? Trunk(RtxModel tree)
    {
        const int sides = 8;
        const float top = 2.5f;
        if (tree.Branches.Count == 0 || tree.Nodes[tree.Branches[0].FirstNode] is not { Radius: >= TrunkRadius } foot)
        {
            return null;
        }
        var vertices = new Vector3[sides * 2];
        for (int k = 0; k < sides; k++)
        {
            float angle = k * MathF.Tau / sides;
            var around = new Vector3(foot.Position.X + foot.Radius * MathF.Cos(angle), foot.Position.Y + foot.Radius * MathF.Sin(angle), 0);
            vertices[k] = around with { Z = foot.Position.Z - 0.5f };
            vertices[sides + k] = around with { Z = top };
        }
        var triangles = new List<int>();
        for (int k = 0; k < sides; k++)
        {
            int next = (k + 1) % sides;
            triangles.AddRange([k, next, sides + next, k, sides + next, sides + k]);
            if (k >= 1 && k < sides - 1)
            {
                triangles.AddRange([0, k + 1, k, sides, sides + k, sides + k + 1]);
            }
        }
        return (vertices, [.. triangles]);
    }

    /// <summary>An <c>.hkx</c>'s collision in model space, leaving out <paramref name="skipped"/> bodies.</summary>
    private static (Vector3[], int[])? Model(byte[] bytes, string[] skipped)
    {
        List<HkxNode> bodies = [.. HkxShapes.Read(HkxFile.Parse(bytes))
            .SelectMany(v => v.Children).Where(n => n.Kind == HkxNodeKind.Body)];
        var byName = bodies.GroupBy(b => b.Label, StringComparer.OrdinalIgnoreCase).ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);
        var vertices = new List<Vector3>();
        var triangles = new List<int>();
        foreach (HkxNode body in bodies.Where(b => !b.IsTwin && !skipped.Contains(b.Label, StringComparer.OrdinalIgnoreCase)))
        {
            HkxNode used = byName.TryGetValue("NAV_" + body.Label, out HkxNode? nav) ? nav : body;
            foreach (HkxPrimitive primitive in used.Primitives())
            {
                int first = vertices.Count;
                Vector3[] placed = [.. primitive.ModelVertices()];
                vertices.AddRange(placed);
                if (primitive.Kind == HkxShapeKind.Mesh)
                {
                    triangles.AddRange(primitive.Triangles.Select(i => first + i));
                    continue;
                }
                // A convex shape's faces are turned outward, so its top faces up whatever order Havok lists them in.
                Vector3 centre = placed.Aggregate(Vector3.Zero, (sum, v) => sum + v) / placed.Length;
                for (int t = 0; t < primitive.Triangles.Length; t += 3)
                {
                    Vector3 a = placed[primitive.Triangles[t]], b = placed[primitive.Triangles[t + 1]], c = placed[primitive.Triangles[t + 2]];
                    bool outward = Vector3.Dot(Vector3.Cross(b - a, c - a), (a + b + c) / 3 - centre) >= 0;
                    triangles.AddRange(outward
                        ? [first + primitive.Triangles[t], first + primitive.Triangles[t + 1], first + primitive.Triangles[t + 2]]
                        : [first + primitive.Triangles[t], first + primitive.Triangles[t + 2], first + primitive.Triangles[t + 1]]);
                }
            }
        }
        return triangles.Count == 0 ? null : ([.. vertices], [.. triangles]);
    }
}
