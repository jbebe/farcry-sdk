using System.Xml.Linq;

namespace JackAll.Tools.Xbm;

/// <summary>One property a shader takes, and the section and width a material stores it in.</summary>
public sealed class ShaderParameter
{
    public required string Name { get; init; }

    public required XbmSection Section { get; init; }

    /// <summary>How many floats the value holds; 0 outside the float section.</summary>
    public int Width { get; init; }

    /// <summary>The descriptor's own grouping: Diffuse, Mask, Specular, and so on.</summary>
    public required string Group { get; init; }

    public bool IsColor { get; init; }

    /// <summary>The descriptor's default, as it writes it; null when it gives none.</summary>
    public string? Default { get; init; }
}

/// <summary>A shader template and every property a material may give it, in descriptor order.</summary>
public sealed class ShaderDescriptor
{
    private const string Clean = "Clean";
    private const string Broken = "Broken";

    public required string Name { get; init; }

    public required IReadOnlyList<ShaderParameter> Parameters { get; init; }

    public ShaderParameter? Find(string key) => Parameters.FirstOrDefault(p => p.Name == key);

    /// <summary>The twin a weapon's or vehicle's condition overwrites this value from, or null for a
    /// value it does not drive.</summary>
    public string? CleanTwin(string key)
        => !key.EndsWith(Clean, StringComparison.Ordinal) && !key.EndsWith(Broken, StringComparison.Ordinal)
           && Find(key + Clean) is not null && Find(key + Broken) is not null
            ? key + Clean
            : null;
}

/// <summary>
/// What every shader takes: the game's own <c>descriptors.xml</c> without what retail never stores,
/// plus what retail stores that it does not declare - see docs/docs/file-formats/xbm-xbg.md.
/// </summary>
public sealed class ShaderSchema
{
    public const string GamePath = @"engine\providerdescriptors\descriptors.xml";

    private const string MaterialGroup = "Material";

    private static readonly string[] Lit =
        ["Vehicle", "Generic", "Cloth", "Hair", "Leaf", "RealtreeTrunk", "BigLeaf", "Grass", "Weapon"];

    // Keys retail materials carry outside their descriptor, by the shaders that carry them, counted
    // over all 2,379; every float3 one is a colour.
    private static readonly (string Name, string Type, string Group, string[] Shaders)[] Carried =
    [
        ("LogicalMaterialId", "int", MaterialGroup, [.. Lit, "Skin", "Unlit"]),
        ("VertexColorEnabled", "int", MaterialGroup, Lit),
        ("AmbientOcclusion", "int", MaterialGroup, ["Vehicle", "Generic", "RealtreeTrunk", "Weapon"]),
        ("AccessoryCategory", "int", MaterialGroup, ["Generic", "Unlit", "Weapon"]),
        ("Maskable", "int", MaterialGroup, ["Generic"]),
        ("RainOccluder", "int", MaterialGroup, ["Generic"]),
        ("SpecialPickup", "int", MaterialGroup, ["Generic"]),
        ("TwoSided", "int", MaterialGroup, ["Hair", "Leaf", "BigLeaf", "Grass"]),
        ("VertexAlphaEnabled", "int", MaterialGroup, ["Hair"]),
        ("AlphaTestValue", "int", MaterialGroup, ["Grass"]),
        ("TranslucencyEnabled", "int", MaterialGroup, ["Leaf"]),
        ("TranslucencyMask", "int", MaterialGroup, ["BigLeaf"]),
        ("LightEmissionPower", "float", MaterialGroup, ["Unlit"]),
        ("NormalPower", "float", "Normal", ["Skin"]),
        ("USpeed", "float", "Animation", ["Unlit"]),
        ("VSpeed", "float", "Animation", ["Unlit"]),
        ("AngularSpeed", "float", "Animation", ["Unlit"]),
        ("AnimType", "int", "Animation", ["Unlit"]),
        ("NbFrames", "int", "Animation", ["Unlit"]),
        ("UVGroupMapChannel0", "int", "UV Groups", ["Vehicle", "Unlit", "Leaf", "Grass"]),
        ("UVGroupMapChannel1", "int", "UV Groups", ["Vehicle"]),
        ("UVGroupMapChannel2", "int", "UV Groups", ["Vehicle"]),
        ("UVGroupMapChannel3", "int", "UV Groups", ["Vehicle"]),
        ("DiffuseColorBase", "float3", "Diffuse", ["Vehicle"]),
        ("DiffuseColor1", "float3", "Diffuse", ["Vehicle"]),
        ("DiffuseColor2", "float3", "Diffuse", ["Vehicle"]),
        ("DiffuseTiling1", "float2", "Diffuse", ["Vehicle"]),
        ("DiffuseTiling2", "float2", "Diffuse", ["Vehicle"]),
        ("SpecularColorBase", "float3", "Specular", ["Vehicle"]),
        ("SpecularColor1", "float3", "Specular", ["Vehicle"]),
        ("SpecularTiling1", "float2", "Specular", ["Vehicle"]),
        ("NormalTiling1", "float2", "Normal", ["Vehicle"]),
    ];

