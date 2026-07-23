using System.IO;
using FFMpegCore;
using FFMpegCore.Enums;

namespace JackAll.App.Audio;

/// <summary>
/// Thin wrapper around the bundled ffmpeg.exe (shipped at data\ffmpeg.exe next to the app) for
/// everything the .sbao handler needs: any input format -> 48 kHz stereo Ogg Vorbis for repacking
/// (Far Cry 2 plays music at a fixed 48 kHz; anything else plays at the wrong speed), Ogg -> mp3 for
/// export, and Ogg -> wav for preview playback, since WPF's MediaElement has no built-in Ogg Vorbis
/// decoder and can't be relied on to find one on the host.
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

    /// <summary>Transcodes any ffmpeg-readable audio file to 48 kHz stereo Ogg Vorbis.</summary>
    public static Task TranscodeToOggAsync(string inputPath, string outputPath, int quality = 6)
    {
        EnsureConfigured();
        return FFMpegArguments
            .FromFileInput(inputPath)
            .OutputToFile(outputPath, overwrite: true, addArguments: options => options
                .WithAudioCodec(AudioCodec.LibVorbis)
                .WithAudioSamplingRate(RequiredSampleRate)
                .WithCustomArgument($"-ac {RequiredChannels}")
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
}
