namespace JackAll.Core.Format.Fcb;

/// <summary>Names hashes a tree's class definitions do not know.</summary>
public interface IFcbNames
{
    string? ClassName(uint hash);

    /// <summary>The member's name, and its type when <paramref name="value"/> fits one; BinHex otherwise.</summary>
    (string? Name, FcbMemberType Type) Member(uint hash, byte[] value);
}

public static class FcbNames
{
    public static string? ClassNameOf(FcbClass cls, uint hash, IFcbNames? fallback) => cls.Name ?? fallback?.ClassName(hash);

    /// <summary>
    /// <paramref name="cls"/>'s member for <paramref name="hash"/> when its type fits
    /// <paramref name="value"/>, else what <paramref name="fallback"/> makes of it, keeping the
    /// class's name when the fallback has none.
    /// </summary>
    public static FcbMember? MemberOf(FcbClass cls, uint hash, byte[] value, IFcbNames? fallback)
    {
        FcbMember? member = cls.FindMember(hash);
        if (fallback is null || member is { Type: not FcbMemberType.BinHex } && FcbValueCodec.TryDecode(member.Type, value, out _))
        {
            return member;
        }
        (string? name, FcbMemberType type) = fallback.Member(hash, value);
        return new FcbMember(name ?? member?.Name, type);
    }
}
