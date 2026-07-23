using JackAll.Core.Format;

namespace JackAll.Core.Tests;

/// <summary>
/// Run against the real .xbm samples in Fixtures/Xbm (extracted from
/// research/reference-files/format-samples/graphics_xbm_swap_example.zip) rather than a synthetic
/// fixture, for the same reason as <see cref="XbtTextureTests"/>: the only authority on what the
/// engine actually writes is what it actually shipped. These four were also cross-checked against
/// all 2,286 .xbm files in a real Far Cry 2 install - every one parsed without error.
/// </summary>
public class XbmMaterialTests
{
    private static string? FindSamplesDir()
    {
        string dir = @".\Fixtures\Xbm";
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
        foreach (string file in Directory.EnumerateFiles(dir, "*.xbm"))
        {
            data.Add(file);
        }
        return data;
    }

    [Fact]
    public void The_sample_files_were_actually_found()
    {
        Assert.True(
            FindSamplesDir() is not null,
            ".\\Fixtures\\Xbm was not found, so every sample-backed test in " +
            "this class silently no-opped.");
    }

    [Theory]
    [MemberData(nameof(SampleFiles))]
    public void A_shipped_xbm_parses_with_a_non_empty_name_and_template(string path)
    {
        if (string.IsNullOrEmpty(path)) return;

        XbmMaterial material = XbmMaterial.Parse(File.ReadAllBytes(path));

        Assert.False(string.IsNullOrWhiteSpace(material.Name));
        Assert.False(string.IsNullOrWhiteSpace(material.Template));
    }

    [Theory]
    [MemberData(nameof(SampleFiles))]
    public void Every_texture_slot_points_at_an_xbt(string path)
    {
        if (string.IsNullOrEmpty(path)) return;

        XbmMaterial material = XbmMaterial.Parse(File.ReadAllBytes(path));

        Assert.NotEmpty(material.Textures);
        Assert.All(material.Textures, tex => Assert.EndsWith(".xbt", tex.Value, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Parse_rejects_a_file_without_the_HSEM_header()
        => Assert.Throws<InvalidDataException>(() => XbmMaterial.Parse("not an xbm file at all"u8.ToArray()));

    [Fact]
    public void Parse_rejects_an_HSEM_file_with_no_LTMD_chunk()
        => Assert.Throws<InvalidDataException>(() => XbmMaterial.Parse("HSEMno material chunk here"u8.ToArray()));
}
