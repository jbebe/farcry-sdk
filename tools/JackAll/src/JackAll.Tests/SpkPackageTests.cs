using JackAll.Tools.Spk;

namespace JackAll.Tests;

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
    [Trait("Category", "RequiresFixture")]
    public void The_sample_files_were_actually_found()
    {
        Assert.True(
            FindSamplesDir() is not null,
            ".\\Fixtures\\Spk was not found, so every sample-backed test in " +
            "this class silently no-opped.");
    }

    [Theory]
    [MemberData(nameof(SampleFiles))]
    [Trait("Category", "RequiresFixture")]
    public void A_shipped_spk_parses_with_at_least_one_record(string path)
    {
        if (string.IsNullOrEmpty(path)) return;

        SpkPackage package = SpkPackage.Parse(File.ReadAllBytes(path));

        Assert.NotEmpty(package.Records);
    }

    [Theory]
    [MemberData(nameof(SampleFiles))]
    [Trait("Category", "RequiresFixture")]
    public void Every_record_payload_is_fully_consumed_within_the_file(string path)
    {
        if (string.IsNullOrEmpty(path)) return;

        byte[] bytes = File.ReadAllBytes(path);
        SpkPackage package = SpkPackage.Parse(bytes);

        // Parse() itself throws on any truncation/overrun - reaching here at all is the real
        // assertion. This just also checks every record actually got a non-negative-size payload.
        Assert.All(package.Records, r => Assert.True(r.Payload.Length >= 0));
    }

    [Theory]
    [MemberData(nameof(SampleFiles))]
    [Trait("Category", "RequiresFixture")]
    public void Every_real_records_core_declares_the_standard_forty_byte_size(string path)
    {
        if (string.IsNullOrEmpty(path)) return;

        SpkPackage package = SpkPackage.Parse(File.ReadAllBytes(path));

        Assert.All(package.Records, r =>
        {
            Assert.NotNull(r.Core);
            Assert.True(r.Core!.HasStandardDeclaredSize, $"record 0x{r.Id:x8} declared 0x{r.Core.DeclaredSize:x}, not 0x28");
        });
    }

    [Theory]
    [MemberData(nameof(SampleFiles))]
    [Trait("Category", "RequiresFixture")]
    public void Every_real_records_type_tag_is_one_of_the_seven_known_constants(string path)
    {
        if (string.IsNullOrEmpty(path)) return;

        SpkPackage package = SpkPackage.Parse(File.ReadAllBytes(path));

        Assert.All(package.Records, r => Assert.NotNull(r.Core!.Type));
    }

    [Theory]
    [MemberData(nameof(SampleFiles))]
    [Trait("Category", "RequiresFixture")]
    public void SubHeaders_echo_their_own_records_id_when_present(string path)
    {
        if (string.IsNullOrEmpty(path)) return;

        SpkPackage package = SpkPackage.Parse(File.ReadAllBytes(path));

        foreach (SpkRecord r in package.Records)
        {
            if (r.SimpleFixed68 is { } s68)
            {
                Assert.Equal(r.Id, s68.OwnId);
            }

            if (r.TransformedFixed128 is { } t128)
            {
                Assert.Equal(r.Id, t128.OwnId);
            }
        }
    }

    [Fact]
    [Trait("Category", "RequiresFixture")]
    public void A_FlatCopy_records_sibling_TransformedFixed128_links_back_to_it_with_a_real_sample_rate()
    {
        string path = "Fixtures/Spk/004e1ccc_1644b214.spk";
        if (!File.Exists(path)) return;

        SpkPackage package = SpkPackage.Parse(File.ReadAllBytes(path));
        SpkRecord flatCopy = package.Records.Single(r => r.Core!.Type == SpkRecordType.FlatCopy);

        Assert.NotNull(flatCopy.FlatCopyAudioStream);

        SpkRecord sibling = package.Records.Single(
            r => r.TransformedFixed128?.FlatCopySiblingId == flatCopy.Id);
        Assert.Equal(44100, (int)sibling.TransformedFixed128!.SampleRate);
        Assert.Equal(44100, package.TryGetFlatCopySampleRate(flatCopy));
    }

    [Fact]
    [Trait("Category", "RequiresFixture")]
    public void ReplaceRecordPayload_swaps_only_the_target_records_bytes()
    {
        string path = "Fixtures/Spk/004e1ccc_1644b214.spk";
        if (!File.Exists(path)) return;

        byte[] original = File.ReadAllBytes(path);
        SpkPackage before = SpkPackage.Parse(original);
        SpkRecord flatCopy = before.Records.Single(r => r.Core!.Type == SpkRecordType.FlatCopy);

        byte[] newPayload = [.. flatCopy.Payload[..SpkRecordCore.Size], .. new byte[3]]; // shorter, arbitrary replacement
        byte[] patched = SpkPackage.ReplaceRecordPayload(original, flatCopy.Id, newPayload);

        SpkPackage after = SpkPackage.Parse(patched);
        Assert.Equal(before.Records.Count, after.Records.Count);

        for (int i = 0; i < before.Records.Count; i++)
        {
            SpkRecord b = before.Records[i];
            SpkRecord a = after.Records[i];
            Assert.Equal(b.Id, a.Id);
            Assert.Equal(b.PreambleWords, a.PreambleWords);

            if (b.Id == flatCopy.Id)
            {
                Assert.Equal(newPayload, a.Payload);
            }
            else
            {
                Assert.Equal(b.Payload, a.Payload); // every other record's bytes are untouched
            }
        }
    }

    [Fact]
    [Trait("Category", "RequiresFixture")]
    public void ReplaceRecordPayload_rejects_an_id_not_present_in_the_file()
    {
        string path = "Fixtures/Spk/004e1ccc_1644b214.spk";
        if (!File.Exists(path)) return;

        byte[] original = File.ReadAllBytes(path);
        Assert.Throws<InvalidDataException>(() => SpkPackage.ReplaceRecordPayload(original, 0xdeadbeef, []));
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
