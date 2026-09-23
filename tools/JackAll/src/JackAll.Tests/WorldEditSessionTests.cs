using System.Numerics;
using JackAll.Core.Format;
using JackAll.Core.Format.Fcb;
using JackAll.Tools.World;

namespace JackAll.Tests;

/// <summary>
/// The Map tab's paste, move and delete, carried through the same assembler a mod build runs, over a
/// retail sector (12 entities in two mission layers, sector 56 of a 10-wide map).
/// </summary>
[Trait("Category", "RequiresFixture")]
public class WorldEditSessionTests
{
    internal const string FixturePath = "Fixtures/WorldSector/worldsector56.data.fcb";
    private const string SectorPath = @"levels\mp_14_woodlands\generated\worldsectors\worldsector56.data.fcb";
    private const int SectorsPerSide = 10;

    /// <summary>A point inside sector 56, which spans x 384-448 and y 320-384.</summary>
    private static readonly Vector3 InSector = new(400f, 350f, 12f);

    [Fact]
    public void The_fixture_file_was_actually_found()
        => Assert.True(File.Exists(FixturePath),
            $"{FixturePath} was not found - every test in this class silently no-opped.");

    [Fact]
    public void A_paste_lands_in_main_as_a_new_entity_and_leaves_the_original_alone()
    {
        if (!File.Exists(FixturePath)) return;

        (WorldEditSession session, byte[] baseFcb, List<WorldEntity> entities) = Load();
        WorldEntity original = entities.First(e => e.LayerPathId != MissionLayers.MainName);
        WorldEntity pasted = session.Paste(CopiedEntity.Of(original, "mp_14_woodlands"), InSector)[0];

        FcbObject root = Assemble(baseFcb, session);
        List<FcbObject> all = EntitiesOf(root).ToList();
        Assert.Equal(entities.Count + 1, all.Count);

        FcbObject added = Assert.Single(all, e => FcbEntityFields.ReadU64(e, WorldHashes.DisEntityId) == pasted.Id);
        Assert.Equal(InSector, FcbEntityFields.ReadVector3(added, WorldHashes.HidPos));
        Assert.Equal(pasted.Name, FcbEntityFields.ReadString(added, WorldHashes.HidName));
        Assert.NotEqual(original.Name, pasted.Name);
        Assert.Contains(added, LayerNamed(root, MissionLayers.MainName).Children);

        FcbObject kept = Assert.Single(all, e => FcbEntityFields.ReadU64(e, WorldHashes.DisEntityId) == original.Id);
        Assert.Equal(original.Position, FcbEntityFields.ReadVector3(kept, WorldHashes.HidPos));
    }

    [Fact]
    public void A_move_rewrites_only_the_moved_entity_position()
    {
        if (!File.Exists(FixturePath)) return;

        (WorldEditSession session, byte[] baseFcb, List<WorldEntity> entities) = Load();
        WorldEntity moved = entities[0];
        moved.Position = InSector;
        session.Moved(moved);

        FcbObject root = Assemble(baseFcb, session);
        Assert.Equal(entities.Count, EntitiesOf(root).Count());
        foreach (FcbObject entity in EntitiesOf(root))
        {
            ulong id = FcbEntityFields.ReadU64(entity, WorldHashes.DisEntityId);
            Vector3? expected = id == moved.Id ? InSector : entities.Single(e => e.Id == id).Position;
            Assert.Equal(expected, FcbEntityFields.ReadVector3(entity, WorldHashes.HidPos));
        }
    }

    [Fact]
    public void A_delete_becomes_a_layout_delete_and_an_unsaved_paste_just_disappears()
    {
        if (!File.Exists(FixturePath)) return;

        (WorldEditSession session, byte[] baseFcb, List<WorldEntity> entities) = Load();
        WorldEntity doomed = entities[0];
        session.Delete(doomed);
        WorldEntity pasted = session.Paste(CopiedEntity.Of(entities[1], "mp_14_woodlands"), InSector)[0];
        session.Delete(pasted);

        (IReadOnlyList<EntityFragment> fragments, IReadOnlyList<DeletedEntity> deleted) = session.Pending();
        Assert.Empty(fragments);
        DeletedEntity only = Assert.Single(deleted);
        Assert.Equal(doomed.Id, only.Id);

        string layout = new ContainerLayout([], deleted: [FcbFragments.EntityFragmentId(only.Id)]).Render();
        FcbObject root = FcbDocument.Deserialize(
            FcbAssembler.Apply(baseFcb, new Dictionary<string, string> { [ContainerLayout.Id] = layout }));
        Assert.Equal(entities.Count - 1, EntitiesOf(root).Count());
        Assert.DoesNotContain(EntitiesOf(root), e => FcbEntityFields.ReadU64(e, WorldHashes.DisEntityId) == doomed.Id);
    }

