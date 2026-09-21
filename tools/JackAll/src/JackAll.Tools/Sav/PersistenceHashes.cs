using JackAll.Core.Format.Fcb;

namespace JackAll.Tools.Sav;

/// <summary>CRC32 tags of the nodes and fields <c>CPersistenceDB::SaveDB</c> writes into a save.</summary>
public static class PersistenceHashes
{
    public static readonly uint PersistenceDb = FcbClassDefinitions.Crc32Ascii("PersistenceDB");

    /// <summary>The record containers on every PersistenceDB node.</summary>
    public static readonly uint[] RecordContainers =
    [
        FcbClassDefinitions.Crc32Ascii("HierarchiesQueue"),
        FcbClassDefinitions.Crc32Ascii("Hierarchy"),
        FcbClassDefinitions.Crc32Ascii("Entities"),
        FcbClassDefinitions.Crc32Ascii("OmniEntities"),
    ];

    /// <summary>One persisted entity: its <see cref="Id"/> and the <see cref="State"/> restored over it.</summary>
    public static readonly uint Record = FcbClassDefinitions.Crc32Ascii("Record");
    public static readonly uint State = FcbClassDefinitions.Crc32Ascii("State");
    public static readonly uint Id = FcbClassDefinitions.Crc32Ascii("Id");
}
