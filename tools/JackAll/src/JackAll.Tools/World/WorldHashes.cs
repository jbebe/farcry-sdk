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

    /// <summary>The .xbg path inside an archetype template's graphics component; the field's source
    /// string is unknown (no candidate name hashes to it), so it stays a literal.</summary>
    public const uint MeshFileName = 0xBF9B3A5C;
}
