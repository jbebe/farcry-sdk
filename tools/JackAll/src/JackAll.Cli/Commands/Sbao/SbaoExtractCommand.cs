using JackAll.Cli.Infrastructure;
using JackAll.Tools.Sbao;
using Spectre.Console.Cli;
using System.ComponentModel;

namespace JackAll.Cli.Commands.Sbao;

/// <summary>
/// Splits an Ogg-backed .sbao into its playable <c>.ogg</c> stream and a raw <c>.sbaoheader</c> holding
/// the 40-byte engine header (including its per-asset GUID). The App only exports the .ogg because it
/// keeps the original header in memory for re-import; a standalone CLI needs that header written out so
/// <c>sbao build</c> can reassemble a byte-faithful .sbao without the original file on hand.
/// </summary>
public sealed class SbaoExtractCommand : CliCommand<SbaoExtractCommand.Settings>
{
    public sealed class Settings : CommandSettings
    {
        [CommandArgument(0, "<file.sbao>")]
        [Description("The Ogg-backed .sbao to split.")]
        public string Input { get; init; } = null!;

        [CommandOption("-o|--out-dir <dir>")]
        [Description("Directory for the .ogg/.sbaoheader pair (default: next to the input).")]
        public string? OutDir { get; init; }
    }

    protected override int Run(Settings settings, CancellationToken cancellationToken)
    {
        byte[] sbao = CliIO.ReadInput(settings.Input);
        (byte[] header, byte[] ogg) = SbaoAudio.Split(sbao);

        string baseName = Path.GetFileNameWithoutExtension(settings.Input);
        string oggPath = CliIO.ResolveOutput(
            settings.OutDir is null ? null : Path.Combine(settings.OutDir, baseName + ".ogg"),
            settings.Input, baseName + ".ogg");
        string headerPath = Path.ChangeExtension(oggPath, ".sbaoheader");

        CliIO.WriteOutput(oggPath, ogg);
        CliIO.WriteOutput(headerPath, header);

        CliIO.ReportWrote(oggPath);
        CliIO.ReportWrote(headerPath);
        return 0;
    }
}
