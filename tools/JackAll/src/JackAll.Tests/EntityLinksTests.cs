using System.Xml.Linq;
using JackAll.Core;
using JackAll.Core.Format.Fcb;
using JackAll.Tools.World;

namespace JackAll.Tests;

/// <summary>Event links and prefab members, written and read back, and read off a retail sector.</summary>
public class EntityLinksTests
{
    [Fact]
    public void A_link_added_to_an_entity_without_events_reads_back_as_written()
    {
        var entity = new FcbObject { TypeHash = WorldHashes.Entity };
        var link = new EntityLink("OnStateChange", 42, "CLightEvent", "DeactivateLight");

        EntityLinks.Add(entity, link);

        Assert.Equal([link], EntityLinks.Read(entity));
    }

    [Fact]
    public void Removing_a_link_leaves_the_others_in_order()
    {
        var entity = new FcbObject { TypeHash = WorldHashes.Entity };
        EntityLinks.Add(entity, new EntityLink("A", 1, "CSoundEvent", "PlaySound"));
        EntityLinks.Add(entity, new EntityLink("B", 2, "CSoundEvent", "PlaySound"));
        EntityLinks.Add(entity, new EntityLink("C", 3, "CSoundEvent", "PlaySound"));

        EntityLinks.RemoveAt(entity, 1);

        Assert.Equal(["A", "C"], EntityLinks.Read(entity).Select(l => l.Output));
    }

    [Fact]
    public void Retargeting_rewrites_both_copies_of_the_target_id()
    {
        var entity = new FcbObject { TypeHash = WorldHashes.Entity };
        EntityLinks.Add(entity, new EntityLink("A", 1, "CLightEvent", "ActivateLight"));

        EntityLinks.Retarget(entity, new Dictionary<ulong, ulong> { [1] = 9 });

        FcbObject link = FcbEntityFields.FindComponent(entity, WorldHashes.CEventComponent)!.Children[0].Children[0];
        Assert.Equal(9ul, EntityLinks.Read(entity)[0].TargetId);
        Assert.Equal(9ul, BitConverter.ToUInt64(link.Children[0].Values[0xDCC35857]));
    }

    [Fact]
    public void Prefab_members_read_back_as_written()
    {
        var entity = new FcbObject { TypeHash = WorldHashes.Entity };
        PrefabChild[] members = [new("Crate_1", 5), new("Crate_2", 6)];

        EntityGroups.SetChildren(entity, members);

        Assert.True(EntityGroups.IsPrefab(entity));
        Assert.Equal(members, EntityGroups.ChildrenOf(entity));
    }

    /// <summary>The link documented in entity-instancing.md: a light switched off on a state change.</summary>
    [Fact]
    public void A_retail_link_and_its_prefab_read_as_documented()
    {
        if (Fixture.Read(WorldSectorFragmentTests.Sector3859) is not { } bytes) return;

        List<FcbObject> entities = [.. FcbDocument.Deserialize(bytes).Children
            .SelectMany(layer => layer.Children).Where(e => e.TypeHash == WorldHashes.Entity)];

        EntityLink link = Assert.Single(entities.SelectMany(EntityLinks.Read),
            l => l.TargetId == 2058516086820713175 && l.EventName == "DeactivateLight");
        Assert.Equal(("OnStateChange", "CLightEvent"), (link.Output, link.EventClass));
        Assert.Contains(entities.SelectMany(EntityGroups.ChildrenOf), c => c is { Name: "SpotLight_646", Id: 2058516086820713175 });
    }

    /// <summary>Each event's fields come from the class its tag names, so a light's <c>hidType</c> resolves.</summary>
    [Fact]
    public void Every_field_of_a_retail_link_decodes_by_name()
    {
        if (Fixture.Read(WorldSectorFragmentTests.Sector3859) is not { } bytes) return;

        XElement sector = XElement.Parse(FcbXml.ToXml(FcbDocument.Deserialize(bytes), BundledAssets.LoadFcbClasses()));

        List<XElement> links = [.. sector.Descendants("object").Where(o => (string?)o.Attribute("type") == "Link")];
        Assert.NotEmpty(links);
        Assert.All(links.Descendants("value"), v => Assert.NotNull(v.Attribute("name")));
        Assert.Contains(links.Descendants("value"), v => (string?)v.Attribute("name") == "hidType");
    }

    /// <summary>A grouped prefab is built with the class fields every retail one carries.</summary>
    [Fact]
    public void Every_retail_prefab_names_its_class_as_a_new_group_does()
    {
        if (Fixture.Read(WorldSectorFragmentTests.Sector3859) is not { } bytes) return;

        List<FcbObject> prefabs = [.. FcbDocument.Deserialize(bytes).Children
            .SelectMany(layer => layer.Children).Where(EntityGroups.IsPrefab)];

        Assert.NotEmpty(prefabs);
        Assert.All(prefabs, p =>
        {
            Assert.StartsWith("C", FcbEntityFields.ReadString(p, WorldHashes.TextHidEntityClass));
            Assert.Equal(FcbClassDefinitions.Crc32Ascii(FcbEntityFields.ReadString(p, WorldHashes.TextHidEntityClass)),
                FcbEntityFields.ReadU32(p, WorldHashes.HidEntityClass));
        });
    }
}
