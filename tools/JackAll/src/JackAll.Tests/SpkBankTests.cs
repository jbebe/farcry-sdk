using System.Xml.Linq;
using JackAll.Tools.Audio;
using JackAll.Tools.Spk;

namespace JackAll.Tests;

public class SpkBankTests
{
    /// <summary>The bullet pass-by crack: a random container of 12 IMA-ADPCM clips plus an even chance of silence.</summary>
    public const string PassBy = "Spk/00448bd2.spk";

    /// <summary>A multilayer of three layers, each with a volume and a pitch curve on a game parameter.</summary>
    public const string Multilayer = "Spk/0044f143.spk";

    /// <summary>Bullet impacts: a material switch over random containers, the bank a ricochet mod edits.</summary>
    public const string Impacts = "Spk/004565a3.spk";

    /// <summary>A bark bank: one preamble per record, and non-zero bytes padding some records.</summary>
    public const string Bark = "Spk/bark_1820776.spk";

    public static TheoryData<string> Banks => new() { PassBy, Multilayer, Impacts, Bark };

    [Fact]
    [Trait("Category", "RequiresFixture")]
    public void The_fixtures_were_actually_found() => Fixture.AssertPresent(PassBy, Multilayer, Impacts, Bark);

    [Theory]
    [MemberData(nameof(Banks))]
    public void A_shipped_bank_rebuilds_byte_for_byte(string path)
    {
        if (Fixture.Read(path) is not { } bytes) return;

        Fixture.AssertSameBytes(path, bytes, SpkBank.Parse(bytes).Write());
    }

    [Theory]
    [MemberData(nameof(Banks))]
    public void A_shipped_bank_rebuilds_byte_for_byte_through_its_xml(string path)
    {
        if (Fixture.Read(path) is not { } bytes) return;

        var files = new Dictionary<string, byte[]>();
        string xml = SpkBankXml.ToXml(SpkBank.Parse(bytes), (name, data) => files[name] = data);

        Fixture.AssertSameBytes(path, bytes, SpkBankXml.FromXml(xml, name => files[name]).Write());
    }

    [Fact]
    public void Equal_weights_and_silence_read_as_plain_choices()
    {
        if (Fixture.Read(PassBy) is not { } bytes) return;

        XElement random = Decode(bytes).Elements("Random").Single();

        Assert.Equal("1", (string?)random.Attribute("silence"));
        Assert.Equal(12, random.Elements("Choice").Count());
        Assert.All(random.Elements("Choice"), c => Assert.Null(c.Attribute("weight")));
    }

    [Fact]
    public void A_multilayer_reads_its_pitch_curves()
    {
        if (Fixture.Read(Multilayer) is not { } bytes) return;

        XElement multilayer = Decode(bytes).Elements("Multilayer").Single();

        Assert.Equal(3, multilayer.Elements("Layer").Count());
        Assert.All(multilayer.Elements("Layer"), l => Assert.Equal(
            ["volume", "pitch"], l.Elements("Curve").Select(c => (string?)c.Attribute("target"))));
    }

    [Fact]
    public void A_material_switch_picks_between_random_containers()
    {
        if (Fixture.Read(Impacts) is not { } bytes) return;

        SpkBank bank = SpkBank.Parse(bytes);
        SpkBankRecord material = bank.Records.First(r => r.Layout == SpkLayout.Switch);

        Assert.NotEmpty(material.Entries);
        Assert.Contains(material.Entries, e => bank.Find(e.Ref)?.Layout == SpkLayout.Random);
    }

