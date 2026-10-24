using System.Globalization;
using System.Numerics;
using JackAll.Tools.Xbg;

namespace JackAll.Tools.Hkx;

public enum HkxShapeKind { Box, Hull, Mesh, Capsule, Sphere }

public enum HkxNodeKind { Variant, Body, Shape, Constraint }

/// <summary>A drawable collision surface: triangles in the shape's own space, and the matrix placing them in the model.</summary>
public sealed record HkxPrimitive(HkxObject Shape, HkxShapeKind Kind, Matrix4x4 ToModel, Vector3[] Vertices, int[] Triangles)
{
    public IEnumerable<Vector3> ModelVertices() => Vertices.Select(v => Vector3.Transform(v, ToModel));
}

/// <summary>A shape collection's child by its shape key, and the box around its collision in the collection's space.</summary>
public readonly record struct HkxChild(uint Key, Vector3 Min, Vector3 Max);

/// <summary>A row of an <c>.hkx</c>'s collision tree: a variant, body, shape or constraint.</summary>
public sealed class HkxNode(HkxNodeKind kind, HkxObject? obj, string label)
{
    public HkxNodeKind Kind { get; } = kind;
    public HkxObject? Object { get; } = obj;
    public string Label { get; } = label;
    public HkxPrimitive? Primitive { get; init; }

    /// <summary>The member that places a shape: its translate or transform shape's when it has one, else its own.</summary>
    public HkxLink? Link { get; init; }

    public List<HkxNode> Children { get; } = [];

    /// <summary>A body's <c>HIGH_</c> or <c>NAV_</c> twin, which the game pairs with the body it is named after.</summary>
    public bool IsTwin => Kind == HkxNodeKind.Body
        && (Label.StartsWith("HIGH_", StringComparison.Ordinal) || Label.StartsWith("NAV_", StringComparison.Ordinal));

    /// <summary>This node's surface and every one beneath it.</summary>
    public IEnumerable<HkxPrimitive> Primitives()
        => (Primitive is null ? Array.Empty<HkxPrimitive>() : [Primitive]).Concat(Children.SelectMany(c => c.Primitives()));

    /// <summary>This node and every one beneath it.</summary>
    public IEnumerable<HkxNode> Descendants() => Children.SelectMany(c => c.Descendants()).Prepend(this);
}

/// <summary>
/// An <c>.hkx</c>'s bodies, shapes and constraints as a tree, each shape's collision surface placed in model
/// space (Z up, as the <c>.xbg</c>). Boxes and hulls are drawn grown by their convex radius, which is where
/// Havok collides.
/// </summary>
public static class HkxShapes
{
    private const int Int32Indices = 2;
    private const int SphereSegments = 16, SphereRings = 8;
    private const float DegenerateArea = 1e-7f;
    internal const uint ShapesSubpartKey = 0x80000000;

    public static List<HkxNode> Read(HkxFile file)
    {
        HkxObject root = file.Root;
        int variants = root.Count("namedVariants");
        var nodes = new List<HkxNode>();
        for (int i = 0; i < variants; i++)
        {
            HkxObject? variant = root.GetPointer($"namedVariants[{i}].variant");
            var node = new HkxNode(HkxNodeKind.Variant, variant, root.GetString($"namedVariants[{i}].name") ?? variant?.Class.Name ?? "?");
            foreach (HkxObject system in variant?.Class.Name == "hkpPhysicsData" ? variant.GetPointers("systems").OfType<HkxObject>() : Enumerable.Empty<HkxObject>())
            {
                node.Children.AddRange(system.GetPointers("rigidBodies").OfType<HkxObject>().Select(Body));
                node.Children.AddRange(system.GetPointers("constraints").OfType<HkxObject>().Select(Constraint));
            }
            nodes.Add(node);
        }
        return nodes;
    }

