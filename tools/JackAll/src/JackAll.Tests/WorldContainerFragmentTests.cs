using JackAll.Core;
using JackAll.Core.Format.Fcb;
using JackAll.Core.Mods;

namespace JackAll.Tests;

/// <summary>
/// The three world-level containers that place entities in mission layers beside the sectors:
/// <c>*.omnis.fcb</c>, <c>*.managers.fcb</c> and <c>*.mapsdata.fcb</c>. They split per placed entity
/// the way a sector does; mapsdata groups its layers one level down, under a node per level cell.
/// </summary>
public class WorldContainerFragmentTests
{
    // A campaign world's omnis.
    private const string Omnis = "World/world1.omnis.fcb";

    // An MP map's managers.
    private const string Managers = "World/mp_17_dunes.managers.fcb";

    // A campaign world's mapsdata, so it spans many level cells.
    private const string MapsData = "World/world2.mapsdata.fcb";

    public static TheoryData<string> Containers => new() { Omnis, Managers, MapsData };

    private static FcbContainerSplitter Splitter => new(BundledAssets.LoadFcbClasses());

    [Fact]
    [Trait("Category", "RequiresFixture")]
    public void The_fixtures_were_actually_found() => Fixture.AssertPresent(Omnis, Managers, MapsData);

    /// <summary>Each of the three is recognised, and every entity in it is addressable.</summary>
    [Theory]
    [MemberData(nameof(Containers))]
    public void Every_placed_entity_gets_one_uniquely_addressable_fragment(string fixture)
    {
        if (Fixture.Read(fixture) is not { } bytes) return;

        FcbObject root = FcbDocument.Deserialize(bytes);
        Assert.True(FcbFragments.IsLayerBearing(root), $"{fixture} was not recognised.");

        int entities = FcbFragments.LayersOf(root)
            .SelectMany(l => l.Children)
            .Count(e => e.TypeHash == WorldHashes.Entity
                        && e.Values.TryGetValue(WorldHashes.DisEntityId, out byte[]? id) && id.Length >= 8);

        IReadOnlyList<FcbFragment> fragments = FcbFragments.List(root);
        Assert.Equal(entities, fragments.Count);
        Assert.Equal(fragments.Count, fragments.Select(f => f.Id).Distinct(FcbFragments.IdComparer).Count());
    }

    /// <summary>
    /// Every fragment extracted and spliced straight back reproduces the container it came from.
    /// </summary>
    [Theory]
    [MemberData(nameof(Containers))]
    public void Every_fragment_extracts_and_splices_back_unchanged(string fixture)
    {
        if (Fixture.Read(fixture) is not { } original) return;

        IContainerTree tree = Splitter.Open(original);
        IReadOnlyList<FcbFragmentInfo> rows = tree.List();
        Assert.NotEmpty(rows);

        Dictionary<string, string> everyFragment = rows.ToDictionary(
            r => r.Id, r => tree.Extract(r.Id)!, FcbFragments.IdComparer);

        var ids = new HashSet<string>(rows.Select(r => r.Id), FcbFragments.IdComparer);
        Assert.Equal(
            tree.Skeleton(ids.Contains),
            Splitter.Open(Splitter.Apply(original, everyFragment)).Skeleton(ids.Contains));
    }

    /// <summary>A container's own layout applied back to it moves nothing - the property that lets a
    /// mod state only what it changed.</summary>
    [Theory]
    [MemberData(nameof(Containers))]
    public void Applying_a_containers_own_layout_changes_nothing(string fixture)
    {
        if (Fixture.Read(fixture) is not { } original) return;

        FcbObject root = FcbDocument.Deserialize(original);
        ContainerLayout layout = ContainerLayout.Of(root);
        Assert.Null(ContainerLayout.Diff(
            root,
            FcbDocument.Deserialize(FcbAssembler.Apply(
                original, new Dictionary<string, string> { [ContainerLayout.Id] = layout.Render() }))));
    }

    /// <summary>
    /// mapsdata holds one <c>main</c> per level cell, so a layer's path alone does not identify it.
    /// This is what the cell-qualified key exists for.
    /// </summary>
    [Fact]
    public void A_mapsdata_layers_identity_includes_its_level_cell()
    {
        if (Fixture.Read(MapsData) is not { } bytes) return;

        ContainerLayout layout = ContainerLayout.Of(FcbDocument.Deserialize(bytes));
        LayerSpec[] mains = [.. layout.Layers.Where(l => MissionLayers.IsMain(l.Path))];

        Assert.True(mains.Length > 1, $"{MapsData} has only {mains.Length} 'main' layer(s).");
        Assert.All(mains, l => Assert.NotNull(l.Under));
        Assert.Equal(mains.Length, mains.Select(l => l.Key).Distinct(StringComparer.OrdinalIgnoreCase).Count());
    }

    /// <summary>A layout creates a layer inside the level cell it names, not at the container root.</summary>
    [Fact]
    public void A_layout_creates_a_mapsdata_layer_under_the_cell_it_names()
    {
        if (Fixture.Read(MapsData) is not { } original) return;

        FcbObject root = FcbDocument.Deserialize(original);
        string cell = ContainerLayout.CellKey(FcbFragments.LayerParentsOf(root).First());
        const string added = @"missions\ghostpatrols\test\patrol_01";

        byte[] assembled = FcbAssembler.Apply(original, new Dictionary<string, string>
        {
            [ContainerLayout.Id] =
                $"<layout><layer path=\"{added}\" under=\"{cell}\" /></layout>",
        });

        FcbObject rebuilt = FcbDocument.Deserialize(assembled);
        FcbObject owner = FcbFragments.LayerParentsOf(rebuilt)
            .Single(p => p.Children.Any(c =>
                c.TypeHash == WorldHashes.MissionLayer && MissionLayers.NameOf(c) == added));

        Assert.Equal(cell, ContainerLayout.CellKey(owner));
        Assert.DoesNotContain(rebuilt.Children, c => c.TypeHash == WorldHashes.MissionLayer);
    }
}
