using System.ComponentModel;
using JackAll.Cli.Infrastructure;
using JackAll.Tools.Hkx;
using Spectre.Console;
using Spectre.Console.Cli;

namespace JackAll.Cli.Commands.Hkx;

/// <summary>Brings an edited .hkx's MOPP code, cached bounds and radii in line with its shapes.</summary>
public sealed class HkxRefreshCommand : CliCommand<HkxRefreshCommand.Settings>
{
    public sealed class Settings : CommandSettings
    {
        [CommandArgument(0, "<file.hkx>")]
        [Description("The edited file.")]
        public string Input { get; init; } = null!;

        [CommandOption("--original <file.hkx>")]
        [Description("The file before the edits, such as the retail one: each cache moves by as much as the shapes did since it.")]
        public string? Original { get; init; }

        [CommandOption("-o|--out <file.hkx>")]
        [Description("Output path (default: the input, refreshed in place).")]
        public string? Out { get; init; }

        public override ValidationResult Validate()
            => string.IsNullOrWhiteSpace(Original) ? ValidationResult.Error("--original is required: the file before the edits.") : ValidationResult.Success();
    }

    protected override int Run(Settings settings, CancellationToken cancellationToken)
        => HkxCli.Save(HkxFile.Parse(CliIO.ReadInput(settings.Input)), HkxFile.Parse(CliIO.ReadInput(settings.Original!)), settings.Out ?? settings.Input);
}
