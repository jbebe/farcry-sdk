using JackAll.Core.Format.Move;

namespace JackAll.Tests;

/// <summary>
/// The addressing layer a MOVE fragment is built on: every object has a name that does not mention
/// where it sits in the file, and that name resolves back to it.
/// </summary>
public sealed class MoveStateIndexTests
{
    public static TheoryData<string> CorpusFiles()
    {
        TheoryData<string> data = [];
        foreach (string path in Fc2Corpus.Find(".bin").Where(p =>
            Path.GetDirectoryName(p)?.EndsWith("move", StringComparison.OrdinalIgnoreCase) == true
            && !Path.GetFileNameWithoutExtension(p)
                .EndsWith("named", StringComparison.OrdinalIgnoreCase)))
        {
            data.Add(path);
        }

        if (data.Count == 0)
        {
            data.Add(string.Empty);
        }

        return data;
    }

    /// <summary>
    /// The identity gate the split turns on: a fragment is keyed by <c>m_stateNameHash</c>, so every
    /// listed state must have one and no two may share it.
    /// </summary>
    [Theory]
    [MemberData(nameof(CorpusFiles))]
    public void Every_state_has_a_distinct_name_hash(string path)
    {
        if (path.Length == 0) return;

        MoveStateIndex index = MoveStateIndex.Build(MoveCodec.Load(File.ReadAllBytes(path)));

        List<uint?> hashes = [.. index.Slots.Select(MoveStateIndex.NameHashOf)];
        Assert.All(hashes, h => Assert.NotNull(h));
        Assert.Equal(hashes.Count, hashes.Distinct().Count());
    }

    /// <summary>
    /// <c>nbState</c> counts slots, not states. Deriving it from the number of distinct states would
    /// emit 1,687 for <c>movemgr.bin</c> and corrupt the file.
    /// </summary>
    [Theory]
    [MemberData(nameof(CorpusFiles))]
    public void Nested_states_hold_a_slot_without_owning_a_fragment(string path)
    {
        if (path.Length == 0) return;

        MoveStateIndex index = MoveStateIndex.Build(MoveCodec.Load(File.ReadAllBytes(path)));

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
    [MemberData(nameof(CorpusFiles))]
    public void Every_object_addresses_and_resolves_back_to_itself(string path)
    {
        if (path.Length == 0) return;

        MoveFile file = MoveCodec.Load(File.ReadAllBytes(path));
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
    [MemberData(nameof(CorpusFiles))]
    public void References_that_leave_a_state_are_addressable(string path)
    {
        if (path.Length == 0) return;

        MoveFile file = MoveCodec.Load(File.ReadAllBytes(path));
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

        Assert.True(crossing > 0, "the corpus is expected to cross state boundaries");
    }
}
