using JackAll.Core.Format;
using JackAll.Core.Format.Fcb;
using JackAll.Core.Mods;
using JackAll.Tools.World;

namespace JackAll.Tests;

/// <summary>
/// Deep fragments over a real <c>worldsector*.data.fcb</c> (docs/design/fcb-deep-fragments.md): one
/// override unit per placed entity, keyed on its <c>disEntityId</c>, so editing a single entity no
/// longer stages â€” or conflicts over â€” the whole sector. The fixture is a median-sized sector
/// extracted from the dedicated server's worlds.fat (12 entities over 2 mission layers).
/// </summary>
[Trait("Category", "RequiresFixture")]
public class WorldSectorFragmentTests : IDisposable
{
    private const string FixturePath = "Fixtures/WorldSector/worldsector56.data.fcb";

    /// <summary>The fixture's real in-archive path, so staged override paths hash to the same
    /// container hash the VFS would use.</summary>
    private const string SectorPath = @"levels\mp_14_woodlands\generated\worldsectors\worldsector56.data.fcb";

    private readonly string _sandbox =
        Path.Combine(Path.GetTempPath(), "jackall-worldsector-fragment", Guid.NewGuid().ToString("N"));

    [Fact]
    public void The_fixture_file_was_actually_found()
        => Assert.True(File.Exists(FixturePath),
            $"{FixturePath} was not found - every test in this class silently no-opped.");

    [Fact]
    public void Every_entity_with_a_disEntityId_gets_one_uniquely_addressable_fragment()
    {
        if (!File.Exists(FixturePath)) return;

        FcbObject root = FcbDocument.Deserialize(File.ReadAllBytes(FixturePath));
        IReadOnlyList<FcbFragment> fragments = FcbFragments.List(root);

        int addressableEntities = root.Children
            .Where(layer => layer.TypeHash == WorldHashes.MissionLayer)
            .SelectMany(layer => layer.Children)
            .Count(e => e.TypeHash == WorldHashes.Entity
                        && e.Values.TryGetValue(WorldHashes.DisEntityId, out byte[]? id) && id.Length >= 8);

        Assert.True(addressableEntities > 0, "The fixture has no addressable entities - it proves nothing.");
        Assert.Equal(addressableEntities, fragments.Count);
        Assert.Equal(fragments.Count, fragments.Select(f => f.Id).Distinct(FcbFragments.IdComparer).Count());
        Assert.All(fragments, f =>
        {
            Assert.Matches(@"^[^\\]+\.\d+\.xml$", f.Id);
            ulong disEntityId = BitConverter.ToUInt64(f.Node.Values[WorldHashes.DisEntityId], 0);
            // The trailing numeric id is authoritative: the canonical form is the bare id alone.
            Assert.Equal($"{disEntityId}.xml", FcbFragments.Canonicalize(f.Id));
        });
    }

    [Fact]
    public void Replacing_one_entity_changes_only_that_entity()
    {
        if (!File.Exists(FixturePath)) return;

        byte[] baseFcb = File.ReadAllBytes(FixturePath);
        FcbObject original = FcbDocument.Deserialize(baseFcb);
        IReadOnlyList<FcbFragment> fragments = FcbFragments.List(original);

        string targetId = fragments[0].Id;
        byte[] editedXml = TestSupport.RenderWithValueSetAt(fragments[0].Node, [], 0xAAAA0001, [0x2A, 0x00, 0x00, 0x00]);

        byte[] assembled = FcbAssembler.Apply(
            baseFcb, new Dictionary<string, string> { [targetId] = System.Text.Encoding.UTF8.GetString(editedXml) });
        FcbObject rebuilt = FcbDocument.Deserialize(assembled);

        Assert.Equal(original.Children.Count, rebuilt.Children.Count);
        IReadOnlyList<FcbFragment> rebuiltFragments = FcbFragments.List(rebuilt);
        Assert.Equal(fragments.Count, rebuiltFragments.Count);

        for (int i = 0; i < fragments.Count; i++)
        {
            Assert.Equal(fragments[i].Id, rebuiltFragments[i].Id);
            string expected = FcbFragments.IdComparer.Equals(fragments[i].Id, targetId)
                ? System.Text.Encoding.UTF8.GetString(editedXml)
                : FcbXml.ToXml(fragments[i].Node, FcbClassDefinitions.Empty);
            Assert.Equal(expected, FcbXml.ToXml(rebuiltFragments[i].Node, FcbClassDefinitions.Empty));
        }
    }

