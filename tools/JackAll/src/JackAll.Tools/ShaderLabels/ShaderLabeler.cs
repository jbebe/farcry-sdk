using System.Text;
using JackAll.Core.Format;
using JackAll.Core.Vfs;
using JackAll.Tools.Labels;
using JackAll.Tools.Shader;

namespace JackAll.Tools.ShaderLabels;

/// <summary>
/// Labels every Direct3D 9 shader object by the permutations that load it: what its shaders draw, the
/// passes and options they share, and the quality presets that draw it.
/// </summary>
public static class ShaderLabeler
{
    public const string FromIndex = "index";

    private const int MaxListed = 3;

    private static readonly (string Option, string Name)[] Lights =
        [("AMBIENT", "ambient"), ("SUN", "sun"), ("DIRECTIONAL", "directional"), ("OMNI", "omni"), ("SPOT", "spot")];

    /// <summary>Options a pass or light already says, left out of the shared features.</summary>
    private static readonly HashSet<string> PassOptions =
        ["SHADOW", "DEPTH", "DEPTH_ENCODE", "PICKING", .. Lights.Select(l => l.Option)];

    private static readonly Dictionary<string, string> FeatureNames = new()
    {
        ["RECEIVE_SHADOWS"] = "receives shadows",
        ["INSTANCING"] = "instanced",
        ["SKINNING"] = "skinned",
        ["ALPHA_TEST"] = "alpha-tested",
        ["ALPHA_TO_COVERAGE"] = "alpha to coverage",
        ["SAMPLES2"] = "2x MSAA",
        ["SAMPLES4"] = "4x MSAA",
        ["HW_SM_COMPARE"] = "hardware shadow compare",
        ["FETCH4"] = "Fetch4 shadows",
        ["SIMPLE_SHADOWMAP"] = "plain shadow map",
    };

    /// <summary>A permutation, with a bit per level of each setting that draws it.</summary>
    private sealed record Permutation(string Shader, string Name, IReadOnlySet<string> Options, int[] DrawnAt);

    /// <summary>Whether a game file is one of the D3D9 objects this labels.</summary>
    public static bool Covers(VfsFile file)
        => file.Path.StartsWith(ShaderIndex.Root, StringComparison.OrdinalIgnoreCase) && ShaderIndex.Stages.Contains(file.Type.Extension);

    /// <summary>Labels the objects of an install's <c>shadersobj</c> D3D9 tree from its own tables.</summary>
    public static IReadOnlyList<FileLabel> FromGame(GameVfs vfs)
    {
        byte[] Read(string path) => vfs.ReadOriginal(NameHash.Compute(path))
            ?? throw new FileNotFoundException($"The game has no {path}.");

        return Label(
            ShaderInitData.Parse(Read(@"engine\shaders\fastinitdata_d3d9.bin")),
            RenderQualityConfig.Parse(Encoding.UTF8.GetString(Read(@"engine\settings\defaultrenderconfig.xml"))),
            ShaderIndex.Stages.ToDictionary(s => s, s => ShaderIndex.Parse(Read(ShaderIndex.Root + "index." + s))));
    }

    /// <summary>One label per object any permutation loads, keyed by the object's archive path.</summary>
    public static IReadOnlyList<FileLabel> Label(ShaderInitData init, RenderQualityConfig config, IReadOnlyDictionary<string, ShaderIndex> tables)
    {
        var uses = new Dictionary<string, List<Permutation>>(StringComparer.OrdinalIgnoreCase);
        foreach (ulong mask in init.Permutations)
        {
            ShaderDefinition shader = init.ShaderOf(mask);
            string name = shader.PermutationName(mask);
            HashSet<string> options = [.. shader.OptionsIn(mask).Select(o => o.Name)];
            int[] drawnAt = [.. config.Settings.Select(setting => setting.Levels
                .Select((level, i) => setting.Allows(level, shader.Name, options) ? 1 << i : 0).Sum())];
            var permutation = new Permutation(shader.Name, name, options, drawnAt);

            uint key = ShaderIndex.KeyOf(name);
            foreach ((string stage, ShaderIndex table) in tables)
            {
                if (table.Lookup(key) is { } hash and not 0)
                {
                    string path = ShaderIndex.Root + ShaderIndex.PathOf(hash, stage);
                    if (!uses.TryGetValue(path, out List<Permutation>? list))
                    {
                        uses[path] = list = [];
                    }
                    list.Add(permutation);
                }
            }
        }

        int[][] presetLevels = [.. RenderQualityConfig.Presets.Select(preset => config.Settings
            .Select(s => s.PresetLevels.GetValueOrDefault(preset, -1)).ToArray())];
        return [.. uses.Select(u => Describe(u.Key, u.Value, config, presetLevels))];
    }

