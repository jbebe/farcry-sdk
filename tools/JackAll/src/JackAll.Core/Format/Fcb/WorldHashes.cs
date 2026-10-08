namespace JackAll.Core.Format.Fcb;

/// <summary>
/// CRC32 name hashes of the FCB objects and fields read and written inside
/// <c>worldsector*.data.fcb</c> and <c>entitylibrary.fcb</c> trees.
/// </summary>
public static class WorldHashes
{
    public static readonly uint WorldSector = FcbClassDefinitions.Crc32Ascii("WorldSector");
    public static readonly uint MissionLayer = FcbClassDefinitions.Crc32Ascii("MissionLayer");

    /// <summary>The root of a world's <c>*.mapsdata.fcb</c>, whose children are the level cells that
    /// hold its mission layers.</summary>
    public static readonly uint MapsData = FcbClassDefinitions.Crc32Ascii("mapsdata");

    /// <summary>The roots of a world's <c>*.omnis.fcb</c> and <c>*.managers.fcb</c>. Capitalised
    /// unlike <see cref="MapsData"/>; both names are confirmed by FC3/FC4 class files.</summary>
    public static readonly uint Omnis = FcbClassDefinitions.Crc32Ascii("Omnis");
    public static readonly uint Managers = FcbClassDefinitions.Crc32Ascii("Managers");
    public static readonly uint Entity = FcbClassDefinitions.Crc32Ascii("Entity");
    public static readonly uint EntityPrototype = FcbClassDefinitions.Crc32Ascii("EntityPrototype");
    public static readonly uint Components = FcbClassDefinitions.Crc32Ascii("Components");

    /// <summary>The root of a library, whose children are the <see cref="EntityLibrary"/> groups.</summary>
    public static readonly uint EntityLibraries = FcbClassDefinitions.Crc32Ascii("EntityLibraries");
    /// <summary>One group inside an <see cref="EntityLibraries"/> root, not the root itself.</summary>
    public static readonly uint EntityLibrary = FcbClassDefinitions.Crc32Ascii("EntityLibrary");

    public static readonly uint TextPathId = FcbClassDefinitions.Crc32Ascii("text_PathId");
    public static readonly uint PathId = FcbClassDefinitions.Crc32Ascii("PathId");
    public static readonly uint DisEntityId = FcbClassDefinitions.Crc32Ascii("disEntityId");
    public static readonly uint HidName = FcbClassDefinitions.Crc32Ascii("hidName");
    /// <summary>What an <see cref="EntityLibrary"/> group and an <see cref="EntityPrototype"/> call
    /// themselves, unlike a placed entity's <see cref="HidName"/>.</summary>
    public static readonly uint Name = FcbClassDefinitions.Crc32Ascii("Name");
    public static readonly uint TplCreatureType = FcbClassDefinitions.Crc32Ascii("tplCreatureType");
    public static readonly uint HidPos = FcbClassDefinitions.Crc32Ascii("hidPos");
    public static readonly uint HidPosPrecise = FcbClassDefinitions.Crc32Ascii("hidPos_precise");
    public static readonly uint HidAngles = FcbClassDefinitions.Crc32Ascii("hidAngles");
    public static readonly uint HidEntityClass = FcbClassDefinitions.Crc32Ascii("hidEntityClass");

    /// <summary>The class name <see cref="HidEntityClass"/> hashes, which a class-bound entity such as a prefab carries.</summary>
    public static readonly uint TextHidEntityClass = FcbClassDefinitions.Crc32Ascii("text_hidEntityClass");
    public static readonly uint HidResourceCount = FcbClassDefinitions.Crc32Ascii("hidResourceCount");
    public static readonly uint HidConstEntity = FcbClassDefinitions.Crc32Ascii("hidConstEntity");

    /// <summary>The list of entities a prefab entity owns, one <c>Child</c> per owned id.</summary>
    public static readonly uint EntityChildren = FcbClassDefinitions.Crc32Ascii("Children");

