using JackAll.Core.Format.Fcb;

namespace JackAll.Tools.World;

/// <summary>
/// CRC32 name hashes of the FCB objects and fields the map editor reads and writes inside
/// <c>worldsector*.data.fcb</c> and <c>entitylibrary.fcb</c> trees.
/// </summary>
public static class WorldHashes
{
    public static readonly uint MissionLayer = FcbClassDefinitions.Crc32Ascii("MissionLayer");
    public static readonly uint Entity = FcbClassDefinitions.Crc32Ascii("Entity");
    public static readonly uint EntityPrototype = FcbClassDefinitions.Crc32Ascii("EntityPrototype");
    public static readonly uint Components = FcbClassDefinitions.Crc32Ascii("Components");

    public static readonly uint TextPathId = FcbClassDefinitions.Crc32Ascii("text_PathId");
    public static readonly uint PathId = FcbClassDefinitions.Crc32Ascii("PathId");
    public static readonly uint DisEntityId = FcbClassDefinitions.Crc32Ascii("disEntityId");
    public static readonly uint HidName = FcbClassDefinitions.Crc32Ascii("hidName");
    public static readonly uint TplCreatureType = FcbClassDefinitions.Crc32Ascii("tplCreatureType");
    public static readonly uint HidPos = FcbClassDefinitions.Crc32Ascii("hidPos");
    public static readonly uint HidPosPrecise = FcbClassDefinitions.Crc32Ascii("hidPos_precise");
    public static readonly uint HidAngles = FcbClassDefinitions.Crc32Ascii("hidAngles");

    public static readonly uint CGraphicComponent = FcbClassDefinitions.Crc32Ascii("CGraphicComponent");
    /// <summary>The .xbg path on a graphics component (or on its per-slot "object" children).</summary>
    public static readonly uint TextObjModel = FcbClassDefinitions.Crc32Ascii("text_objModel");
    /// <summary>The parts of a mesh an entity actually draws, semicolon-delimited. Empty on almost
    /// everything; a wardrobe file needs it to pick one outfit out of the whole rack.</summary>
    public static readonly uint HidMeshName = FcbClassDefinitions.Crc32Ascii("hidMeshName");

    /// <summary>The per-slot child a library archetype's graphics component nests its fields in.</summary>
    public static readonly uint GraphicObject = FcbClassDefinitions.Crc32Ascii("object");
}
