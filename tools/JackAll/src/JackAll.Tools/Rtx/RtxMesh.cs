using System.Numerics;
using JackAll.Tools.Xbg;

namespace JackAll.Tools.Rtx;

/// <summary>
/// Turns a parsed RealTree into the same triangle lists an <c>.xbg</c> parses to, so the model
/// pipeline bakes, textures, tiers and draws a tree exactly as it does every other mesh.
/// </summary>
/// <remarks>
/// A RealTree stores no triangles: the branches are a skeleton of tapered tubes the engine skins at
/// runtime, and only the modelled leaves ship as real geometry. So the branches are tessellated
/// here, at a ring count this file chooses, while the leaves are drawn from what the file authored.
/// </remarks>
public static class RtxMesh
{
    /// <summary>Sides per branch ring, at the fine tier and the coarse one. The only numbers here
    /// that are a choice rather than a reading of the file.</summary>
    private const int FineSides = 6;
    private const int CoarseSides = 3;

    private const int FineLod = 0;
    private const int CoarseLod = 2;

    private const string BranchPart = "BRANCHES";
    private const string CardPart = "LEAFCARDS";
    private const string FoliagePart = "FOLIAGE";

    /// <summary>
    /// The species as a mesh with two detail tiers: coarser branch rings and the leaves' own
    /// coarsest level make up the far one.
    /// </summary>
    public static XbgModel ToMesh(RtxModel tree)
    {
        // Slots the species does not use are dropped, so a material index still addresses this list.
        int[] slots = [RtxModel.SlotBark, RtxModel.SlotLeafCards, RtxModel.SlotHybridLeaves];
        var materials = new List<string>();
        var indexBySlot = new Dictionary<int, int>();
        foreach (int slot in slots)
        {
            if (tree.Materials[slot] is { Length: > 0 } material)
            {
                indexBySlot[slot] = materials.Count;
                materials.Add(Path.ChangeExtension(material, ".xbm"));
            }
        }

        var mesh = new Builder(materials, indexBySlot);
        (int Lod, int Sides)[] tiers = [(FineLod, FineSides), (CoarseLod, CoarseSides)];
        foreach ((int lod, int sides) in tiers)
        {
            mesh.Add(Branches(tree, sides), lod, BranchPart, RtxModel.SlotBark);
            mesh.Add(HybridLeaves(tree, lod == FineLod), lod, FoliagePart, RtxModel.SlotHybridLeaves);
        }

        // The cards are authored one apiece with no coarser form, so they stand at both tiers.
        mesh.Add(LeafCards(tree), FineLod, CardPart, RtxModel.SlotLeafCards);

        return new XbgModel
        {
            Materials = materials,
            Submeshes = mesh.Submeshes,
            LodLevels = [FineLod, CoarseLod],
        };
    }

    /// <summary>Collects the submeshes, binding each to the material its slot resolved to.</summary>
    private sealed class Builder(List<string> materials, Dictionary<int, int> indexBySlot)
    {
        public List<XbgSubmesh> Submeshes { get; } = [];

        public void Add(Geometry geometry, int lod, string part, int slot)
        {
            if (geometry.Indices.Count == 0)
            {
                return;
            }

            int material = indexBySlot.GetValueOrDefault(slot, -1);
            Submeshes.Add(new XbgSubmesh
            {
                LodLevel = lod,
                MaterialIndex = material,
                MaterialName = material >= 0 ? materials[material] : "",
                PartName = part,
                Positions = [.. geometry.Positions],
                Normals = [.. geometry.Normals],
                Uvs = [.. geometry.Uvs],
                Indices = [.. geometry.Indices],
            });
        }
    }

    /// <summary>
    /// Each limb as a tube of rings, one per node at that node's own radius, closed by a cone over
    /// the last node's length - which is the only length in the chain that does not reach a node.
    /// </summary>
    private static Geometry Branches(RtxModel tree, int sides)
    {
        var geometry = new Geometry();
        foreach (RtxBranch branch in tree.Branches)
        {
            int first = branch.FirstNode;
            int last = Math.Min(branch.LastNode, tree.Nodes.Count - 1);
            if (first < 0 || last <= first)
            {
                continue;
            }

            // Carried along the limb rather than rebuilt per node, so consecutive rings line up
            // instead of twisting against each other.
            Vector3 across = FirstAcross(tree.Nodes[first].Direction);
            float along = 0f;
            int previous = -1;
            for (int node = first; node <= last; node++)
            {
                RtxNode current = tree.Nodes[node];
                across = Transport(across, current.Direction);
                int ring = Ring(geometry, current.Position, current.Direction, across, current.Radius, along, sides);
                if (previous >= 0)
                {
                    Skin(geometry, previous, ring, sides);
                }

                previous = ring;
                along += current.Length;
            }

            RtxNode tip = tree.Nodes[last];
            int cap = Ring(geometry, tip.Position + (tip.Direction * tip.Length), tip.Direction, across,
                0f, along, sides);
            Skin(geometry, previous, cap, sides);
        }

        return geometry;
    }

