using JackAll.Core.Format;
using JackAll.Tools.Spk;

namespace JackAll.Tests;

/// <summary>
/// Run against real .spk samples extracted from a shipped Far Cry 2 install (Fixtures/Spk), for the
/// same reason as <see cref="XbtTextureTests"/>/<see cref="XbmMaterialTests"/>: the only authority on
/// what the engine actually writes is what it actually shipped. The container format here was traced
/// live via GhidraMCP against Dunia.dll's real sound-bank loader (see <see cref="SpkPackage"/>'s
/// remarks).
/// </summary>
public class SpkPackageTests
{
    /// <summary>A bank carrying its own audio beside the event that plays it.</summary>
    public const string WithAudio = "Spk/004e1ccc_1644b214.spk";
    public const string ListEvent = "Spk/004bf5ea_5c852949.spk";
    public const string ParamsOnly = "Spk/804e1e46_0f3fe99f.spk";

    public static TheoryData<string> Banks => new() { WithAudio, ListEvent, ParamsOnly };

    [Fact]
    [Trait("Category", "RequiresFixture")]
    public void The_fixtures_were_actually_found() => Fixture.AssertPresent(WithAudio, ListEvent, ParamsOnly);

    [Theory]
    [MemberData(nameof(Banks))]
    public void A_shipped_spk_parses_with_at_least_one_record(string path)
    {
        if (Fixture.Read(path) is not { } bytes) return;

        SpkPackage package = SpkPackage.Parse(bytes);

        Assert.NotEmpty(package.Records);
    }

    [Theory]
    [MemberData(nameof(Banks))]
    public void Every_record_payload_is_fully_consumed_within_the_file(string path)
    {
        if (Fixture.Read(path) is not { } bytes) return;

        SpkPackage package = SpkPackage.Parse(bytes);

        // Parse() itself throws on any truncation/overrun - reaching here at all is the real
        // assertion. This just also checks every record actually got a non-negative-size payload.
        Assert.All(package.Records, r => Assert.True(r.Payload.Length >= 0));
    }

    [Theory]
    [MemberData(nameof(Banks))]
    public void Every_real_records_core_declares_the_standard_forty_byte_size(string path)
    {
        if (Fixture.Read(path) is not { } bytes) return;

        SpkPackage package = SpkPackage.Parse(bytes);

        Assert.All(package.Records, r =>
        {
            Assert.NotNull(r.Core);
            Assert.True(r.Core!.HasStandardDeclaredSize, $"record 0x{r.Id:x8} declared 0x{r.Core.DeclaredSize:x}, not 0x28");
        });
    }

    [Theory]
    [MemberData(nameof(Banks))]
    public void Every_real_records_type_tag_is_one_of_the_seven_known_constants(string path)
    {
        if (Fixture.Read(path) is not { } bytes) return;

        SpkPackage package = SpkPackage.Parse(bytes);

        Assert.All(package.Records, r => Assert.NotNull(r.Core!.Type));
    }

