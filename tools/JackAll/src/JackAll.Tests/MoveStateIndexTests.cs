using JackAll.Core.Format.Move;

namespace JackAll.Tests;

/// <summary>
/// The addressing layer a MOVE fragment is built on: every object has a name that does not mention
/// where it sits in the file, and that name resolves back to it.
/// </summary>
public sealed class MoveStateIndexTests
{
    public const string Manager = "Move/movemgr.bin";
    public const string Dlc = "Move/dlc1.bin";

    /// <summary>The base game's graph with its names kept: the only source of the channel table.</summary>
    public const string Named = "Move/movemgrnamed.bin";

    /// <summary>The two kinds of loadable graph: the base game's, and an expansion's that has no
    /// manager of its own. The named twin is the authoring form - only ~90% decoded, and the engine
    /// refuses it - so it is not one of them.</summary>
    public static TheoryData<string> Graphs => new() { Manager, Dlc };

    [Fact]
    [Trait("Category", "RequiresFixture")]
    public void The_fixtures_were_actually_found() => Fixture.AssertPresent(Manager, Dlc, Named);

    /// <summary>
    /// The identity the split turns on: a fragment is keyed by <c>m_stateNameHash</c>, so every
    /// listed state must have one and no two may share it.
    /// </summary>
    [Theory]
    [MemberData(nameof(Graphs))]
    public void Every_state_has_a_distinct_name_hash(string path)
    {
        if (Fixture.Read(path) is not { } graph) return;

        MoveStateIndex index = MoveStateIndex.Build(MoveCodec.Load(graph));

        List<uint?> hashes = [.. index.Slots.Select(MoveStateIndex.NameHashOf)];
        Assert.All(hashes, h => Assert.NotNull(h));
        Assert.Equal(hashes.Count, hashes.Distinct().Count());
    }

    /// <summary>
    /// <c>nbState</c> counts slots, not states. Deriving it from the number of distinct states would
    /// emit 1,687 for <c>movemgr.bin</c> and corrupt the file.
    /// </summary>
    [Theory]
    [MemberData(nameof(Graphs))]
    public void Nested_states_hold_a_slot_without_owning_a_fragment(string path)
    {
        if (Fixture.Read(path) is not { } graph) return;

        MoveStateIndex index = MoveStateIndex.Build(MoveCodec.Load(graph));

        int topLevel = index.TopLevelStates.Count();
        Assert.Equal((uint)index.Slots.Count, index.StateMachine.Field("nbState"));
        Assert.Equal(index.Slots.Count - topLevel, index.Slots.Count(index.IsNested));
        Assert.True(topLevel <= index.Slots.Count);
    }

    /// <summary>
    /// Every object is either the manager's own scaffolding or reachable by a route that names no
    /// file offset - which is what lets a fragment reference across its own boundary.
    /// </summary>
    [Theory]
    [MemberData(nameof(Graphs))]
    public void Every_object_addresses_and_resolves_back_to_itself(string path)
    {
        if (Fixture.Read(path) is not { } graph) return;

        MoveFile file = MoveCodec.Load(graph);
        MoveStateIndex index = MoveStateIndex.Build(file);

        int addressed = 0;
        foreach (MoveObject obj in file.Objects)
        {
            if (index.AddressOf(obj) is not { } address)
            {
                // Only the manager's scaffolding has no owning state.
                Assert.Null(index.StateOf(obj));
                continue;
            }

            Assert.Same(obj, index.Resolve(address));
            addressed++;
        }

        Assert.True(addressed > 0);
        // The scaffolding is a handful of objects; everything else belongs to a state.
        Assert.True(file.Objects.Count - addressed < 32);
    }

    /// <summary>
    /// The measurement that killed the simple <c>&lt;xref state="hash"/&gt;</c> form: most references
    /// that leave a state land deep inside another one, not on its root.
    /// </summary>
    [Theory]
    [MemberData(nameof(Graphs))]
    public void References_that_leave_a_state_are_addressable(string path)
    {
        if (Fixture.Read(path) is not { } graph) return;

        MoveFile file = MoveCodec.Load(graph);
        MoveStateIndex index = MoveStateIndex.Build(file);

        int crossing = 0;
        foreach (MoveObject obj in file.Objects)
        {
            MoveObject? from = index.StateOf(obj);
            foreach (MoveOp op in obj.Ops)
            {
                if (op.Kind != MoveOpKind.PointerRef || index.StateOf(op.Target!) == from)
                {
                    continue;
                }

                crossing++;
                MoveAddress address = index.AddressOf(op.Target!)
                    ?? throw new InvalidOperationException($"unaddressable target in {obj.ClassName}");
                Assert.Same(op.Target, index.Resolve(address));
            }
        }

        Assert.True(crossing > 0, "the graph is expected to cross state boundaries");
    }
}
