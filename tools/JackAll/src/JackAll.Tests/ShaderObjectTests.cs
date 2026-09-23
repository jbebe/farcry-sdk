using JackAll.Tools.Shader;

namespace JackAll.Tests;

/// <summary>
/// Runs against shipped <c>shadersobj</c> files rather than synthetic ones: the only authority on the
/// container is what the game actually ships, and the whole point of the reader is that a rebuilt
/// object is byte-identical to the one it replaced.
/// </summary>
public class ShaderObjectTests
{
    private const string Pixel = "Shader/h11/shadernumber_4cf23f91.pso";
    private const string Vertex = "Shader/h30/shadernumber_3aa04cb0.vso";
    private const string Index = "Shader/index.pso";

    [Fact]
    [Trait("Category", "RequiresFixture")]
    public void The_fixtures_were_actually_found() => Fixture.AssertPresent(Pixel, Vertex, Index);

    [Theory]
    [InlineData(Pixel)]
    [InlineData(Vertex)]
    public void A_shipped_object_round_trips_byte_identically(string fixture)
    {
        if (Fixture.Read(fixture) is not { } original) return;

        byte[] rebuilt = ShaderObject.Parse(original).Build();

        Assert.True(Fixture.FirstDifference(original, rebuilt) < 0,
            Fixture.DescribeDifference(fixture, original, rebuilt));
    }

    [Theory]
    [InlineData(Pixel, "ps_3_0")]
    [InlineData(Vertex, "vs_3_0")]
    public void A_shipped_object_declares_a_shader_model_3_profile(string fixture, string profile)
    {
        if (Fixture.Read(fixture) is not { } bytes) return;

        Assert.Equal(profile, ShaderObject.Parse(bytes).Profile);
    }

    [Theory]
    [InlineData(Pixel)]
    [InlineData(Vertex)]
    public void An_objects_bucket_folder_is_its_hash_low_seven_bits(string fixture)
    {
        uint hash = uint.Parse(
            Path.GetFileNameWithoutExtension(fixture)["shadernumber_".Length..],
            System.Globalization.NumberStyles.HexNumber);

        Assert.Equal(
            Path.Combine(Path.GetFileName(Path.GetDirectoryName(fixture)!), Path.GetFileName(fixture)),
            ShaderIndex.PathOf(hash, Path.GetExtension(fixture).TrimStart('.')));
    }

    [Fact]
    public void A_shaders_no_option_permutation_keys_on_the_crc32_of_its_name()
    {
        if (Fixture.Read(Index) is not { } index) return;

        ShaderIndex table = ShaderIndex.Parse(index);
        string[] unresolved =
            ["celestialbody", "skydome", "starsphere", "skydisk", "cloudnoisecombine", "cloudnoiseblur"];

        unresolved = [.. unresolved.Where(s => table.Lookup(ShaderIndex.KeyOf(s)) is null or 0)];

        Assert.True(unresolved.Length == 0,
            "no permutation keyed on the name's CRC32: " + string.Join(", ", unresolved));
    }

    [Fact]
    public void A_file_that_is_not_a_shader_object_is_refused()
        => Assert.Throws<InvalidDataException>(() => ShaderObject.Parse("not a shader"u8));

    [Fact]
    public void A_binding_table_survives_the_xml_round_trip()
    {
        var shader = new ShaderObject(
            4,
            [new ShaderParameter(0x0DF869CE, 0x11C8, 1, 1), new ShaderParameter(0x20085F45, 0x004C, 1, 1)],
            [0x00, 0x03, 0xFF, 0xFF]);

        ShaderObject restored = ShaderObject.FromXml(shader.ToXml(), shader.Bytecode);

        Assert.Equal(shader.Kind, restored.Kind);
        Assert.Equal(shader.Parameters, restored.Parameters);
        Assert.Equal(shader.Build(), restored.Build());
    }

    [Theory]
    [InlineData(Pixel)]
    [InlineData(Vertex)]
    public void A_shipped_object_carries_no_constant_table_to_strip(string fixture)
    {
        if (Fixture.Read(fixture) is not { } bytes) return;

        byte[] bytecode = ShaderObject.Parse(bytes).Bytecode;

        Assert.Equal(bytecode.Length, ShaderObject.StripConstantTable(bytecode).Length);
    }

    [Fact]
    public void Stripping_leaves_bytecode_that_carries_no_constant_table_alone()
    {
        // Version token, one instruction, end token — nothing to strip.
        byte[] bytecode = [0x00, 0x03, 0xFF, 0xFF, 0x01, 0x00, 0x00, 0x02, 0xFF, 0xFF, 0x00, 0x00];

        Assert.Same(bytecode, ShaderObject.StripConstantTable(bytecode));
    }

    [Fact]
    public void Stripping_drops_a_leading_comment_block_and_keeps_the_rest()
    {
        // Version token, a two-dword comment token, then the end token.
        byte[] bytecode =
        [
            0x00, 0x03, 0xFF, 0xFF,
            0xFE, 0xFF, 0x02, 0x00, 0xAA, 0xAA, 0xAA, 0xAA, 0xBB, 0xBB, 0xBB, 0xBB,
            0xFF, 0xFF, 0x00, 0x00,
        ];

        Assert.Equal(
            [0x00, 0x03, 0xFF, 0xFF, 0xFF, 0xFF, 0x00, 0x00],
            ShaderObject.StripConstantTable(bytecode));
    }

    [Fact]
    public void A_parameters_register_is_its_binding_shifted_past_the_flags()
    {
        // ViewProjectionMatrix, which every shipped object binds at c4.
        Assert.Equal(4, new ShaderParameter(0, 0x0108, 4, 4).Register);
        Assert.Equal(8, new ShaderParameter(0, 0x0108, 4, 4).Flags);
    }
}
