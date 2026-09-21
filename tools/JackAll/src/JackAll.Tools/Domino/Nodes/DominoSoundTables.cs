using System.Globalization;
using System.Xml.Linq;

namespace JackAll.Tools.Domino.Nodes;

/// <summary>The sound type names (<c>config\soundconfig.xml</c>) and mixes
/// (<c>databases\soundmixing\soundmixings.xml</c>) that Domino's sound pins refer to.</summary>
public sealed class DominoSoundTables
{
    public const string SoundConfigPath = @"config\soundconfig.xml";
    public const string SoundMixingsPath = @"databases\soundmixing\soundmixings.xml";

    private readonly Dictionary<int, string> _types;
    private readonly Dictionary<string, uint> _mixStarts;

    private DominoSoundTables(Dictionary<int, string> types, Dictionary<string, uint> mixStarts)
    {
        _types = types;
        _mixStarts = mixStarts;
    }

    /// <summary>A missing or unreadable table just leaves its lookups empty.</summary>
    public static DominoSoundTables Load(Func<string, string?> readText)
    {
        var types = new Dictionary<int, string>();
        foreach (XElement type in Elements(readText(SoundConfigPath), "SoundType"))
        {
            if (int.TryParse((string?)type.Attribute("id"), NumberStyles.Integer, CultureInfo.InvariantCulture, out int id)
                && (string?)type.Attribute("name") is { } name)
            {
                types.TryAdd(id, name);
            }
        }

        var mixes = new Dictionary<string, uint>(StringComparer.OrdinalIgnoreCase);
        foreach (XElement mix in Elements(readText(SoundMixingsPath), "SoundMixing"))
        {
            string? start = (string?)mix.Attribute("sndStart");
            if ((string?)mix.Attribute("Name") is { } name && start is { Length: > 2 }
                && uint.TryParse(start.AsSpan(2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out uint id)
                && id != uint.MaxValue)
            {
                mixes.TryAdd(name, id);
            }
        }

        return new DominoSoundTables(types, mixes);
    }

    public string? SoundTypeName(int id) => _types.GetValueOrDefault(id);

    /// <summary>The sound a mix plays when it starts, or null when it plays none.</summary>
    public uint? MixStartSound(string mix) => _mixStarts.TryGetValue(mix, out uint id) ? id : null;

    private static IEnumerable<XElement> Elements(string? xml, string name)
    {
        if (xml is null)
        {
            return [];
        }

        try
        {
            return XDocument.Parse(xml).Descendants(name).ToList();
        }
        catch (System.Xml.XmlException)
        {
            return [];
        }
    }
}
