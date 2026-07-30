using JackAll.Tools.Format;
using Spectre.Console;

namespace JackAll.Cli.Infrastructure;

/// <summary>
/// Shared Vorbis-format facts for the sbao commands. Far Cry 2's music .sbao are 48 kHz stereo (the
/// format the App transcodes every import to), while dialogue/localization banks are commonly 32 kHz
/// mono — both ship in the retail game. So a mismatch is a heads-up to match whatever you're replacing,
/// not proof the game will reject it: the container reassembly itself is lossless either way.
/// </summary>
internal static class SbaoFormat
{
    public const int MusicSampleRate = 48_000;
    public const int MusicChannels = 2;

    public static bool IsMusicFormat((int SampleRate, int Channels)? vorbis)
        => vorbis is { SampleRate: MusicSampleRate, Channels: MusicChannels };

    public static string Describe((int SampleRate, int Channels)? vorbis)
        => vorbis is { } v ? $"{v.SampleRate} Hz, {v.Channels} ch" : "unrecognized Vorbis header";

    /// <summary>Prints an advisory note when <paramref name="ogg"/> isn't the 48 kHz stereo music
    /// format — used by the build/replace commands, which reassemble a container without transcoding.</summary>
    public static void WarnIfNotMusicFormat(byte[] ogg)
    {
        (int SampleRate, int Channels)? vorbis = SbaoAudio.TryReadVorbisId(ogg);
        if (IsMusicFormat(vorbis))
        {
            return;
        }

        AnsiConsole.MarkupLine(
            $"[yellow]Note:[/] the .ogg is {Describe(vorbis).EscapeMarkup()}. Far Cry 2's music .sbao use " +
            $"{MusicSampleRate} Hz / {MusicChannels} ch (dialogue/loc banks are often 32000 Hz mono) — " +
            "match the format of whatever you're replacing.");
    }
}
