using JackAll.Cli.Infrastructure;
using JackAll.Tools.Audio;
using JackAll.Tools.Sbao;
using JackAll.Tools.Spk;
using Spectre.Console.Cli;
using Spectre.Console;
using System.ComponentModel;

namespace JackAll.Cli.Commands.Spk;

/// <summary>
/// Replaces one record's audio in an .spk bank with an already-encoded file - the CLI form of the
/// App's "Import…" button, minus the ffmpeg transcoding step (the same container-level-only design as
/// <c>sbao build</c>): an Ogg-backed record needs an already-Ogg-Vorbis replacement (ideally at its
/// own rate/channel count - a mismatch is only a warning, since the container swap itself is lossless
/// either way); an IMA-ADPCM-backed record needs a 16-bit PCM <c>.wav</c>, encoded natively via
/// <see cref="ImaAdpcm.Encode"/>. Only the target record's payload changes - ids, preamble words, and
/// every other record are carried forward byte-for-byte via
/// <see cref="SpkPackage.ReplaceRecordPayload"/> - and the result is round-tripped back through
/// <see cref="SpkPackage.Parse"/> as a validity check before being written.
/// </summary>
public sealed class SpkImportCommand : CliCommand<SpkImportCommand.Settings>
{
    public sealed class Settings : CommandSettings
    {
        [CommandArgument(0, "<file.spk>")]
        [Description("The .spk sound bank to patch.")]
        public string Input { get; init; } = null!;

        [CommandArgument(1, "<record-id>")]
        [Description("The record id to replace, e.g. 0x004e1c50 (see `spk list`).")]
        public string RecordId { get; init; } = null!;

        [CommandArgument(2, "<audio-file>")]
        [Description("Replacement audio: .ogg for an Ogg-backed record, .wav (16-bit PCM) for an IMA-ADPCM one.")]
        public string AudioFile { get; init; } = null!;

        [CommandOption("-o|--out <file.spk>")]
        [Description("Output .spk path (default: overwrites the input - the usual case, since a Loose " +
                      "override needs the same filename as the original).")]
        public string? Out { get; init; }
    }

    protected override int Run(Settings settings, CancellationToken cancellationToken)
    {
        uint id = SpkFormat.ParseRecordId(settings.RecordId);
        byte[] original = CliIO.ReadInput(settings.Input);
        SpkPackage package = SpkPackage.Parse(original);
        SpkRecord record = package.Records.FirstOrDefault(r => r.Id == id)
            ?? throw new InvalidDataException($"No record with id 0x{id:x8} in {settings.Input}.");

        if (record.FlatCopyAudioStream is not { } currentAudio)
        {
            throw new InvalidDataException(
                $"Record 0x{id:x8} is a {SpkFormat.DescribeKind(record)} record, not audio - only FlatCopy records hold audio.");
        }

        byte[] replacement = CliIO.ReadInput(settings.AudioFile);
        byte[] newAudioStream = (SbaoAudio.TryReadVorbisId(currentAudio) is { } currentVorbis)
            ? BuildOggReplacement(replacement, id, currentVorbis)
            : BuildImaAdpcmReplacement(replacement, currentAudio, package, record);

        byte[] newPayload = [.. record.Payload[..SpkRecordCore.Size], .. newAudioStream];
        byte[] patched = SpkPackage.ReplaceRecordPayload(original, id, newPayload);
        SpkPackage.Parse(patched); // validity check, same as the App before staging

        string outPath = settings.Out ?? settings.Input;
        CliIO.WriteOutput(outPath, patched);
        CliIO.ReportWrote(outPath);
        return 0;
    }

    private static byte[] BuildOggReplacement(byte[] replacement, uint id, (int SampleRate, int Channels) current)
    {
        (int SampleRate, int Channels)? replacementVorbis = SbaoAudio.TryReadVorbisId(replacement);
        if (replacementVorbis is null)
        {
            throw new InvalidDataException(
                $"Record 0x{id:x8} is Ogg-backed ({current.SampleRate} Hz, {current.Channels} ch), but the " +
                "replacement file isn't a recognizable Ogg Vorbis stream - this CLI doesn't transcode.");
        }

        if (replacementVorbis != current)
        {
            AnsiConsole.MarkupLine(
                $"[yellow]Note:[/] replacement is {replacementVorbis.Value.SampleRate} Hz/{replacementVorbis.Value.Channels} ch, " +
                $"the record being replaced was {current.SampleRate} Hz/{current.Channels} ch. The container swap " +
                "is lossless either way, but a mismatch may play back oddly in-game.");
        }

        return replacement; // verbatim - already a complete Ogg Vorbis file
    }

    private static byte[] BuildImaAdpcmReplacement(byte[] replacement, byte[] currentAudio, SpkPackage package, SpkRecord record)
    {
        int channels = ImaAdpcm.Decode(currentAudio).Channels;
        int sampleRate = package.TryGetFlatCopySampleRate(record) ?? SpkFormat.FallbackSampleRateHz;

        WavAudio.Pcm16Audio pcm = WavAudio.ReadPcm16(replacement);
        if (pcm.Channels != channels || pcm.SampleRate != sampleRate)
        {
            AnsiConsole.MarkupLine(
                $"[yellow]Note:[/] replacement .wav is {pcm.SampleRate} Hz/{pcm.Channels} ch, the record being " +
                $"replaced was {sampleRate} Hz/{channels} ch. Re-sample/re-mix upstream (e.g. with ffmpeg) for an " +
                "exact match - this CLI encodes whatever you give it as-is.");
        }

        return ImaAdpcm.Encode(pcm.Samples, pcm.Channels);
    }
}
