using System.Xml.Linq;

namespace JackAll.Tools.Shader;

/// <summary>The options one level of a setting adds to or removes from a shader.</summary>
public sealed record ShaderTweak(IReadOnlySet<string> Add, IReadOnlySet<string> Remove);

/// <summary>One level of a setting: its tweaks by shader name, and the options it removes from every shader.</summary>
public sealed record QualityLevel(string Id, IReadOnlyDictionary<string, ShaderTweak> Tweaks, IReadOnlySet<string> RemovedEverywhere);

/// <summary>A setting of the render config (Vegetation, Texture, Shadow…), its PC levels lowest first,
/// and the index of the level each preset picks.</summary>
public sealed record QualitySetting(string Name, IReadOnlyList<QualityLevel> Levels, IReadOnlyDictionary<string, int> PresetLevels)
{
    private readonly Dictionary<string, HashSet<string>> _addedAtAnyLevel = Levels
        .SelectMany(l => l.Tweaks)
        .GroupBy(t => t.Key, StringComparer.OrdinalIgnoreCase)
        .ToDictionary(g => g.Key, g => g.SelectMany(t => t.Value.Add).ToHashSet(), StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Whether a permutation of <paramref name="shader"/> setting <paramref name="options"/> is drawn
    /// at <paramref name="level"/>: it sets none of the options the level removes, and of those the
    /// setting adds at any level either none or exactly the ones this level adds.
    /// </summary>
    public bool Allows(QualityLevel level, string shader, IReadOnlySet<string> options)
    {
        level.Tweaks.TryGetValue(shader, out ShaderTweak? tweak);
        if (level.RemovedEverywhere.Overlaps(options) || tweak?.Remove.Overlaps(options) == true)
        {
            return false;
        }
        if (!_addedAtAnyLevel.TryGetValue(shader, out HashSet<string>? managed))
        {
            return true;
        }

        bool setsAny = false, differs = false;
        foreach (string add in managed)
        {
            bool sets = options.Contains(add);
            setsAny |= sets;
            differs |= sets != (tweak?.Add.Contains(add) == true);
        }
        return !setsAny || !differs;
    }
}

/// <summary>
/// The quality model of <c>engine\settings\defaultrenderconfig.xml</c>: the D3D9 menu presets, the
/// level each picks for every setting, and what each level adds to or removes from a shader's options.
/// </summary>
/// <remarks>
/// Only settings that change shader options are kept: those with <c>&lt;shader&gt;</c> lines, and
/// Shadow, whose <c>DisableShadow</c> level drops the shadow passes and receivers of every shader.
/// </remarks>
public sealed class RenderQualityConfig
{
    /// <summary>The D3D9 menu presets, lowest first.</summary>
    public static readonly IReadOnlyList<string> Presets = ["low", "medium", "high", "veryhigh", "ultrahigh"];

    /// <summary>The PC D3D9 level ids, lowest first; consoles, legacy and D3D10 levels are left out.</summary>
    private static readonly string[] LevelOrder = ["off", .. Presets];

    private static readonly HashSet<string> ShadowOptions = ["SHADOW", "RECEIVE_SHADOWS"];

    private RenderQualityConfig(IReadOnlyList<QualitySetting> settings) => Settings = settings;

    public IReadOnlyList<QualitySetting> Settings { get; }

    public static RenderQualityConfig Parse(string xml)
    {
        XElement profile = XDocument.Parse(xml).Root ?? throw new InvalidDataException("The render config is empty.");
        XElement[] presets = [.. profile.Element("RenderQuality")?.Elements("quality") ?? []];

        var settings = new List<QualitySetting>();
        foreach (XElement block in profile.Elements())
        {
            QualityLevel[] levels = [.. LevelOrder
                .Select(id => block.Elements("quality").FirstOrDefault(q => (string?)q.Attribute("id") == id))
                .OfType<XElement>().Select(ReadLevel)];
            if (!levels.Any(l => l.Tweaks.Count > 0 || l.RemovedEverywhere.Count > 0))
            {
                continue;
            }

            var presetLevels = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            foreach (string preset in Presets)
            {
                string? id = presets.FirstOrDefault(p => (string?)p.Attribute("id") == preset)
                    ?.Attribute(block.Name.LocalName + "Quality")?.Value;
                int level = Array.FindIndex(levels, l => l.Id.Equals(id, StringComparison.OrdinalIgnoreCase));
                if (level >= 0)
                {
                    presetLevels[preset] = level;
                }
            }
            settings.Add(new QualitySetting(block.Name.LocalName, levels, presetLevels));
        }
        return new RenderQualityConfig(settings);
    }

    private static QualityLevel ReadLevel(XElement quality) => new(
        (string)quality.Attribute("id")!,
        quality.Elements("Shaders").Elements("shader")
            .GroupBy(s => (string?)s.Attribute("name") ?? "", StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => new ShaderTweak(
                g.SelectMany(s => Names(s.Attribute("add"))).ToHashSet(),
                g.SelectMany(s => Names(s.Attribute("remove"))).ToHashSet()), StringComparer.OrdinalIgnoreCase),
        (string?)quality.Attribute("DisableShadow") == "1" ? ShadowOptions : []);

    private static string[] Names(XAttribute? list)
        => (list?.Value ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
}