    /// <summary>The name prefix is cosmetic: an override staged under a stale (renamed) name still
    /// finds and replaces its entity, because the trailing numeric id is what's matched.</summary>
    [Fact]
    public void An_override_staged_under_a_renamed_entity_still_resolves_by_its_numeric_id()
    {
        if (!File.Exists(FixturePath)) return;

        byte[] baseFcb = File.ReadAllBytes(FixturePath);
        FcbObject original = FcbDocument.Deserialize(baseFcb);
        FcbFragment target = FcbFragments.List(original)[0];
        ulong disEntityId = BitConverter.ToUInt64(target.Node.Values[WorldHashes.DisEntityId], 0);

        Assert.Same(target.Node, FcbFragments.Find(original, $"some_other_name.{disEntityId}.xml"));
        Assert.Same(target.Node, FcbFragments.Find(original, $"{disEntityId}.xml"));

        byte[] editedXml = TestSupport.RenderWithValueSetAt(target.Node, [], 0xAAAA0001, [0x01, 0x00, 0x00, 0x00]);
        byte[] assembled = FcbAssembler.Apply(baseFcb, new Dictionary<string, string>
        {
            [$"some_other_name.{disEntityId}.xml"] = System.Text.Encoding.UTF8.GetString(editedXml),
        });

        FcbObject rebuilt = FcbDocument.Deserialize(assembled);
        FcbObject replaced = FcbFragments.Find(rebuilt, target.Id)!;
        Assert.Equal([0x01, 0x00, 0x00, 0x00], replaced.Values[0xAAAA0001]);
        Assert.Equal(FcbFragments.List(original).Count, FcbFragments.List(rebuilt).Count); // replaced, not appended
    }

    [Fact]
    public void A_new_entity_id_is_appended_into_a_mission_layer_not_at_the_root()
    {
        if (!File.Exists(FixturePath)) return;

        byte[] baseFcb = File.ReadAllBytes(FixturePath);
        FcbObject original = FcbDocument.Deserialize(baseFcb);

        var addition = new FcbObject { TypeHash = WorldHashes.Entity };
        addition.Values.Add(WorldHashes.DisEntityId, BitConverter.GetBytes(999999999UL));
        string additionXml = FcbXml.ToXml(addition, FcbClassDefinitions.Empty);

        byte[] assembled = FcbAssembler.Apply(
            baseFcb, new Dictionary<string, string> { ["brand_new.999999999.xml"] = additionXml });
        FcbObject rebuilt = FcbDocument.Deserialize(assembled);

        Assert.Equal(original.Children.Count, rebuilt.Children.Count);
        // The fixture's second layer is its "main" one, so the append must land there - not in the
        // first layer, and not at the root.
        FcbObject mainLayer = rebuilt.Children.First(c =>
            c.TypeHash == WorldHashes.MissionLayer
            && FcbEntityFields.ReadString(c, WorldHashes.TextPathId).Equals("main", StringComparison.OrdinalIgnoreCase));
        FcbObject added = mainLayer.Children[^1];
        Assert.Equal(BitConverter.GetBytes(999999999UL), added.Values[WorldHashes.DisEntityId]);
        Assert.Contains(FcbFragments.List(rebuilt), f => FcbFragments.IdComparer.Equals(f.Id, "999999999.xml"));
    }

    /// <summary>The point of the whole feature: two mods editing *different* entities of the same
    /// sector merge instead of conflicting at file level.</summary>
    [Fact]
    public void Two_layers_overriding_different_entities_of_one_sector_both_survive_a_build()
    {
        if (!File.Exists(FixturePath)) return;

        byte[] baseFcb = File.ReadAllBytes(FixturePath);
        FcbObject vanillaRoot = FcbDocument.Deserialize(baseFcb);
        IReadOnlyList<FcbFragment> fragments = FcbFragments.List(vanillaRoot);
        Assert.True(fragments.Count >= 2);

        FolderModLayer modA = MakeLayer("mod_a", fragments[0].Id,
            TestSupport.RenderWithValueSetAt(fragments[0].Node, [], 0xAAAA0001, [0x01, 0x00, 0x00, 0x00]));
        FolderModLayer modB = MakeLayer("mod_b", fragments[1].Id,
            TestSupport.RenderWithValueSetAt(fragments[1].Node, [], 0xAAAA0002, [0x02, 0x00, 0x00, 0x00]));

        var overrides = FragmentMerge.BuildOverrideIndex([modA, modB]);
        var byFragment = overrides[NameHash.Compute(SectorPath)];
        Assert.Equal(2, byFragment.Count);

        var splitter = new FcbContainerSplitter(FcbClassDefinitions.Empty);
        IContainerTree vanilla = splitter.Open(baseFcb);
        Dictionary<string, string> xmlById = byFragment.ToDictionary(
            kv => kv.Key,
            kv => FragmentMerge.Resolve(splitter, vanilla, kv.Key, kv.Value));
        FcbObject rebuilt = FcbDocument.Deserialize(splitter.Apply(baseFcb, xmlById));

        Assert.Equal(
            [0x01, 0x00, 0x00, 0x00],
            FcbFragments.Find(rebuilt, fragments[0].Id)!.Values[0xAAAA0001]);
        Assert.Equal(
            [0x02, 0x00, 0x00, 0x00],
            FcbFragments.Find(rebuilt, fragments[1].Id)!.Values[0xAAAA0002]);
    }

