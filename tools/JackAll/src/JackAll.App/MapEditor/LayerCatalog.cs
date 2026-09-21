using System.ComponentModel;

namespace JackAll.App.MapEditor;

/// <summary>One drawn layer of an FC2 world, with the viewport toggle the Layers menu writes.</summary>
/// <remarks>A class rather than a record because the mutable properties would otherwise join the
/// synthesized equality: toggling a checkbox would change the item's hash code underneath the
/// grouped collection view holding it, and the list would wedge.</remarks>
public sealed class MapLayer(string group, string name, string summary) : INotifyPropertyChanged
{
    private bool _isVisible = true;
    private string _status = "";

    public string Group { get; } = group;
    public string Name { get; } = name;

    /// <summary>What the layer is, then what the loaded map holds of it.</summary>
    public string ToolTip => _status.Length == 0 ? summary : $"{summary}\n\n{_status}";

    /// <summary>What the loaded map holds of this layer, set once it loads.</summary>
    public string Status
    {
        get => _status;
        set
        {
            _status = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ToolTip)));
        }
    }

    /// <summary>Raises a change so code that clears the layers moves their checkboxes too.</summary>
    public bool IsVisible
    {
        get => _isVisible;
        set
        {
            if (_isVisible == value)
            {
                return;
            }
            _isVisible = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsVisible)));
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
}

public static class LayerCatalog
{
    public static readonly MapLayer Heightmap =
        new("Terrain", "Heightmap", "The ground itself - 65x65 heights per sector, stitched into one field per map.");

    /// <summary>Tints the terrain by surface type while visible. Off by default now that the real
    /// textures draw - the two compete for the same pixels.</summary>
    public static readonly MapLayer SurfaceData =
        new("Terrain", "Surface data",
            "What the ground is made of - the surface type under every sample, which drives footsteps, impacts and fire.")
        { IsVisible = false };

    public static readonly MapLayer Textures =
        new("Terrain", "Textures",
            "How the ground looks: each sector blends up to four layers from the world's 45-entry table, "
            + "weighted by its atlas mask, fading into the sector's baked albedo with distance the way the game does.");

    public static readonly MapLayer Shadow =
        new("Terrain", "Shadow", "Baked lighting, one 64x64 map per sector, multiplied over the terrain.");

    public static readonly MapLayer Water =
        new("Terrain", "Water", "Per-sector water: a still or river flag, the surface height, and the material it uses.");

    public static readonly MapLayer Entities =
        new("Geometry", "Entities", "Everything placed in the world, drawn as its real model where it has one.");

    public static readonly MapLayer Shapes =
        new("Geometry", "Shapes",
            "Authored polylines - zone outlines, paths and sound lines - stored in the world's mapsdata, not per sector.");

    public static readonly MapLayer Roads =
        new("Geometry", "Roads, rivers, paths",
            "Spline sets in the world's mapsdata: roads amber, rivers blue, foot paths violet.");

    public static readonly MapLayer Vegetation =
        new("Geometry", "Vegetation",
            "The per-sector scatter from the landmark files - rocks, grasses and bushes as their own "
            + "meshes, and the RealTree species as theirs.");

    public static readonly MapLayer Triggers =
        new("Markers", "Trigger boxes",
            "The volumes that fire when something enters them - proximity triggers on ordinary entities.");

    public static readonly MapLayer Lights =
        new("Markers", "Lights",
            "Every placed light, from the CDynamicLightComponent on ordinary entities, in its own colour.");

    /// <summary>
    /// The four categories of mesh-less entity that no other layer draws. They are split rather
    /// than pooled because they are what a third of a world's entities are, and one undifferentiated
    /// field of markers over them is unreadable.
    /// </summary>
    public static readonly MapLayer EventNodes =
        new("Markers", "Event nodes", "Logic-only entities - the largest mesh-less group, drawn as a diamond.")
        { IsVisible = false };

    public static readonly MapLayer AiPoints =
        new("Markers", "AI points", "Cover, guard posts and the lean and sit reference points, drawn as a cone.")
        { IsVisible = false };

    public static readonly MapLayer Entrances =
        new("Markers", "Entrances", "The DOOR and WINDOW hints the AI navigates buildings by, drawn as a doorway.")
        { IsVisible = false };

    public static readonly MapLayer Emitters =
        new("Markers", "Emitters", "Particle and sound sources, drawn as a burst.")
        { IsVisible = false };

    public static readonly MapLayer NavMesh =
        new("Markers", "Navmesh",
            "Where the AI can walk - the triangle mesh each campaign sector ships, and which triangles connect.")
        { IsVisible = false };

    public static readonly MapLayer[] Layers =
    [
        Heightmap,
        SurfaceData,
        Textures,
        Shadow,
        Water,

        Entities,
        Vegetation,
        Shapes,
        Roads,

        Lights,
        Triggers,
        Emitters,
        AiPoints,
        Entrances,
        EventNodes,
        NavMesh,
    ];
}
