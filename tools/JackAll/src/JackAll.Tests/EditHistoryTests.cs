using System.Numerics;
using JackAll.Core.Format.Fcb;
using JackAll.Tools.World;
using static JackAll.Tests.WorldEditSessionTests;

namespace JackAll.Tests;

/// <summary>The Map tab's undo and redo, over the same retail sector the session tests use.</summary>
[Trait("Category", "RequiresFixture")]
public class EditHistoryTests
{
    private static readonly Vector3 InSector = new(400f, 350f, 12f);
    private static readonly uint Field = FcbClassDefinitions.Crc32Ascii("fEditedByTest");

    [Fact]
    public void Undoing_a_move_puts_the_entity_back_and_redo_moves_it_again()
    {
        if (!File.Exists(FixturePath)) return;

        (WorldEditSession session, _, List<WorldEntity> entities) = Load();
        var history = new EditHistory();
        WorldEntity entity = entities[0];
        Placement before = Placement.Of(entity);
        Move(session, history, entity, InSector);

        history.Undo();
        Assert.Equal(before.Position, entity.Position);
        history.Redo();
        Assert.Equal(InSector, entity.Position);
    }

    [Fact]
    public void A_new_step_clears_what_could_be_redone()
    {
        if (!File.Exists(FixturePath)) return;

        (WorldEditSession session, _, List<WorldEntity> entities) = Load();
        var history = new EditHistory();
        Move(session, history, entities[0], InSector);
        history.Undo();
        Move(session, history, entities[1], InSector);

        Assert.False(history.CanRedo);
    }

    [Fact]
    public void Keystrokes_into_one_field_are_one_step_and_another_field_is_another()
    {
        if (!File.Exists(FixturePath)) return;

        (WorldEditSession session, _, List<WorldEntity> entities) = Load();
        var history = new EditHistory();
        WorldEntity entity = entities[0];
        FcbObject node = session.EditableNode(entity);
        FcbObject pristine = node.Clone();
        node.Values[Field] = BitConverter.GetBytes(1f);
        FcbObject withField = node.Clone();

        Edit(session, history, entity, f => f.Values[Field] = BitConverter.GetBytes(12f));
        Edit(session, history, entity, f => f.Values[Field] = BitConverter.GetBytes(12.5f));
        Edit(session, history, entity, f => f.Values[FcbClassDefinitions.Crc32Ascii("fOther")] = [1, 0, 0, 0]);

        history.Undo();
        Assert.Equal(BitConverter.GetBytes(12.5f), node.Values[Field]);
        history.Undo();
        Assert.Equal(BitConverter.GetBytes(1f), node.Values[Field]);
        Assert.False(history.CanUndo);
        Assert.Equal(withField.Values.Count, node.Values.Count);
        Assert.NotEqual(pristine.Values.Count, node.Values.Count);
    }

    [Fact]
    public void Two_goes_at_the_same_field_a_while_apart_are_two_steps()
    {
        if (!File.Exists(FixturePath)) return;

        (WorldEditSession session, _, List<WorldEntity> entities) = Load();
        DateTime clock = new(2026, 1, 1);
        var history = new EditHistory(() => clock);
        Edit(session, history, entities[0], f => f.Values[Field] = BitConverter.GetBytes(1f));
        Edit(session, history, entities[0], f => f.Values[Field] = BitConverter.GetBytes(2f));
        clock += EditHistory.MergeWindow * 2;
        Edit(session, history, entities[0], f => f.Values[Field] = BitConverter.GetBytes(3f));

        history.Undo();
        Assert.Equal(BitConverter.GetBytes(2f), session.EditableNode(entities[0]).Values[Field]);
    }

    /// <summary>The inspector holds the working node, so undo rewrites that object rather than
    /// swapping in another one.</summary>
    [Fact]
    public void Undoing_a_field_edit_rewrites_the_node_the_inspector_holds()
    {
        if (!File.Exists(FixturePath)) return;

        (WorldEditSession session, _, List<WorldEntity> entities) = Load();
        var history = new EditHistory();
        WorldEntity entity = entities[0];
        FcbObject held = session.EditableNode(entity);
        Edit(session, history, entity, f => f.Values[Field] = [1, 0, 0, 0]);

        history.Undo();

        Assert.Same(held, session.EditableNode(entity));
        Assert.False(held.Values.ContainsKey(Field));
    }

