using JackAll.Core;

namespace JackAll.Tools.Ai;

/// <summary>A parameter a task class's <c>LoadFromXML</c> reads: int, uint, float, bool, string, fact or position.</summary>
public sealed record AiParameterSpec(string Name, string Type);

/// <summary>
/// The parameters each task class reads, harvested from the engine (<c>assets/ai_tasks.tsv</c>). A
/// parameter in a brain that its class does not read is dead weight the engine ignores.
/// </summary>
public sealed class AiTaskSchema
{
    private readonly Dictionary<string, (string? Parent, AiParameterSpec[] Own)> _classes;

    private AiTaskSchema(Dictionary<string, (string?, AiParameterSpec[])> classes) => _classes = classes;

    public static AiTaskSchema Empty { get; } = new([]);

    public static AiTaskSchema Bundled { get; } = BundledAssets.FindAsset(".aitasks", Path.Combine("assets", "ai_tasks.tsv")) is { } path
        ? Parse(File.ReadLines(path))
        : Empty;

    public static AiTaskSchema Parse(IEnumerable<string> lines)
    {
        var classes = new Dictionary<string, (string?, AiParameterSpec[])>(StringComparer.Ordinal);
        foreach (string line in lines.Where(l => l.Length > 0 && l[0] != '#'))
        {
            string[] cells = line.Split('\t');
            AiParameterSpec[] own =
            [
                .. cells.Skip(2).Select(c => c.LastIndexOf(':') is int colon and > 0
                    ? new AiParameterSpec(c[..colon], c[(colon + 1)..])
                    : new AiParameterSpec(c, "")),
            ];
            classes[cells[0]] = (cells.Length > 1 && cells[1].Length > 0 ? cells[1] : null, own);
        }
        return new AiTaskSchema(classes);
    }

    public bool Knows(string cls) => _classes.ContainsKey(cls);

    /// <summary>Every parameter <paramref name="cls"/> reads, its base classes' first.</summary>
    public IReadOnlyList<AiParameterSpec> ParametersOf(string cls)
    {
        List<AiParameterSpec> all = [];
        for (string? c = cls; c is not null && _classes.TryGetValue(c, out var entry); c = entry.Parent)
        {
            all.InsertRange(0, entry.Own);
        }
        return all;
    }

    public AiParameterSpec? Find(string cls, string name) => ParametersOf(cls).FirstOrDefault(p => p.Name == name);
}
