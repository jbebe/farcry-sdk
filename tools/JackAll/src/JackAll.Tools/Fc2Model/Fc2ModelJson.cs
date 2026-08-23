using System.Text.Json;
using System.Text.Json.Serialization;

namespace JackAll.Tools.Fc2Model;

/// <summary>
/// How a pack's JSON is written, in one place because two codebases read it.
/// </summary>
/// <remarks>
/// Snake case throughout. A pack is a contract between JackAll and whatever editor opens it, and
/// C#'s own property casing is a language-shaped type leaking into a format that has no business
/// carrying one - the reader on the other side is Python.
/// <para>
/// The same options deserialise, so a document written here reads back here whatever the policy is.
/// What the policy decides is only what the other reader has to spell.
/// </para>
/// </remarks>
public static class Fc2ModelJson
{
    /// <summary>For a document inside the pack: not indented, because most of one is float arrays.</summary>
    public static readonly JsonSerializerOptions Compact = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new NegativeZeroConverter() },
    };

    /// <summary>For the manifest, which is the part a person reads.</summary>
    public static readonly JsonSerializerOptions Readable = new(Compact) { WriteIndented = true };
}