    /// <summary>A nested staged path (container path + fragment id) classifies back into the same
    /// container hash and an id the canonical comparer matches - the on-disk round trip.</summary>
    [Fact]
    public void A_staged_entity_override_round_trips_through_the_mod_layer_scan()
    {
        if (!File.Exists(FixturePath)) return;

        FcbObject root = FcbDocument.Deserialize(File.ReadAllBytes(FixturePath));
        FcbFragment fragment = FcbFragments.List(root)[0];

        FolderModLayer layer = MakeLayer("roundtrip", fragment.Id, "<object hash=\"0984415E\" />"u8.ToArray());
        layer.Rescan();

        Assert.True(layer.FragmentOverrides.TryGetValue(NameHash.Compute(SectorPath), out var staged));
        FragmentOverride single = Assert.Single(staged!);
        Assert.True(FcbFragments.IdComparer.Equals(fragment.Id, single.FragmentId));
    }

    /// <summary>The move a fragment override cannot express, expressed.</summary>
    [Fact]
    public void A_layout_moves_an_entity_between_the_fixtures_two_layers()
    {
        if (!File.Exists(FixturePath)) return;

        byte[] baseFcb = File.ReadAllBytes(FixturePath);
        var splitter = new FcbContainerSplitter(FcbClassDefinitions.Empty);
        FcbObject original = FcbDocument.Deserialize(baseFcb);

        FcbFragment target = FcbFragments.List(original).First(f => MissionLayers.IsMain(LayerOf(original, f.Node)));
        string other = original.Children
            .Where(c => c.TypeHash == WorldHashes.MissionLayer)
            .Select(c => FcbEntityFields.ReadString(c, WorldHashes.TextPathId))
            .First(name => !MissionLayers.IsMain(name));

        byte[] assembled = FcbAssembler.Apply(baseFcb, new Dictionary<string, string>
        {
            [ContainerLayout.Id] = Layout($"<layer path=\"{other}\"><entity id=\"{IdOf(target.Node)}\" /></layer>"),
        });

        IContainerTree tree = splitter.Open(assembled);
        Assert.Equal(other, tree.AncestryOf(target.Id)!.ParentName);

        // Moved, not copied or dropped - and every other entity stayed where it was.
        FcbObject rebuilt = FcbDocument.Deserialize(assembled);
        Assert.Equal(FcbFragments.List(original).Count, FcbFragments.List(rebuilt).Count);
        Assert.Equal(original.Children.Count, rebuilt.Children.Count);
        foreach (FcbFragment fragment in FcbFragments.List(original).Where(f => f.Id != target.Id))
        {
            Assert.Equal(LayerOf(original, fragment.Node), LayerOf(rebuilt, FcbFragments.Find(rebuilt, fragment.Id)!));
        }
    }

    /// <summary>
    /// Moving several entities out of one layer at once. The first move shifts the positions of the
    /// ones after it, so this is the case that catches an implementation holding on to where an
    /// entity used to be.
    /// </summary>
    [Fact]
    public void A_layout_moving_several_entities_out_of_one_layer_moves_all_of_them()
    {
        if (!File.Exists(FixturePath)) return;

        byte[] baseFcb = File.ReadAllBytes(FixturePath);
        FcbObject original = FcbDocument.Deserialize(baseFcb);
        const string added = @"missions\outposts\test\zone_03";

        ulong[] ids = [.. original.Children
            .Where(c => c.TypeHash == WorldHashes.MissionLayer && MissionLayers.IsMain(FcbEntityFields.ReadString(c, WorldHashes.TextPathId)))
            .SelectMany(layer => layer.Children)
            .Where(e => e.TypeHash == WorldHashes.Entity)
            .Select(IdOf)
            .Where(id => id != 0)];

        Assert.True(ids.Length >= 3, "the fixture's main layer is too small to prove anything here");

        string entities = string.Join("", ids.Select(id => $"<entity id=\"{id}\" />"));
        byte[] assembled = FcbAssembler.Apply(baseFcb, new Dictionary<string, string>
        {
            [ContainerLayout.Id] = Layout($"<layer path=\"{added}\">{entities}</layer>"),
        });

        FcbObject rebuilt = FcbDocument.Deserialize(assembled);
        Assert.All(ids, id => Assert.Equal(added, LayerOf(rebuilt, FcbFragments.Find(rebuilt, $"{id}.xml")!)));
        Assert.Equal(FcbFragments.List(original).Count, FcbFragments.List(rebuilt).Count);
    }

