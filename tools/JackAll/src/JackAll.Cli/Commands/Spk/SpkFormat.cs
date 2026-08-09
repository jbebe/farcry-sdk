using JackAll.Tools.Audio;
using JackAll.Tools.Sbao;
using JackAll.Tools.Spk;
using System.Globalization;

namespace JackAll.Cli.Commands.Spk;

/// <summary>
/// Shared record-description and codec-detection logic for the spk commands - the CLI's own version of
/// the App's <c>SpkFileHandler</c> row summaries (see <see cref="SpkPackage"/>'s remarks for what each
/// field means and how confident that meaning is), reused by <c>spk list</c> for display and by
/// <c>spk extract</c>/<c>spk import</c> to decide Ogg Vorbis vs IMA-ADPCM per `FlatCopy` record.
/// </summary>
internal static class SpkFormat
{
    public const int FallbackSampleRateHz = 32000; // most common real-install TransformedFixed128 rate

    public static string DescribeKind(SpkRecord r) => r.Core switch
    {
        null => "(malformed)",
        { Type: SpkRecordType.FlatCopy } => "Audio",
        { Type: SpkRecordType.TransformedFixed128 } => "Audio params",
        { Type: SpkRecordType.SimpleFixed68 } => "Sound params",
        { Type: { } t } => t.ToString(),
        _ => $"Unknown (0x{r.Core.RawType:x8})",
    };

    public static string DescribeSummary(SpkPackage package, SpkRecord r)
    {
        if (r.FlatCopyAudioStream is { } audio)
        {
            if (SbaoAudio.TryReadVorbisId(audio) is { } vorbis)
            {
                return $"{DescribeChannels(vorbis.Channels)} - {vorbis.SampleRate} Hz - Ogg Vorbis - {FormatBytes(audio.Length)}";
            }

            try
            {
                ImaAdpcm.DecodedAudio decoded = ImaAdpcm.Decode(audio);
                int? sampleRate = package.TryGetFlatCopySampleRate(r);
                string rateLabel = sampleRate is { } hz ? $"{hz} Hz" : $"~{FallbackSampleRateHz} Hz (no rate on record)";
                return $"{DescribeChannels(decoded.Channels)} - {rateLabel} - IMA-ADPCM - {FormatBytes(audio.Length)}";
            }
            catch (Exception ex)
            {
                return $"couldn't decode: {ex.Message}";
            }
        }

        if (r.TransformedFixed128 is { } t128)
        {
            return $"-> audio 0x{t128.FlatCopySiblingId:x8} - {t128.SampleRate} Hz";
        }

        if (r.SimpleFixed68 is { } s68)
        {
            return $"-> 0x{s68.LinkedId:x8}";
        }

        return r.Core is null ? "too short for the 40-byte record core" : $"{r.Payload.Length:N0} bytes";
    }

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

    /// <summary>Parses a record id as given on the command line - "0x004e1c50" or bare "004e1c50",
    /// matching however it's shown by `spk list`/the App. Always hex; ids are never meaningfully
    /// decimal in this format.</summary>
    public static uint ParseRecordId(string text)
    {
        string trimmed = text.Trim();
        string hex = trimmed.StartsWith("0x", StringComparison.OrdinalIgnoreCase) ? trimmed[2..] : trimmed;
        if (uint.TryParse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out uint value))
        {
            return value;
        }

        throw new FormatException($"Not a valid record id: {text} (expected hex, e.g. 0x004e1c50 or 004e1c50).");
    }
}