    /// <summary>One ring of vertices around a node, returning where it starts.</summary>
    private static int Ring(
        Geometry geometry, Vector3 centre, Vector3 direction, Vector3 across, float radius,
        float along, int sides)
    {
        int start = geometry.Positions.Count;
        Vector3 up = Vector3.Cross(direction, across);
        for (int side = 0; side < sides; side++)
        {
            float angle = MathF.Tau * side / sides;
            Vector3 outward = (across * MathF.Cos(angle)) + (up * MathF.Sin(angle));
            geometry.Positions.Add(centre + (outward * radius));
            geometry.Normals.Add(outward);
            geometry.Uvs.Add(new Vector2((float)side / sides, along));
        }

        return start;
    }

    /// <summary>Two triangles per side, wrapping the last back onto the first.</summary>
    private static void Skin(Geometry geometry, int lower, int upper, int sides)
    {
        for (int side = 0; side < sides; side++)
        {
            int next = (side + 1) % sides;
            geometry.Indices.AddRange([lower + side, upper + side, upper + next]);
            geometry.Indices.AddRange([lower + side, upper + next, lower + next]);
        }
    }

    private static Vector3 FirstAcross(Vector3 direction)
    {
        Vector3 reference = MathF.Abs(direction.Z) < 0.9f ? Vector3.UnitZ : Vector3.UnitX;
        return Transport(Vector3.Cross(reference, direction), direction);
    }

    /// <summary>The nearest vector to <paramref name="across"/> that is square to the new direction.</summary>
    private static Vector3 Transport(Vector3 across, Vector3 direction)
    {
        Vector3 square = across - (direction * Vector3.Dot(across, direction));
        return square.LengthSquared() > 1e-8f
            ? Vector3.Normalize(square)
            : Vector3.Normalize(Vector3.Cross(
                MathF.Abs(direction.Z) < 0.9f ? Vector3.UnitZ : Vector3.UnitX, direction));
    }

    /// <summary>
    /// Every leaf card as its authored quad.
    /// </summary>
    /// <remarks>
    /// The four offsets are all at the card's own radius but are measurably not coplanar, so the
    /// card is a twisted quad rather than a flat one and the file does not say which order its
    /// corners go in. Of the three ways to pair four points into a quad only one has no crossing
    /// edge, and it is the shortest - so the order is measured rather than assumed.
    /// </remarks>
    private static Geometry LeafCards(RtxModel tree)
    {
        var geometry = new Geometry();
        foreach (RtxLeafCard card in tree.LeafCards)
        {
            Vector3[] quad = Wind(card.C0, card.C1, card.C2, card.C3);
            Vector3 normal = FaceNormal(quad);
            int start = geometry.Positions.Count;
            for (int corner = 0; corner < CardCorners.Length; corner++)
            {
                geometry.Positions.Add(card.Position + quad[corner]);
                geometry.Normals.Add(normal);
                geometry.Uvs.Add(CardCorners[corner]);
            }

            geometry.Indices.AddRange([start, start + 1, start + 2]);
            geometry.Indices.AddRange([start, start + 2, start + 3]);
        }

        return geometry;
    }

    private static readonly Vector2[] CardCorners = [new(0, 0), new(1, 0), new(1, 1), new(0, 1)];

    /// <summary>
    /// The one cycle of the four corners that has no crossing edge, which is the shortest - so
    /// equivalently the one leaving the longest pair of edges unused, as its diagonals.
    /// </summary>
    private static Vector3[] Wind(Vector3 a, Vector3 b, Vector3 c, Vector3 d)
    {
        float acbd = Vector3.Distance(a, c) + Vector3.Distance(b, d);
        float adbc = Vector3.Distance(a, d) + Vector3.Distance(b, c);
        float abcd = Vector3.Distance(a, b) + Vector3.Distance(c, d);
        if (acbd >= adbc && acbd >= abcd)
        {
            return [a, b, c, d];
        }

        return adbc >= abcd ? [a, b, d, c] : [a, c, b, d];
    }

    private static Vector3 FaceNormal(Vector3[] quad)
    {
        Vector3 normal = Vector3.Cross(quad[2] - quad[0], quad[3] - quad[1]);
        return normal.LengthSquared() > 1e-12f ? Vector3.Normalize(normal) : Vector3.UnitZ;
    }

    /// <summary>The modelled leaves at one tier, merged into a single list - they share a material
    /// and there can be hundreds of them.</summary>
    private static Geometry HybridLeaves(RtxModel tree, bool fine)
    {
        var geometry = new Geometry();
        foreach (RtxHybridLeaf leaf in tree.HybridLeaves)
        {
            if (leaf.Lods.Count == 0)
            {
                continue;
            }

            RtxLeafLod lod = fine ? leaf.Lods[0] : leaf.Lods[^1];
            int start = geometry.Positions.Count;
            geometry.Positions.AddRange(lod.Positions);
            geometry.Normals.AddRange(lod.Normals);
            geometry.Uvs.AddRange(lod.Uvs);
            foreach (int index in lod.Indices)
            {
                geometry.Indices.Add(start + index);
            }
        }

        return geometry;
    }

    private sealed class Geometry
    {
        public List<Vector3> Positions { get; } = [];
        public List<Vector3> Normals { get; } = [];
        public List<Vector2> Uvs { get; } = [];
        public List<int> Indices { get; } = [];
    }
}