    /// <summary>A layer a mod invents has to be created, and where it lands is what the mod said -
    /// the community's outpost mods put theirs ahead of <c>main</c>.</summary>
    [Fact]
    public void A_layout_creates_a_missing_mission_layer_where_it_says_to()
    {
        if (!File.Exists(FixturePath)) return;

        byte[] baseFcb = File.ReadAllBytes(FixturePath);
        FcbObject original = FcbDocument.Deserialize(baseFcb);
        FcbFragment target = FcbFragments.List(original)[0];
        const string added = @"missions\outposts\test\zone_01";

        byte[] assembled = FcbAssembler.Apply(baseFcb, new Dictionary<string, string>
        {
            [ContainerLayout.Id] = Layout(
                $"<layer path=\"{added}\" before=\"main\"><entity id=\"{IdOf(target.Node)}\" /></layer>"),
        });

        FcbObject rebuilt = FcbDocument.Deserialize(assembled);
        List<FcbObject> layers = [.. rebuilt.Children.Where(c => c.TypeHash == WorldHashes.MissionLayer)];
        int addedAt = layers.FindIndex(l => FcbEntityFields.ReadString(l, WorldHashes.TextPathId) == added);
        int mainAt = layers.FindIndex(l => MissionLayers.IsMain(FcbEntityFields.ReadString(l, WorldHashes.TextPathId)));

        Assert.True(addedAt >= 0, "the layout's new layer was not created");
        Assert.True(addedAt < mainAt, "the new layer did not land ahead of main as the layout asked");
        Assert.Equal(NameHash.Compute(added), FcbEntityFields.ReadU32(layers[addedAt], WorldHashes.PathId));
        Assert.Equal(added, FcbFragments.Find(rebuilt, target.Id) is { } moved ? LayerOf(rebuilt, moved) : null);
    }

    /// <summary>
    /// A created layer carries whatever header values the layout gave it, beyond the two path fields
    /// every layer has. No shipped layer has any, so this is what keeps a mod that does ship one from
    /// having it quietly dropped.
    /// </summary>
    [Fact]
    public void A_created_layer_keeps_the_header_values_the_layout_gave_it()
    {
        if (!File.Exists(FixturePath)) return;

        byte[] baseFcb = File.ReadAllBytes(FixturePath);
        const string added = @"missions\outposts\test\headers";
        const uint headerHash = 0xBEEF0001;

        var layout = ContainerLayout.Parse(Layout(
            $"<layer path=\"{added}\"><value hash=\"{headerHash:X8}\">01020304</value></layer>"));

        // Through Render/Parse as well as Apply, since a staged layout is written out and read back.
        byte[] assembled = FcbAssembler.Apply(baseFcb, new Dictionary<string, string>
        {
            [ContainerLayout.Id] = layout.Render(),
        });

        FcbObject rebuilt = FcbDocument.Deserialize(assembled);
        FcbObject created = Assert.Single(
            rebuilt.Children,
            c => c.TypeHash == WorldHashes.MissionLayer
                && FcbEntityFields.ReadString(c, WorldHashes.TextPathId) == added);

        Assert.Equal(new byte[] { 1, 2, 3, 4 }, created.Values[headerHash]);
    }

    /// <summary>Without a layout a new entity joins <c>main</c>; with one it joins the layer named.</summary>
    [Fact]
    public void A_new_entity_listed_in_a_layout_is_routed_to_that_layer_rather_than_main()
    {
        if (!File.Exists(FixturePath)) return;

        byte[] baseFcb = File.ReadAllBytes(FixturePath);
        const ulong newId = 424242424242UL;
        const string added = @"missions\outposts\test\zone_02";

        var addition = new FcbObject { TypeHash = WorldHashes.Entity };
        addition.Values.Add(WorldHashes.DisEntityId, BitConverter.GetBytes(newId));

        byte[] assembled = FcbAssembler.Apply(baseFcb, new Dictionary<string, string>
        {
            [$"{newId}.xml"] = FcbXml.ToXml(addition, FcbClassDefinitions.Empty),
            [ContainerLayout.Id] = Layout($"<layer path=\"{added}\"><entity id=\"{newId}\" /></layer>"),
        });

        FcbObject rebuilt = FcbDocument.Deserialize(assembled);
        Assert.Equal(added, LayerOf(rebuilt, FcbFragments.Find(rebuilt, $"{newId}.xml")!));
    }

    /// <summary>The layout of a container applied back to itself says nothing new, so it must do
    /// nothing - the property that makes a sparse staged document safe.</summary>
    [Fact]
    public void Applying_a_containers_own_layout_changes_nothing()
    {
        if (!File.Exists(FixturePath)) return;

        byte[] baseFcb = File.ReadAllBytes(FixturePath);
        var splitter = new FcbContainerSplitter(FcbClassDefinitions.Empty);
        string own = splitter.Open(baseFcb).Extract(ContainerLayout.Id)!;

        byte[] assembled = splitter.Apply(baseFcb, new Dictionary<string, string> { [ContainerLayout.Id] = own });

        TestSupport.AssertSameShape(FcbDocument.Deserialize(baseFcb), FcbDocument.Deserialize(assembled));
        Assert.Null(ContainerLayout.Diff(
            ContainerLayout.Of(FcbDocument.Deserialize(baseFcb)), ContainerLayout.Parse(own)));
    }

