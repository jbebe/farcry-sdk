using JackAll.Cli.Infrastructure;
using JackAll.Tools.Format;
using Spectre.Console.Cli;
using Spectre.Console;
using System.ComponentModel;

namespace JackAll.Cli.Commands.Spk;

/// <summary>
/// Extracts one record's audio from an .spk bank. A real `FlatCopy` record holds either Ogg Vorbis or
/// IMA-ADPCM (see <see cref="SpkPackage"/>'s remarks - there's no reliable way to tell which a given
/// record uses without checking; real banks split roughly 74%/26%), detected automatically per record:
/// Ogg-backed audio is written out verbatim as <c>.ogg</c> (no re-encoding, so it round-trips
/// losslessly through <c>spk import</c>); IMA-ADPCM is decoded natively (see <see cref="ImaAdpcm"/>) to
/// a plain 16-bit PCM <c>.wav</c>. This CLI does not transcode - use ffmpeg (or the App, which bundles
/// it) first if you need a different format/rate/channel count before re-importing.
/// </summary>
public sealed class SpkExtractCommand : CliCommand<SpkExtractCommand.Settings>
{
    public sealed class Settings : CommandSettings
    {
        [CommandArgument(0, "<file.spk>")]
        [Description("The .spk sound bank to read.")]
        public string Input { get; init; } = null!;

        [CommandArgument(1, "<record-id>")]
        [Description("The record id to extract, e.g. 0x004e1c50 (see `spk list`).")]
        public string RecordId { get; init; } = null!;

        [CommandOption("-o|--out <file>")]
        [Description("Output path (default: <record-id>.ogg or .wav next to the input, depending on codec).")]
        public string? Out { get; init; }
    }

    protected override int Run(Settings settings, CancellationToken cancellationToken)
    {
        uint id = SpkFormat.ParseRecordId(settings.RecordId);
        SpkPackage package = SpkPackage.Parse(CliIO.ReadInput(settings.Input));
        SpkRecord record = package.Records.FirstOrDefault(r => r.Id == id)
            ?? throw new InvalidDataException($"No record with id 0x{id:x8} in {settings.Input}.");

        if (record.FlatCopyAudioStream is not { } audio)
        {
            throw new InvalidDataException(
                $"Record 0x{id:x8} is a {SpkFormat.DescribeKind(record)} record, not audio - only FlatCopy records hold audio.");
        }

        if (SbaoAudio.TryReadVorbisId(audio) is { } vorbis)
        {
            string oggPath = CliIO.ResolveOutput(settings.Out, settings.Input, $"{id:x8}.ogg");
            CliIO.WriteOutput(oggPath, audio);
            CliIO.ReportWrote(oggPath);
            AnsiConsole.MarkupLine($"  Ogg Vorbis, {vorbis.SampleRate} Hz, {vorbis.Channels} ch");
            return 0;
        }

        ImaAdpcm.DecodedAudio decoded = ImaAdpcm.Decode(audio);
        int? sampleRate = package.TryGetFlatCopySampleRate(record);
        byte[] wav = WavAudio.Write(decoded.Samples, decoded.Channels, sampleRate ?? SpkFormat.FallbackSampleRateHz);

        string wavPath = CliIO.ResolveOutput(settings.Out, settings.Input, $"{id:x8}.wav");
        CliIO.WriteOutput(wavPath, wav);
        CliIO.ReportWrote(wavPath);
        AnsiConsole.MarkupLine($"  IMA-ADPCM, {sampleRate ?? SpkFormat.FallbackSampleRateHz} Hz, {decoded.Channels} ch"
            + (sampleRate is null ? " [yellow](no sibling TransformedFixed128 rate found - guessed)[/]" : ""));
        return 0;
    }
}
