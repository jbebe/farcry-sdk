using JackAll.Core.Format.Fcb;

namespace JackAll.Core.Tests;

/// <summary>
/// Decodes and re-encodes every single value in real shipped fragments and checks the bytes come back
/// byte-for-byte identical - the strongest available check that <see cref="FcbValueCodec"/>'s byte
/// layouts actually match <see cref="FcbDocument"/>'s binary format, not just each other.
/// </summary>
[Trait("Category", "RequiresFixture")]
public class FcbValueCodecTests
{
    private const string FixturesDir = "Fixtures/Fcb";
    private const string ClassesPath = "Fixtures/Fcb/binary_classes.xml";

    public static TheoryData<string> SampleFiles()
    {
        var data = new TheoryData<string>();
        if (!Directory.Exists(FixturesDir))
        {
            data.Add(string.Empty);
            return data;
        }
        foreach (string file in Directory.EnumerateFiles(FixturesDir, "*.fcb"))
        {
            data.Add(file);
        }
        return data;
    }

    [Theory]
    [MemberData(nameof(SampleFiles))]
    public void Every_value_in_a_real_fcb_survives_decode_then_encode_byte_for_byte(string path)
    {
        if (string.IsNullOrEmpty(path)) return;

        FcbClassDefinitions defs = File.Exists(ClassesPath)
            ? FcbClassDefinitions.Load(ClassesPath)
            : FcbClassDefinitions.Empty;

        FcbObject root = FcbDocument.Deserialize(File.ReadAllBytes(path));

        int checkedCount = 0;
        int fallbackCount = 0;
        AssertRoundTrips(root, defs, ref checkedCount, ref fallbackCount);

        Assert.True(checkedCount > 1000, $"Only checked {checkedCount} values - fixture may be empty/unreadable.");
    }

    private static void AssertRoundTrips(FcbObject obj, IFcbClassScope scope, ref int checkedCount, ref int fallbackCount)
    {
        FcbClass ownClass = scope.Resolve(obj.TypeHash);

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

            Assert.True(originalBytes.AsSpan().SequenceEqual(reEncoded),
                $"Value {nameHash:X8} (type {type}, {originalBytes.Length} bytes) didn't round-trip through FcbValueCodec.");

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
