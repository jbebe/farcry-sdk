using JackAll.Tools.Shader;

namespace JackAll.Tests;

/// <summary>
/// Runs against the shipped <c>shadersobj</c> export rather than a synthetic fixture: the only
/// authority on the container is what the game actually ships, and the whole point of the reader is
/// that a rebuilt object is byte-identical to the one it replaced.
/// </summary>
public class ShaderObjectTests
{
    private static string ObjDirectory => Path.Combine(
        Fc2Corpus.Root, "shadersobj", "engine", "shaders", "obj");

    private static IEnumerable<string> Objects()
        => Directory.Exists(ObjDirectory)
            ? Directory.EnumerateFiles(ObjDirectory, "shadernumber_*", SearchOption.AllDirectories)
                .Where(f => !f.EndsWith(".rs", StringComparison.OrdinalIgnoreCase))
                .Order()
            : [];

    [Fact]
    [Trait("Category", "RequiresFixture")]
    public void The_shader_objects_were_actually_found()
        => Assert.True(Objects().Any(), Fc2Corpus.MissingMessage(".pso"));

    [Fact]
    [Trait("Category", "RequiresFixture")]
    public void Every_shipped_object_round_trips_byte_identically()
    {
        var failures = new List<string>();
        int parsed = 0;

        foreach (string path in Objects())
        {
            byte[] original = File.ReadAllBytes(path);
            byte[] rebuilt = ShaderObject.Parse(original).Build();
            parsed++;

            int difference = Fc2Corpus.FirstDifference(original, rebuilt);
            if (difference >= 0 || original.Length != rebuilt.Length)
            {
                failures.Add($"{Path.GetFileName(path)} at byte {difference}");
            }
        }

        Assert.True(failures.Count == 0, $"{failures.Count} of {parsed} objects changed: "
            + string.Join(", ", failures.Take(10)));
    }

    [Fact]
    [Trait("Category", "RequiresFixture")]
    public void Every_shipped_object_declares_a_shader_model_3_profile()
    {
        string[] unexpected = Objects()
            .Select(path => (path, ShaderObject.Parse(File.ReadAllBytes(path)).Profile))
            .Where(x => x.Profile != (x.path.EndsWith(".vso", StringComparison.OrdinalIgnoreCase)
                ? "vs_3_0"
                : "ps_3_0"))
            .Select(x => $"{Path.GetFileName(x.path)} is {x.Profile}")
            .Take(10)
            .ToArray();

        Assert.True(unexpected.Length == 0, string.Join(", ", unexpected));
    }

    [Fact]
    [Trait("Category", "RequiresFixture")]
    public void An_objects_bucket_folder_is_its_hash_low_seven_bits()
    {
        string[] misfiled = Objects()
            .Where(path =>
            {
                uint hash = uint.Parse(
                    Path.GetFileNameWithoutExtension(path)["shadernumber_".Length..],
                    System.Globalization.NumberStyles.HexNumber);
                return ShaderIndex.PathOf(hash, Path.GetExtension(path).TrimStart('.'))
                    != Path.Combine(Path.GetFileName(Path.GetDirectoryName(path)!), Path.GetFileName(path));
            })
            .Take(10)
            .ToArray();

        Assert.True(misfiled.Length == 0, string.Join(", ", misfiled));
    }

    [Fact]
    [Trait("Category", "RequiresFixture")]
    public void A_shaders_no_option_permutation_keys_on_the_crc32_of_its_name()
    {
        string index = Path.Combine(ObjDirectory, "index.pso");
        if (!File.Exists(index))
        {
            return;
        }

        ShaderIndex table = ShaderIndex.Parse(File.ReadAllBytes(index));
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

    [Fact]
    [Trait("Category", "RequiresFixture")]
    public void No_shipped_object_carries_a_constant_table_to_strip()
    {
        string[] changed = Objects()
            .Select(path => (path, Bytecode: ShaderObject.Parse(File.ReadAllBytes(path)).Bytecode))
            .Where(x => ShaderObject.StripConstantTable(x.Bytecode).Length != x.Bytecode.Length)
            .Select(x => Path.GetFileName(x.path))
            .Take(10)
            .ToArray();

        Assert.True(changed.Length == 0, string.Join(", ", changed));
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
