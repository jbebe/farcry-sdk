using System.Xml.Linq;
using JackAll.Core.Legacy;

namespace JackAll.Tests;

/// <summary>
/// The diff and partial merge a legacy-mod pick rests on, at the plain XML and text level: whatever
/// subset of changes is taken, the result must be the base with exactly those changes on it.
/// </summary>
public class LegacyDiffTests
{
    private static readonly XElement Weapon = XElement.Parse("""
        <object hash="1">
          <value name="Name" type="String">AK47</value>
          <object type="Damage">
            <value name="fDamage" type="Float">10</value>
            <value name="fRange" type="Float">50</value>
          </object>
          <object type="Slot"><value name="id" type="Int32">1</value></object>
          <object type="Slot"><value name="id" type="Int32">2</value></object>
          <object type="Slot"><value name="id" type="Int32">3</value></object>
        </object>
        """);

    private static XElement Modded(Action<XElement> edit)
    {
        var copy = new XElement(Weapon);
        edit(copy);
        return copy;
    }

    private static XElement Value(XElement root, string name) => root.Descendants("value").First(v => (string?)v.Attribute("name") == name);

    [Fact]
    public void Taking_every_change_rebuilds_the_mod_and_taking_none_leaves_the_base()
    {
        XElement mod = Modded(w =>
        {
            Value(w, "fDamage").Value = "20";
            Value(w, "fRange").Value = "80";
        });

        (List<XmlChange> changes, bool exact) = XmlTreeDiff.Diff(Weapon, mod);

        Assert.True(exact);
        Assert.Equal(["Damage/fDamage", "Damage/fRange"], changes.Select(c => c.Path));
        Assert.Equal(XmlTreeDiff.Canonical(mod), XmlTreeDiff.Canonical(XmlTreeDiff.Merge(Weapon, mod, _ => true)));
        Assert.Equal(XmlTreeDiff.Canonical(Weapon), XmlTreeDiff.Canonical(XmlTreeDiff.Merge(Weapon, mod, _ => false)));
    }

    [Fact]
    public void A_partial_merge_applies_only_the_taken_change()
    {
        XElement mod = Modded(w =>
        {
            Value(w, "fDamage").Value = "20";
            Value(w, "fRange").Value = "80";
        });

        XElement merged = XmlTreeDiff.Merge(Weapon, mod, path => path == "Damage/fRange");

        Assert.Equal("10", Value(merged, "fDamage").Value);
        Assert.Equal("80", Value(merged, "fRange").Value);
    }

    [Fact]
    public void An_entry_inserted_into_a_list_is_one_addition_rather_than_every_later_entry_changing()
    {
        XElement mod = Modded(w => w.Elements("object").Skip(1).First()
            .AddAfterSelf(XElement.Parse("""<object type="Slot"><value name="id" type="Int32">9</value></object>""")));

        (List<XmlChange> changes, bool exact) = XmlTreeDiff.Diff(Weapon, mod);

        Assert.True(exact);
        XmlChange added = Assert.Single(changes);
        Assert.Equal(ChangeKind.Add, added.Kind);
        Assert.Equal("Slot[+1]", added.Path);
        Assert.Equal(
            ["1", "9", "2", "3"],
            XmlTreeDiff.Merge(Weapon, mod, _ => true).Elements("object").Where(o => (string?)o.Attribute("type") == "Slot")
                .Select(o => o.Element("value")!.Value));
    }

    [Fact]
    public void An_empty_base_takes_each_child_as_its_own_addition()
    {
        var empty = new XElement("layout");
        XElement layout = XElement.Parse("""
            <layout>
              <remove path="missions\a" />
              <layer path="missions\b"><entity id="7" /></layer>
            </layout>
            """);

        (List<XmlChange> changes, _) = XmlTreeDiff.Diff(empty, layout);
        XElement merged = XmlTreeDiff.Merge(empty, layout, path => path.StartsWith("layer", StringComparison.Ordinal));

        Assert.Equal(["remove[missions\\a]", "layer[missions\\b]"], changes.Select(c => c.Path));
        Assert.Equal("layer", Assert.Single(merged.Elements()).Name.LocalName);
    }

    [Fact]
    public void A_nested_document_is_compared_decoded_and_left_encoded_unless_its_change_is_taken()
    {
        static XElement? Decode(XElement leaf) => leaf.Value.StartsWith("doc:", StringComparison.Ordinal)
            ? XElement.Parse(leaf.Value[4..].Replace('\'', '"'))
            : null;
        XElement vanilla = XElement.Parse("""<root><value name="d" type="Rml">doc:&lt;d a='1' b='2' /&gt;</value></root>""");
        XElement reencoded = XElement.Parse("""<root><value name="d" type="Rml">doc:&lt;d b='2' a='1' /&gt;</value></root>""");
        XElement changed = XElement.Parse("""<root><value name="d" type="Rml">doc:&lt;d a='5' b='2' /&gt;</value></root>""");

        Assert.Empty(XmlTreeDiff.Diff(vanilla, reencoded, Decode).Changes);
        Assert.Equal("d/d@a", Assert.Single(XmlTreeDiff.Diff(vanilla, changed, Decode).Changes).Path);
        Assert.Equal(vanilla.ToString(), XmlTreeDiff.Merge(vanilla, changed, _ => false, Decode).ToString());
        Assert.Equal("5", (string?)XmlTreeDiff.Merge(vanilla, changed, _ => true, Decode).Descendants("d").Single().Attribute("a"));
    }

    [Fact]
    public void Text_hunks_reproduce_either_side_byte_for_byte_and_apply_one_at_a_time()
    {
        const string vanilla = "a\r\nb\r\nc\r\nd\r\ne";
        const string mod = "a\r\nB\r\nc\r\nd\r\nE";

        List<TextHunk> hunks = TextHunks.Diff(vanilla, mod);

        Assert.Equal([2, 5], hunks.Select(h => h.Line));
        Assert.Equal(mod, TextHunks.Merge(vanilla, mod, _ => true));
        Assert.Equal(vanilla, TextHunks.Merge(vanilla, mod, _ => false));
        Assert.Equal("a\r\nb\r\nc\r\nd\r\nE", TextHunks.Merge(vanilla, mod, line => line == 5));
    }

    [Theory]
    [InlineData("worlds/**/entitylibrary.fcb/**", "worlds/world1/generated/entitylibrary.fcb/weapons/ak47.xml#fCost", true)]
    [InlineData("**/entitylibrary.fcb/**", "entitylibrary.fcb/a.xml", true)]
    [InlineData("worlds/*/entitylibrary.fcb/**", "worlds/world1/generated/entitylibrary.fcb/a.xml", false)]
    [InlineData("worlds/{world1,world2}/**#**/fCost", "worlds/world2/x.fcb/a.xml#Entity/Shop/fCost", true)]
    [InlineData("install/bin/dunia.dll@*", "install/bin/Dunia.dll@0x488f3", true)]
    public void Address_globs_keep_a_single_star_inside_one_segment(string glob, string address, bool matches)
        => Assert.Equal(matches, AddressGlob.Compile(glob).IsMatch(address));
}
