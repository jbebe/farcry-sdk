using System.Numerics;

namespace JackAll.Tools.World;

/// <summary>
/// Cover points, as <c>CCoverGenerator::GenerateCovers</c> lays them: along the navmesh's open edges,
/// wherever an obstacle stands behind the edge at least <see cref="MinCoverHeight"/> tall.
/// </summary>
public static partial class NavMeshGenerator
{
    private const float MinCoverHeight = 0.75f;
    private const int CoverRows = 20;
    private const float CoverRowHeight = 0.1f, CoverColumnWidth = 0.1f;

    /// <summary>How far a probe starts on the walkable side of the edge, and how far into the obstacle it runs.</summary>
    private const float ProbeOut = 0.3f, ProbeIn = 0.6f;

    /// <summary>The stand point's distance from the edge, and the agent that must fit there.</summary>
    private const float StandOff = 0.9f, StandRadius = 0.8f;

    /// <summary>How far above or below the cover the floor under its stand point may be.</summary>
    private const float StandReach = 1.5f;

    /// <summary>How far a boundary may stray from the straight cover laid along it: about a Recast cell.</summary>
    private const float CoverStraightness = 0.25f;

    /// <summary>How far in from each end the probes start, where a voxel corner can let one slip past the obstacle.</summary>
    private const float CoverInset = 0.15f;

    private const byte StartExposed = 0x20, EndExposed = 0x10;

    /// <summary>
    /// Each sector's covers and their vertices. The open edges of a sector are chained into outlines,
    /// walkable side on the left, straightened, and cut into covers: up to 2.6 m whole, up to 3.9 m in
    /// halves, longer as 1.3 m at each end and the rest between.
    /// </summary>
    private static Dictionary<int, (List<NvmCover> Covers, List<Vector3> Vertices)> Covers(
        Mesh mesh, int[] neighbours, ushort[] graphIds, BoundaryIndex boundary, IEnumerable<NavMeshSolid> solids)
    {
        var bySector = mesh.Triangles.Select((t, i) => (t.Sector, i)).GroupBy(p => p.Sector)
            .ToDictionary(g => g.Key, g => g.Select(p => p.i).ToList());
        var obstacles = new PlanBuckets<(Vector3 A, Vector3 B, Vector3 C)>(2);
        foreach (var face in solids.SelectMany(s => s.Faces()))
        {
            obstacles.Add(Vector3.Min(face.A, Vector3.Min(face.B, face.C)), Vector3.Max(face.A, Vector3.Max(face.B, face.C)), face);
        }
        var locator = new PlanBuckets<int>(4);
        for (int t = 0; t < mesh.Triangles.Count; t++)
        {
            (Vector3 min, Vector3 max) = mesh.Bounds(t);
            locator.Add(min, max, t);
        }
        var result = new Dictionary<int, (List<NvmCover>, List<Vector3>)>();
        foreach ((int sector, List<int> triangles) in bySector)
        {
            var covers = new List<NvmCover>();
            var vertices = new List<Vector3>();
            foreach ((List<Vector3> outline, bool closed) in Outlines(mesh, neighbours, triangles))
            {
                int count = outline.Count;
                int segments = closed ? count : count - 1;
                for (int i = 0; i < segments; i++)
                {
                    Vector3 a = outline[i], b = outline[(i + 1) % count];
                    bool hasBefore = closed || i > 0, hasAfter = closed || i + 2 < count;
                    byte start = !hasBefore || Exposed(outline[(i - 1 + count) % count], a, b) ? StartExposed : (byte)0;
                    byte end = !hasAfter || Exposed(a, b, outline[(i + 2) % count]) ? EndExposed : (byte)0;
                    List<(Vector3 From, Vector3 To, byte Type)> pieces = Split(a, b, start, end);
                    foreach ((Vector3 from, Vector3 to, byte type) in pieces)
                    {
                        if (Cover(from, to, obstacles) is not { } height
                            || Stand(from, to, mesh, locator, boundary) is not int node)
                        {
                            continue;
                        }
                        if (vertices.Count == 0 || vertices[^1] != from)
                        {
                            vertices.Add(from);
                        }
                        covers.Add(new NvmCover((byte)MathF.Min(MathF.Round(height * 125), 250), graphIds[node], type,
                            (ushort)(vertices.Count - 1)));
                        vertices.Add(to);
                    }
                }
            }
            if (vertices.Count > ushort.MaxValue)
            {
                throw new InvalidOperationException($"Sector {sector} needs {vertices.Count} cover vertices; a cover holds 65535.");
            }
            result[sector] = (covers, vertices);
        }
        return result;
    }

