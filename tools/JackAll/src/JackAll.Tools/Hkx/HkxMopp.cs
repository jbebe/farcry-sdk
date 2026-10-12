using System.Numerics;
using JackAll.Tools.Xbg;

namespace JackAll.Tools.Hkx;

/// <summary>A shape collection's child by its shape key, and the box around its collision in the collection's space.</summary>
public readonly record struct HkxMoppItem(uint Key, Vector3 Min, Vector3 Max);

/// <summary>
/// How MOPP code measures up to its children: the keys it never reaches, the keys it reaches that are no child's or
/// that it reaches twice, and each child it leaves out of a probe box touching the child's bounds.
/// </summary>
public sealed record HkxMoppCheck(List<uint> Unreached, List<uint> Stray, List<(HkxMoppItem Item, Vector3 Min, Vector3 Max)> Lost)
{
    public bool Holds => Unreached.Count == 0 && Stray.Count == 0 && Lost.Count == 0;
}

/// <summary>
/// Havok's MOPP code: a byte-coded tree that culls a shape collection's children by region, on a grid of 2^24 steps
/// per axis that <c>info.offset</c> lays over the collection (its corner in xyz, steps per unit in w).
/// <see cref="Query"/> runs it as the game's box query does; <see cref="Build"/> writes code that keeps every child
/// a query box touches.
/// </summary>
public static class HkxMopp
{
    // The step the longest side of the collection's bounds reaches, as Havok lays the grid.
    private const float GridTop = 254 << 16;

    // Steps each child's bounds grow by, to cover the float rounding of the game's queries.
    private const int Slack = 64;

    private const float DegenerateArea = 1e-7f;
    private const uint ShapesSubpartKey = 0x80000000;
    private const byte WithoutChunks = 1;

    // The diagonal splits 0x13-0x1C by the sign each axis adds with. A split's bytes scale by the number of axes
    // summed, less 255 for each one subtracted.
    private static readonly int[][] Diagonals =
        [[0, 1, 1], [0, 1, -1], [1, 0, 1], [1, 0, -1], [1, 1, 0], [1, -1, 0], [1, 1, 1], [1, 1, -1], [1, -1, 1], [1, -1, -1]];

    /// <summary>The keys of the children a query box may touch, as the game's box query finds them.</summary>
    public static List<uint> Query(Vector4 info, byte[] code, Vector3 min, Vector3 max)
        => new Machine(code, Axes(a => Step(min[a], info, a) - 1), Axes(a => Step(max[a], info, a) + 1)).Keys;

    /// <summary>Every key the code can reach.</summary>
    public static List<uint> Keys(byte[] code) => new Machine(code, Axes(_ => -(1 << 30)), Axes(_ => 1 << 30)).Keys;

    /// <summary>A MOPP shape's grid info and code.</summary>
    public static (Vector4 Info, byte[] Code) Code(HkxObject mopp)
    {
        HkxObject code = mopp.GetPointer("code")!;
        float[] info = code.GetReals("info.offset");
        return (new Vector4(info[0], info[1], info[2], info[3]), code.GetBytes("data"));
    }

    /// <summary>Rewrites a MOPP shape's code from where its collection's children are now.</summary>
    public static void Rebuild(HkxObject mopp)
    {
        (Vector4 info, byte[] data) = Build(Items(mopp));
        HkxObject code = mopp.GetPointer("code")!;
        code.SetReals("info.offset", info.X, info.Y, info.Z, info.W);
        code.SetBytes("data", data);
        code.SetByte("buildType", WithoutChunks);
    }

