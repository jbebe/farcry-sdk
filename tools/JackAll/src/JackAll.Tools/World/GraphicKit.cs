using System.Globalization;
using System.Numerics;
using System.Xml.Linq;
using JackAll.Core.Format;
using JackAll.Core.Format.Fcb;

namespace JackAll.Tools.World;

/// <summary>What a character's kit picked for one of its parts: a colour pair, a library texture
/// and its tiling, or both. The default look changes nothing.</summary>
public readonly record struct PartLook((Vector3 First, Vector3 Second)? Colours, (string Path, float Tiling)? Texture)
{
    /// <summary>
    /// The material as this look repaints it. Which parameters a pick lands on depends on the
    /// material's template, the way <c>CGraphicKitComponent::UpdateColorInMaterial</c> and
    /// <c>UpdateTextureInMaterial</c> route it.
    /// </summary>
    public MaterialSurface Apply(MaterialSurface surface)
    {
        if (Colours is { } colours)
        {
            // Generic takes the pair as BaseColor1/2, which its shader never reads.
            (Vector3 first, Vector3 second) = colours;
            surface = surface.Template switch
            {
                MaterialTemplate.Hair => surface with
                {
                    TintBase = first, Tint = first, SpecularBase = second, SpecularColour = second,
                },
                MaterialTemplate.Cloth or MaterialTemplate.Skin => surface with { TintBase = first, Tint = second },
                _ => surface,
            };
        }

        if (Texture is { } texture)
        {
            var repeat = new Vector2(texture.Tiling);
            surface = surface.Template switch
            {
                MaterialTemplate.Cloth => surface with { SecondDiffusePath = texture.Path, SecondDiffuseTiling = repeat },
                MaterialTemplate.Skin => surface with { BloodPath = texture.Path, BloodTiling = repeat },
                _ => surface with { DiffuseTexturePath = texture.Path, DiffuseTiling = repeat },
            };
        }

        return surface;
    }
}

/// <summary>
/// A character kit's descriptor: every part it can dress an NPC in, and the colour and texture
/// libraries those parts draw from. An archetype embeds it in
/// <c>CFileDescriptorComponent.hidDescriptor</c>; the NPC's <c>CGraphicKitComponent</c> holds the
/// picks.
/// </summary>
public sealed class GraphicKit
{
    private sealed record Part(string MeshName, string? ColourLibrary, string? TextureLibrary);

    private readonly Dictionary<uint, Part> _parts = [];
    private readonly Dictionary<string, List<(Vector3 First, Vector3 Second)>> _colours = [];
    private readonly Dictionary<string, List<(string Path, float Tiling)>> _textures = [];

    /// <summary>The kit an archetype describes, or null when it describes none.</summary>
    public static GraphicKit? Of(FcbObject archetype)
    {
        if (FcbEntityFields.FindComponent(archetype, WorldHashes.CFileDescriptorComponent) is not { } file
            || !file.Values.TryGetValue(WorldHashes.HidDescriptor, out byte[]? rml)
            || FcbXml.TryDecodeRmlValue(rml) is not { } descriptor
            || descriptor.DescendantsAndSelf("component")
                .FirstOrDefault(c => (string?)c.Attribute("class") == "GraphicKitComponent") is not { } component)
        {
            return null;
        }

        var kit = new GraphicKit();
        foreach (XElement part in component.Elements("slot").Elements("part"))
        {
            if ((string?)part.Attribute("id") is { Length: > 0 } id)
            {
                kit._parts.TryAdd(FcbClassDefinitions.Crc32Ascii(id), new Part(
                    ((string?)part.Attribute("meshName") ?? "").ToUpperInvariant(),
                    Overwritten(part.Element("colors")),
                    Overwritten(part.Element("textures"))));
            }
        }

        // The engine keeps the first library of a name.
        foreach (XElement library in component.Elements("colors").Elements("library"))
        {
            kit._colours.TryAdd((string?)library.Attribute("name") ?? "", [.. library.Elements("color")
                .Select(c => (Colour(c.Attribute("color1")), Colour(c.Attribute("color2"))))]);
        }
        foreach (XElement library in component.Elements("textures").Elements("library"))
        {
            kit._textures.TryAdd((string?)library.Attribute("name") ?? "", [.. library.Elements("texture")
                .Select(t => (
                    NameHash.Normalize(Path.ChangeExtension((string?)t.Attribute("path") ?? "", ".xbt")),
                    float.TryParse((string?)t.Attribute("tiling"), NumberStyles.Float, CultureInfo.InvariantCulture,
                        out float tiling) ? tiling : 1f))]);
        }

        return kit;
    }

    /// <summary>
    /// Per upper-case part name, the look an NPC's picks give it: the entity's own
    /// <c>CGraphicKitComponent</c>, else its archetype's. A pick leaves a part alone when the part
    /// does not take overwrites or its index is -1 or out of range.
    /// </summary>
    public Dictionary<string, PartLook> LooksOf(FcbObject entity, FcbObject? archetype)
    {
        var looks = new Dictionary<string, PartLook>(StringComparer.Ordinal);
        FcbObject? picks = PicksOf(entity) ?? (archetype is null ? null : PicksOf(archetype));
        foreach (FcbObject pick in picks?.Children ?? [])
        {
            if (pick.TypeHash != WorldHashes.ActivePartOverwrite
                || FcbEntityFields.ReadU32(pick, WorldHashes.PartID) is not uint id
                || !_parts.TryGetValue(id, out Part? part))
            {
                continue;
            }

            var look = new PartLook(
                Pick(_colours, part.ColourLibrary, Index(pick, WorldHashes.ColorIndex)),
                Pick(_textures, part.TextureLibrary, Index(pick, WorldHashes.TextureIndex)));
            if (look != default)
            {
                looks[part.MeshName] = look;
            }
        }

        return looks;
    }

    private static FcbObject? PicksOf(FcbObject node)
        => FcbEntityFields.FindComponent(node, WorldHashes.CGraphicKitComponent)?.Children
            .FirstOrDefault(c => c.TypeHash == WorldHashes.PartOverwrite);

    private static int Index(FcbObject pick, uint field)
        => FcbEntityFields.ReadU32(pick, field) is uint index ? (int)index : -1;

    private static T? Pick<T>(Dictionary<string, List<T>> libraries, string? library, int index) where T : struct
        => library is not null && libraries.TryGetValue(library, out List<T>? entries) && index >= 0 && index < entries.Count
            ? entries[index]
            : null;

    /// <summary>The library a part's <c>colors</c> or <c>textures</c> element lets picks overwrite
    /// from, or null when it does not.</summary>
    private static string? Overwritten(XElement? element)
        => (string?)element?.Attribute("overwrite") == "1" ? (string?)element!.Attribute("library") : null;

    /// <summary>A kit colour is a decimal <c>0x00BBGGRR</c>, red in the low byte.</summary>
    private static Vector3 Colour(XAttribute? attribute)
    {
        uint packed = uint.TryParse((string?)attribute, NumberStyles.Integer, CultureInfo.InvariantCulture, out uint v) ? v : 0;
        return new Vector3(packed & 0xFF, (packed >> 8) & 0xFF, (packed >> 16) & 0xFF) / 255f;
    }
}
