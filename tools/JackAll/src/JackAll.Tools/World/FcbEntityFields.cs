using System.Numerics;
using System.Text;
using JackAll.Core.Format.Fcb;

namespace JackAll.Tools.World;

/// <summary>
/// Byte codecs for the handful of entity fields the editor reads and writes in bulk. UTF-8 with a
/// trailing NUL, matching FcbValueCodec's String case - which is not used here because its boxed
/// object-per-value shape is built for the property grid, not a quarter-million-call pass.
/// </summary>
public static class FcbEntityFields
{
    public static string ReadString(FcbObject node, uint field)
        => node.Values.TryGetValue(field, out byte[]? bytes)
            ? Encoding.UTF8.GetString(bytes.AsSpan().TrimEnd((byte)0))
            : "";

    public static ulong ReadU64(FcbObject node, uint field)
        => node.Values.TryGetValue(field, out byte[]? bytes) && bytes.Length >= 8
            ? BitConverter.ToUInt64(bytes, 0)
            : 0;

    public static Vector3? ReadVector3(FcbObject node, uint field)
        => node.Values.TryGetValue(field, out byte[]? bytes) && bytes.Length >= 12
            ? new Vector3(BitConverter.ToSingle(bytes, 0), BitConverter.ToSingle(bytes, 4), BitConverter.ToSingle(bytes, 8))
            : null;

    public static float? ReadFloat(FcbObject node, uint field)
        => node.Values.TryGetValue(field, out byte[]? bytes) && bytes.Length >= 4
            ? BitConverter.ToSingle(bytes, 0)
            : null;

    public static uint? ReadU32(FcbObject node, uint field)
        => node.Values.TryGetValue(field, out byte[]? bytes) && bytes.Length >= 4
            ? BitConverter.ToUInt32(bytes, 0)
            : null;

    public static bool ReadBool(FcbObject node, uint field)
        => node.Values.TryGetValue(field, out byte[]? bytes) && bytes.Length >= 1 && bytes[0] != 0;

    /// <summary>Finds one of an entity's components by class hash; they hang off a
    /// <c>Components</c> child rather than the entity itself.</summary>
    public static FcbObject? FindComponent(FcbObject entity, uint typeHash)
    {
        foreach (FcbObject group in entity.Children)
        {
            if (group.TypeHash != WorldHashes.Components)
            {
                continue;
            }
            foreach (FcbObject component in group.Children)
            {
                if (component.TypeHash == typeHash)
                {
                    return component;
                }
            }
        }
        return null;
    }

    public static byte[] Vector3Bytes(Vector3 v)
    {
        var bytes = new byte[12];
        BitConverter.GetBytes(v.X).CopyTo(bytes, 0);
        BitConverter.GetBytes(v.Y).CopyTo(bytes, 4);
        BitConverter.GetBytes(v.Z).CopyTo(bytes, 8);
        return bytes;
    }
}