    /// <summary>
    /// Whether a cover's end at <paramref name="corner"/> is exposed, so an agent can lean out past it: the
    /// outline turns away from the walkable side there by more than 45°.
    /// </summary>
    private static bool Exposed(Vector3 before, Vector3 corner, Vector3 after)
    {
        Vector2 incoming = Vector2.Normalize(new(corner.X - before.X, corner.Y - before.Y));
        Vector2 outgoing = Vector2.Normalize(new(after.X - corner.X, after.Y - corner.Y));
        return incoming.X * outgoing.Y - incoming.Y * outgoing.X < 0 && Vector2.Dot(incoming, outgoing) < MathF.Sqrt(0.5f);
    }

    /// <summary><c>GenerateCover_Split</c>: the pieces of one outline segment, its corner flags on its end pieces.</summary>
    private static List<(Vector3, Vector3, byte)> Split(Vector3 a, Vector3 b, byte start, byte end)
    {
        float length = Vector2.Distance(new(a.X, a.Y), new(b.X, b.Y));
        if (length <= 2.6f)
        {
            return [(a, b, (byte)(start | end))];
        }
        if (length <= 3.9f)
        {
            Vector3 middle = (a + b) / 2;
            return [(a, middle, start), (middle, b, end)];
        }
        Vector3 first = Vector3.Lerp(a, b, 1.3f / length), last = Vector3.Lerp(a, b, 1 - 1.3f / length);
        return [(a, first, start), (first, last, 0), (last, b, end)];
    }

    /// <summary>
    /// <c>GenerateCover</c>: a grid of level probes behind the edge, rows <see cref="CoverRowHeight"/> apart;
    /// the cover is as tall as the first row the obstacle leaves a gap in, or null under
    /// <see cref="MinCoverHeight"/>.
    /// </summary>
    private static float? Cover(Vector3 a, Vector3 b, PlanBuckets<(Vector3 A, Vector3 B, Vector3 C)> obstacles)
    {
        var along = new Vector2(b.X - a.X, b.Y - a.Y);
        float length = along.Length();
        if (length < 1e-3f)
        {
            return null;
        }
        Vector2 left = new Vector2(-along.Y, along.X) / length;
        var outward = new Vector3(left, 0);
        int columns = Math.Max(1, (int)MathF.Round(length / CoverColumnWidth));
        float inset = MathF.Min(CoverInset, length / 4);
        Vector3 low = Vector3.Min(a, b) - new Vector3(ProbeIn), high = Vector3.Max(a, b) + new Vector3(ProbeIn, ProbeIn, CoverRows * CoverRowHeight);
        List<(Vector3, Vector3, Vector3)> near = [.. obstacles.In(low, high).Distinct()
            .Where(f => MathF.Max(f.A.Z, MathF.Max(f.B.Z, f.C.Z)) >= low.Z && MathF.Min(f.A.Z, MathF.Min(f.B.Z, f.C.Z)) <= high.Z)];
        if (near.Count == 0)
        {
            return null;
        }
        for (int row = 0; row < CoverRows; row++)
        {
            for (int column = 0; column < columns; column++)
            {
                float t = (inset + (length - 2 * inset) * (column + 0.5f) / columns) / length;
                Vector3 at = Vector3.Lerp(a, b, t) + new Vector3(0, 0, CoverRowHeight * (row + 0.5f));
                if (!near.Any(face => Crosses(at + outward * ProbeOut, at - outward * ProbeIn, face)))
                {
                    float height = CoverRowHeight * row;
                    return height < MinCoverHeight ? null : height;
                }
            }
        }
        return CoverRows * CoverRowHeight;
    }

    /// <summary><c>IsCoverLocationValid</c>: the triangle an agent stands on to take the cover, when one of
    /// <see cref="StandRadius"/> fits there.</summary>
    private static int? Stand(Vector3 a, Vector3 b, Mesh mesh, PlanBuckets<int> locator, BoundaryIndex boundary)
    {
        var along = new Vector2(b.X - a.X, b.Y - a.Y);
        Vector2 left = new Vector2(-along.Y, along.X) / along.Length();
        Vector3 stand = (a + b) / 2 + new Vector3(left * StandOff, 0);

        // The triangle under the stand point nearest its height.
        (int Triangle, float Z)? under = null;
        foreach (int t in locator.In(stand, stand))
        {
            if (Height(mesh.Vertices[mesh.Corner(t, 0)], mesh.Vertices[mesh.Corner(t, 1)], mesh.Vertices[mesh.Corner(t, 2)], stand) is float z
                && MathF.Abs(z - stand.Z) < StandReach && (under is not var (_, best) || MathF.Abs(z - stand.Z) < MathF.Abs(best - stand.Z)))
            {
                under = (t, z);
            }
        }
        return under is var (triangle, height) && boundary.DistanceTo(stand with { Z = height }) >= StandRadius - CoverStraightness
            ? triangle
            : null;
    }

