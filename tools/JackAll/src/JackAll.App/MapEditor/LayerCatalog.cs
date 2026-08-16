namespace JackAll.App.MapEditor;

/// <summary>
/// One editable data layer of an FC2 world, as cataloged in tmp/fc2-map-layers.md. Static mock
/// data for the Map tab layout: the real editing panels replace <see cref="Controls"/> per phase.
/// </summary>
/// <remarks><see cref="IsVisible"/> is the viewport toggle the layer list's checkbox writes; a
/// layer whose renderer exists reads it every frame.</remarks>
public sealed record MapLayer(string Group, string Name, string Readiness, string Summary, string[] Controls)
{
    public bool IsVisible { get; set; } = true;
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
            "How the ground looks: each sector blends up to four layers from the world's 45-entry table, weighted by its atlas mask.",
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

    /// <summary>Draws the entity markers while visible, and owns the browser panel.</summary>
    public static readonly MapLayer Entities =
        new("Objects", "Entities", "Ready",
            "Everything placed in the world; a placed entity is a delta over its archetype.",
            ["Browse and search", "Select from the list or the viewport", "Read-only field view"]);

    /// <summary>Owns the mission-layer list that filters which entities are shown.</summary>
    public static readonly MapLayer MissionLayers =
        new("Objects", "Mission layers", "Ready",
            "Per-sector scoping: main plus mission-keyed overlays deciding which entities exist when.",
            ["List layers with entity counts", "Show or hide a layer's entities", "Assign entities to layers"]);

    /// <summary>Draws the authored polylines while visible.</summary>
    public static readonly MapLayer Shapes =
        new("Objects", "Shapes", "Ready",
            "Authored polylines - zone outlines, paths and sound lines - stored in the world's mapsdata, not per sector.",
            ["Show polylines", "Sound lines tinted apart", "Point editing"]);

    public static readonly MapLayer[] Layers =
    [
        Heightmap,
        SurfaceData,
        Textures,
        Shadow,
        Water,

        Entities,
        MissionLayers,
        Shapes,
        new("Objects", "Entity library", "Ready",
            "The per-world archetype palette every entity references (1,419 in world1).",
            ["Archetype browser by category", "Thumbnails", "Archetype editing"]),

        new("Dressing", "Omni lights", "Partial",
            "World-scope static light placements (omnis.fcb).",
            ["Light list", "Place / edit lights"]),
        new("Dressing", "Vegetation", "Research",
            "8 seeded collection slots scatter vegetation; cooked zone records partially decoded.",
            ["Collection slot / seed editor", "Mask painting", "RESEARCH: cooked record decode"]),
        new("Dressing", "Roads", "Research",
            "Spline-authored roads; the cooked retail representation is unidentified.",
            ["Viewer first", "RESEARCH: find cooked format"]),
        new("Dressing", "Landmarks", "Partial",
            "Distant-silhouette LOD geometry per sector; goes stale when entities change.",
            ["LOD overlay display", "Stale-check after edits"]),

        new("Glue", "Sectors", "Ready",
            "Streaming glue: per-sector neighbors/flags and world sector dependencies.",
            ["Enable all sectors", "Create new sector", "Violation check (entity outside home sector)"]),
        new("Glue", "Preload", "Research",
            "Per-sector streaming prefetch and world resource-dependency lists.",
            ["View lists", "RESEARCH: regenerate after adding archetypes"]),
        new("Glue", "World settings", "Partial",
            "The world descriptor: sky/sun/fog/lighting presets, time of day, terrain layer table.",
            ["Environment preset slots", "Cross-map preset copy", "Terrain layer table"]),
        new("Glue", "Managers", "Partial",
            "World-scope singleton state (73 manager types) - mostly preserve, inspect raw.",
            ["Raw FCB inspection"]),

        new("Systems", "Navmesh", "Research",
            "AI navigation, one file per campaign sector; missing navmesh plus AI entities crashes the game.",
            ["Presence overlay", "AI-safety warning on placement", "RESEARCH: generation"]),
        new("Systems", "Sound regions", "Research",
            "Per-sector audio region data; records undecoded - preserve byte-for-byte.",
            ["Presence view only"]),
        new("Systems", "Cinematics", "Ready",
            "Keyframed sequences (cameras plus objects) at world and level scope.",
            ["Sequence browser", "Scrub / play preview", "Export / import"]),
        new("Systems", "Mission logic", "Partial",
            "Domino graphs wiring missions to map entities - 606 mission graphs, 215 system boxes.",
            ["Open in Domino viewer (exists)", "Per-world master graph list"]),
        new("Systems", "Map texture", "Ready",
            "The in-game map/compass image per level and world (plain xbt).",
            ["View", "Replace"]),
    ];
}
