using System.Numerics;
using JackAll.Core.Format.Fcb;
using JackAll.Tools.World;
using static JackAll.Tests.WorldEditSessionTests;

namespace JackAll.Tests;

/// <summary>The Map tab's check, over edits made to the retail sector the session tests use.</summary>
public class WorldLintTests
{
    private static readonly Vector3 InSector = new(400f, 350f, 12f);
    private static readonly ArchetypeIndex NoLibrary = ArchetypeIndex.Load([new ArchetypeLayer("missing.fcb")], _ => null);
    private static readonly ArchetypeDefinition Crate =
        new("OA_Props.Props.Crate01", new ArchetypeLayer("library.fcb"), 0, null, new FcbObject { TypeHash = WorldHashes.Entity });

    [Fact]
    public void A_placement_of_an_archetype_the_world_lacks_is_an_error()
    {
        if (Fixture.Read(WorldSectorFragmentTests.Sector56) is not { } sector) return;

        (WorldEditSession session, _, _) = Load(sector);
        WorldEntity placed = session.Place(Crate, InSector, MissionLayers.MainName);

        Assert.Contains(Run(session), f => f.Entity == placed && f is { Kind: "Unknown archetype", Severity: LintSeverity.Error });
    }

    [Fact]
    public void A_character_in_a_sector_without_navmesh_is_an_error_and_with_one_is_fine()
    {
        if (Fixture.Read(WorldSectorFragmentTests.Sector56) is not { } sector) return;

        (WorldEditSession session, _, _) = Load(sector);
        WorldEntity placed = session.Place(Crate, InSector, MissionLayers.MainName);
        placed.Node.Children.Single(c => c.TypeHash == WorldHashes.Components)
            .Children.Add(new FcbObject { TypeHash = FcbClassDefinitions.Crc32Ascii("CPawn") });

        Assert.Contains(Run(session, hasNavMesh: false), f => f.Entity == placed && f.Kind == "Character without navmesh");
        Assert.DoesNotContain(Run(session, hasNavMesh: true), f => f.Kind == "Character without navmesh");
    }

    [Fact]
    public void An_edited_entity_moved_out_of_its_sector_is_flagged_and_an_untouched_one_is_not()
    {
        if (Fixture.Read(WorldSectorFragmentTests.Sector56) is not { } sector) return;

        (WorldEditSession session, _, List<WorldEntity> entities) = Load(sector);
        Assert.DoesNotContain(Run(session), f => f.Kind == "Wrong sector");

        entities[0].Position = new Vector3(10f, 10f, 0f);
        session.Moved(entities[0]);

        WorldFinding finding = Assert.Single(Run(session), f => f.Kind == "Wrong sector");
        Assert.Same(entities[0], finding.Entity);
    }

    [Fact]
    public void A_link_to_nothing_is_flagged_on_the_entity_that_was_edited_to_carry_it()
    {
        if (Fixture.Read(WorldSectorFragmentTests.Sector56) is not { } sector) return;

        (WorldEditSession session, _, List<WorldEntity> entities) = Load(sector);
        EntityLinks.Add(session.EditableNode(entities[0]), new EntityLink("OnStateChange", 12345, "CLightEvent", "ActivateLight"));
        session.Edited(entities[0]);

        Assert.Single(Run(session), f => f.Kind == "Broken link" && f.Entity == entities[0]);
    }

    /// <summary>Deleting what a saved link points at breaks it, though the linking entity is untouched.</summary>
    [Fact]
    public void Deleting_a_link_target_flags_the_link()
    {
        if (Fixture.Read(WorldSectorFragmentTests.Sector56) is not { } sector) return;

        (WorldEditSession session, _, List<WorldEntity> entities) = Load(sector);
        EntityLinks.Add(session.EditableNode(entities[0]), new EntityLink("OnStateChange", entities[1].Id, "CLightEvent", "ActivateLight"));
        session.Edited(entities[0]);
        session.Saved();
        Assert.DoesNotContain(Run(session), f => f.Kind == "Broken link");

        session.Delete(entities[1]);

        Assert.Single(Run(session), f => f.Kind == "Broken link" && f.Entity == entities[0]);
    }

    [Fact]
    public void Deleting_a_prefab_member_flags_the_prefab()
    {
        if (Fixture.Read(WorldSectorFragmentTests.Sector56) is not { } sector) return;

        (WorldEditSession session, _, List<WorldEntity> entities) = Load(sector);
        EntityGroups.SetChildren(session.EditableNode(entities[0]), [new PrefabChild(entities[1].Name, entities[1].Id)]);
        session.Edited(entities[0]);
        session.Saved();

        session.Delete(entities[1]);

        Assert.Single(Run(session), f => f.Kind == "Missing prefab member" && f.Entity == entities[0]);
    }

    private static IReadOnlyList<WorldFinding> Run(WorldEditSession session, bool hasNavMesh = true)
        => WorldLint.Run(session, NoLibrary, _ => hasNavMesh, _ => new HashSet<string>());
}