    /// <summary>The surface of one convex shape that wraps nothing, or null for a class this does not draw.</summary>
    public static HkxPrimitive? Leaf(HkxObject shape, Matrix4x4 toModel)
    {
        if (!Derives(shape.Class, "hkpConvexShape"))
        {
            return null;
        }
        float radius = shape.GetReals("radius")[0];
        return shape.Class.Name switch
        {
            "hkpBoxShape" => Box(shape, Vec(shape.GetReals("halfExtents")) + new Vector3(radius), toModel),
            "hkpSphereShape" => Rounded(shape, HkxShapeKind.Sphere, Vector3.Zero, Vector3.Zero, radius, toModel),
            "hkpCapsuleShape" => Rounded(shape, HkxShapeKind.Capsule, Vec(shape.GetReals("vertexA")), Vec(shape.GetReals("vertexB")), radius, toModel),
            "hkpConvexVerticesShape" when shape.GetPointer("connectivity") is not null => Hull(shape, radius, toModel),
            _ => null,
        };
    }

    /// <summary>
    /// A hull's face planes from its unshrunk vertices, as Havok stores them in <c>planeEquations</c>: an outward
    /// unit normal and the distance that puts a vertex on its face at zero.
    /// </summary>
    public static Vector4[] HullPlanes(HkxObject hull)
    {
        (Vector3[] vertices, int[][] faces) = HullFaces(hull);
        return Planes(vertices, faces);
    }

    /// <summary>The box around a shape's collision in its own space.</summary>
    public static (Vector3 Min, Vector3 Max) Bounds(HkxObject shape)
        => Collection(shape) is { } collection
            ? XbgModel.Bounds(Children(collection).SelectMany(c => new[] { c.Min, c.Max }))
            : Bounds(shape, Matrix4x4.Identity);

    /// <summary>The children of a list or extended mesh, each by the shape key the collection gives it.</summary>
    public static List<HkxChild> Children(HkxObject collection) => collection.Class.Name switch
    {
        "hkpListShape" => [.. Enumerable.Range(0, collection.Count("childInfo"))
            .Select(i => Child((uint)i, Bounds(collection.GetPointer($"childInfo[{i}].shape")!, Matrix4x4.Identity)))],
        "hkpStorageExtendedMeshShape" => MeshChildren(collection),
        _ => throw new NotSupportedException($"{collection.Class.Name} is not a collection this reads."),
    };

    private static List<HkxChild> MeshChildren(HkxObject mesh)
    {
        int subpartShift = 32 - mesh.GetInt("numBitsForSubpartIndex");
        float radius = mesh.GetReals("triangleRadius")[0];
        var children = new List<HkxChild>();
        for (int s = 0; s < mesh.Count("trianglesSubparts"); s++)
        {
            (Vector3[] v, int[] t) = Triangles(mesh, s);
            Vector3 extrusion = Vec(mesh.GetReals($"trianglesSubparts[{s}].extrusion"));
            for (int k = 0; k < t.Length / 3; k++)
            {
                Vector3 a = v[t[k * 3]], b = v[t[k * 3 + 1]], c = v[t[k * 3 + 2]];
                if (!IsDegenerate(a, b, c))
                {
                    children.Add(Child((uint)s << subpartShift | (uint)k, Grown([a, b, c, a + extrusion, b + extrusion, c + extrusion], radius)));
                }
            }
        }
        for (int s = 0; s < mesh.Count("shapesSubparts"); s++)
        {
            Matrix4x4 placement = Placement(mesh, s);
            List<HkxObject?> shapes = mesh.GetPointers($"shapesSubparts[{s}].childShapes");
            for (int k = 0; k < shapes.Count; k++)
            {
                children.Add(Child(ShapesSubpartKey | (uint)s << subpartShift | (uint)k, Bounds(shapes[k]!, placement)));
            }
        }
        return children;
    }

    private static HkxChild Child(uint key, (Vector3 Min, Vector3 Max) bounds) => new(key, bounds.Min, bounds.Max);

    // A triangle the mesh leaves out when it lists its keys, by Havok's own test.
    private static bool IsDegenerate(Vector3 a, Vector3 b, Vector3 c)
    {
        Vector3 u = a - b, v = c - b;
        float dot = Vector3.Dot(u, v);
        return Vector3.Cross(a - b, a - c).LengthSquared() < DegenerateArea || Vector3.Cross(b - a, b - c).LengthSquared() < DegenerateArea
            || u.LengthSquared() * v.LengthSquared() - dot * dot == 0;
    }