    [Fact]
    public void A_paste_outside_every_loaded_sector_is_refused()
    {
        if (!File.Exists(FixturePath)) return;

        (WorldEditSession session, _, List<WorldEntity> entities) = Load();
        Assert.Throws<InvalidOperationException>(() =>
            session.Paste(CopiedEntity.Of(entities[0], "mp_14_woodlands"), new Vector3(10f, 10f, 0f)));
    }

    [Fact]
    public void Grouping_makes_a_prefab_at_the_members_centre_listing_them_in_their_layer()
    {
        if (!File.Exists(FixturePath)) return;

        (WorldEditSession session, _, List<WorldEntity> entities) = Load();
        List<WorldEntity> members = [.. entities.Where(e => e.LayerPathId == MissionLayers.MainName).Take(2)];
        WorldEntity prefab = session.Group(members);

        Assert.Equal((members[0].Position!.Value + members[1].Position!.Value) / 2f, prefab.Position);
        Assert.Equal(MissionLayers.MainName, prefab.LayerPathId);
        Assert.Equal(members.Select(m => m.Id), EntityGroups.ChildrenOf(prefab.Node).Select(c => c.Id));
        Assert.Equal(EntityGroups.PrefabClass, FcbEntityFields.ReadString(prefab.Node, WorldHashes.TextHidEntityClass));
    }

    [Fact]
    public void Grouping_across_mission_layers_is_refused()
    {
        if (!File.Exists(FixturePath)) return;

        (WorldEditSession session, _, List<WorldEntity> entities) = Load();
        WorldEntity inMain = entities.First(e => e.LayerPathId == MissionLayers.MainName);
        WorldEntity elsewhere = entities.First(e => e.LayerPathId != MissionLayers.MainName);

        Assert.Throws<InvalidOperationException>(() => session.Group([inMain, elsewhere]));
    }

    /// <summary>A pasted prefab lists its own new members, never the originals, and links between
    /// members follow them.</summary>
    [Fact]
    public void Pasting_a_prefab_brings_its_members_with_new_ids_where_they_stood_relative_to_it()
    {
        if (!File.Exists(FixturePath)) return;

        (WorldEditSession session, _, List<WorldEntity> entities) = Load();
        List<WorldEntity> members = [.. entities.Where(e => e.LayerPathId == MissionLayers.MainName).Take(2)];
        EntityLinks.Add(session.EditableNode(members[0]), new EntityLink("OnStateChange", members[1].Id, "CLightEvent", "ActivateLight"));
        WorldEntity prefab = session.Group(members);

        var at = new Vector3(420f, 360f, 20f);
        IReadOnlyList<WorldEntity> pasted = session.Paste(session.Copy(prefab), at);

        Assert.Equal(3, pasted.Count);
        Assert.Equal(pasted.Skip(1).Select(m => m.Id), EntityGroups.ChildrenOf(pasted[0].Node).Select(c => c.Id));
        Assert.DoesNotContain(pasted, p => entities.Any(e => e.Id == p.Id) || p.Id == prefab.Id);
        Assert.Equal(members[0].Position!.Value - prefab.Position!.Value, pasted[1].Position!.Value - at);
        Assert.Equal(pasted[2].Id, EntityLinks.Read(pasted[1].Node).Single().TargetId);
    }

    /// <summary>A prefab saved as a bundle pastes back with the same members in the same places.</summary>
    [Fact]
    public void A_prefab_bundle_reads_back_as_the_copy_it_was_written_from()
    {
        if (!File.Exists(FixturePath)) return;

        (WorldEditSession session, _, List<WorldEntity> entities) = Load();
        WorldEntity prefab = session.Group([.. entities.Where(e => e.LayerPathId == MissionLayers.MainName).Take(2)]);
        CopiedEntity copy = session.Copy(prefab);

        CopiedEntity read = PrefabBundle.Read(PrefabBundle.Write(copy));

        Assert.Equal(copy.SourceWorld, read.SourceWorld);
        Assert.Equal(FcbXml.ToXml(copy.Node, FcbClassDefinitions.Empty), FcbXml.ToXml(read.Node, FcbClassDefinitions.Empty));
        Assert.Equal(copy.Members.Select(m => m.Offset), read.Members.Select(m => m.Offset));
        Assert.Equal(copy.Members.Select(m => FcbXml.ToXml(m.Node, FcbClassDefinitions.Empty)),
            read.Members.Select(m => FcbXml.ToXml(m.Node, FcbClassDefinitions.Empty)));
        Assert.Equal(3, session.Paste(read, InSector).Count);
    }

