using JackAll.Core.Format.Fcb;

namespace JackAll.Tools.World;

/// <summary>
/// What an entity that resolved to no mesh actually is. Roughly a third of a world's entities draw
/// no geometry, and they are not one undifferentiated pool: each carries a component that names its
/// purpose, and several of them already have a layer of their own drawing them properly.
/// </summary>
public enum EntityCategory
{
    /// <summary>A light, already drawn by the Lights layer.</summary>
    Light,

    /// <summary>A proximity trigger volume, already drawn by the Triggers layer.</summary>
    Trigger,

    /// <summary>A Realtree plant, already drawn by the Vegetation layer.</summary>
    Vegetation,

    /// <summary>A particle or sound emitter.</summary>
    Emitter,

    /// <summary>A building entrance hint - the <c>DOOR</c> and <c>WINDOW</c> nodes the AI navigates
    /// buildings by.</summary>
    Entrance,

    /// <summary>An AI reference point: cover, a guard post, somewhere to lean or sit.</summary>
    Ai,

    /// <summary>Everything left: pure logic and event nodes, the largest group of the lot.</summary>
    Event,
}

/// <summary>Sorts mesh-less entities into <see cref="EntityCategory"/> by the components they
/// carry.</summary>
public static class WorldEntityCategories
{
    private static readonly uint DynamicLight = FcbClassDefinitions.Crc32Ascii("CDynamicLightComponent");
    private static readonly uint ProximityTrigger = FcbClassDefinitions.Crc32Ascii("CProximityTriggerComponent");
    private static readonly uint Realtree = FcbClassDefinitions.Crc32Ascii("CRealtreeComponent");
    private static readonly uint NewParticles = FcbClassDefinitions.Crc32Ascii("CNewParticlesComponent");
    private static readonly uint Sound = FcbClassDefinitions.Crc32Ascii("CSoundComponent");
    private static readonly uint EntranceInfo = FcbClassDefinitions.Crc32Ascii("CEntranceInfoComponent");
    private static readonly uint BuildingInfo = FcbClassDefinitions.Crc32Ascii("CBuildingInfoComponent");
    private static readonly uint Ai = FcbClassDefinitions.Crc32Ascii("CFCXAIComponent");

    /// <summary>
    /// The first match wins, so the order is the point. Entrances are tested before AI because a
    /// <c>BuildingEntranceInformation</c> node carries both an entrance component and an AI one -
    /// classing it as a plain AI point would bury the door and window hints among 3,000 cover
    /// markers. Anything naming none of these is an event node.
    /// </summary>
    public static EntityCategory Of(FcbObject node)
    {
        if (Has(node, DynamicLight)) return EntityCategory.Light;
        if (Has(node, ProximityTrigger)) return EntityCategory.Trigger;
        if (Has(node, Realtree)) return EntityCategory.Vegetation;
        if (Has(node, NewParticles) || Has(node, Sound)) return EntityCategory.Emitter;
        if (Has(node, EntranceInfo) || Has(node, BuildingInfo)) return EntityCategory.Entrance;
        if (Has(node, Ai)) return EntityCategory.Ai;
        return EntityCategory.Event;
    }

    /// <summary>Whether a dedicated layer already draws this category, so the generic marker pass
    /// must leave it alone rather than stacking a second glyph on it.</summary>
    public static bool HasOwnLayer(this EntityCategory category)
        => category is EntityCategory.Light or EntityCategory.Trigger or EntityCategory.Vegetation;

    private static bool Has(FcbObject node, uint component)
        => FcbEntityFields.FindComponent(node, component) is not null;
}
