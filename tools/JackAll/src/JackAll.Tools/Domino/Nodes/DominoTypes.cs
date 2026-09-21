namespace JackAll.Tools.Domino.Nodes;

/// <summary>Plain-word names for the eleven data pin types the reflection headers declare.</summary>
public static class DominoTypes
{
    private static readonly Dictionary<string, string> Names = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Nomad|entity"] = "entity",
        ["Core|string"] = "text",
        ["Core|int"] = "whole number",
        ["Core|float"] = "number",
        ["Core|bool"] = "true/false",
        ["Nomad|animation"] = "animation",
        ["Nomad|Sound"] = "sound",
        ["Nomad|SoundType"] = "sound type",
        ["Nomad|SoundMixing"] = "sound mix",
        ["Nomad|texture"] = "texture",
        ["Core|boxclass"] = "box type",
    };

    /// <summary>`Nomad|entity` → `entity`; an unknown or missing type reads as `untyped`.</summary>
    public static string Describe(string? type) =>
        type is not null && Names.TryGetValue(type, out string? name) ? name : "untyped";

    /// <summary>A Lua literal as a value of <paramref name="type"/>. An entity is a quoted ID in the
    /// Lua, so only text keeps its quotes - otherwise it would read as a string.</summary>
    public static string FormatLiteral(string? type, string literal) =>
        type is null || type.Equals("Core|string", StringComparison.OrdinalIgnoreCase) ? literal : literal.Trim('"');
}
