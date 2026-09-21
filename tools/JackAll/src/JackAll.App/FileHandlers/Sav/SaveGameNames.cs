using JackAll.App.FileHandlers.Fcb;
using JackAll.Core.Format.Fcb;

namespace JackAll.App.FileHandlers.Sav;

/// <summary>
/// Names what class scoping cannot in a save's <c>PersistenceDB</c>, whose bookkeeping objects resolve
/// to no known class. In order: the first class member of that hash whose type the bytes fit, then the
/// persistence tags, then the names recovered from the binaries. A value with no fitting type reads
/// as String when it is printable text, else BinHex. See docs/docs/file-formats/savegame.md.
/// </summary>
internal sealed class SaveGameNames(FcbClassDefinitions definitions) : IFcbNames
{
    public static readonly Lazy<SaveGameNames> Shared = new(() => new SaveGameNames(FcbDefinitionsProvider.Value.Value));

    private readonly Lazy<(Dictionary<uint, string> Classes, Dictionary<uint, List<FcbMember>> Members)> _flat =
        new(() => Flatten(definitions));

    public string? ClassName(uint hash)
        => _flat.Value.Classes.GetValueOrDefault(hash)
           ?? SaveGamePersistenceTags.ByHash.GetValueOrDefault(hash)
           ?? SaveGameCompiledFieldNames.ByHash.GetValueOrDefault(hash);

    public (string? Name, FcbMemberType Type) Member(uint hash, byte[] value)
    {
        List<FcbMember> declared = _flat.Value.Members.GetValueOrDefault(hash) ?? [];
        if (declared.FirstOrDefault(m => m.Type != FcbMemberType.BinHex && Fits(m.Type, value)) is { } fitting)
        {
            return (fitting.Name, fitting.Type);
        }

        string? name = SaveGamePersistenceTags.ByHash.GetValueOrDefault(hash)
            ?? SaveGameCompiledFieldNames.ByHash.GetValueOrDefault(hash)
            ?? declared.FirstOrDefault(m => m.Name is not null)?.Name;
        return (name, IsPrintableText(value) ? FcbMemberType.String : FcbMemberType.BinHex);
    }

    private static bool Fits(FcbMemberType type, byte[] value) => type == FcbMemberType.String
        ? IsPrintableText(value)
        : FcbValueCodec.TryDecode(type, value, out _);

    private static (Dictionary<uint, string>, Dictionary<uint, List<FcbMember>>) Flatten(FcbClassDefinitions definitions)
    {
        var classes = new Dictionary<uint, string>();
        var members = new Dictionary<uint, List<FcbMember>>();
        foreach (FcbClass cls in definitions.AllClasses())
        {
            if (cls.Name is { } name)
            {
                classes.TryAdd(FcbClassDefinitions.Crc32Ascii(name), name);
            }
            foreach ((uint hash, FcbMember member) in cls.AllMembers())
            {
                if (!members.TryGetValue(hash, out List<FcbMember>? all))
                {
                    members[hash] = all = [];
                }
                if (!all.Any(m => m.Name == member.Name && m.Type == member.Type))
                {
                    all.Add(member);
                }
            }
        }
        return (classes, members);
    }

    /// <summary>NUL-terminated printable ASCII - the one guess safe to make about an untyped value.</summary>
    private static bool IsPrintableText(byte[] value)
        => value is [.., 0] && value.AsSpan(0, value.Length - 1).IndexOfAnyExceptInRange((byte)0x20, (byte)0x7E) < 0;
}

/// <summary>
/// The structural tags <c>CPersistenceDB::SaveDB</c> and its two record classes write, each confirmed
/// by its hash appearing in real saves. See docs/docs/file-formats/savegame.md.
/// </summary>
internal static class SaveGamePersistenceTags
{
    public static readonly IReadOnlyDictionary<uint, string> ByHash = new Dictionary<uint, string>
    {
        // Id, EntityId, State and Description are left to binary_classes.xml, which also types them.
        [0xA9100FC2] = "HierarchyId",
        [0x9C989AA7] = "Record",
        [0x7A2B069C] = "HierarchyRecord",
        [0xA99A06B3] = "Entities",
        [0x788BAA0D] = "Hierarchy",
        [0x7C1C0FBA] = "HierarchiesQueue",
        [0x5134EF37] = "OmniEntities",

        // The two record classes' registered members; BindingHierarchy never appears as a value.
        [0x65A0E5B6] = "MemoryUsage",
        [0x4A1FC981] = "PersistType",
    };
}