    // The box around a convex shape's collision placed by `toSpace`: its core points there, grown by its radius.
    private static (Vector3 Min, Vector3 Max) Bounds(HkxObject shape, Matrix4x4 toSpace)
    {
        if (Unwrap(shape) is var (child, local, _))
        {
            return Bounds(child!, local * toSpace);
        }
        Vector3[] core = shape.Class.Name switch
        {
            "hkpBoxShape" => Corners(Vec(shape.GetReals("halfExtents"))),
            "hkpConvexVerticesShape" => HullVertices(shape),
            "hkpSphereShape" => [Vector3.Zero],
            "hkpCapsuleShape" => [Vec(shape.GetReals("vertexA")), Vec(shape.GetReals("vertexB"))],
            _ => throw new NotSupportedException($"No bounds for a {shape.Class.Name}."),
        };
        return Grown(core.Select(p => Vector3.Transform(p, toSpace)), shape.GetReals("radius")[0]);
    }

    private static (Vector3 Min, Vector3 Max) Grown(IEnumerable<Vector3> points, float radius)
    {
        (Vector3 min, Vector3 max) = XbgModel.Bounds(points);
        return (min - new Vector3(radius), max + new Vector3(radius));
    }

    // A triangle subpart's vertices, scaled as its mesh scales them, and three indices into them per triangle.
    private static (Vector3[] Vertices, int[] Triangles) Triangles(HkxObject mesh, int subpart)
    {
        string part = $"trianglesSubparts[{subpart}].";
        bool wide = mesh.GetInt(part + "stridingType") == Int32Indices;
        int stride = mesh.GetInt(part + "indexStriding") / (wide ? 4 : 2);
        int count = mesh.GetInt(part + "numTriangleShapes");
        HkxObject storage = mesh.GetPointers("meshstorage")[subpart]!;
        int[] indices = storage.GetIntArray(wide ? "indices32" : "indices16");
        float[] v = storage.GetRealArray("vertices");
        Vector3 scale = Vec(mesh.GetReals("scaling"));
        Vector3[] vertices = [.. Enumerable.Range(0, v.Length / 4).Select(k => new Vector3(v[k * 4], v[k * 4 + 1], v[k * 4 + 2]) * scale)];
        int[] triangles = new int[count * 3];
        for (int t = 0; t < count; t++)
        {
            indices.AsSpan(t * stride, 3).CopyTo(triangles.AsSpan(t * 3));
        }
        return (vertices, triangles);
    }

    // The matrix placing a shape subpart's children in its mesh's space.
    private static Matrix4x4 Placement(HkxObject mesh, int subpart)
    {
        string part = $"shapesSubparts[{subpart}].";
        Matrix4x4 local = mesh.GetInt(part + "rotationSet") != 0
            ? Matrix4x4.CreateFromQuaternion(Quat(mesh.GetReals(part + "rotation")))
            : Matrix4x4.Identity;
        return mesh.GetInt(part + "offsetSet") != 0 ? local * Matrix4x4.CreateTranslation(Vec(mesh.GetReals(part + "translation"))) : local;
    }

    internal static Vector3 Vec(float[] f) => new(f[0], f[1], f[2]);

    /// <summary>A vector's xyz followed by <paramref name="w"/>, as a member stores it.</summary>
    internal static float[] Lanes(Vector3 v, float w) => [v.X, v.Y, v.Z, w];

    /// <summary>Whether a shape is a translate or transform shape: one that places the shape it wraps.</summary>
    internal static bool IsWrapper(HkxObject shape) => shape.Class.Name is "hkpConvexTranslateShape" or "hkpConvexTransformShape";

    /// <summary>The list or extended mesh a shape is, or that its MOPP wraps.</summary>
    internal static HkxObject? Collection(HkxObject shape) => shape.Class.Name switch
    {
        "hkpMoppBvTreeShape" => shape.GetPointer("child.childShape"),
        "hkpListShape" or "hkpStorageExtendedMeshShape" => shape,
        _ => null,
    };

