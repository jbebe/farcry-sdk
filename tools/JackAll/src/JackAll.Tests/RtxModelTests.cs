using System.Numerics;
using JackAll.Core.Format;
using JackAll.Tools.Rtx;
using JackAll.Tools.World;

namespace JackAll.Tests;

/// <summary>
/// Reading a RealTree, whose file is a dumped memory image: the offsets are not stored, so the walk
/// that recovers them is what these check. Against retail species, because a synthetic file would
/// only prove the walk agrees with itself.
/// </summary>
public class RtxModelTests
{
    private const string Folder = @".\Fixtures\Rtx";

    private static RtxModel? Species(string name)
    {
        string path = Path.Combine(Folder, name);
        return File.Exists(path) ? RtxModel.Parse(File.ReadAllBytes(path)) : null;
    }

    [Fact]
    [Trait("Category", "RequiresFixture")]
    public void The_fixtures_were_actually_found()
    {
        if (!Directory.Exists(Folder)) return;

        Assert.NotNull(Species("rt_tree_acacia.rtx"));
        Assert.NotNull(Species("hy_bigleaf.rtx"));
    }

    /// <summary>The acacia is the shape the whole savannah is built from: a trunk that tapers, and
    /// foliage hung on it as flat cards.</summary>
    [Fact]
    [Trait("Category", "RequiresFixture")]
    public void A_card_species_reads_as_a_tapering_trunk_hung_with_cards()
    {
        if (Species("rt_tree_acacia.rtx") is not { } tree) return;

        Assert.Equal(@"graphics\Vegetation\Savannah\Realtrees\rt_tree_acacia.rta", tree.Name);
        Assert.Equal(126, tree.Nodes.Count);
        Assert.Equal(17, tree.Branches.Count);
        Assert.Equal(104, tree.LeafCards.Count);
        Assert.Empty(tree.HybridLeaves);

        // The trunk starts below ground at its widest and thins as it climbs - Z is up.
        RtxNode root = tree.Nodes[0];
        Assert.Equal(0.65f, root.Radius, 2);
        Assert.True(root.Position.Z < 0f, "the trunk's first node should sit below the pivot");
        Assert.True(root.Direction.Z > 0.9f, "the trunk should start climbing");
        Assert.True(tree.Nodes[5].Radius < root.Radius, "the trunk should taper");
    }

    /// <summary>The jungle plants model their leaves instead, and ship three levels of each.</summary>
    [Fact]
    [Trait("Category", "RequiresFixture")]
    public void A_modelled_species_reads_as_leaf_meshes_with_their_own_levels()
    {
        if (Species("hy_bigleaf.rtx") is not { } plant) return;

        Assert.Empty(plant.LeafCards);
        Assert.Equal(6, plant.HybridLeaves.Count);

        foreach (RtxHybridLeaf leaf in plant.HybridLeaves)
        {
            Assert.Equal(3, leaf.Lods.Count);

            // Coarser levels have to actually be coarser, or the far tier costs more than the near.
            Assert.True(leaf.Lods[0].Indices.Length > leaf.Lods[^1].Indices.Length);
            foreach (RtxLeafLod lod in leaf.Lods)
            {
                Assert.Equal(0, lod.Indices.Length % 3);
                Assert.All(lod.Indices, i => Assert.InRange(i, 0, lod.Positions.Length - 1));
                Assert.All(lod.Normals, n => Assert.Equal(1f, n.Length(), 3));
                Assert.All(lod.Uvs, uv => Assert.InRange(uv.X, -0.01f, 1.01f));
            }
        }
    }

    /// <summary>
    /// Branches partition the nodes: each names a run, and between them they cover every node once.
    /// Both ends are inclusive, which is the one thing about the table that is easy to read wrong -
    /// off by one and the last node of every limb goes missing.
    /// </summary>
    [Theory]
    [Trait("Category", "RequiresFixture")]
    [InlineData("rt_tree_acacia.rtx")]
    [InlineData("hy_bigleaf.rtx")]
    public void The_branches_cover_every_node_exactly_once(string name)
    {
        if (Species(name) is not { } tree) return;

        var covered = new List<int>();
        foreach (RtxBranch branch in tree.Branches)
        {
            Assert.True(branch.LastNode > branch.FirstNode, "a branch should hold at least one segment");
            for (int node = branch.FirstNode; node <= branch.LastNode; node++)
            {
                covered.Add(node);
            }
        }

        Assert.Equal(Enumerable.Range(0, tree.Nodes.Count), covered.Order());
    }

    /// <summary>A node's length is the gap to the next node along its limb, which is what lets the
    /// chain be drawn as one tube rather than a string of disconnected stubs.</summary>
    [Fact]
    [Trait("Category", "RequiresFixture")]
    public void A_nodes_length_reaches_the_next_node()
    {
        if (Species("rt_tree_acacia.rtx") is not { } tree) return;

        foreach (RtxBranch branch in tree.Branches)
        {
            for (int node = branch.FirstNode; node < branch.LastNode; node++)
            {
                float gap = (tree.Nodes[node + 1].Position - tree.Nodes[node].Position).Length();
                Assert.Equal(gap, tree.Nodes[node].Length, 3);
            }
        }
    }