    /// <summary>The component carrying an entity's mission-layer path. It files a live entity into a
    /// layer; it does not decide which layer's data the entity is spawned from - see
    /// docs/docs/engine-internals/entity-instancing.md.</summary>
    public static readonly uint CMissionComponent = FcbClassDefinitions.Crc32Ascii("CMissionComponent");
    public static readonly uint HidMissionLayerPath = FcbClassDefinitions.Crc32Ascii("hidMissionLayerPath");

    public static readonly uint CGraphicComponent = FcbClassDefinitions.Crc32Ascii("CGraphicComponent");

    /// <summary>The only trigger with geometry: a <see cref="VectorSize"/> box turned by the entity's yaw.</summary>
    public static readonly uint CProximityTriggerComponent = FcbClassDefinitions.Crc32Ascii("CProximityTriggerComponent");
    public static readonly uint VectorSize = FcbClassDefinitions.Crc32Ascii("vectorSize");

    /// <summary>Every placed light: <see cref="HidType"/> 1 omni, 3 spot, reaching <see cref="FRadius"/>.</summary>
    public static readonly uint CDynamicLightComponent = FcbClassDefinitions.Crc32Ascii("CDynamicLightComponent");
    public static readonly uint HidType = FcbClassDefinitions.Crc32Ascii("hidType");
    public static readonly uint FRadius = FcbClassDefinitions.Crc32Ascii("fRadius");

    /// <summary>The component every shipped placed instance carries, with its (usually empty) links.</summary>
    public static readonly uint CEventComponent = FcbClassDefinitions.Crc32Ascii("CEventComponent");
    public static readonly uint HidLinks = FcbClassDefinitions.Crc32Ascii("hidLinks");
    public static readonly uint HidHasAliasName = FcbClassDefinitions.Crc32Ascii("hidHasAliasName");
    /// <summary>The .xbg path on a graphics component (or on its per-slot "object" children).</summary>
    public static readonly uint TextObjModel = FcbClassDefinitions.Crc32Ascii("text_objModel");
    /// <summary>The parts of a mesh an entity actually draws, semicolon-delimited. Empty on almost
    /// everything; a wardrobe file needs it to pick one outfit out of the whole rack.</summary>
    public static readonly uint HidMeshName = FcbClassDefinitions.Crc32Ascii("hidMeshName");

    /// <summary>The per-slot child a library archetype's graphics component nests its fields in.</summary>
    public static readonly uint GraphicObject = FcbClassDefinitions.Crc32Ascii("object");

    /// <summary>A character's kit picks: one <see cref="ActivePartOverwrite"/> per part, naming it by
    /// <see cref="PartID"/> and choosing a <see cref="TextureIndex"/> and <see cref="ColorIndex"/>
    /// into the kit's libraries.</summary>
    public static readonly uint CGraphicKitComponent = FcbClassDefinitions.Crc32Ascii("CGraphicKitComponent");
    public static readonly uint PartOverwrite = FcbClassDefinitions.Crc32Ascii("PartOverwrite");
    public static readonly uint ActivePartOverwrite = FcbClassDefinitions.Crc32Ascii("ActivePartOverwrite");
    public static readonly uint PartID = FcbClassDefinitions.Crc32Ascii("PartID");
    public static readonly uint TextureIndex = FcbClassDefinitions.Crc32Ascii("TextureIndex");
    public static readonly uint ColorIndex = FcbClassDefinitions.Crc32Ascii("ColorIndex");

    /// <summary>Where an archetype embeds its kit descriptor, as an Rml <see cref="HidDescriptor"/>.</summary>
    public static readonly uint CFileDescriptorComponent = FcbClassDefinitions.Crc32Ascii("CFileDescriptorComponent");
    public static readonly uint HidDescriptor = FcbClassDefinitions.Crc32Ascii("hidDescriptor");

    /// <summary>FCSE's entity-data component, which a mod adds - see <see cref="FcseEntityData"/>.</summary>
    public static readonly uint CFCSEDataComponent = FcbClassDefinitions.Crc32Ascii("CFCSEDataComponent");
}
