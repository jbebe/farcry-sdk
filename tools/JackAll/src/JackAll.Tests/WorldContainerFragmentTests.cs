using JackAll.Core;
using JackAll.Core.Format.Fcb;
using JackAll.Core.Mods;

namespace JackAll.Tests;

/// <summary>
/// The three world-level containers that place entities in mission layers beside the sectors:
/// <c>*.omnis.fcb</c>, <c>*.managers.fcb</c> and <c>*.mapsdata.fcb</c>. They split per placed entity
/// the way a sector does; mapsdata groups its layers one level down, under a node per level cell.
/// </summary>
[Trait("Category", "RequiresFixture")]
public class WorldContainerFragmentTests
{
    private static readonly string[] Kinds = [".omnis.fcb", ".managers.fcb", ".mapsdata.fcb"];

    public static TheoryData<string> Containers()
    {
        var data = new TheoryData<string>();
        foreach (string path in Kinds.SelectMany(Fc2Corpus.Find).Order())
        {
            data.Add(path);
        }
        if (data.Count == 0)
        {
            data.Add(string.Empty);
        }
        return data;
    }

    private static FcbContainerSplitter Splitter => new(BundledAssets.LoadFcbClasses());

    [Fact]
    public void The_corpus_actually_holds_these_containers()
        => Assert.True(
            Kinds.All(k => Fc2Corpus.Find(k).Any()),
            Fc2Corpus.MissingMessage(".mapsdata.fcb"));

    /// <summary>Each of the three is recognised, and every entity in it is addressable.</summary>
    [Theory]
    [MemberData(nameof(Containers))]
    public void Every_placed_entity_gets_one_uniquely_addressable_fragment(string path)
    {
        if (path.Length == 0) return;

        FcbObject root = FcbDocument.Deserialize(File.ReadAllBytes(path));
        Assert.True(FcbFragments.IsLayerBearing(root), $"{Path.GetFileName(path)} was not recognised.");

        int entities = FcbFragments.LayersOf(root)
            .SelectMany(l => l.Children)
            .Count(e => e.TypeHash == WorldHashes.Entity
                        && e.Values.TryGetValue(WorldHashes.DisEntityId, out byte[]? id) && id.Length >= 8);

        IReadOnlyList<FcbFragment> fragments = FcbFragments.List(root);
        Assert.Equal(entities, fragments.Count);
        Assert.Equal(fragments.Count, fragments.Select(f => f.Id).Distinct(FcbFragments.IdComparer).Count());
    }

    /// <summary>
    /// The gate the whole split turns on: every fragment extracted and spliced straight back has to
    /// reproduce the container it came from.
    /// </summary>
    [Theory]
    [MemberData(nameof(Containers))]
    public void Every_fragment_extracts_and_splices_back_unchanged(string path)
    {
        if (path.Length == 0) return;

        byte[] original = File.ReadAllBytes(path);
        IContainerTree tree = Splitter.Open(original);
        IReadOnlyList<FcbFragmentInfo> rows = tree.List();
        if (rows.Count == 0) return;

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
    public void Applying_a_containers_own_layout_changes_nothing(string path)
    {
        if (path.Length == 0) return;

        byte[] original = File.ReadAllBytes(path);
        FcbObject root = FcbDocument.Deserialize(original);
        if (!FcbFragments.LayersOf(root).Any()) return;

        ContainerLayout layout = ContainerLayout.Of(root);
        Assert.Null(ContainerLayout.Diff(
            root,
            FcbDocument.Deserialize(FcbAssembler.Apply(
                original, new Dictionary<string, string> { [ContainerLayout.Id] = layout.Render() }))));
    }

    /// <summary>
    /// mapsdata holds one <c>main</c> per level cell - 25 of them in world1 - so a layer's path alone
    /// does not identify it. This is what the cell-qualified key exists for.
    /// </summary>
    [Fact]
    public void A_mapsdata_layers_identity_includes_its_level_cell()
    {
        string? path = Fc2Corpus.Find(".mapsdata.fcb")
            .FirstOrDefault(p => Path.GetFileName(p).StartsWith("world", StringComparison.OrdinalIgnoreCase));
        if (path is null) return;

        ContainerLayout layout = ContainerLayout.Of(FcbDocument.Deserialize(File.ReadAllBytes(path)));
        LayerSpec[] mains = [.. layout.Layers.Where(l => MissionLayers.IsMain(l.Path))];

        Assert.True(mains.Length > 1, $"{Path.GetFileName(path)} has only {mains.Length} 'main' layer(s).");
        Assert.All(mains, l => Assert.NotNull(l.Under));
        Assert.Equal(mains.Length, mains.Select(l => l.Key).Distinct(StringComparer.OrdinalIgnoreCase).Count());
    }

    /// <summary>A layout creates a layer inside the level cell it names, not at the container root.</summary>
    [Fact]
    public void A_layout_creates_a_mapsdata_layer_under_the_cell_it_names()
    {
        string? path = Fc2Corpus.Find(".mapsdata.fcb")
            .FirstOrDefault(p => Path.GetFileName(p).StartsWith("world", StringComparison.OrdinalIgnoreCase));
        if (path is null) return;

        byte[] original = File.ReadAllBytes(path);
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
