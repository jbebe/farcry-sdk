using System.ComponentModel;
using JackAll.Cli.Infrastructure;
using JackAll.Core.Format;
using Spectre.Console.Cli;

namespace JackAll.Cli.Commands.Sbao;

/// <summary>
/// Reassembles an .sbao from an <c>.ogg</c> and the <c>.sbaoheader</c> produced by <c>sbao extract</c>.
/// The CLI works at the container level only — it does not transcode, so the .ogg has to already be in
/// the format you want (a note is printed if it isn't the 48 kHz stereo music format; use ffmpeg
/// upstream, or the App, which bundles it). Validates the result by round-tripping it through
/// <see cref="SbaoAudio.Split"/>.
/// </summary>
public sealed class SbaoBuildCommand : CliCommand<SbaoBuildCommand.Settings>
{
    public sealed class Settings : CommandSettings
    {
        [CommandArgument(0, "<file.ogg>")]
        [Description("The Ogg Vorbis payload (this tool does not transcode).")]
        public string Ogg { get; init; } = null!;

        [CommandArgument(1, "[file.sbaoheader]")]
        [Description("The engine header from `sbao extract` (default: the .ogg path with a .sbaoheader extension).")]
        public string? Header { get; init; }

        [CommandOption("-o|--out <file.sbao>")]
        [Description("Output .sbao path (default: the .ogg path with an .sbao extension).")]
        public string? Out { get; init; }
    }

    protected override int Run(Settings settings, CancellationToken cancellationToken)
    {
        string headerPath = settings.Header ?? Path.ChangeExtension(settings.Ogg, ".sbaoheader");
        string outPath = settings.Out ?? Path.ChangeExtension(settings.Ogg, ".sbao");

        byte[] ogg = CliIO.ReadInput(settings.Ogg);
        byte[] header = CliIO.ReadInput(headerPath);

        SbaoFormat.WarnIfNotMusicFormat(ogg);

        byte[] combined = SbaoAudio.Combine(header, ogg);
        SbaoAudio.Split(combined); // validity check, same as the App before staging

        CliIO.WriteOutput(outPath, combined);
        CliIO.ReportWrote(outPath);
        return 0;
    }
}
