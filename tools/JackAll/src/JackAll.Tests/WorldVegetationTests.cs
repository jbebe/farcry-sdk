using System.Numerics;
using JackAll.Core.Format;
using JackAll.Tools.World;

namespace JackAll.Tests;

/// <summary>
/// Turning the scatter's resource ids back into the assets they name.
/// </summary>
public class WorldVegetationTests
{
    private const string MeshFixture = "Fixtures/Xbg/chairbar01.xbg";

    /// <summary>
    /// A resource id is the CRC32 of the resource's own normalized path - the same hash the .fat
    /// index keys on. That is what makes the scatter drawable at all: 84 of 84 ids across world 1's
    /// landmark files resolve this way.
    /// </summary>
    [Fact]
    public void A_resource_id_is_its_own_path_hash()
    {
        const string Rock = @"graphics\terrain\rocks\desert\ter_desertrock39_nav.xbg";
        const string Tree = @"graphics\vegetation\desert\realtrees\bush.rtx";
        Dictionary<uint, string> byId = WorldVegetation.ResourcesById(
            [Rock.Replace('\\', '/').ToUpperInvariant(), Tree, @"graphics\terrain\rocks\rock.hkx"]);

        Assert.Equal(Rock, byId[NameHash.Compute(Rock)]);
        Assert.Equal(0x847110E1u, NameHash.Compute(Rock));

        // RealTrees are in the map too - they draw as a stand-in rather than not at all - but
        // nothing else a scatter list could name is.
        Assert.Equal(Tree, byId[NameHash.Compute(Tree)]);
        Assert.Equal(2, byId.Count);
    }

    /// <summary>Instances whose resource resolves to a mesh become drawable models; the rest come
    /// back as markers, and none are lost on the way.</summary>
    [Fact]
    [Trait("Category", "RequiresFixture")]
    public void The_split_draws_what_it_can_and_marks_the_rest()
    {
        if (!File.Exists(MeshFixture)) return;

        const string Mesh = @"graphics\props\chair.xbg";
        uint known = NameHash.Compute(Mesh);
        const uint Unknown = 0xDEADBEEF;

        VegetationInstance[] instances =
        [
            new(new Vector3(1, 2, 3), known),
            new(new Vector3(4, 5, 6), known),
            new(new Vector3(7, 8, 9), Unknown),
        ];

        byte[] bytes = File.ReadAllBytes(MeshFixture);
        ScatterSet scatter = WorldVegetation.Split(
            instances,
            new Dictionary<uint, string> { [known] = Mesh },
            path => path.EndsWith(".xbg", StringComparison.OrdinalIgnoreCase) ? bytes : null);

        Assert.Single(scatter.Models);
        Assert.Equal(2, scatter.Instances.Length);
        Assert.All(scatter.Instances, i => Assert.Equal(0, i.Model));
        Assert.Equal([new Vector3(1, 2, 3), new Vector3(4, 5, 6)], scatter.Instances.Select(i => i.Position));
        Assert.Equal([new Vector3(7, 8, 9)], scatter.Markers.Select(m => m.Position));

        // Every instance ends up in exactly one of the two.
        Assert.Equal(instances.Length, scatter.Instances.Length + scatter.Markers.Count);
    }

    /// <summary>A resource that resolves to a path the reader cannot serve falls back to a marker
    /// rather than vanishing.</summary>
    [Fact]
    public void An_unreadable_mesh_falls_back_to_a_marker()
    {
        const string Missing = @"graphics\props\gone.xbg";
        uint id = NameHash.Compute(Missing);

        ScatterSet scatter = WorldVegetation.Split(
            [new VegetationInstance(Vector3.Zero, id)],
            new Dictionary<uint, string> { [id] = Missing },
            _ => null);

        Assert.Empty(scatter.Models);
        Assert.Empty(scatter.Instances);
        Assert.Single(scatter.Markers);
    }
}
