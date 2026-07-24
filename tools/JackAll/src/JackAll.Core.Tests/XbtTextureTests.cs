using JackAll.Core.Format;

namespace JackAll.Core.Tests;

/// <summary>
/// Run against the real .xbt samples in research/reference-files/format-samples rather than a
/// synthetic fixture, for the same reason as <see cref="FatArchiveTests"/>: the only authority on
/// what the engine actually writes is what it actually shipped.
/// </summary>
public class XbtTextureTests
{
    private static string? FindSamplesDir()
    {
        string dir = @".\Fixtures\Xbt";
        return Directory.Exists(dir) ? dir : null;
    }

    public static TheoryData<string> SampleFiles()
    {
        var data = new TheoryData<string>();
        string? dir = FindSamplesDir();
        if (dir is null)
        {
            data.Add(string.Empty); // keeps xUnit from erroring on an empty theory
            return data;
        }
        foreach (string file in Directory.EnumerateFiles(dir, "*.xbt"))
        {
            data.Add(file);
        }
        return data;
    }

    [Fact]
    [Trait("Category", "RequiresFixture")]
    public void The_sample_files_were_actually_found()
    {
        Assert.True(
            FindSamplesDir() is not null,
            ".\\Fixtures\\Xbt was not found, so every sample-backed test in " +
            "this class silently no-opped.");
    }

    [Theory]
    [MemberData(nameof(SampleFiles))]
    [Trait("Category", "RequiresFixture")]
    public void Splitting_then_combining_a_shipped_xbt_reproduces_it_byte_for_byte(string path)
    {
        if (string.IsNullOrEmpty(path)) return;

        byte[] original = File.ReadAllBytes(path);

        (byte[] header, byte[] dds) = XbtTexture.Split(original);
        byte[] rebuilt = XbtTexture.Combine(header, dds);

        Assert.Equal(original, rebuilt);
    }

    [Theory]
    [MemberData(nameof(SampleFiles))]
    [Trait("Category", "RequiresFixture")]
    public void The_dds_payload_starts_with_a_real_dds_signature(string path)
    {
        if (string.IsNullOrEmpty(path)) return;

        (_, byte[] dds) = XbtTexture.Split(File.ReadAllBytes(path));

        Assert.Equal("DDS "u8.ToArray(), dds[..4]);
    }

    [Theory]
    [MemberData(nameof(SampleFiles))]
    [Trait("Category", "RequiresFixture")]
    public void Header_survives_an_xml_round_trip(string path)
    {
        if (string.IsNullOrEmpty(path)) return;

        (byte[] header, _) = XbtTexture.Split(File.ReadAllBytes(path));

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