    private static FileLabel Describe(string path, List<Permutation> permutations, RenderQualityConfig config, int[][] presetLevels)
    {
        string[] shaders = [.. permutations.Select(p => p.Shader).Distinct()];
        string[] categories = [.. shaders.Select(ShaderCovers.Of).Distinct()];

        string[] features = [.. permutations[0].Options
            .Where(o => !PassOptions.Contains(o) && permutations.All(p => p.Options.Contains(o)))
            .Order().Select(o => FeatureNames.GetValueOrDefault(o, o))];
        string[] lit = [.. permutations.Select(p => Lit(p.Options)).Where(l => l.Length > 0).Distinct()];
        string passes = LabelFormat.Join(", ",
            permutations.Any(p => p.Options.Contains("SHADOW")) ? "shadow map" : "",
            permutations.Any(p => p.Options.Contains("DEPTH")) ? "depth" : "",
            lit.Length > 0 ? "lit by " + Listed(lit, " / ") : "");

        string[] presets = [.. RenderQualityConfig.Presets.Where((_, preset) => permutations.Any(p =>
            presetLevels[preset].Select((level, s) => level < 0 || (p.DrawnAt[s] & (1 << level)) != 0).All(drawn => drawn)))];
        string[] governing = [.. config.Settings
            .Select((setting, s) => Governing(setting, permutations.Aggregate(0, (drawn, p) => drawn | p.DrawnAt[s])))
            .OfType<string>()];

        string detail = LabelFormat.Join(" · ",
            Listed(shaders),
            passes,
            string.Join(", ", features),
            presets.Length == RenderQualityConfig.Presets.Count ? "all presets"
                : presets.Length == 0 ? "no preset" : string.Join(", ", presets));
        string evidence = string.Join("; ", governing.Append(permutations.Count == 1
            ? $"1 permutation: {permutations[0].Name}"
            : $"{permutations.Count} permutations, e.g. {permutations[0].Name}"));

        return new FileLabel(path, categories.Length == 1 ? categories[0] : ShaderCovers.Shared, detail, FromIndex, evidence);
    }

    /// <summary>The lights a lit pass draws with, as <c>ambient+sun</c>; empty for a shadow map or depth pass.</summary>
    private static string Lit(IReadOnlySet<string> options)
        => options.Contains("SHADOW") || options.Contains("DEPTH") ? ""
            : string.Join('+', Lights.Where(l => options.Contains(l.Option)).Select(l => l.Name));

    /// <summary>The levels of a setting that draw the object, a bit per level, when not all of them do.</summary>
    private static string? Governing(QualitySetting setting, int drawn)
    {
        IReadOnlyList<QualityLevel> levels = setting.Levels;
        int[] allowed = [.. Enumerable.Range(0, levels.Count).Where(i => (drawn & (1 << i)) != 0)];
        bool contiguous = allowed.Length > 0 && allowed[^1] - allowed[0] + 1 == allowed.Length;

        return allowed.Length == levels.Count ? null
            : allowed.Length == 0 ? $"no {setting.Name} level"
            : allowed.Length == 1 ? $"{setting.Name} = {levels[allowed[0]].Id}"
            : contiguous && allowed[^1] == levels.Count - 1 ? $"{setting.Name} ≥ {levels[allowed[0]].Id}"
            : contiguous && allowed[0] == 0 ? $"{setting.Name} ≤ {levels[allowed[^1]].Id}"
            : $"{setting.Name}: {string.Join(", ", allowed.Select(i => levels[i].Id))}";
    }

    private static string Listed(string[] items, string separator = ", ")
        => string.Join(separator, items.Take(MaxListed)) + LabelFormat.More(items.Length, MaxListed, " +");
}