    [Fact]
    public void A_layout_placing_one_entity_under_two_layers_is_refused()
    {
        string xml = Layout(
            "<layer path=\"a\"><entity id=\"7\" /></layer><layer path=\"b\"><entity id=\"7\" /></layer>");

        InvalidDataException error = Assert.Throws<InvalidDataException>(() => ContainerLayout.Parse(xml));
        Assert.Contains("7", error.Message);
    }

    /// <summary>An id the container does not have is left alone: a layout outlives the exact build it
    /// was written against, and refusing would break a mod on a game version it never named.</summary>
    [Fact]
    public void A_layout_naming_an_entity_the_sector_does_not_have_is_ignored()
    {
        if (!File.Exists(FixturePath)) return;

        byte[] baseFcb = File.ReadAllBytes(FixturePath);
        byte[] assembled = FcbAssembler.Apply(baseFcb, new Dictionary<string, string>
        {
            [ContainerLayout.Id] = Layout("<layer path=\"main\"><entity id=\"999999999999\" /></layer>"),
        });

        TestSupport.AssertSameShape(FcbDocument.Deserialize(baseFcb), FcbDocument.Deserialize(assembled));
    }

    /// <summary>
    /// Two mods re-filing different entities of one sector both get their way - the point of having
    /// an override unit at all. A layout is one document, so this only works because it is merged by
    /// what it means rather than by its lines.
    /// </summary>
    [Fact]
    public void Two_mods_moving_different_entities_of_one_sector_both_survive()
    {
        if (!File.Exists(FixturePath)) return;

        byte[] baseFcb = File.ReadAllBytes(FixturePath);
        FcbObject original = FcbDocument.Deserialize(baseFcb);
        ulong[] ids = [.. FcbFragments.List(original)
            .Where(f => MissionLayers.IsMain(LayerOf(original, f.Node)))
            .Select(f => IdOf(f.Node))
            .Take(2)];
        Assert.Equal(2, ids.Length);

        var splitter = new FcbContainerSplitter(FcbClassDefinitions.Empty);
        Dictionary<string, string> resolved = TestSupport.ResolveFragments(
            splitter, baseFcb, SectorPath, conflicts: null,
            MakeLayer("outposts", ContainerLayout.Id,
                AppText(Layout($"<layer path=\"a\"><entity id=\"{ids[0]}\" /></layer>"))),
            MakeLayer("patrols", ContainerLayout.Id,
                AppText(Layout($"<layer path=\"b\"><entity id=\"{ids[1]}\" /></layer>"))));

        FcbObject rebuilt = FcbDocument.Deserialize(splitter.Apply(baseFcb, resolved));
        Assert.Equal("a", LayerOf(rebuilt, FcbFragments.Find(rebuilt, $"{ids[0]}.xml")!));
        Assert.Equal("b", LayerOf(rebuilt, FcbFragments.Find(rebuilt, $"{ids[1]}.xml")!));
    }

    /// <summary>
    /// Both moving the same entity, to different layers, is the one real collision: load order picks
    /// and it is reported. What the loser must not lose is the rest of its document - a collision over
    /// one entity is not a reason to drop its other moves.
    /// </summary>
    [Fact]
    public void Two_mods_moving_one_entity_to_different_layers_conflict_without_losing_their_other_moves()
    {
        if (!File.Exists(FixturePath)) return;

        byte[] baseFcb = File.ReadAllBytes(FixturePath);
        FcbObject original = FcbDocument.Deserialize(baseFcb);
        ulong[] ids = [.. FcbFragments.List(original).Select(f => IdOf(f.Node)).Take(2)];
        var splitter = new FcbContainerSplitter(FcbClassDefinitions.Empty);
        var conflicts = new System.Collections.Concurrent.ConcurrentQueue<FragmentConflict>();

        Dictionary<string, string> resolved = TestSupport.ResolveFragments(
            splitter, baseFcb, SectorPath, conflicts,
            MakeLayer("outposts2", ContainerLayout.Id, AppText(Layout(
                $"<layer path=\"a\"><entity id=\"{ids[0]}\" /></layer><layer path=\"kept\"><entity id=\"{ids[1]}\" /></layer>"))),
            MakeLayer("patrols2", ContainerLayout.Id, AppText(Layout(
                $"<layer path=\"b\"><entity id=\"{ids[0]}\" /></layer>"))));

        FragmentConflict conflict = Assert.Single(conflicts);
        Assert.True(ContainerLayout.IsLayoutId(conflict.FragmentId));

        FcbObject rebuilt = FcbDocument.Deserialize(splitter.Apply(baseFcb, resolved));
        Assert.Equal("b", LayerOf(rebuilt, FcbFragments.Find(rebuilt, $"{ids[0]}.xml")!));
        Assert.Equal("kept", LayerOf(rebuilt, FcbFragments.Find(rebuilt, $"{ids[1]}.xml")!));
    }

