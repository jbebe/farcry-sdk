using System.Xml.Linq;

namespace JackAll.Core.Mods;

/// <summary>
/// <c>gamemodesconfig.xml</c>: one fragment per property block of <c>&lt;GameModeProperties&gt;</c>
/// (<c>DefaultMuzzleFlashManager.xml</c>) and one per <c>&lt;GameMode&gt;</c> by its <c>Desc</c> name
/// (<c>GameMode.Single.xml</c>), merged attribute by attribute.
/// </summary>
public sealed class GameModesConfigContainerSplitter : XmlElementSplitter
{
    public const string FileName = "gamemodesconfig.xml";

    private const string PropertiesElement = "GameModeProperties";
    private const string ModeElement = "GameMode";
    private const string ModePrefix = ModeElement + ".";

    /// <summary>The attributes a repeated child is told apart by, in the order they are tried.</summary>
    private static readonly string[] KeyAttributes = ["name", "Name", "ClassName", "id"];

    public static GameModesConfigContainerSplitter Instance { get; } = new();

    public static bool IsGameModesConfig(string path)
        => Path.GetFileName(path).Equals(FileName, StringComparison.OrdinalIgnoreCase);

    /// <summary>A property block's id: its element name, as a file.</summary>
    public static string BlockIdOf(string elementName) => FileIdOf(elementName);

    /// <summary>A game mode's id: its <c>Desc</c> name behind <c>GameMode.</c>.</summary>
    public static string ModeIdOf(string modeName) => ModePrefix + FileIdOf(modeName);

    protected override string IdHint => "a game mode needs <Desc Name=\"...\">";

    /// <summary>The game modes, then the blocks. Of two blocks named alike but for case
    /// (<c>EditorUiService</c>, <c>EditorUIService</c>), the second stays with the base file.</summary>
    protected override IEnumerable<XElement> Candidates(XElement root)
        => root.Elements(ModeElement).Concat(root.Element(PropertiesElement)?.Elements() ?? []);

    protected override string? IdOf(XElement element)
    {
        if (element.Name.LocalName == ModeElement)
        {
            return (string?)element.Element("Desc")?.Attribute("Name") is { Length: > 0 } mode ? ModeIdOf(mode) : null;
        }
        return element.Parent?.Name.LocalName is null or PropertiesElement ? BlockIdOf(element.Name.LocalName) : null;
    }

    protected override string KeyOf(XElement child) => NamedKey(child, KeyAttributes);

    /// <summary>A new game mode after the last one, a new block at the end of the properties.</summary>
    protected override void AddNew(XElement root, XElement fragment)
    {
        XElement properties = root.Element(PropertiesElement)
            ?? throw new InvalidDataException($"{FileName} has no <{PropertiesElement}> to splice blocks into.");
        if (fragment.Name.LocalName == ModeElement)
        {
            (root.Elements(ModeElement).LastOrDefault() ?? properties).AddAfterSelf(fragment);
        }
        else
        {
            properties.Add(fragment);
        }
    }
}
