using System.Xml.Linq;
using JackAll.Tools.Hkx;

namespace JackAll.Tests;

/// <summary>An .hkx survives its XML byte for byte, and what is changed or added in the XML lands in the file.</summary>
public sealed class HkxXmlTests
{
    // A MOPP over a list of six boxes; the list is #6, the seat's translate shape #15.
    private const string Chair = "Hkx/chairbar01.hkx";

    // Stores its own class reflection in __types__, and variants that point into it.
    private const string Ragdoll = "Hkx/generic_ragdoll.hkx";

    // An extended mesh: arrays of vertices, indices and a subpart of boxes.
    private const string Garage = "Hkx/colonialgarage02_ruin.hkx";

    [Fact]
    [Trait("Category", "RequiresFixture")]
    public void The_fixtures_were_actually_found() => Fixture.AssertPresent(Chair, Ragdoll, Garage);

    [Theory]
    [InlineData(Chair)]
    [InlineData(Ragdoll)]
    [InlineData(Garage)]
    public void A_file_comes_back_from_its_xml_byte_for_byte(string fixture)
    {
        if (Fixture.Read(fixture) is not { } bytes)
        {
            return;
        }

        Fixture.AssertSameBytes(fixture, bytes, HkxXml.FromXml(HkxXml.ToXml(HkxFile.Parse(bytes))).Write());
    }

    [Fact]
    public void A_value_changed_in_the_xml_lands_in_the_file()
    {
        if (Fixture.Read(Chair) is not { } bytes)
        {
            return;
        }

        XDocument xml = XDocument.Parse(HkxXml.ToXml(HkxFile.Parse(bytes)));
        Value(Object(xml, 15), "translation").Value = "0.5 0.25 1.5 0";

        HkxFile file = HkxFile.Parse(HkxXml.FromXml(xml.ToString()).Write());

        Assert.Equal([0.5f, 0.25f, 1.5f, 0], file.Objects()[15].GetReals("translation"));
    }

    [Fact]
    public void A_box_added_in_the_xml_becomes_the_lists_seventh_child()
    {
        if (Fixture.Read(Chair) is not { } bytes)
        {
            return;
        }

        XDocument xml = XDocument.Parse(HkxXml.ToXml(HkxFile.Parse(bytes)));
        XElement list = Object(xml, 6);
        XElement item = new(list.Elements("array").Single(e => (string?)e.Attribute("name") == "childInfo").Elements("item").Last());
        item.Elements("value").Single(e => (string?)e.Attribute("name") == "shape").SetAttributeValue("ref", 100);
        list.Elements("array").Single(e => (string?)e.Attribute("name") == "childInfo").Add(item);
        xml.Root!.Add(XElement.Parse("""
            <object id="100" class="hkpBoxShape">
              <value name="userData">12</value>
              <value name="radius">0.01</value>
              <value name="halfExtents">0.1 0.2 0.3 0.01</value>
            </object>
            """));

        HkxFile file = HkxFile.Parse(HkxXml.FromXml(xml.ToString()).Write());
        HkxObject childList = file.Objects().First(o => o.Class.Name == "hkpListShape");

        Assert.Equal(7, childList.Count("childInfo"));
        Assert.Equal([0.1f, 0.2f, 0.3f, 0.01f], childList.GetPointer("childInfo[6].shape")!.GetReals("halfExtents"));
    }

    [Fact]
    public void A_member_the_class_lacks_is_named_in_the_error()
    {
        if (Fixture.Read(Chair) is not { } bytes)
        {
            return;
        }

        XDocument xml = XDocument.Parse(HkxXml.ToXml(HkxFile.Parse(bytes)));
        Object(xml, 16).Add(new XElement("value", new XAttribute("name", "halfExtent"), "1 1 1 0"));

        FormatException error = Assert.Throws<FormatException>(() => HkxXml.FromXml(xml.ToString()));
        Assert.Contains("halfExtent", error.Message);
    }

    private static XElement Object(XDocument xml, int id) => xml.Root!.Elements("object").Single(e => (int)e.Attribute("id")! == id);

    private static XElement Value(XElement o, string name) => o.Elements("value").Single(e => (string?)e.Attribute("name") == name);
}