    /// <summary>
    /// A mod repurposing a layer leaves the old one empty, and only then may it go. The guard is what
    /// keeps the removal from ever being a content deletion, which no override is allowed to be.
    /// </summary>
    [Fact]
    public void A_layout_removes_an_emptied_layer_but_never_one_still_holding_entities()
    {
        if (!File.Exists(FixturePath)) return;

        byte[] baseFcb = File.ReadAllBytes(FixturePath);
        FcbObject original = FcbDocument.Deserialize(baseFcb);
        FcbObject doomed = original.Children.First(c =>
            c.TypeHash == WorldHashes.MissionLayer
            && !MissionLayers.IsMain(FcbEntityFields.ReadString(c, WorldHashes.TextPathId)));
        string name = FcbEntityFields.ReadString(doomed, WorldHashes.TextPathId);
        ulong[] itsEntities = [.. doomed.Children.Where(e => e.TypeHash == WorldHashes.Entity).Select(IdOf)];
        Assert.NotEmpty(itsEntities);

        // Asking for it while its entities are still there must change nothing.
        byte[] refused = FcbAssembler.Apply(baseFcb, new Dictionary<string, string>
        {
            [ContainerLayout.Id] = Layout($"<remove path=\"{name}\" />"),
        });
        TestSupport.AssertSameShape(original, FcbDocument.Deserialize(refused));

        // Emptying it in the same document is what lets it go.
        string moves = string.Join("", itsEntities.Select(id => $"<entity id=\"{id}\" />"));
        byte[] emptied = FcbAssembler.Apply(baseFcb, new Dictionary<string, string>
        {
            [ContainerLayout.Id] = Layout($"<remove path=\"{name}\" /><layer path=\"main\">{moves}</layer>"),
        });

        FcbObject rebuilt = FcbDocument.Deserialize(emptied);
        Assert.DoesNotContain(rebuilt.Children, c =>
            c.TypeHash == WorldHashes.MissionLayer
            && FcbEntityFields.ReadString(c, WorldHashes.TextPathId) == name);
        Assert.Equal(FcbFragments.List(original).Count, FcbFragments.List(rebuilt).Count);
        Assert.All(itsEntities, id => Assert.Equal("main", LayerOf(rebuilt, FcbFragments.Find(rebuilt, $"{id}.xml")!)));
    }

    /// <summary>The verb the structural diff was missing: an entity a mod takes off the map.</summary>
    [Fact]
    public void A_layout_deletes_the_entity_it_names_and_leaves_the_rest_alone()
    {
        if (!File.Exists(FixturePath)) return;

        byte[] baseFcb = File.ReadAllBytes(FixturePath);
        FcbObject original = FcbDocument.Deserialize(baseFcb);
        IReadOnlyList<FcbFragment> before = FcbFragments.List(original);
        ulong doomed = IdOf(before[0].Node);

        byte[] assembled = FcbAssembler.Apply(baseFcb, new Dictionary<string, string>
        {
            [ContainerLayout.Id] = Layout($"<delete id=\"{doomed}\" />"),
        });

        FcbObject rebuilt = FcbDocument.Deserialize(assembled);
        Assert.Null(FcbFragments.Find(rebuilt, $"{doomed}.xml"));
        Assert.Equal(before.Count - 1, FcbFragments.List(rebuilt).Count);
        Assert.Equal(original.Children.Count, rebuilt.Children.Count);

        // Every other entity kept its content and its layer.
        foreach (FcbFragment fragment in before.Skip(1))
        {
            FcbObject still = Assert.IsType<FcbObject>(FcbFragments.Find(rebuilt, fragment.Id));
            Assert.Equal(LayerOf(original, fragment.Node), LayerOf(rebuilt, still));
            TestSupport.AssertSameShape(fragment.Node, still);
        }
    }

    /// <summary>
    /// The one collision a per-fragment merge cannot see, because it is between two fragments: one
    /// mod deletes an entity another edits. The entity is kept, and the pair is named so a build can
    /// say so rather than quietly picking.
    /// </summary>
    [Fact]
    public void An_entity_that_is_both_deleted_and_edited_is_kept_and_named()
    {
        if (!File.Exists(FixturePath)) return;

        byte[] baseFcb = File.ReadAllBytes(FixturePath);
        FcbFragment target = FcbFragments.List(FcbDocument.Deserialize(baseFcb))[0];
        ulong id = IdOf(target.Node);

        var staged = new Dictionary<string, string>(FcbFragments.IdComparer)
        {
            [target.Id] = System.Text.Encoding.UTF8.GetString(
                TestSupport.RenderWithValueSetAt(target.Node, [], 0xAAAA0001, [0x7B, 0, 0, 0])),
            [ContainerLayout.Id] = Layout($"<delete id=\"{id}\" />"),
        };

        var splitter = new FcbContainerSplitter(FcbClassDefinitions.Empty);
        Assert.Equal([$"{id}.xml"], splitter.Contradictions(staged).Select(c => c.FragmentId));

        FcbObject rebuilt = FcbDocument.Deserialize(splitter.Apply(baseFcb, staged));
        FcbObject kept = Assert.IsType<FcbObject>(FcbFragments.Find(rebuilt, target.Id));
        Assert.Equal([0x7B, 0, 0, 0], kept.Values[0xAAAA0001]);
    }