    /// <summary>
    /// The three slots are the argument order of the engine's own LOD setup: bark, then whichever
    /// kind of foliage the species carries. A species fills exactly one of the two foliage slots.
    /// </summary>
    [Fact]
    [Trait("Category", "RequiresFixture")]
    public void Each_species_names_a_bark_material_and_one_foliage_material()
    {
        if (Species("rt_tree_acacia.rtx") is not { } tree ||
            Species("hy_bigleaf.rtx") is not { } plant)
        {
            return;
        }

        Assert.EndsWith(".mlm", tree.Materials[RtxModel.SlotBark]);
        Assert.NotNull(tree.Materials[RtxModel.SlotLeafCards]);
        Assert.Null(tree.Materials[RtxModel.SlotHybridLeaves]);

        Assert.NotNull(plant.Materials[RtxModel.SlotBark]);
        Assert.Null(plant.Materials[RtxModel.SlotLeafCards]);
        Assert.NotNull(plant.Materials[RtxModel.SlotHybridLeaves]);
    }

    /// <summary>The arena walk is checked against the size the header declares, so a file it cannot
    /// account for is refused rather than read as whatever the strides happen to land on.</summary>
    [Fact]
    [Trait("Category", "RequiresFixture")]
    public void A_file_the_walk_cannot_account_for_is_refused()
    {
        string path = Path.Combine(Folder, "rt_tree_acacia.rtx");
        if (!File.Exists(path)) return;

        byte[] bytes = File.ReadAllBytes(path);
        // One more node than the arena was packed for.
        bytes[0x118 + 0x10]++;

        Assert.Throws<InvalidDataException>(() => RtxModel.Parse(bytes));
    }

    [Fact]
    public void Something_that_is_not_a_realtree_is_refused()
        => Assert.Throws<InvalidDataException>(() => RtxModel.Parse(new byte[512]));
}

/// <summary>Turning a RealTree skeleton into the triangles the map draws.</summary>
public class RtxMeshTests
{
    private const string Folder = @".\Fixtures\Rtx";

    private static WorldModel? Baked(string name)
    {
        string path = Path.Combine(Folder, name);
        return File.Exists(path)
            ? WorldModels.Bake(name, RtxMesh.ToMesh(RtxModel.Parse(File.ReadAllBytes(path))),
                WorldModels.FineTriangleBudget)
            : null;
    }

    /// <summary>
    /// A species bakes to the same thing an .xbg does, so the scatter draws it through the model
    /// layer unchanged: two tiers, the far one cheaper, and one material range per kind of surface.
    /// </summary>
    [Theory]
    [Trait("Category", "RequiresFixture")]
    [InlineData("rt_tree_acacia.rtx")]
    [InlineData("hy_bigleaf.rtx")]
    public void A_species_bakes_to_two_tiers_over_bark_and_foliage(string name)
    {
        if (Baked(name) is not { } model) return;

        Assert.True(model.Coarse.Count < model.Fine.Count, "the far tier should be the cheaper one");
        Assert.Equal(2, model.MaterialRanges.Count);
        Assert.All(model.MaterialRanges, range => Assert.EndsWith(".xbm", range.MaterialName));
        Assert.All(model.Indices, index => Assert.InRange(index, 0, model.VertexCount - 1));
        Assert.All(model.Vertices, v => Assert.True(float.IsFinite(v)));
    }

    /// <summary>The path the map editor actually takes: a scatter naming a RealTree has to come back
    /// as a placement of real geometry, not as a marker.</summary>
    [Fact]
    [Trait("Category", "RequiresFixture")]
    public void The_scatter_places_a_realtree_as_geometry()
    {
        string path = Path.Combine(Folder, "rt_tree_acacia.rtx");
        if (!File.Exists(path)) return;

        const string Resource = @"graphics\vegetation\savannah\realtrees\rt_tree_acacia.rtx";
        uint id = NameHash.Compute(Resource);
        byte[] bytes = File.ReadAllBytes(path);

        ScatterSet scatter = WorldVegetation.Split(
            [new VegetationInstance(new Vector3(1, 2, 3), id)],
            new Dictionary<uint, string> { [id] = Resource },
            p => p.Equals(Resource, StringComparison.OrdinalIgnoreCase) ? bytes : null);

        Assert.Empty(scatter.Markers);
        ScatterInstance placed = Assert.Single(scatter.Instances);
        Assert.NotEmpty(scatter.Models[placed.Model].Indices);
    }

    /// <summary>Whether the geometry is the species rather than a stand-in is a question about its
    /// size, so the acacia is measured against an acacia.</summary>
    [Fact]
    [Trait("Category", "RequiresFixture")]
    public void The_acacia_comes_out_acacia_sized()
    {
        if (Baked("rt_tree_acacia.rtx") is not { } model) return;

        Vector3 size = model.LocalMax - model.LocalMin;
        Assert.InRange(size.Z, 5f, 12f);
        Assert.InRange(size.X, 5f, 15f);

        // A tree standing on its pivot, not floating over it or buried under it.
        Assert.InRange(model.LocalMin.Z, -2f, 0f);
    }
}
