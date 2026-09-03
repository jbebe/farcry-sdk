namespace JackAll.Core.Format.Fcb;

/// <summary>
/// CRC32 name hashes of the FCB objects and fields read and written inside
/// <c>worldsector*.data.fcb</c> and <c>entitylibrary.fcb</c> trees.
/// </summary>
public static class WorldHashes
{
    public static readonly uint WorldSector = FcbClassDefinitions.Crc32Ascii("WorldSector");
    public static readonly uint MissionLayer = FcbClassDefinitions.Crc32Ascii("MissionLayer");
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

    /// <summary>The component carrying an entity's mission-layer path. It files a live entity into a
    /// layer; it does not decide which layer's data the entity is spawned from - see
    /// docs/docs/engine-internals/entity-instancing.md.</summary>
    public static readonly uint CMissionComponent = FcbClassDefinitions.Crc32Ascii("CMissionComponent");
    public static readonly uint HidMissionLayerPath = FcbClassDefinitions.Crc32Ascii("hidMissionLayerPath");

    public static readonly uint CGraphicComponent = FcbClassDefinitions.Crc32Ascii("CGraphicComponent");
    /// <summary>The .xbg path on a graphics component (or on its per-slot "object" children).</summary>
    public static readonly uint TextObjModel = FcbClassDefinitions.Crc32Ascii("text_objModel");
    /// <summary>The parts of a mesh an entity actually draws, semicolon-delimited. Empty on almost
    /// everything; a wardrobe file needs it to pick one outfit out of the whole rack.</summary>
    public static readonly uint HidMeshName = FcbClassDefinitions.Crc32Ascii("hidMeshName");

    /// <summary>The per-slot child a library archetype's graphics component nests its fields in.</summary>
    public static readonly uint GraphicObject = FcbClassDefinitions.Crc32Ascii("object");
}