    /// <summary>The shape a MOPP, transform or translate shape wraps, the matrix placing it in the wrapper's space, and the wrapper's label.</summary>
    internal static (HkxObject? Child, Matrix4x4 Local, string Label)? Unwrap(HkxObject shape) => shape.Class.Name switch
    {
        "hkpMoppBvTreeShape" => (shape.GetPointer("child.childShape"), Matrix4x4.Identity, "MOPP"),
        "hkpConvexTransformShape" => (shape.GetPointer("childShape.childShape"), Transform(shape.GetReals("transform")), "Transform"),
        "hkpConvexTranslateShape" => (shape.GetPointer("childShape.childShape"), Matrix4x4.CreateTranslation(Vec(shape.GetReals("translation"))), "Translate"),
        _ => null,
    };

    // A hull's vertices as stored, before its convex radius grows them.
    private static Vector3[] HullVertices(HkxObject hull)
    {
        float[] packed = hull.GetRealArray("rotatedVertices");
        // Four vertices per block of twelve floats: their x values, then their y values, then their z values.
        return [.. Enumerable.Range(0, hull.GetInt("numVertices")).Select(k =>
        {
            int block = k / 4 * 12, lane = k % 4;
            return new Vector3(packed[block + lane], packed[block + 4 + lane], packed[block + 8 + lane]);
        })];
    }

    // The eight corners of a box centred on the origin.
    private static Vector3[] Corners(Vector3 half) => [.. Enumerable.Range(0, 8).Select(i => new Vector3(
        (i & 1) != 0 ? half.X : -half.X, (i & 2) != 0 ? half.Y : -half.Y, (i & 4) != 0 ? half.Z : -half.Z))];

    private static HkxNode Body(HkxObject body)
    {
        var node = new HkxNode(HkxNodeKind.Body, body, body.GetString("name") ?? "(unnamed body)");
        if (body.GetPointer("collidable.shape") is { } shape)
        {
            node.Children.Add(Shape(shape, Transform(body.GetReals("motion.motionState.transform")), new HkxLink(body, "collidable.shape")));
        }
        return node;
    }

    private static HkxNode Constraint(HkxObject constraint)
    {
        HkxObject? data = constraint.GetPointer("data");
        string a = constraint.GetPointer("entities[0]")?.GetString("name") ?? "?";
        string b = constraint.GetPointer("entities[1]")?.GetString("name") ?? "?";
        var node = new HkxNode(HkxNodeKind.Constraint, constraint, $"{constraint.GetString("name")}: {Short(data)} ({a} -> {b})");
        if (data is not null)
        {
            node.Children.Add(new HkxNode(HkxNodeKind.Constraint, data, Short(data)));
        }
        return node;
    }

    // A shape's node, `link` being the member it hangs from; a translate or transform shape passes its own on to the shape it wraps.
    private static HkxNode Shape(HkxObject shape, Matrix4x4 toModel, HkxLink link)
    {
        if (Unwrap(shape) is var (wrapped, local, label))
        {
            var wrapper = new HkxNode(HkxNodeKind.Shape, shape, label) { Link = link };
            if (wrapped is not null)
            {
                wrapper.Children.Add(Shape(wrapped, local * toModel, shape.Class.Name == "hkpMoppBvTreeShape" ? new HkxLink(shape, "child.childShape") : link));
            }
            return wrapper;
        }

        switch (shape.Class.Name)
        {
            case "hkpListShape":
                int count = shape.Count("childInfo");
                var list = new HkxNode(HkxNodeKind.Shape, shape, $"List ({count})") { Link = link };
                for (int i = 0; i < count; i++)
                {
                    if (shape.GetPointer($"childInfo[{i}].shape") is { } child)
                    {
                        list.Children.Add(Shape(child, toModel, new HkxLink(shape, $"childInfo[{i}].shape")));
                    }
                }
                return list;
            case "hkpStorageExtendedMeshShape":
                return Mesh(shape, toModel, link);
            default:
                return Leaf(shape, toModel) is { } primitive
                    ? new HkxNode(HkxNodeKind.Shape, shape, Label(primitive)) { Primitive = primitive, Link = link }
                    : new HkxNode(HkxNodeKind.Shape, shape, $"{Short(shape)} (not drawn)") { Link = link };
        }
    }

