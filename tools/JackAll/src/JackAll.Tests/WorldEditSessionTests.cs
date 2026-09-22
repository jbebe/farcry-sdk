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
        WorldEntity pasted = session.Paste(CopiedEntity.Of(original, "mp_14_woodlands"), InSector);

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
        WorldEntity pasted = session.Paste(CopiedEntity.Of(entities[1], "mp_14_woodlands"), InSector);
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
    public void A_prefab_owning_other_entities_cannot_be_copied()
    {
        var children = new FcbObject { TypeHash = WorldHashes.EntityChildren };
        children.Children.Add(new FcbObject { TypeHash = FcbClassDefinitions.Crc32Ascii("Child") });
        var node = new FcbObject { TypeHash = WorldHashes.Entity };
        node.Children.Add(children);
        var entity = new WorldEntity
        {
            Node = node,
            HomeSector = new WorldSectorDocument { SourcePath = SectorPath, SectorId = 56, PristineRoot = new FcbObject() },
            LayerPathId = MissionLayers.MainName,
        };

        Assert.Throws<InvalidOperationException>(() => CopiedEntity.Of(entity, "world1"));
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
