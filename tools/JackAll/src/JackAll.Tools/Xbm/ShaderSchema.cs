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

    /// <summary>A key retail materials carry that the descriptor does not declare.</summary>
    public bool Undeclared { get; init; }
}

/// <summary>A shader template and every property a material may give it, in descriptor order.</summary>
public sealed class ShaderDescriptor
{
    private const string Clean = "Clean";
    private const string Broken = "Broken";

    public required string Name { get; init; }

    public required IReadOnlyList<ShaderParameter> Parameters { get; init; }

    public ShaderParameter? Find(string key) => Parameters.FirstOrDefault(p => p.Name == key);

    /// <summary>
    /// The clean twin of a value a weapon's or vehicle's condition interpolates, or null for one
    /// it does not.
    /// </summary>
    /// <remarks>
    /// The unsuffixed value equals its clean twin in every shipped material, and the condition
    /// overwrites it with a blend of the two twins, so editing it alone changes nothing in game.
    /// </remarks>
    public string? CleanTwin(string key)
        => !key.EndsWith(Clean, StringComparison.Ordinal) && !key.EndsWith(Broken, StringComparison.Ordinal)
           && Find(key + Clean) is not null && Find(key + Broken) is not null
            ? key + Clean
            : null;
}

/// <summary>
/// What every shader takes, read from the game's own <c>descriptors.xml</c>.
/// </summary>
/// <remarks>
/// The descriptor is not the whole story in either direction. Retail never stores a parameter it
/// marks <c>hide</c> or one named <c>…AndGroup…</c>, which the engine composes from a tiling and a UV
/// group, so neither is offered; and retail stores a few dozen keys no descriptor declares, which
/// <see cref="Carried"/> adds back.
/// </remarks>
public sealed class ShaderSchema
{
    public const string GamePath = @"engine\providerdescriptors\descriptors.xml";

    private const string MaterialGroup = "Material";

    private static readonly string[] Lit =
        ["Vehicle", "Generic", "Cloth", "Hair", "Leaf", "RealtreeTrunk", "BigLeaf", "Grass", "Weapon"];

    // Keys retail materials carry outside their descriptor, by the shaders that carry them, counted
    // over all 2,379.
    private static readonly (string Name, XbmSection Section, int Width, string Group, bool Color, string[] Shaders)[] Carried =
    [
        ("LogicalMaterialId", XbmSection.Integer, 0, MaterialGroup, false, [.. Lit, "Skin", "Unlit"]),
        ("VertexColorEnabled", XbmSection.Integer, 0, MaterialGroup, false, Lit),
        ("AmbientOcclusion", XbmSection.Integer, 0, MaterialGroup, false, ["Vehicle", "Generic", "RealtreeTrunk", "Weapon"]),
        ("AccessoryCategory", XbmSection.Integer, 0, MaterialGroup, false, ["Generic", "Unlit", "Weapon"]),
        ("Maskable", XbmSection.Integer, 0, MaterialGroup, false, ["Generic"]),
        ("RainOccluder", XbmSection.Integer, 0, MaterialGroup, false, ["Generic"]),
        ("SpecialPickup", XbmSection.Integer, 0, MaterialGroup, false, ["Generic"]),
        ("TwoSided", XbmSection.Integer, 0, MaterialGroup, false, ["Hair", "Leaf", "BigLeaf", "Grass"]),
        ("VertexAlphaEnabled", XbmSection.Integer, 0, MaterialGroup, false, ["Hair"]),
        ("AlphaTestValue", XbmSection.Integer, 0, MaterialGroup, false, ["Grass"]),
        ("TranslucencyEnabled", XbmSection.Integer, 0, MaterialGroup, false, ["Leaf"]),
        ("TranslucencyMask", XbmSection.Integer, 0, MaterialGroup, false, ["BigLeaf"]),
        ("NormalPower", XbmSection.Float, 1, "Normal", false, ["Skin"]),
        ("USpeed", XbmSection.Float, 1, "Animation", false, ["Unlit"]),
        ("VSpeed", XbmSection.Float, 1, "Animation", false, ["Unlit"]),
        ("AngularSpeed", XbmSection.Float, 1, "Animation", false, ["Unlit"]),
        ("AnimType", XbmSection.Integer, 0, "Animation", false, ["Unlit"]),
        ("NbFrames", XbmSection.Integer, 0, "Animation", false, ["Unlit"]),
        ("LightEmissionPower", XbmSection.Float, 1, MaterialGroup, false, ["Unlit"]),
        ("UVGroupMapChannel0", XbmSection.Integer, 0, "UV Groups", false, ["Vehicle", "Unlit", "Leaf", "Grass"]),
        ("UVGroupMapChannel1", XbmSection.Integer, 0, "UV Groups", false, ["Vehicle"]),
        ("UVGroupMapChannel2", XbmSection.Integer, 0, "UV Groups", false, ["Vehicle"]),
        ("UVGroupMapChannel3", XbmSection.Integer, 0, "UV Groups", false, ["Vehicle"]),
        ("DiffuseColorBase", XbmSection.Float, 3, "Diffuse", true, ["Vehicle"]),
        ("DiffuseColor1", XbmSection.Float, 3, "Diffuse", true, ["Vehicle"]),
        ("DiffuseColor2", XbmSection.Float, 3, "Diffuse", true, ["Vehicle"]),
        ("DiffuseTiling1", XbmSection.Float, 2, "Diffuse", false, ["Vehicle"]),
        ("DiffuseTiling2", XbmSection.Float, 2, "Diffuse", false, ["Vehicle"]),
        ("SpecularColorBase", XbmSection.Float, 3, "Specular", true, ["Vehicle"]),
        ("SpecularColor1", XbmSection.Float, 3, "Specular", true, ["Vehicle"]),
        ("SpecularTiling1", XbmSection.Float, 2, "Specular", false, ["Vehicle"]),
        ("NormalTiling1", XbmSection.Float, 2, "Normal", false, ["Vehicle"]),
    ];

