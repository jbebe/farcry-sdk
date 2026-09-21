using JackAll.Core.Format.Fcb;
using JackAll.Tools.World;

namespace JackAll.Tests;

/// <summary>
/// The engine's instance-over-archetype merge as the Map tab reads and writes it: children paired by
/// tag in order, the instance winning every field it has, and edits that keep the pairing intact.
/// </summary>
public class EntityMergeTests
{
    private static readonly uint A = FcbClassDefinitions.Crc32Ascii("a");
    private static readonly uint B = FcbClassDefinitions.Crc32Ascii("b");
    private static readonly uint C = FcbClassDefinitions.Crc32Ascii("c");
    private static readonly uint X = FcbClassDefinitions.Crc32Ascii("x");
    private static readonly uint Slot = FcbClassDefinitions.Crc32Ascii("object");
    private static readonly uint Other = FcbClassDefinitions.Crc32Ascii("Other");
    private static readonly uint Extra = FcbClassDefinitions.Crc32Ascii("Extra");

    private static FcbObject Node(uint type, params (uint Hash, byte Value)[] values)
    {
        var node = new FcbObject { TypeHash = type };
        foreach ((uint hash, byte value) in values)
        {
            node.Values[hash] = [value];
        }
        return node;
    }

    /// <summary>An archetype with two same-tag slots and one other child, as a library graphic
    /// component with two mesh slots has.</summary>
    private static FcbObject Archetype()
    {
        FcbObject archetype = Node(WorldHashes.Entity, (A, 1), (B, 2));
        archetype.Children.Add(Node(Slot, (X, 10)));
        archetype.Children.Add(Node(Slot, (X, 20)));
        archetype.Children.Add(Node(Other, (X, 30)));
        return archetype;
    }

    [Fact]
    public void An_added_child_materializes_the_inherited_parent_and_can_be_removed_again()
    {
        FcbObject instance = Node(WorldHashes.Entity);
        MergedNode merged = MergedNode.Of(instance, Archetype());
        MergedNode other = merged.Children[2];

        MergedNode added = other.AddChild(Node(Extra, (X, 5)));

        FcbObject materialized = Assert.Single(instance.Children);
        Assert.Equal(Other, materialized.TypeHash);
        Assert.Same(added.Instance, Assert.Single(materialized.Children));
        Assert.Same(added, other.Children.Last());

        other.RemoveChild(added);

        Assert.Empty(materialized.Children);
        Assert.DoesNotContain(added, other.Children);
    }

    [Fact]
    public void A_child_the_archetype_has_can_be_neither_added_again_nor_removed()
    {
        MergedNode merged = MergedNode.Of(Node(WorldHashes.Entity), Archetype());

        Assert.Throws<InvalidOperationException>(() => merged.AddChild(Node(Other)));
        Assert.Throws<InvalidOperationException>(() => merged.RemoveChild(merged.Children[2]));
    }

    [Fact]
    public void The_instance_wins_a_shared_field_and_inherits_the_rest()
    {
        FcbObject instance = Node(WorldHashes.Entity, (A, 9), (C, 3));
        MergedNode merged = MergedNode.Of(instance, Archetype());

        Assert.Equal(
            [(A, FieldOrigin.Overridden, (byte)9), (C, FieldOrigin.InstanceOnly, (byte)3), (B, FieldOrigin.Inherited, (byte)2)],
            merged.Fields.Select(f => (f.Hash, f.Origin, f.Value[0])));
        Assert.Equal(1, merged.Fields[0].ArchetypeValue![0]);
    }

    [Fact]
    public void Children_pair_by_tag_in_order_and_extras_are_appended()
    {
        FcbObject instance = Node(WorldHashes.Entity);
        instance.Children.Add(Node(Extra, (X, 99)));
        instance.Children.Add(Node(Slot, (X, 11)));
        MergedNode merged = MergedNode.Of(instance, Archetype());

        Assert.Equal([Slot, Slot, Other, Extra], merged.Children.Select(c => c.TypeHash));
        Assert.Equal(11, merged.Children[0].Fields.Single().Value[0]);
        Assert.Equal(FieldOrigin.Overridden, merged.Children[0].Fields.Single().Origin);
        Assert.Null(merged.Children[1].Instance);
        Assert.Null(merged.Children[3].Archetype);
    }

    /// <summary>A standalone entity has no archetype, so everything on it is its own.</summary>
    [Fact]
    public void A_standalone_entity_reads_as_all_its_own()
    {
        FcbObject instance = Node(WorldHashes.Entity, (A, 1));
        instance.Children.Add(Node(Slot, (X, 5)));
        MergedNode merged = MergedNode.Of(instance, null);

        Assert.All(merged.Fields, f => Assert.Equal(FieldOrigin.InstanceOnly, f.Origin));
        Assert.Equal(FieldOrigin.InstanceOnly, merged.Children.Single().Fields.Single().Origin);
    }

    /// <summary>
    /// Overriding a field in the second slot has to create an instance node for the first slot as
    /// well: pairing is positional within the tag, so a lone new node would pair with the first.
    /// </summary>
    [Fact]
    public void Overriding_the_second_slot_keeps_it_paired_with_the_second_slot()
    {
        FcbObject archetype = Archetype();
        FcbObject instance = Node(WorldHashes.Entity);
        MergedNode.Of(instance, archetype).Children[1].SetValue(X, [77]);

        Assert.Equal([0, 1], instance.Children.Select(c => c.Values.Count));
        MergedNode reread = MergedNode.Of(instance, archetype);
        Assert.Equal(10, reread.Children[0].Fields.Single().Value[0]);
        Assert.Equal(77, reread.Children[1].Fields.Single().Value[0]);
        Assert.Equal(FieldOrigin.Overridden, reread.Children[1].Fields.Single().Origin);
    }