    /// <summary>A copy of a prefab whose members are gone must not claim the original's members.</summary>
    [Fact]
    public void A_pasted_prefab_never_lists_the_originals_members()
    {
        if (!File.Exists(FixturePath)) return;

        (WorldEditSession session, _, List<WorldEntity> entities) = Load();
        WorldEntity prefab = session.Group([.. entities.Where(e => e.LayerPathId == MissionLayers.MainName).Take(2)]);

        WorldEntity pasted = session.Paste(CopiedEntity.Of(prefab, "mp_14_woodlands"), InSector)[0];

        Assert.Empty(EntityGroups.ChildrenOf(pasted.Node));
    }

    [Fact]
    public void A_mesh_new_to_the_world_brings_its_materials_from_another_depload_but_not_what_is_listed()
    {
        const string mesh = @"graphics\vehicles\land\magicbus\magicbus01.xbg";
        uint meshHash = NameHash.Compute(mesh);
        const uint material = 0x1111, sharedMaterial = 0x2222, texture = 0x3333;
        uint materialType = DepLoadTypes.Hash("CMaterialResource");
        uint textureType = DepLoadTypes.Hash("CTextureResource");

        var target = new DepLoadFile([
            new DepLoadParent(sharedMaterial, 0, [new DepLoadChild(texture, textureType)]),
        ]);
        var donor = new DepLoadFile([
            new DepLoadParent(meshHash, 0, [new DepLoadChild(material, materialType), new DepLoadChild(sharedMaterial, materialType)]),
            new DepLoadParent(material, 1, [new DepLoadChild(texture, textureType)]),
            new DepLoadParent(sharedMaterial, 2, [new DepLoadChild(texture, textureType)]),
        ]);
        var files = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase)
        {
            [WorldEditDependencies.DepLoadPathOf("world1")] = DepLoadDocument.Encode(target),
            [WorldEditDependencies.DepLoadPathOf("world2")] = DepLoadDocument.Encode(donor),
        };

        (IReadOnlyList<DepLoadParent> additions, IReadOnlyList<string> unresolved) =
            WorldEditDependencies.DepLoadAdditions(
                "world1", [mesh, @"graphics\nowhere.xbg"], files.GetValueOrDefault, files.Keys);

