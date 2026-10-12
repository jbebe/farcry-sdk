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
        HkMember m = line.Member;
        if (m.Type == HkType.Enum)
        {
            return m.Enum is { } choices ? Choice(o, line, choices, write) : null;
        }
        if (Type(m.Type) is not { } type)
        {
            return null;
        }
        PropertyRow row = PropertyRow.Build(0, line.Path, type, o.GetRaw(line.FullPath), null);
        row.Changed += () =>
        {
            if (row.IsRowValid)
            {
                write(row.EncodeValue());
            }
        };
        return row;
    }

    // An enum as a pick of its names; the row holds the pick's index, the member the value of that name.
    private static PropertyRow? Choice(HkxObject o, HkxDumpLine line, HkEnum choices, Action<byte[]> write)
    {
        byte[] saved = o.GetRaw(line.FullPath);
        int value = o.GetInt(line.FullPath);
        int index = choices.Items.Select(i => i.Value).ToList().IndexOf(value);
        if (index < 0)
        {
            return null;
        }
        PropertyRow row = PropertyRow.Build(0, line.Path, FcbMemberType.Enum, BitConverter.GetBytes((uint)index), null, [.. choices.Items.Select(i => i.Name)]);
        row.Changed += () =>
        {
            if (row.IsRowValid)
            {
                write(BitConverter.GetBytes(choices.Items[(int)BitConverter.ToUInt32(row.EncodeValue())].Value)[..saved.Length]);
            }
        };
        return row;
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