    private static HkxNode Mesh(HkxObject mesh, Matrix4x4 toModel, HkxLink link)
    {
        var node = new HkxNode(HkxNodeKind.Shape, mesh, "Extended mesh") { Link = link };
        List<HkxObject?> storage = mesh.GetPointers("meshstorage");
        for (int i = 0; i < storage.Count; i++)
        {
            (Vector3[] vertices, int[] triangles) = Triangles(mesh, i);
            node.Children.Add(new HkxNode(HkxNodeKind.Shape, storage[i], $"Triangles ({triangles.Length / 3})")
            {
                Primitive = new HkxPrimitive(storage[i]!, HkxShapeKind.Mesh, toModel, vertices, triangles),
            });
        }

        int groups = mesh.Count("shapesSubparts");
        for (int k = 0; k < groups; k++)
        {
            Matrix4x4 local = Placement(mesh, k);
            List<HkxObject?> shapes = mesh.GetPointers($"shapesSubparts[{k}].childShapes");
            var group = new HkxNode(HkxNodeKind.Shape, mesh, $"Shapes ({shapes.Count})");
            for (int j = 0; j < shapes.Count; j++)
            {
                if (shapes[j] is { } child)
                {
                    group.Children.Add(Shape(child, local * toModel, new HkxLink(mesh, $"shapesSubparts[{k}].childShapes[{j}]")));
                }
            }
            node.Children.Add(group);
        }
        return node;
    }

    /// <summary>A box centred on the origin as corners and three indices into them per triangle.</summary>
    public static (Vector3[] Vertices, int[] Triangles) BoxMesh(Vector3 half)
        => (Corners(half), Fan([[4, 5, 7, 6], [0, 2, 3, 1], [1, 3, 7, 5], [0, 4, 6, 2], [2, 6, 7, 3], [0, 1, 5, 4]]));

    private static HkxPrimitive Box(HkxObject shape, Vector3 half, Matrix4x4 toModel)
    {
        (Vector3[] corners, int[] triangles) = BoxMesh(half);
        return new HkxPrimitive(shape, HkxShapeKind.Box, toModel, corners, triangles);
    }

    // A sphere split at its equator, each half centred on one end of the A-B segment.
    private static HkxPrimitive Rounded(HkxObject shape, HkxShapeKind kind, Vector3 a, Vector3 b, float radius, Matrix4x4 toModel)
    {
        Vector3 axis = a == b ? Vector3.UnitZ : Vector3.Normalize(b - a);
        var vertices = new List<Vector3>();
        for (int ring = 0; ring <= SphereRings; ring++)
        {
            float polar = MathF.PI * ring / SphereRings;
            for (int segment = 0; segment < SphereSegments; segment++)
            {
                float around = 2 * MathF.PI * segment / SphereSegments;
                var unit = new Vector3(MathF.Sin(polar) * MathF.Cos(around), MathF.Sin(polar) * MathF.Sin(around), MathF.Cos(polar));
                vertices.Add((Vector3.Dot(unit, axis) >= 0 ? b : a) + unit * radius);
            }
        }
        var triangles = new List<int>();
        for (int ring = 0; ring < SphereRings; ring++)
        {
            for (int segment = 0; segment < SphereSegments; segment++)
            {
                int i = ring * SphereSegments + segment, j = ring * SphereSegments + (segment + 1) % SphereSegments;
                triangles.AddRange([i, i + SphereSegments, j, j, i + SphereSegments, j + SphereSegments]);
            }
        }
        return new HkxPrimitive(shape, kind, toModel, [.. vertices], [.. triangles]);
    }

    // Each vertex moves out to where its faces, pushed out by the radius, meet.
    private static HkxPrimitive Hull(HkxObject shape, float radius, Matrix4x4 toModel)
    {
        (Vector3[] vertices, int[][] faces) = HullFaces(shape);
        Vector3[] normals = [.. Planes(vertices, faces).Select(p => new Vector3(p.X, p.Y, p.Z))];
        Vector3[] grown = [.. vertices.Select((v, i) => v + Push(normals.Where((n, f) => float.IsFinite(n.X) && faces[f].Contains(i)), radius))];
        return new HkxPrimitive(shape, HkxShapeKind.Hull, toModel, grown, Fan(faces));
    }