    [Fact]
    public void An_override_survives_a_round_trip_through_xml()
    {
        FcbObject archetype = Archetype();
        FcbObject instance = Node(WorldHashes.Entity, (A, 9));
        MergedNode.Of(instance, archetype).Children[1].SetValue(X, [77]);

        FcbObject reread = FcbXml.FromXml(FcbXml.ToXml(instance, FcbClassDefinitions.Empty));
        Assert.Equal(77, MergedNode.Of(reread, archetype).Children[1].Fields.Single().Value[0]);
    }

    /// <summary>Reverting the only override under a slot takes the instance back to what it was,
    /// placeholder siblings included.</summary>
    [Fact]
    public void Reverting_the_last_override_removes_every_node_it_created()
    {
        FcbObject archetype = Archetype();
        FcbObject instance = Node(WorldHashes.Entity, (A, 9));
        MergedNode merged = MergedNode.Of(instance, archetype);
        merged.Children[1].SetValue(X, [77]);

        merged.Children[1].Revert(X);

        Assert.Empty(instance.Children);
        Assert.Null(merged.Children[0].Instance);
        Assert.Equal(FieldOrigin.Inherited, merged.Children[1].Fields.Single().Origin);
        Assert.Equal(9, instance.Values[A][0]);
    }

    /// <summary>An empty node ahead of a real override holds that override's pairing, so it stays.</summary>
    [Fact]
    public void Reverting_the_first_slot_keeps_the_placeholder_the_second_needs()
    {
        FcbObject archetype = Archetype();
        FcbObject instance = Node(WorldHashes.Entity);
        MergedNode merged = MergedNode.Of(instance, archetype);
        merged.Children[0].SetValue(X, [55]);
        merged.Children[1].SetValue(X, [77]);

        merged.Children[0].Revert(X);

        Assert.Equal(2, instance.Children.Count);
        Assert.Equal(77, MergedNode.Of(instance, archetype).Children[1].Fields.Single().Value[0]);
    }

    [Fact]
    public void Reverting_a_field_only_the_archetype_has_changes_nothing()
    {
        FcbObject instance = Node(WorldHashes.Entity, (A, 9));
        MergedNode.Of(instance, Archetype()).Revert(B);

        Assert.Equal([A], instance.Values.Keys);
    }

    private static string SectorPath => Path.Combine(
        Fc2Corpus.Root, @"worlds\worlds\levels\w1_b_2\generated\worldsectors\worldsector4027.data.fcb");

    private static string LibraryPath => Path.Combine(
        Fc2Corpus.Root, @"worlds\worlds\worlds\world1\generated\entitylibrary.fcb");

    [Fact]
    [Trait("Category", "RequiresFixture")]
    public void The_retail_sector_and_library_were_actually_found()
    {
        Assert.True(File.Exists(SectorPath), $"{SectorPath} was not found - the retail merge test silently no-opped.");
        Assert.True(File.Exists(LibraryPath), $"{LibraryPath} was not found - the retail merge test silently no-opped.");
    }

    /// <summary>
    /// Every archetype-bound entity in a retail outpost sector merges over its world1 archetype: its
    /// own fields read as its own, everything else as inherited, and nothing is lost either way.
    /// </summary>
    [Fact]
    [Trait("Category", "RequiresFixture")]
    public void Every_retail_instance_merges_over_its_archetype()
    {
        if (!File.Exists(SectorPath) || !File.Exists(LibraryPath)) return;

        ArchetypeIndex index = ArchetypeIndex.Load(
            [new ArchetypeLayer(@"worlds\world1\generated\entitylibrary.fcb")], _ => File.ReadAllBytes(LibraryPath));
        FcbObject sector = FcbDocument.Deserialize(File.ReadAllBytes(SectorPath));

        int merged = 0;
        foreach (FcbObject instance in sector.Children.SelectMany(layer => layer.Children))
        {
            string archetypeName = FcbEntityFields.ReadString(instance, WorldHashes.TplCreatureType);
            if (archetypeName.Length == 0)
            {
                continue;
            }

            FcbObject archetype = index.Winner(archetypeName)?.Node
                ?? throw new Xunit.Sdk.XunitException($"{archetypeName} is not in world1's library.");
            AssertCovers(MergedNode.Of(instance, archetype));
            merged++;
        }
        Assert.True(merged > 0);
    }

    private static void AssertCovers(MergedNode node)
    {
        var fields = node.Fields.ToDictionary(f => f.Hash);
        foreach ((uint hash, byte[] value) in node.Instance?.Values ?? [])
        {
            Assert.Equal(value, fields[hash].Value);
            Assert.NotEqual(FieldOrigin.Inherited, fields[hash].Origin);
        }
        foreach ((uint hash, byte[] value) in node.Archetype?.Values ?? [])
        {
            Assert.Equal(value, fields[hash].ArchetypeValue);
        }
        Assert.Equal(
            (node.Instance?.Children.Count ?? 0) + (node.Archetype?.Children.Count ?? 0),
            node.Children.Sum(c => (c.Instance is null ? 0 : 1) + (c.Archetype is null ? 0 : 1)));
        foreach (MergedNode child in node.Children)
        {
            AssertCovers(child);
        }
    }
}