        Assert.Equal([meshHash, material], additions.Select(p => p.Hash));
        Assert.Equal([@"graphics\nowhere.xbg"], unresolved);
    }

    /// <summary>A field edit stages the entity whole, with the edit in it, and leaves the loaded tree alone.</summary>
    [Fact]
    public void A_field_edit_stages_the_entity_with_the_edit_and_everything_else_it_had()
    {
        if (!File.Exists(FixturePath)) return;

        (WorldEditSession session, byte[] baseFcb, List<WorldEntity> entities) = Load();
        WorldEntity edited = entities[0];
        uint field = FcbClassDefinitions.Crc32Ascii("bEditedByTest");
        MergedNode.Of(session.EditableNode(edited), null).SetValue(field, [1]);
        session.Edited(edited);

        Assert.True(session.IsModified(edited));
        Assert.False(edited.Node.Values.ContainsKey(field));
        FcbObject staged = Assert.Single(EntitiesOf(Assemble(baseFcb, session)),
            e => FcbEntityFields.ReadU64(e, WorldHashes.DisEntityId) == edited.Id);
        Assert.Equal([1], staged.Values[field]);
        Assert.Equal(edited.Node.Values.Count + 1, staged.Values.Count);
        Assert.Equal(edited.Node.Children.Count, staged.Children.Count);
    }

    /// <summary>A placement carries the fields every shipped archetype-bound instance does, and lands
    /// under the mission layer it was dropped on once the sector's layout files it there.</summary>
    [Fact]
    public void A_placement_is_a_minimal_instance_filed_under_its_mission_layer()
    {
        if (!File.Exists(FixturePath)) return;

        (WorldEditSession session, byte[] baseFcb, List<WorldEntity> entities) = Load();
        string layer = entities.First(e => e.LayerPathId != MissionLayers.MainName).LayerPathId;
        var archetype = new ArchetypeDefinition(
            "OA_Props.Props.Crate01", new ArchetypeLayer("library.fcb"), 0, null, new FcbObject { TypeHash = WorldHashes.Entity });
        WorldEntity placed = session.Place(archetype, InSector, layer);

        Assert.Equal("Props.Crate01_1", placed.Name);
        (string container, LayerSpec spec) = Assert.Single(session.LayerPlacements());
        Assert.Equal(SectorPath, container);
        Assert.Equal([placed.Id], spec.Entities);

        (IReadOnlyList<EntityFragment> fragments, _) = session.Pending();
        var overrides = fragments.ToDictionary(f => f.FragmentId, f => FcbXml.ToXml(f.Node, FcbClassDefinitions.Empty));
        overrides[ContainerLayout.Id] = new ContainerLayout([spec]).Render();
        FcbObject root = FcbDocument.Deserialize(FcbAssembler.Apply(baseFcb, overrides));

        FcbObject added = Assert.Single(LayerNamed(root, layer).Children,
            e => FcbEntityFields.ReadU64(e, WorldHashes.DisEntityId) == placed.Id);
        Assert.Equal(archetype.Name, FcbEntityFields.ReadString(added, WorldHashes.TplCreatureType));
        Assert.Equal(InSector, FcbEntityFields.ReadVector3(added, WorldHashes.HidPos));
        Assert.Equal(InSector, FcbEntityFields.ReadVector3(added, WorldHashes.HidPosPrecise));
        Assert.NotNull(FcbEntityFields.FindComponent(added, WorldHashes.CEventComponent));
        Assert.Equal(entities.Count + 1, EntitiesOf(root).Count());
    }

    /// <summary>A turn saves as <c>hidAngles</c>, whether or not the entity carried them before.</summary>
    [Fact]
    public void A_turn_saves_its_angles()
    {
        if (!File.Exists(FixturePath)) return;

        (WorldEditSession session, byte[] baseFcb, List<WorldEntity> entities) = Load();
        WorldEntity turned = entities[0];
        turned.Angles = new System.Numerics.Vector3(5f, -10f, 135f);
        session.Moved(turned);
        turned.Node.Values.Remove(WorldHashes.HidAngles);

        FcbObject staged = Assert.Single(EntitiesOf(Assemble(baseFcb, session)),
            e => FcbEntityFields.ReadU64(e, WorldHashes.DisEntityId) == turned.Id);
        Assert.Equal(turned.Angles, FcbEntityFields.ReadVector3(staged, WorldHashes.HidAngles));
    }

    internal static (WorldEditSession Session, byte[] BaseFcb, List<WorldEntity> Entities) Load()
    {
        byte[] baseFcb = File.ReadAllBytes(FixturePath);
        var map = new TerrainMap
        {
            Name = "mp_14_woodlands",
            SectorsPerSide = SectorsPerSide,
            Sectors = [(@"levels\mp_14_woodlands\generated\sdat\sd56.sdat", 56)],
        };
        Fc2World world = WorldLoader.Load(map, path =>
            path.Equals(SectorPath, StringComparison.OrdinalIgnoreCase) ? baseFcb : null);
        return (new WorldEditSession(world, SectorsPerSide), baseFcb, [.. world.Entities]);
    }

    internal static FcbObject Assemble(byte[] baseFcb, WorldEditSession session)
    {
        (IReadOnlyList<EntityFragment> fragments, _) = session.Pending();
        return FcbDocument.Deserialize(FcbAssembler.Apply(baseFcb, fragments.ToDictionary(
            f => f.FragmentId, f => FcbXml.ToXml(f.Node, FcbClassDefinitions.Empty))));
    }

    internal static IEnumerable<FcbObject> EntitiesOf(FcbObject root)
        => root.Children
            .Where(layer => layer.TypeHash == WorldHashes.MissionLayer)
            .SelectMany(layer => layer.Children)
            .Where(node => node.TypeHash == WorldHashes.Entity);

    private static FcbObject LayerNamed(FcbObject root, string name)
        => root.Children.Single(layer =>
            layer.TypeHash == WorldHashes.MissionLayer && MissionLayers.NameOf(layer) == name);
}