    [Fact]
    public void An_authored_bank_derives_its_sample_words_from_the_audio()
    {
        SpkBank bank = SpkBankXml.FromXml("""
            <SoundBank preamble="0x00fc0200">
              <Audio id="0x00fc0204" file="a.wav" />
              <Sample id="0x00fc0203" audio="0x00fc0204" gainDb="-3" />
              <Rolloff id="0x00fc0205"><Point m="0" db="0" /><Point m="80" db="-96" /></Rolloff>
              <Play id="0x00fc0200" sound="0x00fc0203" rolloff="0x00fc0205" />
            </SoundBank>
            """, _ => Wav(frames: 1000, channels: 1, rate: 44100));

        SpkBankRecord audio = bank.Find(0x00fc0204)!;
        uint[] words = bank.DerivedWords(bank.Find(0x00fc0203)!);

        Assert.Equal((uint)audio.Data.Length, words[SpkLayout.SampleByteLength]);
        Assert.Equal((uint)audio.Data.Length, words[SpkLayout.SampleOneShotBytes]);
        Assert.Equal(1u, words[SpkLayout.SampleChannels]);
        Assert.Equal(44100u, words[SpkLayout.SampleRate]);
        Assert.Equal(0xFFFD0000u, words[5]);
        Assert.Equal(3u, words[SpkLayout.SampleCodec]);
        Assert.Empty(SpkBankLint.Check(bank, 0x00fc0200));

        SpkBank rebuilt = SpkBank.Parse(bank.Write());
        Assert.All(rebuilt.Records, r => Assert.Empty(r.Pins));
        Assert.Equal([new SpkPoint(0, 0), new SpkPoint(80, -96)], rebuilt.Find(0x00fc0205)!.Points);
    }

    [Theory]
    [InlineData("3", "1", null, new uint[] { 0xC000, 0x4000, 0 })]
    [InlineData("0.5", "0.25", "0.25", new uint[] { 0x8000, 0x4000, 0x4000 })]
    [InlineData(null, null, "1", new uint[] { 0x5555, 0x5555, 0x5555 })]
    [InlineData("0x1000", "0x2000", null, new uint[] { 0x1000, 0x2000, 0 })]
    public void Random_weights_are_relative_probabilities_or_raw(string? a, string? b, string? silence, uint[] expected)
    {
        string Weight(string? w) => w is null ? "" : $" weight=\"{w}\"";
        SpkBank bank = SpkBankXml.FromXml($"""
            <SoundBank>
              <Random id="0x1"{(silence is null ? "" : $" silence=\"{silence}\"")}>
                <Choice resource="0x2"{Weight(a)} />
                <Choice resource="0x3"{Weight(b)} />
              </Random>
            </SoundBank>
            """, _ => []);

        SpkBankRecord random = bank.Records.Single();
        uint[] actual = [.. random.Entries.Select(e => e.Value), random.Word(8)];
        Assert.Equal(expected, actual);
    }

    [Fact]
    public void A_misspelt_attribute_is_an_error()
    {
        Assert.Throws<InvalidDataException>(() => SpkBankXml.FromXml(
            """<SoundBank><Play id="0x1" sond="0x2" /></SoundBank>""", _ => []));
    }

    [Fact]
    public void Lint_flags_a_reference_to_the_wrong_kind_and_a_stereo_sample_under_a_rolloff()
    {
        SpkBank bank = SpkBankXml.FromXml("""
            <SoundBank>
              <Audio id="0x3" file="a.wav" />
              <Sample id="0x2" audio="0x3" />
              <Play id="0x1" sound="0x2" rolloff="0x00442c37" />
              <MultiEvent id="0x4"><Child event="0x2" /></MultiEvent>
            </SoundBank>
            """, _ => Wav(frames: 100, channels: 2, rate: 22050));

        IReadOnlyList<SpkProblem> problems = SpkBankLint.Check(bank, 0x1);

        Assert.Contains(problems, p => p.Severity == SpkProblemSeverity.Error && p.Message.Contains("MultiEvent 0x00000004"));
        Assert.Contains(problems, p => p.Severity == SpkProblemSeverity.Warning && p.Message.Contains("stereo"));
    }

    private static XElement Decode(byte[] bytes) => XElement.Parse(SpkBankXml.ToXml(SpkBank.Parse(bytes), (_, _) => { }));

    private static byte[] Wav(int frames, int channels, int rate) =>
        WavAudio.Write(new short[frames * channels], channels, rate);
}
