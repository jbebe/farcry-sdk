using System.Text;
using System.Xml.Linq;
using JackAll.Core.Format.Rml;

namespace JackAll.Tools.World;

/// <summary>
/// One entry of the world's terrain layer table: a texture plus how it is applied. <see cref="Index"/>
/// is what <c>sector&lt;id&gt;.desc.fcb</c>'s <c>DetailTexMask</c> packs, and <see cref="SurfaceTypeId"/>
/// is what the sector's surface-type palette stores.
/// </summary>
/// <param name="ProjectionAxis">Which plane the texture projects along - 0 = X, 1 = Y, 2 = Z. Rock is
/// commonly authored as two layers, an <c>_X</c> and a <c>_Y</c>, so cliffs get a sideways projection
/// instead of the stretched top-down one.</param>
public sealed record TerrainLayer(
    int Index, string Name, byte SurfaceTypeId, string TexturePath, float Tiling, int ProjectionAxis);

/// <summary>
/// The terrain layer table from a world's <c>&lt;name&gt;.game.xml</c>: the 45 <c>&lt;Layer&gt;</c>
/// entries naming every terrain texture, and the <c>SurfaceTypeID</c> each one declares.
/// </summary>
/// <remarks>
/// Several layers usually share one surface type (all the jungle underbrush variants are type 19),
/// so a surface type maps to a set of layer names rather than exactly one. There is no table of
/// surface-type names anywhere in the shipped data, which is why the labels here are built out of
/// the layer names that use them.
/// </remarks>
public sealed class TerrainLayerTable
{
    private readonly Dictionary<byte, List<string>> _layersBySurfaceType;

    private TerrainLayerTable(Dictionary<byte, List<string>> layersBySurfaceType, IReadOnlyList<TerrainLayer> layers)
    {
        _layersBySurfaceType = layersBySurfaceType;
        Layers = layers;
    }

    /// <summary>The layers in table order - a layer's index is simply its position here, which is what
    /// <c>DetailTexMask</c> stores.</summary>
    public IReadOnlyList<TerrainLayer> Layers { get; }

    public TerrainLayer? this[int index] => index >= 0 && index < Layers.Count ? Layers[index] : null;

    public static TerrainLayerTable Empty { get; } = new([], []);

    /// <summary>A human-readable label for a surface type, or null when no layer declares it.</summary>
    public string? Label(byte surfaceType)
    {
        if (!_layersBySurfaceType.TryGetValue(surfaceType, out List<string>? names))
        {
            return null;
        }
        return names.Count == 1 ? names[0] : $"{names[0]} +{names.Count - 1} more";
    }

    public static TerrainLayerTable Load(string mapName, Func<string, byte[]?> readByPath)
    {
        byte[]? bytes = readByPath($@"worlds\{mapName}\generated\{mapName}.game.xml");
        if (bytes is null || Parse(bytes) is not { } root)
        {
            return Empty;
        }

        var bySurfaceType = new Dictionary<byte, List<string>>();
        var layers = new List<TerrainLayer>();
        // Only the descriptor's own <Layers> block is the terrain table. The file carries ~1,680 more
        // <Layer> elements under MissionsDef, and counting those would shift every layer index.
        foreach (XElement layer in root.Element("Layers")?.Elements("Layer") ?? [])
        {
            string name = layer.Attribute("Name")?.Value ?? "unnamed";
            byte.TryParse(layer.Attribute("SurfaceTypeID")?.Value, out byte surfaceType);
            float.TryParse(layer.Attribute("Tiling")?.Value,
                System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out float tiling);
            int.TryParse(layer.Attribute("ProjAxis")?.Value, out int projAxis);
            layers.Add(new TerrainLayer(
                layers.Count, name, surfaceType,
                layer.Attribute("Texture")?.Value ?? "",
                tiling > 0 ? tiling : 1f,
                projAxis));

            if (layer.Attribute("SurfaceTypeID") is null)
            {
                continue;
            }
            if (!bySurfaceType.TryGetValue(surfaceType, out List<string>? names))
            {
                bySurfaceType[surfaceType] = names = [];
            }
            if (!names.Contains(name))
            {
                names.Add(name);
            }
        }
        return new TerrainLayerTable(bySurfaceType, layers);
    }

    /// <summary>Worlds ship this file in either form, so both are tried before giving up.</summary>
    private static XElement? Parse(byte[] bytes)
    {
        if (RmlDocument.TryDeserialize(bytes, out XElement? rml))
        {
            return rml;
        }

        try
        {
            return XDocument.Parse(Encoding.UTF8.GetString(bytes)).Root;
        }
        catch (Exception e) when (e is System.Xml.XmlException or ArgumentException)
        {
            return null;
        }
    }
}
