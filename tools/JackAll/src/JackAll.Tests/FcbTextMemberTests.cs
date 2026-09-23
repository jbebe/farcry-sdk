using JackAll.Core.Format.Fcb;

namespace JackAll.Tests;

/// <summary>A hashed member's <c>text_</c> twin is named from the member, declared or not.</summary>
public class FcbTextMemberTests
{
    private static readonly Lazy<FcbClassDefinitions> Defs = new(() => FcbClassDefinitions.Load("Fixtures/Fcb/binary_classes.xml"));

    private static uint H(string name) => FcbClassDefinitions.Crc32Ascii(name);

    private static FcbClass Class(string name) => Defs.Value.GetClass(H(name));

    [Theory]
    [InlineData("CFileDescriptorComponent", "text_fileName")]
    [InlineData("CVehicle", "text_matimpSmallCollisionImpact")]
    public void A_declared_hash_only_twin_is_named(string name, string twin)
    {
        FcbClass cls = Class(name);
        Assert.Equal(new FcbMember(twin, FcbMemberType.String), cls.FindMember(H(twin)));
        Assert.Contains(cls.AllMembers(), m => m.Member.Name == twin);
    }

    [Fact]
    public void An_undeclared_twin_is_a_named_string()
        => Assert.Equal(
            new FcbMember("text_sUsageString", FcbMemberType.String),
            Class("CUsableComponent").FindMember(H("text_sUsageString")));

    [Fact]
    public void An_inherited_members_twin_is_named()
    {
        FcbClass vehicle = Class("CVehicle");
        string inherited = vehicle.Super!.AllMembers().Select(m => m.Member.Name).OfType<string>().First();
        Assert.Equal("text_" + inherited, vehicle.FindMember(H("text_" + inherited))?.Name);
    }

    [Fact]
    public void A_hash_that_is_no_members_twin_stays_unknown()
        => Assert.Null(Class("CUsableComponent").FindMember(H("text_fileName")));

    [Theory]
    [InlineData("text_fileName", "fileName")]
    [InlineData("fileName", null)]
    [InlineData(null, null)]
    public void TextOf_names_the_member_the_engine_reads(string? name, string? read)
        => Assert.Equal(read, FcbMember.TextOf(name));
}