    /// <summary>The children of the list or extended mesh a MOPP shape wraps, each by the key the collection gives it.</summary>
    public static List<HkxMoppItem> Items(HkxObject mopp)
    {
        HkxObject collection = mopp.GetPointer("child.childShape")!;
        return collection.Class.Name switch
        {
            "hkpListShape" => [.. Enumerable.Range(0, collection.Count("childInfo"))
                .Select(i => Item((uint)i, HkxShapes.Bounds(collection.GetPointer($"childInfo[{i}].shape")!, Matrix4x4.Identity)))],
            "hkpStorageExtendedMeshShape" => Mesh(collection),
            _ => throw new NotSupportedException($"A MOPP over {collection.Class.Name} is not one this builds."),
        };
    }

    /// <summary>Code that keeps every item a query box touches, and the info laying its grid over them.</summary>
    public static (Vector4 Info, byte[] Code) Build(IReadOnlyList<HkxMoppItem> items)
    {
        if (items.Count == 0)
        {
            return (new Vector4(0, 0, 0, 1), [0x00]);
        }
        (Vector3 min, Vector3 max) = Bounds(items);
        Vector3 size = max - min;
        float scale = GridTop / MathF.Max(MathF.Max(size.X, size.Y), MathF.Max(size.Z, 1e-4f));
        // The corner sits two slacks below the items, so every grown bound stays on the grid.
        Vector3 corner = min - new Vector3(2 * Slack / scale);
        List<Cell> cells = [.. items.Select(i => new Cell(i.Key,
            Axes(a => (int)Math.Floor(((double)i.Min[a] - corner[a]) * scale) - Slack),
            Axes(a => (int)Math.Ceiling(((double)i.Max[a] - corner[a]) * scale) + Slack)))];
        return (new Vector4(corner, scale), [.. Node(cells, 0, [0, 0, 0])]);
    }

    /// <summary>
    /// Measures code against its items, by every key it reaches and by probe boxes: random ones over the items, and
    /// thin ones touching each face of the bounds of up to <paramref name="probes"/> items.
    /// </summary>
    public static HkxMoppCheck Check(IReadOnlyList<HkxMoppItem> items, Vector4 info, byte[] code, int probes)
    {
        List<uint> keys = [.. items.Select(i => i.Key)], reached = Keys(code);
        List<(HkxMoppItem, Vector3, Vector3)> lost = [.. Probes(items, probes).SelectMany(box =>
        {
            HashSet<uint> kept = [.. Query(info, code, box.Min, box.Max)];
            return items.Where(i => Touches(i, box.Min, box.Max) && !kept.Contains(i.Key)).Select(i => (i, box.Min, box.Max));
        })];
        return new HkxMoppCheck([.. keys.Except(reached)],
            [.. reached.Except(keys), .. reached.GroupBy(k => k).Where(g => g.Count() > 1).Select(g => g.Key)], lost);
    }

    private static List<HkxMoppItem> Mesh(HkxObject mesh)
    {
        int subpartShift = 32 - mesh.GetInt("numBitsForSubpartIndex");
        float radius = mesh.GetReals("triangleRadius")[0];
        var items = new List<HkxMoppItem>();
        for (int s = 0; s < mesh.Count("trianglesSubparts"); s++)
        {
            (Vector3[] v, int[] t) = HkxShapes.Triangles(mesh, s);
            Vector3 extrusion = HkxShapes.Vec(mesh.GetReals($"trianglesSubparts[{s}].extrusion"));
            for (int k = 0; k < t.Length / 3; k++)
            {
                Vector3 a = v[t[k * 3]], b = v[t[k * 3 + 1]], c = v[t[k * 3 + 2]];
                if (!IsDegenerate(a, b, c))
                {
                    items.Add(Item((uint)s << subpartShift | (uint)k, HkxShapes.Grown([a, b, c, a + extrusion, b + extrusion, c + extrusion], radius)));
                }
            }
        }
        for (int s = 0; s < mesh.Count("shapesSubparts"); s++)
        {
            Matrix4x4 placement = HkxShapes.Placement(mesh, s);
            List<HkxObject?> children = mesh.GetPointers($"shapesSubparts[{s}].childShapes");
            for (int k = 0; k < children.Count; k++)
            {
                items.Add(Item(ShapesSubpartKey | (uint)s << subpartShift | (uint)k, HkxShapes.Bounds(children[k]!, placement)));
            }
        }
        return items;
    }

