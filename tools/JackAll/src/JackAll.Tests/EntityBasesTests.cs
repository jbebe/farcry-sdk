using JackAll.Core.Format.Fcb;
using JackAll.Tools.Sav;
using JackAll.Tools.World;

namespace JackAll.Tests;

/// <summary>What a document's entities merge over: a data file's archetypes, a save's placed entities.</summary>
public class EntityBasesTests
{
    private static readonly uint Health = FcbClassDefinitions.Crc32Ascii("fHealth");
    private static readonly uint Mesh = FcbClassDefinitions.Crc32Ascii("text_objModel");

    private static FcbObject Entity(params (uint Hash, byte[] Value)[] values)
    {
        var node = new FcbObject { TypeHash = WorldHashes.Entity };
        foreach ((uint hash, byte[] value) in values)
        {
            node.Values[hash] = value;
        }
        return node;
    }

    private static FcbObject Record(ulong id, FcbObject state)
    {
        var record = new FcbObject { TypeHash = PersistenceHashes.Record };
        record.Values[PersistenceHashes.Id] = BitConverter.GetBytes(id);
        record.Children.Add(state);
        return record;
    }

    [Fact]
    public void A_placed_entity_merges_over_the_archetype_it_names()
    {
        FcbObject archetype = Entity((Health, [1]));
        FcbObject placed = Entity((WorldHashes.TplCreatureType, FcbEntityFields.StringBytes("npc.guard")));
        var bases = new ArchetypeBases(name => name == "npc.guard" ? archetype : null);

        (FcbObject? found, string details) = bases.BaseOf(placed, null);

        Assert.Same(archetype, found);
        Assert.Equal("archetype npc.guard", details);
        Assert.Null(new ArchetypeBases(null).BaseOf(placed, null).Base);
    }

    /// <summary>A record's state reads over the placed entity with its id, which reads over its archetype.</summary>
    [Fact]
    public void A_saved_state_stacks_on_the_placed_entity_and_its_archetype()
    {
        FcbObject archetype = Entity((Health, [1]), (Mesh, [2]));
        var placed = new WorldEntity
        {
            Node = Entity((Health, [5])),
            HomeSector = new WorldSectorDocument { SourcePath = "", SectorId = 0, PristineRoot = new FcbObject() },
            LayerPathId = "",
            Id = 42,
            Name = "guard",
            ArchetypeName = "npc.guard",
        };
        var state = new FcbObject { TypeHash = PersistenceHashes.State };
        state.Values[Health] = [9];
        FcbObject record = Record(42, state);
        var bases = new SaveGameBases("world1", id => id == 42 ? placed : null, name => name == "npc.guard" ? archetype : null);

        Assert.True(bases.IsEntity(state, record));
        (FcbObject? found, string details) = bases.BaseOf(state, record);
        MergedNode stacked = MergedNode.Of(state, found);

        Assert.Equal(
            [(Health, FieldOrigin.Overridden, (byte)9), (Mesh, FieldOrigin.Inherited, (byte)2)],
            stacked.Fields.Select(f => (f.Hash, f.Origin, f.Value[0])));
        Assert.Equal(5, stacked.Fields[0].BaseValue![0]);
        Assert.StartsWith("persisted over guard", details);
        Assert.Null(bases.BaseOf(state, Record(7, state)).Base);
        Assert.False(bases.IsEntity(state, null));
    }
}
