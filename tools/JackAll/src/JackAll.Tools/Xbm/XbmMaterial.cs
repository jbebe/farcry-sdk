using System.Globalization;

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
/// A material flattened for display: name, shader template, texture slot bindings, and every other
/// keyed property rendered as text.
/// </summary>
/// <remarks>
/// A projection over <see cref="XbmFile"/>, which owns the format.
/// <para>
/// It previously carried its own parser, which found the material chunk by scanning for its tag
/// rather than walking the container, and guessed the body's leading bytes from a list of candidate
/// offsets - accepting the first that produced a printable name. The preamble is a fixed five bytes
/// in a standalone material; what varies is that the copy an `.xbg` embeds leads with the name and
/// part instead, which is a different layout rather than a different offset.
/// </para>
/// <para>
/// Values are formatted here, so a caller wanting numbers should read <see cref="XbmFile"/> rather
/// than parse these back.
/// </para>
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
        List<XbmProperty> properties = [];
        foreach (int width in XbmFile.GroupSizes)
        {
            foreach (XbmEntry entry in material.Section(XbmSection.Float, width))
            {
                properties.Add(new XbmProperty
                {
                    Key = entry.Key,
                    Value = string.Join(", ", entry.Floats.Select(FormatFloat)),
                });
            }
        }

        foreach (XbmEntry entry in material.Section(XbmSection.Integer))
        {
            properties.Add(new XbmProperty
            {
                Key = entry.Key,
                Value = entry.Integer.ToString(CultureInfo.InvariantCulture),
            });
        }

        return new XbmMaterial
        {
            Name = material.Name,
            Template = material.Shader,
            Textures = [.. material.Section(XbmSection.Texture)
                .Select(entry => new XbmProperty { Key = entry.Key, Value = entry.Path })],
            Properties = properties,
        };
    }

    /// <summary>Invariant so the text stays machine-readable wherever it is parsed back - a comma
    /// decimal separator would collide with the separator between a vector's components.</summary>
    private static string FormatFloat(float f) => f.ToString("0.###", CultureInfo.InvariantCulture);
}
