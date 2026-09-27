using JackAll.Tools.Audio;
using JackAll.Tools.Sbao;
using JackAll.Tools.Spk;
using Spectre.Console;

namespace JackAll.Cli.Commands.Spk;

/// <summary>
/// Shared record-description and codec-detection logic for the spk commands - the CLI's own version of
/// the App's <c>SpkFileHandler</c> row summaries (see <see cref="SpkPackage"/>'s remarks for what each
/// field means and how confident that meaning is), reused by <c>spk list</c> for display and by
/// <c>spk extract</c>/<c>spk import</c> to decide Ogg Vorbis vs IMA-ADPCM per `FlatCopy` record.
/// </summary>
internal static class SpkFormat
{
    public static string DescribeKind(SpkRecord r) => r.Core switch
    {
        null => "(malformed)",
        { Type: SpkRecordType.FlatCopy } => "Audio",
        { Type: SpkRecordType.TransformedFixed128 } when r.TransformedFixed128 is { Kind: var kind } =>
            SpkLayout.ForResource(kind) is { Kind: not null, Element: var element } ? element : $"Resource (kind {kind})",
        { Type: SpkRecordType.SimpleFixed68 } => r.SimpleFixed68?.KnownEventType switch
        {
            SpkEventType.List => "Event list",
            SpkEventType.Switch => "Event switch",
            _ => "Sound event",
        },
        { Type: { } t } => t.ToString(),
        _ => $"Unknown (0x{r.Core.RawType:x8})",
    };

    public static string DescribeSummary(SpkPackage package, SpkRecord r)
    {
        if (r.FlatCopyAudioStream is { } audio)
        {
            if (SbaoAudio.TryReadVorbisId(audio) is { } vorbis)
            {
                return $"{DescribeChannels(vorbis.Channels)} - {vorbis.SampleRate} Hz - Ogg Vorbis - {FormatBytes(audio.Length)}{DescribeLengthMismatch(package, r)}";
            }

            try
            {
                ImaAdpcm.DecodedAudio decoded = ImaAdpcm.Decode(audio);
                int? sampleRate = package.TryGetFlatCopySampleRate(r);
                string rateLabel = sampleRate is { } hz ? $"{hz} Hz" : $"~{SpkBank.FallbackSampleRate} Hz (no rate on record)";
                return $"{DescribeChannels(decoded.Channels)} - {rateLabel} - IMA-ADPCM - {FormatBytes(audio.Length)}{DescribeLengthMismatch(package, r)}";
            }
            catch (Exception ex)
            {
                return $"couldn't decode: {ex.Message}";
            }
        }

        if (r.TransformedFixed128 is { Kind: (uint)SpkResourceKind.Sample } t128)
        {
            return $"-> audio 0x{t128.FlatCopySiblingId:x8} - {t128.SampleRate} Hz";
        }

        if (r.TransformedFixed128 is not null)
        {
            return "a container - `spk decode` shows its children";
        }

        if (r.SimpleFixed68 is { } s68)
        {
            if (s68.IsComposite)
            {
                string children = s68.ChildIds.Count == 0
                    ? "(empty list)"
                    : string.Join(" ", s68.ChildIds.Select(id => $"0x{id:x8}"));
                return $"plays {s68.ChildIds.Count} -> {children}";
            }

            return $"-> 0x{s68.LinkedId:x8}";
        }

        return r.Core is null ? "too short for the 40-byte record core" : $"{r.Payload.Length:N0} bytes";
    }

    /// <summary>Flags an audio record whose descriptor declares a different length than its stream.</summary>
    private static string DescribeLengthMismatch(SpkPackage package, SpkRecord r) =>
        package.DeclaredAudioLengthMatches(r) == false && package.TryGetAudioDescriptor(r) is { } t128
            ? $"  (!) descriptor declares {t128.AudioByteLength:N0} B"
            : "";

    private static string DescribeChannels(int channels) => channels switch
    {
        1 => "Mono",
        2 => "Stereo",
        _ => $"{channels}ch",
    };

    public static string FormatBytes(long bytes) => bytes switch
    {
        >= 1024 * 1024 => $"{bytes / (1024.0 * 1024.0):0.#} MB",
        >= 1024 => $"{bytes / 1024.0:0.#} KB",
        _ => $"{bytes} B",
    };

    /// <summary>Prints what <see cref="SpkBankLint"/> found; false when any of it is an error.</summary>
    public static bool Report(IReadOnlyList<SpkProblem> problems)
    {
        foreach (SpkProblem problem in problems)
        {
            string label = problem.Severity switch
            {
                SpkProblemSeverity.Error => "[red]error[/]",
                SpkProblemSeverity.Warning => "[yellow]warning[/]",
                _ => "[grey]note[/]",
            };
            AnsiConsole.MarkupLine($"  {label} {problem.Message.EscapeMarkup()}");
        }
        return problems.All(p => p.Severity != SpkProblemSeverity.Error);
    }
}