    private static (Vector3[] Vertices, int[][] Faces) HullFaces(HkxObject hull)
    {
        Vector3[] vertices = HullVertices(hull);
        HkxObject connectivity = hull.GetPointer("connectivity")!;
        int[] indices = connectivity.GetIntArray("vertexIndices");
        int start = 0;
        int[][] faces = [.. connectivity.GetIntArray("numVerticesPerFace").Select(n =>
        {
            int[] face = indices[start..(start + n)];
            start += n;
            return face;
        })];
        return (vertices, faces);
    }

    // Each face's Newell normal, turned to face away from the hull's centre.
    private static Vector4[] Planes(Vector3[] vertices, int[][] faces)
    {
        Vector3 centre = vertices.Aggregate(Vector3.Zero, (a, v) => a + v) / vertices.Length;
        return [.. faces.Select(face =>
        {
            Vector3 normal = default, mean = default;
            for (int i = 0; i < face.Length; i++)
            {
                Vector3 p = vertices[face[i]], q = vertices[face[(i + 1) % face.Length]];
                normal += new Vector3((p.Y - q.Y) * (p.Z + q.Z), (p.Z - q.Z) * (p.X + q.X), (p.X - q.X) * (p.Y + q.Y));
                mean += p;
            }
            mean /= face.Length;
            normal = Vector3.Normalize(normal);
            if (Vector3.Dot(normal, mean - centre) < 0)
            {
                normal = -normal;
            }
            return new Vector4(normal, -Vector3.Dot(normal, mean));
        })];
    }

    // The offset whose distance along every face normal is the radius: least squares over the vertex's faces.
    private static Vector3 Push(IEnumerable<Vector3> normals, float radius)
    {
        Matrix4x4 a = default;
        Vector3 sum = default;
        foreach (Vector3 n in normals)
        {
            a.M11 += n.X * n.X; a.M12 += n.X * n.Y; a.M13 += n.X * n.Z;
            a.M22 += n.Y * n.Y; a.M23 += n.Y * n.Z; a.M33 += n.Z * n.Z;
            sum += n;
        }
        (a.M21, a.M31, a.M32, a.M44) = (a.M12, a.M13, a.M23, 1);
        // A vertex on no face with a surface, as some retail hulls list, is not moved.
        return Matrix4x4.Invert(a, out Matrix4x4 inverse) && MathF.Abs(a.GetDeterminant()) > 1e-6f
            ? Vector3.TransformNormal(sum * radius, inverse)
            : sum.LengthSquared() > 1e-12f ? Vector3.Normalize(sum) * radius : Vector3.Zero;
    }

    // A face of fewer than three vertices, which some retail hulls list, has no surface to draw.
    private static int[] Fan(int[][] faces)
        => [.. faces.Where(f => f.Length >= 3).SelectMany(f => Enumerable.Range(1, f.Length - 2).SelectMany(i => new[] { f[0], f[i], f[i + 1] }))];

    /// <summary>
    /// The sixteen floats of an hkTransform: its three rotation columns and translation, each stored as a vector,
    /// which are the rows of a System.Numerics matrix (one that transforms row vectors).
    /// </summary>
    public static float[] TransformReals(Matrix4x4 m)
        => [m.M11, m.M12, m.M13, 0, m.M21, m.M22, m.M23, 0, m.M31, m.M32, m.M33, 0, m.M41, m.M42, m.M43, 0];

    private static Matrix4x4 Transform(float[] f) => new(
        f[0], f[1], f[2], 0, f[4], f[5], f[6], 0, f[8], f[9], f[10], 0, f[12], f[13], f[14], 1);

    private static bool Derives(HkClass c, string name) => c.Name == name || (c.Parent is { } p && Derives(p, name));

    private static Quaternion Quat(float[] f) => new(f[0], f[1], f[2], f[3]);

    private static string Short(HkxObject? o) => o?.Class.Name.Replace("hkp", "") ?? "?";

    private static string Label(HkxPrimitive p)
    {
        return p.Kind switch
        {
            HkxShapeKind.Box when p.Vertices[7] - p.Vertices[0] is var size => "Box " + string.Join(" x ", new[] { size.X, size.Y, size.Z }.Select(x => x.ToString("0.###", CultureInfo.InvariantCulture))),
            HkxShapeKind.Hull => $"Hull ({p.Vertices.Length} vertices)",
            _ => p.Kind.ToString(),
        };
    }
}