    [Theory]
    [MemberData(nameof(Banks))]
    public void SubHeaders_echo_their_own_records_id_when_present(string path)
    {
        if (Fixture.Read(path) is not { } bytes) return;

        SpkPackage package = SpkPackage.Parse(bytes);

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
    public void A_FlatCopy_records_sibling_TransformedFixed128_links_back_to_it_with_a_real_sample_rate()
    {
        if (Fixture.Read(WithAudio) is not { } bytes) return;

        SpkPackage package = SpkPackage.Parse(bytes);
        SpkRecord flatCopy = package.Records.Single(r => r.Core!.Type == SpkRecordType.FlatCopy);

        Assert.NotNull(flatCopy.FlatCopyAudioStream);

        SpkRecord sibling = package.Records.Single(
            r => r.TransformedFixed128?.FlatCopySiblingId == flatCopy.Id);
        Assert.Equal(44100, (int)sibling.TransformedFixed128!.SampleRate);
        Assert.Equal(44100, package.TryGetFlatCopySampleRate(flatCopy));
    }

    [Fact]
    public void ReplaceRecordPayload_swaps_only_the_target_records_bytes()
    {
        if (Fixture.Read(WithAudio) is not { } original) return;

        SpkPackage before = SpkPackage.Parse(original);
        SpkRecord flatCopy = before.Records.Single(r => r.Core!.Type == SpkRecordType.FlatCopy);

        // Shorter, arbitrary replacement
        byte[] newPayload = [.. flatCopy.Payload[..SpkRecordCore.Size], .. new byte[3]];
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
                // Every other record's bytes are untouched
                Assert.Equal(b.Payload, a.Payload);
            }
        }
    }

    [Fact]
    public void ReplaceAudio_rewrites_the_descriptors_declared_length()
    {
        if (Fixture.Read(WithAudio) is not { } original) return;

        SpkPackage before = SpkPackage.Parse(original);
        SpkRecord flatCopy = before.Records.Single(r => r.Core!.Type == SpkRecordType.FlatCopy);

        SpkPackage after = SpkPackage.Parse(before.ReplaceAudio(original, flatCopy, new byte[3]));
        TransformedFixed128SubHeader descriptor = after.TryGetAudioDescriptor(flatCopy)!;

        Assert.Equal(3u, descriptor.AudioByteLength);
        Assert.Equal(3u, descriptor.AudioByteLengthMirror);
    }

    [Fact]
    public void ReplaceRecordPayload_rejects_an_id_not_present_in_the_file()
    {
        if (Fixture.Read(WithAudio) is not { } original) return;

        Assert.Throws<InvalidDataException>(() => SpkPackage.ReplaceRecordPayload(original, 0xdeadbeef, []));
    }

    /// <summary>The bank behind the Dart Rifle's first-person shot, and the reason this case is worth
    /// a fixture: it holds one record, that record holds no audio, and the word a leaf event uses to
    /// point at its sound is `0` here - so read as a leaf it looks like a file leading nowhere. It is
    /// a list event, and its four trailing bytes name the bank that does have the audio.</summary>
    [Fact]
    public void A_list_event_exposes_its_children_and_no_link()
    {
        if (Fixture.Read(ListEvent) is not { } bytes) return;

        SpkPackage package = SpkPackage.Parse(bytes);
        SimpleFixed68SubHeader s68 = Assert.Single(package.Records).SimpleFixed68!;

        Assert.Equal(SpkEventType.List, s68.KnownEventType);
        Assert.True(s68.IsComposite);
        Assert.Equal([0x004bf5e9u], s68.ChildIds);
        Assert.Empty(s68.SwitchKeys);

        // word[2] is a byte offset into the child list here, not an id - reading it as a link is what
        // made this bank render as "-> 0x00000000".
        Assert.Null(s68.LinkedId);
        Assert.Equal(0u, s68.RawWord2);
    }

    [Theory]
    [MemberData(nameof(Banks))]
    public void A_leaf_event_exposes_a_link_and_no_children(string path)
    {
        if (Fixture.Read(path) is not { } bytes) return;

        SpkPackage package = SpkPackage.Parse(bytes);

        foreach (SpkRecord r in package.Records)
        {
            if (r.SimpleFixed68 is not { IsComposite: false } leaf)
            {
                continue;
            }

            Assert.Equal(leaf.RawWord2, leaf.LinkedId);
            Assert.Empty(leaf.ChildIds);
            Assert.Empty(leaf.SwitchKeys);
        }
    }

    /// <summary>Locks in <see cref="TransformedFixed128SubHeader.AudioByteLength"/>: shipped records
    /// always agree with the stream they describe (exact across every paired record this
    /// was checked against), so a mismatch means a tool edited the audio without rewriting the
    /// descriptor - which is what makes it worth surfacing in both front ends.</summary>
    [Theory]
    [MemberData(nameof(Banks))]
    public void A_shipped_records_declared_audio_length_matches_its_actual_stream(string path)
    {
        if (Fixture.Read(path) is not { } bytes) return;

        SpkPackage package = SpkPackage.Parse(bytes);

        foreach (SpkRecord r in package.Records.Where(r => r.FlatCopyAudioStream is not null))
        {
            if (package.DeclaredAudioLengthMatches(r) is not { } matches)
            {
                // No descriptor sibling in this bank to compare against
                continue;
            }

            Assert.True(matches,
                $"0x{r.Id:x8} in {Path.GetFileName(path)} declares " +
                $"{package.TryGetAudioDescriptor(r)!.AudioByteLength} bytes but carries " +
                $"{r.FlatCopyAudioStream!.Length}.");
        }
    }

    /// <summary>
    /// A sound id is not a path hash - it becomes one only through the filename the engine builds from
    /// it (<c>soundbinary\&lt;id:08x&gt;.spk</c>, see the `.spk` page's loading pipeline). That
    /// derivation is what lets a viewer follow an event's child id to the bank holding the audio, so it
    /// is pinned here against the engine's own numbers: these two CRCs are copied out of a shipped
    /// world's `depload.xml`, which spells out both the path and its hash.
    /// </summary>
    [Fact]
    public void A_sound_ids_bank_path_hashes_to_the_value_depload_records()
    {
        Assert.Equal(1552230729u, NameHash.Compute(@"soundbinary\004bf5ea.spk"));
        Assert.Equal(1424403779u, NameHash.Compute(@"soundbinary\004bf5e9.spk"));
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
