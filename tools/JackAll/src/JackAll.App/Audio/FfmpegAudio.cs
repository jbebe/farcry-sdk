using System.IO;
using FFMpegCore;
using FFMpegCore.Enums;
using JackAll.Core.Format;

namespace JackAll.App.Audio;

/// <summary>
/// Thin wrapper around the bundled ffmpeg.exe (shipped at data\ffmpeg.exe next to the app) for
/// everything the .sbao and .spk handlers need: any input format -> 48 kHz stereo Ogg Vorbis for
/// .sbao repacking (Far Cry 2 plays music at a fixed 48 kHz; anything else plays at the wrong speed),
/// Ogg -> mp3 for export, Ogg -> wav for preview playback (WPF's MediaElement has no built-in Ogg
/// Vorbis decoder and can't be relied on to find one on the host), and any input format -> raw PCM wav
/// at an arbitrary rate/channel count for .spk's `FlatCopy` audio-replace flow, which encodes that PCM
/// itself rather than shelling out to ffmpeg for the actual codec (see <see cref="Format.ImaAdpcm"/>).
/// </summary>
public static class FfmpegAudio
{
    public const int RequiredSampleRate = 48000;
    public const int RequiredChannels = 2;

    private static bool _configured;

    private static void EnsureConfigured()
    {
        if (_configured)
        {
            return;
        }

        string exe = Path.Combine(AppContext.BaseDirectory, "data", "ffmpeg.exe");
        GlobalFFOptions.Configure(new FFOptions { BinaryFolder = Path.GetDirectoryName(exe)! });
        _configured = true;
    }

    /// <summary>Transcodes any ffmpeg-readable audio file to 48 kHz stereo Ogg Vorbis - Far Cry 2's
    /// one fixed requirement for `.sbao` music.</summary>
    public static Task TranscodeToOggAsync(string inputPath, string outputPath, int quality = 6)
        => TranscodeToOggAsync(inputPath, outputPath, RequiredSampleRate, RequiredChannels, quality);

    /// <summary>Transcodes any ffmpeg-readable audio file to Ogg Vorbis at a specific sample rate and
    /// channel count - used when replacing an Ogg-backed `.spk` `FlatCopy` record (see
    /// <see cref="SbaoAudio.TryReadVorbisId"/>'s use in `SpkFileHandler`), which - unlike `.sbao` music
    /// - has no single required rate; the replacement is transcoded to match whatever the record being
    /// replaced already used.</summary>
    public static Task TranscodeToOggAsync(string inputPath, string outputPath, int sampleRate, int channels, int quality = 6)
    {
        EnsureConfigured();
        return FFMpegArguments
            .FromFileInput(inputPath)
            .OutputToFile(outputPath, overwrite: true, addArguments: options => options
                .WithAudioCodec(AudioCodec.LibVorbis)
                .WithAudioSamplingRate(sampleRate)
                .WithCustomArgument($"-ac {channels}")
                .WithCustomArgument($"-q:a {quality}"))
            .ProcessAsynchronously();
    }

    /// <summary>Transcodes to a good-quality (~245 kbps average) VBR mp3, matching libmp3lame's "V0" preset.</summary>
    public static Task TranscodeToMp3Async(string inputPath, string outputPath)
    {
        EnsureConfigured();
        return FFMpegArguments
            .FromFileInput(inputPath)
            .OutputToFile(outputPath, overwrite: true, addArguments: options => options
                .WithAudioCodec(AudioCodec.LibMp3Lame)
                .WithCustomArgument("-q:a 0"))
            .ProcessAsynchronously();
    }

    /// <summary>Transcodes to PCM wav purely for preview playback (see class remarks for why).</summary>
    public static Task TranscodeToWavAsync(string inputPath, string outputPath)
    {
        EnsureConfigured();
        return FFMpegArguments
            .FromFileInput(inputPath)
            .OutputToFile(outputPath, overwrite: true, addArguments: options => options.WithCustomArgument("-vn"))
            .ProcessAsynchronously();
    }

    /// <summary>Transcodes any ffmpeg-readable audio file to uncompressed 16-bit PCM wav at a specific
    /// sample rate and channel count - used by the .spk `FlatCopy` audio-replace flow, which needs raw
    /// samples to feed <see cref="Format.ImaAdpcm.Encode"/>, at whatever rate/channel count the record
    /// being replaced already uses (there's no single fixed target the way `.sbao` has one).</summary>
    public static Task TranscodeToPcmWavAsync(string inputPath, string outputPath, int sampleRate, int channels)
    {
        EnsureConfigured();
        return FFMpegArguments
            .FromFileInput(inputPath)
            .OutputToFile(outputPath, overwrite: true, addArguments: options => options
                .WithAudioSamplingRate(sampleRate)
                .WithCustomArgument($"-ac {channels}")
                .WithCustomArgument("-c:a pcm_s16le"))
            .ProcessAsynchronously();
    }
}
