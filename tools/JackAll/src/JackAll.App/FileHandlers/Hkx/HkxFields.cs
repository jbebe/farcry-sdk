using JackAll.App.FileHandlers.Fcb.FcbEditor;
using JackAll.Core.Format.Fcb;
using JackAll.Tools.Hkx;

namespace JackAll.App.FileHandlers.Hkx;

/// <summary>The HKX inspector's editors: one of the FCB editor's property rows for each plain saved value.</summary>
internal static class HkxFields
{
    /// <summary>
    /// A row editing the value a dump line shows, handing each valid edit's bytes to <paramref name="write"/>; null for
    /// a value that stays read-only: a pointer, array, string or matrix.
    /// </summary>
    public static PropertyRow? For(HkxObject o, HkxDumpLine line, Action<byte[]> write)
    {
        if (Field(o, line) is not var (type, bytes, names, save))
        {
            return null;
        }
        PropertyRow row = PropertyRow.Build(0, line.Path, type, bytes, null, names);
        row.Changed += () =>
        {
            if (row.IsRowValid)
            {
                write(save(row.EncodeValue()));
            }
        };
        return row;
    }

    // The type a value is edited as, its bytes as that type, an enum's names, and how an edit's bytes are saved.
    private static (FcbMemberType Type, byte[] Bytes, string[]? Names, Func<byte[], byte[]> Save)? Field(HkxObject o, HkxDumpLine line)
    {
        HkMember m = line.Member;
        if (m.Type != HkType.Enum)
        {
            return Type(m.Type) is { } type ? (type, o.GetRaw(line.FullPath), null, bytes => bytes) : null;
        }
        // An enum is a pick of its names: the row holds the pick's index, the member the value of that name.
        int width = o.GetRaw(line.FullPath).Length;
        int[] values = [.. m.Enum?.Items.Select(i => i.Value) ?? []];
        int index = Array.IndexOf(values, o.GetInt(line.FullPath));
        return index < 0 ? null : (FcbMemberType.Enum, BitConverter.GetBytes((uint)index), [.. m.Enum!.Items.Select(i => i.Name)],
            bytes => BitConverter.GetBytes(values[BitConverter.ToUInt32(bytes)])[..width]);
    }

    private static FcbMemberType? Type(HkType type) => type switch
    {
        HkType.Bool => FcbMemberType.Bool,
        HkType.Int8 or HkType.Char => FcbMemberType.Int8,
        HkType.UInt8 => FcbMemberType.UInt8,
        HkType.Int16 => FcbMemberType.Int16,
        HkType.UInt16 => FcbMemberType.UInt16,
        HkType.Int32 => FcbMemberType.Int32,
        HkType.UInt32 or HkType.ULong => FcbMemberType.UInt32,
        HkType.Int64 => FcbMemberType.Int64,
        HkType.UInt64 => FcbMemberType.UInt64,
        HkType.Real => FcbMemberType.Float,
        HkType.Vector4 or HkType.Quaternion => FcbMemberType.Vector4,
        _ => null,
    };
}