    /// <summary>Every shader by the name a material spells it, aliases included.</summary>
    public required IReadOnlyDictionary<string, ShaderDescriptor> Shaders { get; init; }

    /// <summary>The shaders a material can name, without the aliases.</summary>
    public IEnumerable<string> Names => Shaders.Where(s => s.Key == s.Value.Name).Select(s => s.Key);

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
            List<ShaderParameter> parameters = [];
            foreach (XElement parameter in element.Elements("parameter"))
            {
                string key = Attribute(parameter, "name");
                if (parameter.Attribute("hide")?.Value == "1" || key.Contains("AndGroup", StringComparison.Ordinal))
                {
                    continue;
                }
                (XbmSection section, int width) = Storage(Attribute(parameter, "type"));
                parameters.Add(new ShaderParameter
                {
                    Name = key,
                    Section = section,
                    Width = width,
                    Group = parameter.Attribute("group")?.Value ?? MaterialGroup,
                    IsColor = parameter.Attribute("uitype")?.Value == "color",
                    Default = parameter.Attribute("defaultvalue")?.Value,
                });
            }

            foreach (var carried in Carried.Where(c => c.Shaders.Contains(name)))
            {
                parameters.Add(new ShaderParameter
                {
                    Name = carried.Name,
                    Section = carried.Section,
                    Width = carried.Width,
                    Group = carried.Group,
                    IsColor = carried.Color,
                    Undeclared = true,
                });
            }

            var descriptor = new ShaderDescriptor { Name = name, Parameters = parameters };
            shaders[name] = descriptor;
            foreach (XElement alias in element.Elements("alias"))
            {
                shaders[alias.Value.Trim()] = descriptor;
            }
        }
        return new ShaderSchema { Shaders = shaders };
    }

    private static (XbmSection Section, int Width) Storage(string type) => type switch
    {
        "sampler2D" or "samplerCUBE" => (XbmSection.Texture, 0),
        "float" or "float1" => (XbmSection.Float, 1),
        "float2" => (XbmSection.Float, 2),
        "float3" => (XbmSection.Float, 3),
        "float4" => (XbmSection.Float, 4),
        "int" or "bool" => (XbmSection.Integer, 0),
        _ => throw new InvalidDataException($"descriptors.xml declares a parameter of unknown type '{type}'."),
    };

    private static string Attribute(XElement element, string name)
        => element.Attribute(name)?.Value
           ?? throw new InvalidDataException($"A <{element.Name}> in descriptors.xml has no {name}.");
}
