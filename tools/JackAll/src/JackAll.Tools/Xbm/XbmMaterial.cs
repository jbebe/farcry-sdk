namespace JackAll.Tools.Xbm;

/// <summary>One keyed entry from an .xbm's material definition - a texture slot ("DiffuseTexture1" ->
/// an .xbt path) or a numeric property (tiling, color, specular power, ...) already formatted for
/// display.</summary>
public sealed class XbmProperty
{
    public required string Key { get; init; }
    public required string Value { get; init; }
}

/// <summary>
/// A material flattened to text: name, shader template, texture slot bindings, and every other keyed
/// property in <see cref="XbmEntry.Text"/> form.
/// </summary>
/// <remarks>
/// A projection over <see cref="XbmFile"/>, which owns the format; a caller wanting numbers should
/// read that rather than parse these back.
/// </remarks>
public sealed class XbmMaterial
{
    public required string Name { get; init; }
    public required string Template { get; init; }
    public required IReadOnlyList<XbmProperty> Textures { get; init; }
    public required IReadOnlyList<XbmProperty> Properties { get; init; }

    public static XbmMaterial Parse(byte[] data)
    {
        XbmFile material = XbmFile.Parse(data);
        IEnumerable<XbmEntry> properties = XbmFile.GroupSizes.ToArray()
            .SelectMany(width => material.Section(XbmSection.Float, width))
            .Concat(material.Section(XbmSection.Integer));
        return new XbmMaterial
        {
            Name = material.Name,
            Template = material.Shader,
            Textures = [.. material.Section(XbmSection.Texture).Select(Property)],
            Properties = [.. properties.Select(Property)],
        };
    }

    private static XbmProperty Property(XbmEntry entry) => new() { Key = entry.Key, Value = entry.Text };
}