using System.Text.Json;
using System.Text.Json.Nodes;

namespace JackAll.Tests;

/// <summary>
/// Reads the decoded-field dump the Python codecs emit, and deep-compares a C# decode against it.
/// </summary>
/// <remarks>
/// A byte-exact round trip cannot catch a symmetric misreading: a reader that swaps two same-width
/// fields and a writer that swaps them back still reproduces the file. This compares the decoded
/// meaning instead, against the implementation the corpus numbers were established with.
/// <para>
/// Scaffolding for the port. Generate a dump with
/// <c>python tools/BlenderFC2/tests/fielddump.py &lt;format&gt;</c>; it lands under
/// <c>tmp/fielddump/</c>, which is gitignored, so these gates no-op wherever it is absent. Delete
/// this once fc2fmt goes.
/// </para>
/// </remarks>
internal static class Fc2FieldDump
{
    public static string DirectoryPath { get; } = Path.Combine(TestSupport.RepositoryRoot, "tmp", "fielddump");

    public static string PathFor(string format) => Path.Combine(DirectoryPath, format + ".jsonl");

    public static bool Present(string format) => File.Exists(PathFor(format));

    public static string MissingMessage(string format)
        => $"{PathFor(format)} is absent, so the differential gate for .{format} no-opped. "
           + $"Regenerate it with: python tools/BlenderFC2/tests/fielddump.py {format}";

    /// <summary>Each dumped file: the corpus-relative path, and the fields Python decoded.</summary>
    public static IEnumerable<(string Path, JsonNode Fields)> Read(string format)
    {
        if (!Present(format))
        {
            yield break;
        }

        foreach (string line in File.ReadLines(PathFor(format)))
        {
            if (line.Length == 0)
            {
                continue;
            }
            JsonNode entry = JsonNode.Parse(line)
                ?? throw new InvalidDataException($"Unparseable line in {PathFor(format)}.");
            yield return (entry["path"]!.GetValue<string>(), entry["fields"]!);
        }
    }

    /// <summary>
    /// The first place two trees disagree, as a dotted path, or null when they match.
    /// </summary>
    public static string? FirstDifference(JsonNode? expected, JsonNode? actual, string path = "")
    {
        if (expected is null || actual is null)
        {
            return expected is null && actual is null ? null : $"{Where(path)}: one side is absent";
        }

        if (expected is JsonObject expectedObject)
        {
            if (actual is not JsonObject actualObject)
            {
                return $"{Where(path)}: expected an object, got {actual.GetValueKind()}";
            }

            foreach (string key in expectedObject.Select(pair => pair.Key).Except(actualObject.Select(pair => pair.Key)))
            {
                return $"{Where(path)}: missing field '{key}'";
            }
            foreach (string key in actualObject.Select(pair => pair.Key).Except(expectedObject.Select(pair => pair.Key)))
            {
                return $"{Where(path)}: unexpected field '{key}'";
            }
            foreach ((string key, JsonNode? value) in expectedObject)
            {
                if (FirstDifference(value, actualObject[key], Join(path, key)) is { } difference)
                {
                    return difference;
                }
            }
            return null;
        }

        if (expected is JsonArray expectedArray)
        {
            if (actual is not JsonArray actualArray)
            {
                return $"{Where(path)}: expected an array, got {actual.GetValueKind()}";
            }
            if (expectedArray.Count != actualArray.Count)
            {
                return $"{Where(path)}: {expectedArray.Count} entries expected, {actualArray.Count} produced";
            }
            for (int i = 0; i < expectedArray.Count; i++)
            {
                if (FirstDifference(expectedArray[i], actualArray[i], $"{path}[{i}]") is { } difference)
                {
                    return difference;
                }
            }
            return null;
        }

        return Leaf(expected) == Leaf(actual)
            ? null
            : $"{Where(path)}: expected {Leaf(expected)}, got {Leaf(actual)}";
    }

    /// <summary>
    /// A leaf as text. Every dumped value is an integer or a string - floats travel as their raw
    /// bits - so there is no formatting to disagree about.
    /// </summary>
    private static string Leaf(JsonNode node)
        => node.GetValueKind() == JsonValueKind.String
            ? $"\"{node.GetValue<string>()}\""
            : node.ToJsonString();

    private static string Join(string path, string key) => path.Length == 0 ? key : $"{path}.{key}";

    private static string Where(string path) => path.Length == 0 ? "(root)" : path;
}