    [Fact]
    public void Undoing_a_delete_restores_the_entity_and_leaves_nothing_pending()
    {
        if (!File.Exists(FixturePath)) return;

        (WorldEditSession session, _, List<WorldEntity> entities) = Load();
        var history = new EditHistory();
        WorldEntity doomed = entities[0];
        history.Push(PresenceStep.Deleted(session, [session.Delete(doomed)]));

        history.Undo();

        Assert.Contains(doomed, session.World.Entities);
        (IReadOnlyList<EntityFragment> fragments, IReadOnlyList<DeletedEntity> deleted) = session.Pending();
        Assert.Empty(fragments);
        Assert.Empty(deleted);
        Assert.False(session.IsDirty);
    }

    [Fact]
    public void Undoing_a_delete_keeps_the_edits_the_entity_had()
    {
        if (!File.Exists(FixturePath)) return;

        (WorldEditSession session, _, List<WorldEntity> entities) = Load();
        var history = new EditHistory();
        WorldEntity entity = entities[0];
        Edit(session, history, entity, f => f.Values[Field] = [1, 0, 0, 0]);
        history.Push(PresenceStep.Deleted(session, [session.Delete(entity)]));

        history.Undo();

        EntityFragment staged = Assert.Single(session.Pending().Fragments);
        Assert.Equal([1, 0, 0, 0], staged.Node.Values[Field]);
    }

    /// <summary>A delete already saved is in the staged layout, so undoing it must restage the entity
    /// and say which delete to take back out.</summary>
    [Fact]
    public void Undoing_a_saved_delete_restages_the_entity()
    {
        if (!File.Exists(FixturePath)) return;

        (WorldEditSession session, _, List<WorldEntity> entities) = Load();
        var history = new EditHistory();
        WorldEntity doomed = entities[0];
        history.Push(PresenceStep.Deleted(session, [session.Delete(doomed)]));
        session.Saved();

        history.Undo();

        Assert.Single(session.Pending().Fragments, f => f.Node.Values[WorldHashes.DisEntityId].SequenceEqual(BitConverter.GetBytes(doomed.Id)));
        Assert.Equal(doomed.Id, Assert.Single(session.Restored).Id);
        history.Redo();
        Assert.Empty(session.Restored);
        Assert.Equal(doomed.Id, Assert.Single(session.Pending().Deleted).Id);
    }

    [Fact]
    public void Undoing_an_add_removes_it_and_redo_brings_it_back_as_new()
    {
        if (!File.Exists(FixturePath)) return;

        (WorldEditSession session, _, List<WorldEntity> entities) = Load();
        var history = new EditHistory();
        WorldEntity pasted = session.Paste(CopiedEntity.Of(entities[1], "mp_14_woodlands"), InSector);
        history.Push(PresenceStep.Added(session, pasted));

        history.Undo();
        Assert.DoesNotContain(pasted, session.World.Entities);
        Assert.False(session.IsDirty);

        history.Redo();
        Assert.Contains(pasted, session.World.Entities);
        Assert.Single(session.Additions);
    }

    /// <summary>Once saved, an added entity is in the workspace, so undoing it deletes it from there.</summary>
    [Fact]
    public void Undoing_a_saved_add_deletes_it()
    {
        if (!File.Exists(FixturePath)) return;

        (WorldEditSession session, _, List<WorldEntity> entities) = Load();
        var history = new EditHistory();
        WorldEntity pasted = session.Paste(CopiedEntity.Of(entities[1], "mp_14_woodlands"), InSector);
        history.Push(PresenceStep.Added(session, pasted));
        session.Saved();

        history.Undo();

        Assert.Equal(pasted.Id, Assert.Single(session.Pending().Deleted).Id);
    }

    [Fact]
    public void Undoing_a_move_after_a_save_restages_the_entity()
    {
        if (!File.Exists(FixturePath)) return;

        (WorldEditSession session, _, List<WorldEntity> entities) = Load();
        var history = new EditHistory();
        Move(session, history, entities[0], InSector);
        session.Saved();

        history.Undo();

        Assert.True(session.IsModified(entities[0]));
    }

    private static void Move(WorldEditSession session, EditHistory history, WorldEntity entity, Vector3 to)
    {
        Placement before = Placement.Of(entity);
        entity.Position = to;
        session.Moved(entity);
        history.Push(new MoveStep(session, new Dictionary<WorldEntity, (Placement, Placement)>
        {
            [entity] = (before, Placement.Of(entity)),
        }));
    }

    private static void Edit(WorldEditSession session, EditHistory history, WorldEntity entity, Action<FcbObject> edit)
    {
        FcbObject node = session.EditableNode(entity);
        FcbObject before = node.Clone();
        edit(node);
        session.Edited(entity);
        history.Push(new NodeEditStep(session, entity, before, node.Clone(), "Edit"));
    }
}