    private static HkxMoppItem Item(uint key, (Vector3 Min, Vector3 Max) bounds) => new(key, bounds.Min, bounds.Max);

    private static (Vector3 Min, Vector3 Max) Bounds(IReadOnlyList<HkxMoppItem> items) => XbgModel.Bounds(items.SelectMany(i => new[] { i.Min, i.Max }));

    // A triangle the mesh leaves out when it lists its keys, by Havok's own test.
    private static bool IsDegenerate(Vector3 a, Vector3 b, Vector3 c)
    {
        Vector3 u = a - b, v = c - b;
        float dot = Vector3.Dot(u, v);
        return Vector3.Cross(a - b, a - c).LengthSquared() < DegenerateArea || Vector3.Cross(b - a, b - c).LengthSquared() < DegenerateArea
            || u.LengthSquared() * v.LengthSquared() - dot * dot == 0;
    }

    // Random boxes over the items, and thin ones just touching each face of the bounds of up to `count` items.
    private static IEnumerable<(Vector3 Min, Vector3 Max)> Probes(IReadOnlyList<HkxMoppItem> items, int count)
    {
        (Vector3 min, Vector3 max) = Bounds(items);
        Vector3 size = max - min, thin = size * 0.01f;
        var random = new Random(1);
        Vector3 Next() => new(random.NextSingle(), random.NextSingle(), random.NextSingle());

        for (int i = 0; i < count; i++)
        {
            Vector3 centre = min - size * 0.25f + Next() * size * 1.5f, half = Next() * size * 0.2f;
            yield return (centre - half, centre + half);
        }
        foreach (HkxMoppItem item in items.Count <= count ? items : items.OrderBy(_ => random.Next()).Take(count))
        {
            Vector3 centre = (item.Min + item.Max) / 2;
            (Vector3, Vector3) Face(int axis, float from, float to)
            {
                (Vector3 low, Vector3 high) = (centre - thin, centre + thin);
                (low[axis], high[axis]) = (from, to);
                return (low, high);
            }
            for (int axis = 0; axis < 3; axis++)
            {
                yield return Face(axis, item.Min[axis] - thin[axis], item.Min[axis]);
                yield return Face(axis, item.Max[axis], item.Max[axis] + thin[axis]);
            }
        }
    }

    private static bool Touches(HkxMoppItem i, Vector3 min, Vector3 max)
        => i.Min.X <= max.X && i.Min.Y <= max.Y && i.Min.Z <= max.Z && min.X <= i.Max.X && min.Y <= i.Max.Y && min.Z <= i.Max.Z;

    // Code for `cells` on the byte grid `shift` halvings below the top one, whose origin in its own steps is `origin`.
    private static List<byte> Node(List<Cell> cells, int shift, int[] origin)
    {
        int[] lo = Axes(a => cells.Min(c => On(c.Lo[a], shift, origin[a])));
        int[] hi = Axes(a => cells.Max(c => On(c.Hi[a], shift, origin[a])));
        int zoom = Enumerable.Range(1, Math.Min(4, 16 - shift)).LastOrDefault(z => Enumerable.Range(0, 3).All(a => (hi[a] - lo[a] + 1) << z <= 255));
        if (zoom > 0)
        {
            return [(byte)zoom, (byte)lo[0], (byte)lo[1], (byte)lo[2], .. Node(cells, shift + zoom, Axes(a => (origin[a] + lo[a]) << zoom))];
        }
        if (cells.Count == 1)
        {
            return [.. Enumerable.Range(0, 3).SelectMany(a => new[] { (byte)(0x26 + a), (byte)lo[a], (byte)(hi[a] + 1) }), .. Terminal(cells[0].Key)];
        }

        int axis = Enumerable.Range(0, 3).MaxBy(a => hi[a] - lo[a]);
        List<Cell> sorted = [.. cells.OrderBy(c => (long)c.Lo[axis] + c.Hi[axis])];
        List<Cell> left = sorted[..(sorted.Count / 2)], right = sorted[(sorted.Count / 2)..];
        byte below = (byte)(left.Max(c => On(c.Hi[axis], shift, origin[axis])) + 1);
        byte above = (byte)right.Min(c => On(c.Lo[axis], shift, origin[axis]));
        List<byte> l = Node(left, shift, origin), r = Node(right, shift, origin);
        return l.Count switch
        {
            <= 0xFF => [(byte)(0x10 + axis), below, above, (byte)l.Count, .. l, .. r],
            <= 0xFFFF => [(byte)(0x23 + axis), below, above, 0, 0, .. BigEndian((uint)l.Count, 2), .. l, .. r],
            // Past a 16-bit jump the right side is reached through a 24-bit one, placed before the left.
            _ => [(byte)(0x23 + axis), below, above, 0, 4, 0, 0, 0x07, .. BigEndian((uint)l.Count, 3), .. l, .. r],
        };
    }

