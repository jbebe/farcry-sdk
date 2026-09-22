using System.Text.Json;
using System.Text.Json.Serialization;

namespace JackAll.Core.Format.Fcb;

/// <summary>
/// The entity and component classes the engine registers, with their properties' names, wire types
/// and enum labels. Built by <c>tools/fc2re/build_component_schema.py</c> from the engine's own
/// <c>RegisterProperties</c> code; see docs/docs/engine-internals/entity-component-schema.md.
/// </summary>
public sealed class ComponentSchema
{
    public static ComponentSchema Empty { get; } = new() { Classes = [] };

    public required IReadOnlyList<SchemaClass> Classes { get; init; }

    public static ComponentSchema Load(string path)
        => new() { Classes = JsonSerializer.Deserialize(File.ReadAllText(path), ComponentSchemaJson.Default.SchemaDocument)?.Classes ?? [] };
}

public sealed record SchemaClass(string Name, string? Parent, string Kind, bool Creatable, IReadOnlyList<SchemaMember> Members)
{
    public bool IsComponent => Kind == "component";
}

/// <summary>
/// A <c>value</c>; a <c>container</c> of child nodes tagged <see cref="Element"/>, inside a child
/// named after the member when <see cref="Wrapped"/>; a <c>group</c> whose members sit in a child
/// named after it; or a <c>conditional</c> group or <c>embedded</c> class whose members sit in the
/// same node. <see cref="Cpp"/> is the engine's own value type, e.g. <c>CPathID</c>.
/// </summary>
public sealed record SchemaMember(
    string Name,
    string Kind,
    string? Type,
    string? Cpp,
    IReadOnlyList<string>? Labels,
    string? Element,
    bool? Wrapped,
    IReadOnlyList<SchemaMember>? Members);

internal sealed record SchemaDocument(IReadOnlyList<SchemaClass> Classes);

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(SchemaDocument))]
internal partial class ComponentSchemaJson : JsonSerializerContext;
