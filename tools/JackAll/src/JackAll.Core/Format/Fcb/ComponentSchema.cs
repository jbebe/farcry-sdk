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
    private readonly Dictionary<uint, SchemaClass> _byHash;

    private ComponentSchema(IReadOnlyList<SchemaClass> classes)
    {
        Classes = classes;
        _byHash = classes.ToDictionary(c => FcbClassDefinitions.Crc32Ascii(c.Name));
    }

    public static ComponentSchema Empty { get; } = new([]);

    public IReadOnlyList<SchemaClass> Classes { get; }

    public static ComponentSchema Load(string path)
    {
        SchemaDocument? document = JsonSerializer.Deserialize(File.ReadAllText(path), ComponentSchemaJson.Default.SchemaDocument);
        return new ComponentSchema(document?.Classes ?? []);
    }

    public SchemaClass? Find(uint classHash) => _byHash.GetValueOrDefault(classHash);
}

public sealed record SchemaClass(
    string Name, string? Parent, string Kind, bool Creatable, IReadOnlyList<string> Uses, IReadOnlyList<SchemaMember> Members)
{
    public bool IsComponent => Kind == "component";
}

/// <summary>
/// A <c>value</c>; a <c>container</c> of child nodes tagged <see cref="Element"/>, inside a child
/// named after the member when <see cref="Wrapped"/>; a <c>group</c> whose members sit in a child
/// named after it; or a <c>conditional</c> group whose members sit in the same node.
/// </summary>
public sealed record SchemaMember(
    string Name,
    string Kind,
    int? Flags,
    string? Type,
    string? Cpp,
    IReadOnlyList<string>? Labels,
    string? Element,
    bool? Wrapped,
    string? Condition,
    IReadOnlyList<SchemaMember>? Members);

internal sealed record SchemaDocument(string? Generator, IReadOnlyList<SchemaClass> Classes);

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(SchemaDocument))]
internal partial class ComponentSchemaJson : JsonSerializerContext;