    [Fact]
    public void A_layout_that_both_deletes_and_places_one_entity_is_refused()
    {
        InvalidDataException error = Assert.Throws<InvalidDataException>(() => ContainerLayout.Parse(
            Layout("<delete id=\"7\" /><layer path=\"a\"><entity id=\"7\" /></layer>")));

        Assert.Contains("7", error.Message);
    }

    /// <summary>Deleting is the one thing a lone entity fragment still cannot say, so a layout that
    /// says it must survive the round trip a merge puts it through.</summary>
    [Fact]
    public void A_deletion_survives_being_canonicalised_and_merged()
    {
        var splitter = new FcbContainerSplitter(FcbClassDefinitions.Empty);
        string once = splitter.Canonicalize(ContainerLayout.Id, Layout("<delete id=\"42\" />"));

        Assert.Equal(once, splitter.Canonicalize(ContainerLayout.Id, once));
        Assert.Equal([42UL], ContainerLayout.Parse(once).Deleted);

        // Two mods deleting different entities of one sector both get their way.
        (string merged, bool conflict) = splitter.Merge(
            ContainerLayout.Id, Layout(""), Layout("<delete id=\"42\" />"), Layout("<delete id=\"43\" />"));

        Assert.False(conflict);
        Assert.Equal([42UL, 43UL], ContainerLayout.Parse(merged).Deleted.Order());
    }

    private static byte[] AppText(string xml) => System.Text.Encoding.UTF8.GetBytes(xml);

    private static string Layout(string body) => $"<layout>{body}</layout>";

    private static ulong IdOf(FcbObject entity) => FcbEntityFields.ReadU64(entity, WorldHashes.DisEntityId);

    /// <summary>A fragment id carries no layer, so the only way to see one is to ask the container.</summary>
    [Fact]
    public void Every_entity_reports_the_mission_layer_it_sits_under()
    {
        if (!File.Exists(FixturePath)) return;

        FcbObject root = FcbDocument.Deserialize(File.ReadAllBytes(FixturePath));
        IContainerTree tree = new FcbContainerSplitter(FcbClassDefinitions.Empty).Open(File.ReadAllBytes(FixturePath));

        foreach (FcbObject layer in root.Children.Where(c => c.TypeHash == WorldHashes.MissionLayer))
        {
            string expected = FcbEntityFields.ReadString(layer, WorldHashes.TextPathId);
            foreach (FcbObject entity in layer.Children.Where(e => e.TypeHash == WorldHashes.Entity))
            {
                string id = FcbFragments.EntityFragmentId(
                    FcbEntityFields.ReadU64(entity, WorldHashes.DisEntityId));
                FragmentAncestry ancestry = Assert.IsType<FragmentAncestry>(tree.AncestryOf(id));

                Assert.Equal(FragmentParentKind.MissionLayer, ancestry.Kind);
                Assert.Equal(expected, ancestry.ParentName);
                Assert.Equal(FcbEntityFields.ReadU32(layer, WorldHashes.PathId), ancestry.ParentPathId);
            }
        }
    }

    /// <summary>Vanilla agrees with itself: an entity under a mission layer either declares that same
    /// layer or declares none at all, which the engine reads as <c>main</c>.</summary>
    [Fact]
    public void No_vanilla_entity_disagrees_with_the_layer_it_sits_under()
    {
        if (!File.Exists(FixturePath)) return;

        IContainerTree tree = new FcbContainerSplitter(FcbClassDefinitions.Empty).Open(File.ReadAllBytes(FixturePath));
        IReadOnlyList<FcbFragmentInfo> rows = tree.List();

        Assert.NotEmpty(rows);
        Assert.All(rows, row => Assert.False(
            tree.AncestryOf(row.Id)!.IsLayerMismatch,
            $"{row.Id} reads as a layer mismatch in an untouched sector."));
    }

    /// <summary>
    /// A component that names no layer holds -1, not a layer id, and the engine reads that as
    /// <c>main</c>. Treating it as an id makes most of the shipped game look mis-filed.
    /// </summary>
    [Fact]
    public void An_entity_declaring_no_layer_at_all_is_not_a_mismatch()
    {
        if (!File.Exists(FixturePath)) return;

        byte[] baseFcb = File.ReadAllBytes(FixturePath);
        FcbObject original = FcbDocument.Deserialize(baseFcb);
        FcbFragment target = FcbFragments.List(original)
            .First(f => MissionLayers.IsMain(LayerOf(original, f.Node)));

        byte[] assembled = FcbAssembler.Apply(baseFcb, new Dictionary<string, string>
        {
            [target.Id] = FcbXml.ToXml(
                WithDeclaredLayer(target.Node, MissionLayers.NoLayer), FcbClassDefinitions.Empty),
        });

        FragmentAncestry ancestry = new FcbContainerSplitter(FcbClassDefinitions.Empty)
            .Open(assembled).AncestryOf(target.Id)!;

        Assert.Null(ancestry.DeclaredPathId);
        Assert.False(ancestry.IsLayerMismatch);
    }