    private static byte[] Terminal(uint key)
    {
        int width = key <= 0xFF ? 1 : key <= 0xFFFF ? 2 : key <= 0xFFFFFF ? 3 : 4;
        return key < 0x20 ? [(byte)(0x30 + key)] : [(byte)(0x4F + width), .. BigEndian(key, width)];
    }

    private static byte[] BigEndian(uint value, int count) => [.. Enumerable.Range(0, count).Select(i => (byte)(value >> (8 * (count - 1 - i))))];

    // A top-grid step on the byte grid `shift` halvings down, relative to that grid's origin.
    private static int On(int step, int shift, int origin) => (step >> (16 - shift)) - origin;

    // A coordinate's step on the top grid, rounded in single precision as the game rounds it.
    private static int Step(float x, Vector4 info, int axis) => (int)Math.Clamp(MathF.Round((x - info[axis]) * info.W), -(1 << 30), 1 << 30);

    private static int[] Axes(Func<int, int> f) => [f(0), f(1), f(2)];

    // A child's bounds in top-grid steps.
    private sealed record Cell(uint Key, int[] Lo, int[] Hi);

    // A query's box on the byte grid `Shift` halvings below the top one, that grid's origin, and what terminals add to their key.
    private sealed record Frame(int[] Min, int[] Max, int[] Origin, int Shift, uint KeyBase);

    // The game's box query: walks the code, following each side of a split the box reaches and collecting terminals.
    private sealed class Machine
    {
        private readonly byte[] _code;
        private readonly int[] _low, _high;

        public Machine(byte[] code, int[] low, int[] high)
        {
            (_code, _low, _high) = (code, low, high);
            Run(0, At(0, [0, 0, 0], 0));
        }

        public List<uint> Keys { get; } = [];

