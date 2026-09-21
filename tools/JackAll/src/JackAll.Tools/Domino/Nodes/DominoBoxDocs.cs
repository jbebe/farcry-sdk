using System.Reflection;
using System.Text.Json;

namespace JackAll.Tools.Domino.Nodes;

/// <summary>What a `system\` box does, written from its Lua implementation. <see cref="Pins"/> only
/// covers pins whose meaning isn't obvious from their name and type.</summary>
public sealed record BoxDoc(string Summary, IReadOnlyDictionary<string, string> Pins);

/// <summary>The hand-written box descriptions bundled as <c>DominoBoxDocs.json</c>, keyed by the box
/// script's lower-case file stem.</summary>
public static class DominoBoxDocs
{
    private sealed record Entry(string Summary, Dictionary<string, string>? Pins);

    private static readonly Lazy<IReadOnlyDictionary<string, BoxDoc>> Docs = new(Load);

    public static BoxDoc? For(string nodeTypePath) =>
        Docs.Value.GetValueOrDefault(NodeSignature.ShortNameFor(nodeTypePath));

    private static IReadOnlyDictionary<string, BoxDoc> Load()
    {
        using Stream stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("JackAll.Tools.Domino.Nodes.DominoBoxDocs.json")
            ?? throw new InvalidOperationException("DominoBoxDocs.json is not embedded.");
        var entries = JsonSerializer.Deserialize<Dictionary<string, Entry>>(stream, new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;
        return entries.ToDictionary(
            e => e.Key,
            e => new BoxDoc(e.Value.Summary, e.Value.Pins ?? []),
            StringComparer.OrdinalIgnoreCase);
    }
}