    /// <summary>
    /// The check that would have caught the sentinel: nothing in the shipped game reads as a layer
    /// mismatch. The fixture alone could not, being one MP sector whose only tagged entity carries a
    /// real id, while most retail entities carry the unset value instead.
    /// </summary>
    [Fact]
    [Trait("Category", "RequiresFixture")]
    public void No_entity_in_the_shipped_game_reads_as_a_layer_mismatch()
    {
        string[] sectors = [.. Fc2Corpus.Find(".fcb")
            .Where(p => Path.GetFileName(p).StartsWith("worldsector", StringComparison.OrdinalIgnoreCase)
                     || Path.GetFileName(p).StartsWith("landmark", StringComparison.OrdinalIgnoreCase))
            .Where((_, i) => i % 100 == 0)];

        if (sectors.Length == 0) return;

        var splitter = new FcbContainerSplitter(FcbClassDefinitions.Empty);
        List<string> mismatched = [];
        int declaringNothing = 0;

        foreach (string path in sectors)
        {
            IContainerTree tree = splitter.Open(File.ReadAllBytes(path));
            foreach (FcbFragmentInfo row in tree.List())
            {
                if (tree.AncestryOf(row.Id) is not { Kind: FragmentParentKind.MissionLayer } ancestry)
                {
                    continue;
                }
                if (ancestry.DeclaredPathId is null)
                {
                    declaringNothing++;
                }
                if (ancestry.IsLayerMismatch)
                {
                    mismatched.Add($"{Path.GetFileName(path)}\\{row.Id} sits under \"{ancestry.ParentName}\"");
                }
            }
        }

        Assert.Empty(mismatched);

        // Without this the assertion above passes on a scan that read nothing worth reading.
        Assert.True(
            declaringNothing > 0,
            $"{sectors.Length} shipped sectors held no entity that declares no layer, so the -1 case went untested.");
    }

    /// <summary>The silently-wrong edit this whole feature exists to catch: the component says one
    /// layer, the sector nests the entity under another, and the nesting is what spawns it.</summary>
    [Fact]
    public void A_component_naming_another_layer_is_reported_as_a_mismatch()
    {
        if (!File.Exists(FixturePath)) return;

        byte[] baseFcb = File.ReadAllBytes(FixturePath);
        FcbObject original = FcbDocument.Deserialize(baseFcb);
        FcbFragment target = FcbFragments.List(original)
            .First(f => MissionLayers.IsMain(LayerOf(original, f.Node)));

        var splitter = new FcbContainerSplitter(FcbClassDefinitions.Empty);
        byte[] assembled = FcbAssembler.Apply(baseFcb, new Dictionary<string, string>
        {
            [target.Id] = FcbXml.ToXml(WithDeclaredLayer(target.Node, 0x0BADF00D), FcbClassDefinitions.Empty),
        });

        FragmentAncestry ancestry = splitter.Open(assembled).AncestryOf(target.Id)!;
        Assert.Equal(0x0BADF00Du, ancestry.DeclaredPathId);
        Assert.True(ancestry.IsLayerMismatch);
    }

    /// <summary>The layer this entity is nested under, which no fragment id records.</summary>
    private static string LayerOf(FcbObject root, FcbObject entity)
        => root.Children
            .Where(layer => layer.Children.Contains(entity))
            .Select(layer => FcbEntityFields.ReadString(layer, WorldHashes.TextPathId))
            .First();

    /// <summary>A copy of <paramref name="entity"/> carrying a mission component that claims
    /// <paramref name="pathId"/>, the shape a mod adds when it means to move an entity.</summary>
    private static FcbObject WithDeclaredLayer(FcbObject entity, uint pathId)
    {
        var component = new FcbObject { TypeHash = WorldHashes.CMissionComponent };
        component.Values.Add(WorldHashes.HidMissionLayerPath, BitConverter.GetBytes(pathId));

        var components = new FcbObject { TypeHash = WorldHashes.Components };
        components.Children.Add(component);

        var copy = new FcbObject { TypeHash = entity.TypeHash };
        foreach ((uint hash, byte[] value) in entity.Values)
        {
            copy.Values.Add(hash, value);
        }
        copy.Children.Add(components);
        return copy;
    }

    private FolderModLayer MakeLayer(string name, string fragmentId, byte[] content)
    {
        string dir = Path.Combine(_sandbox, name);
        Directory.CreateDirectory(dir);
        var layer = new FolderModLayer(dir, name);
        layer.Stage(
            NameHash.Compute($"{SectorPath}\\{fragmentId}"), $"{SectorPath}\\{fragmentId}", "xml", content);
        return layer;
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_sandbox)) Directory.Delete(_sandbox, recursive: true);
        }
        catch { /* temp cleanup is best-effort */ }
    }
}

