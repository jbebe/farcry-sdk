using System.ComponentModel;
using JackAll.Cli.Infrastructure;
using JackAll.Tools.Hkx;
using Spectre.Console.Cli;

namespace JackAll.Cli.Commands.Hkx;

/// <summary>Writes an .hkx as editable XML, which hkx encode builds back.</summary>
public sealed class HkxDecodeCommand : CliCommand<HkxDecodeCommand.Settings>
{
    public sealed class Settings : CommandSettings
    {
        [CommandArgument(0, "<file.hkx>")]
        [Description("The Havok file.")]
        public string Input { get; init; } = null!;

        [CommandOption("-o|--out <file.xml>")]
        [Description("Output path (default: the input path with an .xml extension).")]
        public string? Out { get; init; }
    }

    protected override int Run(Settings settings, CancellationToken cancellationToken)
    {
        string outPath = settings.Out ?? Path.ChangeExtension(settings.Input, ".xml");
        CliIO.WriteOutput(outPath, HkxXml.ToXml(HkxFile.Parse(CliIO.ReadInput(settings.Input))));
        CliIO.ReportWrote(outPath);
        return 0;
    }
}