    /// <summary>Every shader by the name a material spells it, aliases included.</summary>
    public required IReadOnlyDictionary<string, ShaderDescriptor> Shaders { get; init; }

    /// <summary>The shaders a material can name, without the aliases, in order.</summary>
    public IEnumerable<string> Names
        => Shaders.Where(s => s.Key == s.Value.Name).Select(s => s.Key).Order(StringComparer.Ordinal);

    public ShaderDescriptor? Find(string shader) => Shaders.GetValueOrDefault(shader);

    public static ShaderSchema Parse(byte[] xml)
    {
        using var stream = new MemoryStream(xml);
        XElement root = XDocument.Load(stream).Root
            ?? throw new InvalidDataException("descriptors.xml has no root element.");

        Dictionary<string, ShaderDescriptor> shaders = new(StringComparer.Ordinal);
        foreach (XElement element in root.Elements("descriptor"))
        {
            string name = Attribute(element, "name");
            List<ShaderParameter> parameters = [.. element.Elements("parameter")
                .Where(p => p.Attribute("hide")?.Value != "1"
                            && !Attribute(p, "name").Contains("AndGroup", StringComparison.Ordinal))
                .Select(p => Parameter(
                    Attribute(p, "name"), Attribute(p, "type"), p.Attribute("group")?.Value ?? MaterialGroup,
                    p.Attribute("uitype")?.Value == "color", p.Attribute("defaultvalue")?.Value))];
            parameters.AddRange(Carried
                .Where(c => c.Shaders.Contains(name))
                .Select(c => Parameter(c.Name, c.Type, c.Group, c.Type == "float3", null)));

            var descriptor = new ShaderDescriptor { Name = name, Parameters = parameters };
            shaders[name] = descriptor;
            foreach (XElement alias in element.Elements("alias"))
            {
                shaders[alias.Value.Trim()] = descriptor;
            }
        }
        return new ShaderSchema { Shaders = shaders };
    }

    private static ShaderParameter Parameter(string name, string type, string group, bool color, string? fallback)
    {
        (XbmSection section, int width) = type switch
        {
            "sampler2D" or "samplerCUBE" => (XbmSection.Texture, 0),
            "float" or "float1" => (XbmSection.Float, 1),
            "float2" => (XbmSection.Float, 2),
            "float3" => (XbmSection.Float, 3),
            "float4" => (XbmSection.Float, 4),
            "int" or "bool" => (XbmSection.Integer, 0),
            _ => throw new InvalidDataException($"descriptors.xml declares a parameter of unknown type '{type}'."),
        };
        return new ShaderParameter
        {
            Name = name, Section = section, Width = width, Group = group, IsColor = color, Default = fallback,
        };
    }

    private static string Attribute(XElement element, string name)
        => element.Attribute(name)?.Value
           ?? throw new InvalidDataException($"A <{element.Name}> in descriptors.xml has no {name}.");
}
