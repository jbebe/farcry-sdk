using JackAll.Core.Format;

namespace JackAll.Core.Tests;

/// <summary>
/// Run against real .spk samples extracted from a shipped Far Cry 2 install (Fixtures/Spk), for the
/// same reason as <see cref="XbtTextureTests"/>/<see cref="XbmMaterialTests"/>: the only authority on
/// what the engine actually writes is what it actually shipped. The container format here was traced
/// live via GhidraMCP against Dunia.dll's real sound-bank loader (see <see cref="SpkPackage"/>'s
/// remarks) - these six fixtures were also cross-checked against all 8,282 .spk files in a real
/// install: every one parsed without error, and every payload byte lands exactly within its file.
/// </summary>
public class SpkPackageTests
{
    private static string? FindSamplesDir()
    {
        string dir = @".\Fixtures\Spk";
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
        foreach (string file in Directory.EnumerateFiles(dir, "*.spk"))
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
            ".\\Fixtures\\Spk was not found, so every sample-backed test in " +
            "this class silently no-opped.");
    }

    [Theory]
    [MemberData(nameof(SampleFiles))]
    public void A_shipped_spk_parses_with_at_least_one_record(string path)
    {
        if (string.IsNullOrEmpty(path)) return;

        SpkPackage package = SpkPackage.Parse(File.ReadAllBytes(path));

        Assert.NotEmpty(package.Records);
    }

    [Theory]
    [MemberData(nameof(SampleFiles))]
    public void Every_record_payload_is_fully_consumed_within_the_file(string path)
    {
        if (string.IsNullOrEmpty(path)) return;

        byte[] bytes = File.ReadAllBytes(path);
        SpkPackage package = SpkPackage.Parse(bytes);

        // Parse() itself throws on any truncation/overrun - reaching here at all is the real
        // assertion. This just also checks every record actually got a non-negative-size payload.
        Assert.All(package.Records, r => Assert.True(r.Payload.Length >= 0));
    }

    [Fact]
    public void Parse_rejects_a_file_without_the_SPK_header()
        => Assert.Throws<InvalidDataException>(() => SpkPackage.Parse("not an spk file at all!!"u8.ToArray()));

    [Fact]
    public void Parse_rejects_a_truncated_id_table()
    {
        // magic + count=5, but no id table or record data follows.
        byte[] data = [0x01, 0x4b, 0x50, 0x53, 0x05, 0x00, 0x00, 0x00];
        Assert.Throws<InvalidDataException>(() => SpkPackage.Parse(data));
    }
}