    /// <summary>Whether the segment from <paramref name="p"/> to <paramref name="q"/> passes through the triangle.</summary>
    private static bool Crosses(Vector3 p, Vector3 q, (Vector3 A, Vector3 B, Vector3 C) t)
    {
        Vector3 d = q - p, e1 = t.B - t.A, e2 = t.C - t.A;
        Vector3 h = Vector3.Cross(d, e2);
        float det = Vector3.Dot(e1, h);
        if (MathF.Abs(det) < 1e-9f)
        {
            return false;
        }
        Vector3 s = p - t.A;
        float u = Vector3.Dot(s, h) / det;
        if (u < 0 || u > 1)
        {
            return false;
        }
        Vector3 qv = Vector3.Cross(s, e1);
        float v = Vector3.Dot(d, qv) / det;
        if (v < 0 || u + v > 1)
        {
            return false;
        }
        float along = Vector3.Dot(e2, qv) / det;
        return along >= 0 && along <= 1;
    }

    /// <summary>
    /// A sector's open edges chained head to tail into outlines, each straightened to within
    /// <see cref="CoverStraightness"/>. An outline the sector's border cuts is open; one that comes
    /// round to its start is closed.
    /// </summary>
    private static List<(List<Vector3>, bool)> Outlines(Mesh mesh, int[] neighbours, List<int> triangles)
    {
        var outgoing = new Dictionary<int, List<int>>();
        var incoming = new HashSet<int>();
        foreach (int t in triangles)
        {
            for (int k = 0; k < 3; k++)
            {
                if (neighbours[t * 3 + k] < 0)
                {
                    int from = mesh.Corner(t, k), to = mesh.Corner(t, (k + 1) % 3);
                    (outgoing.TryGetValue(from, out var list) ? list : outgoing[from] = []).Add(to);
                    incoming.Add(to);
                }
            }
        }

        var outlines = new List<(List<Vector3>, bool)>();
        List<int> Walk(int start)
        {
            var chain = new List<int> { start };
            int at = start;
            while (outgoing.TryGetValue(at, out var next) && next.Count > 0)
            {
                at = next[^1];
                next.RemoveAt(next.Count - 1);
                chain.Add(at);
                if (at == start)
                {
                    break;
                }
            }
            return chain;
        }
        foreach (int start in outgoing.Keys.Where(v => !incoming.Contains(v)).ToList())
        {
            while (outgoing[start].Count > 0)
            {
                outlines.Add((Straighten([.. Walk(start).Select(v => mesh.Vertices[v])]), false));
            }
        }
        foreach (int start in outgoing.Keys.ToList())
        {
            while (outgoing[start].Count > 0)
            {
                List<int> chain = Walk(start);
                bool closed = chain.Count > 2 && chain[^1] == chain[0];
                if (closed)
                {
                    chain.RemoveAt(chain.Count - 1);
                }
                outlines.Add((Straighten([.. chain.Select(v => mesh.Vertices[v])], closed), closed));
            }
        }
        return outlines;
    }

    /// <summary>Douglas-Peucker in plan; a closed outline is first cut at its two most distant corners.</summary>
    private static List<Vector3> Straighten(List<Vector3> points, bool closed = false)
    {
        if (closed && points.Count > 3)
        {
            int far = Enumerable.Range(1, points.Count - 1).MaxBy(i => Vector2.DistanceSquared(new(points[i].X, points[i].Y), new(points[0].X, points[0].Y)));
            List<Vector3> first = Straighten(points[..(far + 1)]), second = Straighten([.. points[far..], points[0]]);
            return [.. first, .. second.Skip(1).SkipLast(1)];
        }
        if (points.Count < 3)
        {
            return points;
        }
        Vector3 a = points[0], b = points[^1];
        var ab = new Vector2(b.X - a.X, b.Y - a.Y);
        float worst = 0;
        int at = -1;
        for (int i = 1; i < points.Count - 1; i++)
        {
            var ap = new Vector2(points[i].X - a.X, points[i].Y - a.Y);
            float t = ab.LengthSquared() > 1e-12f ? Math.Clamp(Vector2.Dot(ap, ab) / ab.LengthSquared(), 0, 1) : 0;
            float distance = Vector2.Distance(ap, ab * t);
            if (distance > worst)
            {
                (worst, at) = (distance, i);
            }
        }
        if (worst <= CoverStraightness)
        {
            return [a, b];
        }
        List<Vector3> left = Straighten(points[..(at + 1)]), right = Straighten(points[at..]);
        return [.. left, .. right.Skip(1)];
    }
}