        private void Run(int pc, Frame f)
        {
            while (pc >= 0 && pc < _code.Length)
            {
                byte op = _code[pc];
                switch (op)
                {
                    case 0x00:
                        return;
                    case <= 0x04:
                        f = At(f.Shift + op, Axes(a => (_code[pc + 1 + a] + f.Origin[a]) << op), f.KeyBase);
                        pc += 4;
                        break;
                    case 0x05:
                        pc += 2 + _code[pc + 1];
                        break;
                    case 0x06:
                        pc += 3 + (int)Read(pc + 1, 2);
                        break;
                    case 0x07:
                        pc += 4 + (int)Read(pc + 1, 3);
                        break;
                    case 0x09:
                        f = f with { KeyBase = f.KeyBase + _code[pc + 1] };
                        pc += 2;
                        break;
                    case 0x0A:
                        f = f with { KeyBase = f.KeyBase + Read(pc + 1, 2) };
                        pc += 3;
                        break;
                    case 0x0B:
                        f = f with { KeyBase = Read(pc + 1, 4) };
                        pc += 5;
                        break;
                    case 0x0D:
                        pc += 5;
                        break;
                    case >= 0x10 and <= 0x12:
                        pc = Split(f, op - 0x10, _code[pc + 1], _code[pc + 2], pc + 4, pc + 4 + _code[pc + 3]);
                        break;
                    case >= 0x13 and <= 0x1C:
                        int[] signs = Diagonals[op - 0x13];
                        int scale = signs.Count(s => s != 0), bias = -255 * signs.Count(s => s < 0);
                        pc = Fork(f, Sum(signs, f.Min, f.Max) < _code[pc + 1] * scale + bias, pc + 4,
                            _code[pc + 2] * scale + bias < Sum(signs, f.Max, f.Min), pc + 4 + _code[pc + 3]);
                        break;
                    case >= 0x20 and <= 0x22:
                        pc = Split(f, op - 0x20, _code[pc + 1] + 1, _code[pc + 1], pc + 3, pc + 3 + _code[pc + 2]);
                        break;
                    case >= 0x23 and <= 0x25:
                        pc = Split(f, op - 0x23, _code[pc + 1], _code[pc + 2], pc + 7 + (int)Read(pc + 3, 2), pc + 7 + (int)Read(pc + 5, 2));
                        break;
                    case >= 0x26 and <= 0x28:
                        pc = f.Max[op - 0x26] < _code[pc + 1] || _code[pc + 2] <= f.Min[op - 0x26] ? -1 : pc + 3;
                        break;
                    case >= 0x29 and <= 0x2B:
                        pc = _high[op - 0x29] < Read(pc + 1, 3) || Read(pc + 4, 3) < _low[op - 0x29] ? -1 : pc + 7;
                        break;
                    case >= 0x30 and <= 0x4F:
                        Keys.Add(f.KeyBase + op - 0x30u);
                        return;
                    case >= 0x50 and <= 0x53:
                        Keys.Add(f.KeyBase + Read(pc + 1, op - 0x4F));
                        return;
                    case >= 0x60 and <= 0x63:
                        pc += 2;
                        break;
                    case >= 0x64 and <= 0x67:
                        pc += 3;
                        break;
                    case >= 0x68 and <= 0x6B:
                        pc += 5;
                        break;
                    default:
                        throw new InvalidDataException($"MOPP opcode 0x{op:X2} at {pc} is not one this reads.");
                }
            }
        }

        // The query's box on the byte grid `shift` halvings below the top one, relative to that grid's origin.
        private Frame At(int shift, int[] origin, uint keyBase)
            => new(Axes(a => (_low[a] >> (16 - shift)) - origin[a]), Axes(a => (_high[a] >> (16 - shift)) - origin[a] + 1), origin, shift, keyBase);

        // A split on one axis: the left side if the box's min is below `below`, the right if its max is above `above`.
        private int Split(Frame f, int axis, int below, int above, int leftAt, int rightAt)
            => Fork(f, f.Min[axis] < below, leftAt, above < f.Max[axis], rightAt);

        // Follows each side of a split the box reaches, the left by recursion when both are; the next pc, or -1 for neither.
        private int Fork(Frame f, bool left, int leftAt, bool right, int rightAt)
        {
            if (left && right)
            {
                Run(leftAt, f);
            }
            return right ? rightAt : left ? leftAt : -1;
        }

        // A diagonal's sum over the box: each axis's `same` bound where it adds, minus its `other` bound where it subtracts.
        private static int Sum(int[] signs, int[] same, int[] other)
            => Enumerable.Range(0, 3).Sum(a => signs[a] > 0 ? same[a] : signs[a] < 0 ? -other[a] : 0);

        private uint Read(int at, int count)
        {
            uint value = 0;
            for (int i = 0; i < count; i++)
            {
                value = value << 8 | _code[at + i];
            }
            return value;
        }
    }
}
