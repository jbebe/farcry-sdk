using JackAll.Tools.Xbt;

namespace JackAll.Tests;

/// <summary>
/// Run against real .xbt files rather than a synthetic fixture, for the same reason as
/// <see cref="FatArchiveTests"/>: the only authority on what the engine actually writes is what it
/// actually shipped.
/// </summary>
public class XbtTextureTests
{
    // A DXT5 HUD texture whose DDS header claims no mip count.
    private const string Hud = "Xbt/crosshair_line.xbt";

    // A DXT1 normal map with a full mip chain.
    private const string Normal = "Xbt/nail_n.xbt";

    [Fact]
    [Trait("Category", "RequiresFixture")]
    public void The_fixtures_were_actually_found() => Fixture.AssertPresent(Hud, Normal);

    [Theory]
    [InlineData(Hud)]
    [InlineData(Normal)]
    public void Splitting_then_combining_a_shipped_xbt_reproduces_it_byte_for_byte(string fixture)
    {
        if (Fixture.Read(fixture) is not { } original) return;

        (byte[] header, byte[] dds) = XbtTexture.Split(original);

        Fixture.AssertSameBytes(fixture, original, XbtTexture.Combine(header, dds));
    }

    [Theory]
    [InlineData(Hud)]
    [InlineData(Normal)]
    public void The_dds_payload_starts_with_a_real_dds_signature(string fixture)
    {
        if (Fixture.Read(fixture) is not { } xbt) return;

        (_, byte[] dds) = XbtTexture.Split(xbt);

        Assert.Equal("DDS "u8.ToArray(), dds[..4]);
    }

    [Theory]
    [InlineData(Hud)]
    [InlineData(Normal)]
    public void Header_survives_an_xml_round_trip(string fixture)
    {
        if (Fixture.Read(fixture) is not { } xbt) return;

        (byte[] header, _) = XbtTexture.Split(xbt);

        string xml = XbtTexture.ToXml(header);
        byte[] roundTripped = XbtTexture.HeaderFromXml(xml);

        Assert.Equal(header, roundTripped);
    }

    [Fact]
    public void Split_rejects_a_file_without_the_TBX_signature()
        => Assert.Throws<InvalidDataException>(() => XbtTexture.Split("DDS not-an-xbt-file"u8.ToArray()));

    [Fact]
    public void Split_rejects_a_header_with_no_embedded_DDS_marker()
        => Assert.Throws<InvalidDataException>(() => XbtTexture.Split("TBX\0no dds here at all"u8.ToArray()));
}
