using System.ComponentModel;

namespace JackAll.App.MapEditor;

/// <summary>
/// One editable data layer of an FC2 world, as cataloged in tmp/fc2-map-layers.md. Static mock
/// data for the Map tab layout: the real editing panels replace <see cref="Controls"/> per phase.
/// </summary>
/// <remarks><see cref="IsVisible"/> is the viewport toggle the layer list's checkbox writes; a
/// layer whose renderer exists reads it every frame.</remarks>
/// <remarks>A class rather than a record because that mutable property would otherwise join the
/// synthesized equality: toggling a checkbox would change the item's hash code underneath the
/// grouped collection view holding it, and the list would wedge.</remarks>
public sealed class MapLayer(string group, string name, string readiness, string summary, string[] controls)
    : INotifyPropertyChanged
{
    private bool _isVisible = true;

    public string Group { get; } = group;
    public string Name { get; } = name;
    public string Readiness { get; } = readiness;
    public string Summary { get; } = summary;
    public string[] Controls { get; } = controls;

    /// <summary>Whether a renderer reads <see cref="IsVisible"/> every frame. The rows that draw
    /// nothing are the editor's roadmap - worth listing, but a checkbox on one is a control that
    /// silently does nothing, which is worse than no control.</summary>
    public bool Drawn { get; init; } = true;

    /// <summary>Shown only where the toggle means something.</summary>
    public System.Windows.Visibility ToggleVisibility
        => Drawn ? System.Windows.Visibility.Visible : System.Windows.Visibility.Collapsed;

    /// <summary>Raises a change so code that clears the layers moves their checkboxes too; the
    /// checkbox alone would not need it.</summary>
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
        new("Terrain", "Heightmap", "Ready",
            "The ground itself - 65x65 heights per sector, stitched into one field per map.",
            ["Brush tools: raise / lower / flatten / smooth", "Brush size, strength, flatten target height", "Extended brushes: blur, smear, airbrush, hill, slope"]);

    /// <summary>Tints the terrain by surface type while visible. Off by default now that the real
    /// textures draw - the two compete for the same pixels.</summary>
    public static readonly MapLayer SurfaceData =
        new("Terrain", "Surface data", "Ready",
            "What the ground is made of - the surface type under every sample, which drives footsteps, impacts and fire.",
            ["Tint terrain by surface type", "Legend with coverage", "Surface painting", "Hole editing"])
        { IsVisible = false };

    /// <summary>Draws the real blended terrain textures while visible.</summary>
    public static readonly MapLayer Textures =
        new("Terrain", "Textures", "Ready",
            "How the ground looks: each sector blends up to four layers from the world's 45-entry table, "
            + "weighted by its atlas mask, fading into the sector's baked albedo with distance the way the game does.",
            ["Show real terrain textures", "Layer table browser", "Mask channel painting", "Per-sector layer assignment"]);

    /// <summary>Multiplies the baked lighting into the terrain while visible.</summary>
    public static readonly MapLayer Shadow =
        new("Terrain", "Shadow", "Ready",
            "Baked lighting, one 64x64 map per sector, multiplied over the terrain.",
            ["Show baked lighting", "Second channel is unidentified", "Regeneration after sculpting (unsolved)"]);

    /// <summary>Draws the water surfaces while visible.</summary>
    public static readonly MapLayer Water =
        new("Terrain", "Water", "Ready",
            "Per-sector water: a still or river flag, the surface height, and the material it uses.",
            ["Show water surfaces", "Per-sector height and material", "Enable / disable per sector"]);

    /// <summary>Draws the entity models and markers while visible, and owns the browser panel.</summary>
    public static readonly MapLayer Entities =
        new("Geometry", "Entities", "Ready",
            "Everything placed in the world; a placed entity is a delta over its archetype.",
            ["Draw as real models, markers, or both", "Browse and search", "Select from the list or the viewport", "Read-only field view"]);

    /// <summary>Owns the mission-layer list that filters which entities are shown.</summary>
    public static readonly MapLayer MissionLayers =
        new("Geometry", "Mission layers", "Ready",
            "Per-sector scoping: main plus mission-keyed overlays deciding which entities exist when.",
            ["List layers with entity counts", "Show or hide a layer's entities", "Assign entities to layers"]);

    /// <summary>Draws the authored polylines while visible.</summary>
    public static readonly MapLayer Shapes =
        new("Geometry", "Shapes", "Ready",
            "Authored polylines - zone outlines, paths and sound lines - stored in the world's mapsdata, not per sector.",
            ["Show polylines", "Sound lines tinted apart", "Point editing"]);

    /// <summary>Draws the road, river and path splines while visible.</summary>
    public static readonly MapLayer Roads =
        new("Geometry", "Roads, rivers, paths", "Ready",
            "Spline sets in the world's mapsdata: roads amber, rivers blue, foot paths violet.",
            ["Show splines", "Control points carry position, tangent and widths", "Spline editing"]);

    /// <summary>Draws the scatter while visible, every resource as its own geometry.</summary>
    public static readonly MapLayer Vegetation =
        new("Geometry", "Vegetation", "Ready",
            "The per-sector scatter from the landmark files - rocks, grasses and bushes as their own "
            + "meshes, and the RealTree species as theirs: the branch skeleton drawn as tubes, hung "
            + "with the leaf cards or modelled leaves the file authored.",
            ["Draw every placed resource", "RealTree (.rtx) draws as its own species",
             "Placement editing"]);

    /// <summary>Draws the proximity trigger boxes as wireframes while visible.</summary>
    public static readonly MapLayer Triggers =
        new("Markers", "Trigger boxes", "Ready",
            "The volumes that fire when something enters them - proximity triggers on ordinary entities.",
            ["Show trigger boxes", "Disabled triggers dimmed", "vectorSize read as full extent (unconfirmed)"]);

    /// <summary>Draws a marker per placed light while visible, in the light's own colour.</summary>
    public static readonly MapLayer Lights =
        new("Markers", "Lights", "Ready",
            "Every placed light, from the CDynamicLightComponent on ordinary entities - omni (point) and spot.",
            ["Show lights in their own colour", "Disabled lights dimmed", "Radius and cone not drawn yet"]);

    /// <summary>
    /// The four categories of mesh-less entity that no other layer draws. They are split rather
    /// than pooled because they are what a third of a world's entities are, and one undifferentiated
    /// field of markers over them is unreadable - the AI points alone outnumber every light,
    /// trigger and entrance put together. Each draws its own glyph at a fixed size on screen.
    /// </summary>
    public static readonly MapLayer EventNodes =
        new("Markers", "Event nodes", "Ready",
            "Logic-only entities - the largest mesh-less group, drawn as a diamond.",
            ["Show event nodes", "Fixed size on screen", "Event graph not yet readable"])
        { IsVisible = false };

    public static readonly MapLayer AiPoints =
        new("Markers", "AI points", "Ready",
            "Cover, guard posts and the lean and sit reference points, drawn as a cone.",
            ["Show AI reference points", "Fixed size on screen", "Facing direction not yet drawn"])
        { IsVisible = false };

    public static readonly MapLayer Entrances =
        new("Markers", "Entrances", "Ready",
            "The DOOR and WINDOW hints the AI navigates buildings by, drawn as a doorway.",
            ["Show entrance hints", "Fixed size on screen", "Link back to the building not drawn"])
        { IsVisible = false };

    public static readonly MapLayer Emitters =
        new("Markers", "Emitters", "Ready",
            "Particle and sound sources, drawn as a burst.",
            ["Show emitters", "Fixed size on screen", "Effect and sound names not yet resolved"])
        { IsVisible = false };

    /// <summary>Draws a marker per walkable node while visible, green where the ground is flat
    /// enough to walk and red where the engine's slope limit rejects it.</summary>
    public static readonly MapLayer NavMesh =
        new("Markers", "Navmesh", "Ready",
            "Where the AI can walk - one node per walkable patch, only on campaign sectors.",
            ["Show walkable nodes", "Steep nodes tinted red", "Generation after sculpting (unsolved)"])
        { IsVisible = false };

    public static readonly MapLayer[] Layers =
    [
        // Grouped by what the list is actually for: what the viewport draws, and in what form.
        // Geometry is the world's own surfaces, Markers is everything drawn as an indicator over
        // them, and Planned is the roadmap - rows that carry no renderer and so no toggle.
        Heightmap,
        SurfaceData,
        Textures,
        Shadow,
        Water,

        Entities,
        MissionLayers,
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

        new("Planned", "Entity library", "Library tab",
            "The archetype palette every entity references, resolved across its override chain.",
            ["Browse and edit it in the Library tab"]) { Drawn = false },
        new("Planned", "Landmarks", "Partial",
            "Per-sector distant-silhouette LOD. The geometry itself now draws under Entities, which "
            + "reads the landmark files; what is left here is keeping it from going stale after edits.",
            ["Stale-check after edits", "Regenerate a sector's landmark set"]) { Drawn = false },
        new("Planned", "Sectors", "Ready",
            "Streaming glue: per-sector neighbors/flags and world sector dependencies.",
            ["Enable all sectors", "Create new sector", "Violation check (entity outside home sector)"])
            { Drawn = false },
        new("Planned", "Preload", "Research",
            "Per-sector streaming prefetch and world resource-dependency lists.",
            ["View lists", "RESEARCH: regenerate after adding archetypes"]) { Drawn = false },
        new("Planned", "World settings", "Partial",
            "The world descriptor: sky/sun/fog/lighting presets, time of day, terrain layer table.",
            ["Environment preset slots", "Cross-map preset copy", "Terrain layer table"]) { Drawn = false },
        new("Planned", "Managers", "Partial",
            "World-scope singleton state (73 manager types) - mostly preserve, inspect raw.",
            ["Raw FCB inspection"]) { Drawn = false },
        new("Planned", "Sound regions", "Research",
            "Per-sector audio region data; records undecoded - preserve byte-for-byte.",
            ["Presence view only"]) { Drawn = false },
        new("Planned", "Cinematics", "Ready",
            "Keyframed sequences (cameras plus objects) at world and level scope.",
            ["Sequence browser", "Scrub / play preview", "Export / import"]) { Drawn = false },
        new("Planned", "Mission logic", "Partial",
            "Domino graphs wiring missions to map entities - 606 mission graphs, 215 system boxes.",
            ["Open in Domino viewer (exists)", "Per-world master graph list"]) { Drawn = false },
        new("Planned", "Map texture", "Ready",
            "The in-game map/compass image per level and world (plain xbt).",
            ["View", "Replace"]) { Drawn = false },
    ];
}
