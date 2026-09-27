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
/// <c>sbao build</c>): an Ogg-backed record takes an Ogg Vorbis replacement verbatim; an IMA-ADPCM one
/// takes a 16-bit PCM <c>.wav</c>, encoded natively via <see cref="ImaAdpcm.Encode"/>. The samples
/// playing it re-derive their lengths, frame counts, rate and channels from the new stream; every
/// other record is written back unchanged.
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
        SpkBank bank = SpkBank.Parse(CliIO.ReadInput(settings.Input));
        SpkBankRecord audio = bank.Find(id)
            ?? throw new InvalidDataException($"No record with id 0x{id:x8} in {settings.Input}.");
        if (SpkBank.DescribeAudio(audio) is not { } current)
        {
            throw new InvalidDataException($"Record 0x{id:x8} is not audio - only FlatCopy records hold audio.");
        }

        byte[] replacement = CliIO.ReadInput(settings.AudioFile);
        if (current.Ogg)
        {
            (int SampleRate, int Channels) vorbis = SbaoAudio.TryReadVorbisId(replacement)
                ?? throw new InvalidDataException(
                    $"Record 0x{id:x8} is Ogg-backed, but the replacement file isn't a recognizable Ogg Vorbis " +
                    "stream - this CLI doesn't transcode.");
            WarnIfChanged(current.SampleRate, current.Channels, vorbis.SampleRate, vorbis.Channels);
            bank.ReplaceAudio(audio, replacement, null);
        }
        else
        {
            WavAudio.Pcm16Audio pcm = WavAudio.ReadPcm16(replacement);
            WarnIfChanged(current.SampleRate, current.Channels, pcm.SampleRate, pcm.Channels);
            bool looping = bank.SamplesPlaying(id).Any(s => s.Word(SpkLayout.SampleLoop) == 1);
            bank.ReplaceAudio(audio, ImaAdpcm.Encode(pcm.Samples, pcm.Channels, looping), pcm.SampleRate);
        }

        byte[] patched = bank.Write();
        // A read-back check, as encode does: a bank that fails to parse is never written.
        SpkBank.Parse(patched);
        string outPath = settings.Out ?? settings.Input;
        CliIO.WriteOutput(outPath, patched);
        CliIO.ReportWrote(outPath);
        return 0;
    }

    private static void WarnIfChanged(int rate, int channels, int newRate, int newChannels)
    {
        if (rate != newRate || channels != newChannels)
        {
            AnsiConsole.MarkupLine(
                $"[yellow]Note:[/] the replacement is {newRate} Hz/{newChannels} ch, the record was {rate} Hz/{channels} ch. " +
                "The samples playing it now declare the new format.");
        }
    }
}
