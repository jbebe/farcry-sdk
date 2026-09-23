using JackAll.Core;
using JackAll.Core.Format.Fcb;

namespace JackAll.Tests;

/// <summary>
/// Each value in a retail library decodes and re-encodes byte for byte: <see cref="FcbValueCodec"/>'s
/// layouts match <see cref="FcbDocument"/>'s binary format, not just each other.
/// </summary>
public class FcbValueCodecTests
{
    private static readonly Lazy<FcbClassDefinitions> Classes = new(BundledAssets.LoadFcbClasses);

    [Theory]
    [MemberData(nameof(FcbDocumentTests.EntityLibraries), MemberType = typeof(FcbDocumentTests))]
    public void Every_value_in_a_real_fcb_survives_decode_then_encode_byte_for_byte(string fixture)
    {
        if (Fixture.Read(fixture) is not { } bytes) return;

        FcbObject root = FcbDocument.Deserialize(bytes);

        int checkedCount = 0;
        int fallbackCount = 0;
        AssertRoundTrips(root, Classes.Value, ref checkedCount, ref fallbackCount);

        Assert.True(checkedCount > 1000, $"Only checked {checkedCount} values - fixture may be empty/unreadable.");
    }

    private static void AssertRoundTrips(FcbObject obj, IFcbClassScope scope, ref int checkedCount, ref int fallbackCount)
    {
        FcbClass ownClass = scope.Resolve(obj);

        foreach ((uint nameHash, byte[] originalBytes) in obj.Values)
        {
            // Same fallback FcbXml.WriteValueEntry applies: an unresolved member, or one whose config
            // says a type the actual bytes don't match, is treated as BinHex - a pure byte passthrough
            // that trivially round-trips, so it's still worth counting rather than skipping.
            FcbMemberType declaredType = ownClass.FindMember(nameHash)?.Type ?? FcbMemberType.BinHex;
            FcbMemberType type = declaredType == FcbMemberType.BinHex || FcbValueCodec.TryDecode(declaredType, originalBytes, out _)
                ? declaredType
                : FcbMemberType.BinHex;

            Assert.True(FcbValueCodec.TryDecode(type, originalBytes, out object decoded),
                $"Type '{type}' failed to decode its own already-validated bytes (hash {nameHash:X8}).");

            byte[] reEncoded = FcbValueCodec.Encode(type, decoded);

            Fixture.AssertSameBytes($"value {nameHash:X8} ({type})", originalBytes, reEncoded);

            checkedCount++;
            if (type == FcbMemberType.BinHex && declaredType != FcbMemberType.BinHex)
            {
                fallbackCount++;
            }
        }

        foreach (FcbObject child in obj.Children)
        {
            AssertRoundTrips(child, ownClass, ref checkedCount, ref fallbackCount);
        }
    }
}
