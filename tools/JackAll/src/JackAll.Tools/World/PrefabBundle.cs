using System.Globalization;
using System.Numerics;
using System.Xml.Linq;
using JackAll.Core.Format.Fcb;

namespace JackAll.Tools.World;

/// <summary>
/// A copied prefab kept as a file, so it can be pasted into any world later: the prefab's node, then
/// each member's node with where it stood relative to the prefab. Nodes are written without class
/// names, which keeps the file independent of the class definitions it was saved under.
/// </summary>
public static class PrefabBundle
{
    public const string Extension = ".jackprefab";

    public static string Write(CopiedEntity copy)
        => new XElement("prefab",
            new XAttribute("world", copy.SourceWorld),
            new XElement("root", XElement.Parse(FcbXml.ToXml(copy.Node, FcbClassDefinitions.Empty))),
            copy.Members.Select(m => new XElement("member",
                new XAttribute("x", Number(m.Offset.X)), new XAttribute("y", Number(m.Offset.Y)), new XAttribute("z", Number(m.Offset.Z)),
                XElement.Parse(FcbXml.ToXml(m.Node, FcbClassDefinitions.Empty)))))
            .ToString();

    /// <summary>The copy a bundle holds. Its members come from no loaded entity, so they draw as
    /// markers until a save and reload bakes them.</summary>
    public static CopiedEntity Read(string xml)
    {
        XElement prefab = XElement.Parse(xml);
        XElement root = prefab.Element("root")?.Elements().SingleOrDefault()
            ?? throw new InvalidDataException("The bundle holds no prefab entity.");
        return new CopiedEntity(FcbXml.FromXml(root.ToString()), (string?)prefab.Attribute("world") ?? "")
        {
            Members = [.. prefab.Elements("member").Select(m => new CopiedMember(
                FcbXml.FromXml(m.Elements().Single().ToString()),
                new Vector3(Parse(m, "x"), Parse(m, "y"), Parse(m, "z")),
                null))],
        };
    }

    private static string Number(float value) => value.ToString("R", CultureInfo.InvariantCulture);

    private static float Parse(XElement element, string name)
        => float.Parse((string?)element.Attribute(name) ?? "0", CultureInfo.InvariantCulture);
}
