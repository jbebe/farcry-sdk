using JackAll.Core.Naming;
using JackAll.Tools.Audio;

namespace JackAll.Tests;

public class FileTypeSnifferTests
{
    private static readonly byte[] Wav = WavAudio.Write([1, -1], channels: 1, sampleRate: 48000);

    [Fact]
    public void A_bank_can_replace_another_bank()
    {
        if (Fixture.Read(SpkBankTests.PassBy) is not { } replaced || Fixture.Read(SpkBankTests.Multilayer) is not { } bank) return;

        Assert.Null(FileTypeSniffer.Mismatch(replaced, bank));
    }

    [Theory]
    [InlineData(SpkBankTests.PassBy, "spk")]
    [InlineData("Sbao/004b49fc.sbao", "sbao")]
    public void A_wav_cannot_replace_a_sound_file(string path, string extension)
    {
        if (Fixture.Read(path) is not { } replaced) return;

        Assert.Equal(extension, FileTypeSniffer.IdentifyByContent(replaced).Extension);
        Assert.Equal("wem", FileTypeSniffer.Mismatch(replaced, Wav)?.Extension);
    }

    [Fact]
    public void A_text_format_is_left_unchecked()
        => Assert.Null(FileTypeSniffer.Mismatch("<?xml version=\"1.0\"?>"u8, Wav));
}
