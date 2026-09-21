using JackAll.Tools.World;

namespace JackAll.App.MapEditor.Gl;

/// <summary>The instance streams and outlines the marker layers draw, and the one-line summaries of
/// what a loaded map holds of each.</summary>
internal static class WorldMarkers
{
    /// <summary>The glyph, layer toggle and colour each drawn category gets. Categories a dedicated
    /// layer already owns are absent, which is what keeps a light from being drawn twice.</summary>
    public static readonly (EntityCategory Category, MarkerGlyph Glyph, MapLayer Layer, float R, float G, float B)[]
        DrawnCategories =
        [
            (EntityCategory.Event, MarkerGlyph.Diamond, LayerCatalog.EventNodes, 0.55f, 0.60f, 0.70f),
            (EntityCategory.Ai, MarkerGlyph.Cone, LayerCatalog.AiPoints, 0.45f, 0.75f, 0.55f),
            (EntityCategory.Entrance, MarkerGlyph.Doorway, LayerCatalog.Entrances, 0.85f, 0.70f, 0.35f),
            (EntityCategory.Emitter, MarkerGlyph.Burst, LayerCatalog.Emitters, 0.80f, 0.50f, 0.75f),
        ];

    /// <summary>Refills each category layer from the entities the model layer could not draw. Runs
    /// only on a marker rebuild, so it costs nothing per frame.</summary>
    public static void RebuildCategoryMarkers(
        IEnumerable<WorldEntity> visible, WorldModelSet models, IReadOnlyDictionary<EntityCategory, EntityMarkerLayer> layers)
    {
        var byCategory = new Dictionary<EntityCategory, List<WorldEntity>>();
        foreach (WorldEntity entity in visible)
        {
            if (models.ModelIndicesByEntity.ContainsKey(entity))
            {
                continue;
            }

            EntityCategory category = WorldEntityCategories.Of(entity.Node);
            if (category.HasOwnLayer())
            {
                continue;
            }

            if (!byCategory.TryGetValue(category, out List<WorldEntity>? bucket))
            {
                byCategory[category] = bucket = [];
            }
            bucket.Add(entity);
        }

        foreach ((EntityCategory category, _, _, float r, float g, float b) in DrawnCategories)
        {
            if (!layers.TryGetValue(category, out EntityMarkerLayer? layer))
            {
                continue;
            }

            List<WorldEntity> entities = byCategory.GetValueOrDefault(category) ?? [];
            var stream = new float[Math.Max(1, entities.Count) * EntityMarkerLayer.Stride];
            for (int i = 0; i < entities.Count; i++)
            {
                System.Numerics.Vector3 position = entities[i].Position!.Value;
                int at = i * EntityMarkerLayer.Stride;
                stream[at] = position.X;
                stream[at + 1] = position.Y;
                stream[at + 2] = position.Z;
                stream[at + 3] = r;
                stream[at + 4] = g;
                stream[at + 5] = b;
            }
            layer.SetInstances(stream, entities.Count);
        }
    }

    /// <summary>Model tint keyed to the archetype, so the same kind of object reads the same everywhere.</summary>
    public static (byte R, byte G, byte B) MarkerColour(WorldEntity entity)
        => SurfaceTypeTexture.ColourFor((byte)(StableHash(entity.ArchetypeName) & 0x7F));

    /// <summary>One marker per plant, coloured by the resource it instantiates so a species reads
    /// the same across the map.</summary>
    public static float[] BuildVegetationMarkers(IReadOnlyList<VegetationInstance> vegetation)
    {
        var stream = new float[vegetation.Count * EntityMarkerLayer.Stride];
        for (int i = 0; i < vegetation.Count; i++)
        {
            VegetationInstance plant = vegetation[i];
            (byte r, byte g, byte b) = SurfaceTypeTexture.ColourFor((byte)(plant.ResourceId & 0x7F));
            int at = i * EntityMarkerLayer.Stride;
            stream[at] = plant.Position.X;
            stream[at + 1] = plant.Position.Y;
            stream[at + 2] = plant.Position.Z;
            stream[at + 3] = r / 255f;
            stream[at + 4] = g / 255f;
            stream[at + 5] = b / 255f;
        }
        return stream;
    }

    /// <summary>Each trigger box as its twelve edges: the two rectangles plus the four uprights.
    /// Reusing the polyline layer keeps this to line data rather than a renderer of its own.</summary>
    public static List<WorldShape> BuildTriggerOutlines(IReadOnlyList<TriggerVolume> triggers)
    {
        var outlines = new List<WorldShape>(triggers.Count * 6);
        foreach (TriggerVolume trigger in triggers)
        {
            System.Numerics.Vector3[] c = trigger.Corners();
            string kind = trigger.Enabled ? "trigger" : "trigger-off";

            outlines.Add(new WorldShape(kind, trigger.Name, "box", [c[0], c[1], c[3], c[2], c[0]]));
            outlines.Add(new WorldShape(kind, trigger.Name, "box", [c[4], c[5], c[7], c[6], c[4]]));
            for (int i = 0; i < 4; i++)
            {
                outlines.Add(new WorldShape(kind, trigger.Name, "box", [c[i], c[i + 4]]));
            }
        }
        return outlines;
    }

    /// <summary>One marker per light in its own emitted colour, dimmed hard when the light ships
    /// disabled so the two read apart at a glance.</summary>
    public static float[] BuildLightMarkers(IReadOnlyList<WorldLight> lights)
    {
        var stream = new float[lights.Count * EntityMarkerLayer.Stride];
        for (int i = 0; i < lights.Count; i++)
        {
            WorldLight light = lights[i];
            float dim = light.Enabled ? 1f : 0.2f;
            int at = i * EntityMarkerLayer.Stride;
            stream[at] = light.Position.X;
            stream[at + 1] = light.Position.Y;
            stream[at + 2] = light.Position.Z;
            stream[at + 3] = light.Colour.X * dim;
            stream[at + 4] = light.Colour.Y * dim;
            stream[at + 5] = light.Colour.Z * dim;
        }
        return stream;
    }

    public static string Describe(NavMesh mesh) => mesh.NodeCount == 0
        ? "No navmesh in this map."
        : $"{mesh.NodeCount:N0} triangles, {mesh.Vertices.Length:N0} vertices and " +
          $"{mesh.LinkCount:N0} links across {mesh.SectorCount:N0} sectors.";

    public static string Describe(IReadOnlyList<TriggerVolume> triggers)
    {
        if (triggers.Count == 0)
        {
            return "No proximity triggers in this map.";
        }

        int off = triggers.Count(t => !t.Enabled);
        int rotated = triggers.Count(t => t.Yaw != 0);
        return $"{triggers.Count:N0} proximity triggers; {rotated:N0} rotated" +
               (off > 0 ? $", {off:N0} start disabled." : ".") +
               " vectorSize is drawn as the box's full extent, centred on the entity - neither is " +
               "confirmed from the engine.";
    }

    public static string Describe(IReadOnlyList<WorldLight> lights)
    {
        if (lights.Count == 0)
        {
            return "No lights in this map.";
        }

        int spots = lights.Count(l => l.IsSpot);
        int off = lights.Count(l => !l.Enabled);
        return $"{lights.Count:N0} lights: {lights.Count - spots:N0} omni, {spots:N0} spot" +
               (off > 0 ? $"; {off:N0} start disabled." : ".");
    }

    private static uint StableHash(string text)
    {
        uint hash = 2166136261;
        foreach (char c in text)
        {
            hash = (hash ^ c) * 16777619;
        }
        return hash;
    }
}
