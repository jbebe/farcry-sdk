using System.ComponentModel;
using JackAll.Cli.Infrastructure;
using JackAll.Tools.Hkx;
using Spectre.Console.Cli;

namespace JackAll.Cli.Commands.Hkx;

/// <summary>Builds XML from hkx decode back into an .hkx, refreshing its caches against the original and checking it.</summary>
public sealed class HkxEncodeCommand : CliCommand<HkxEncodeCommand.Settings>
{
    public sealed class Settings : CommandSettings
    {
        [CommandArgument(0, "<file.xml>")]
        [Description("The XML to build.")]
        public string Input { get; init; } = null!;

        [CommandOption("--original <file.hkx>")]
        [Description("The file the XML was decoded from: the caches of shapes edited since then are refreshed against it. "
            + "Without it they are written as the XML has them.")]
        public string? Original { get; init; }

        [CommandOption("-o|--out <file.hkx>")]
        [Description("Output path (default: the input path with an .hkx extension).")]
        public string? Out { get; init; }
    }

    protected override int Run(Settings settings, CancellationToken cancellationToken)
    {
        string outPath = settings.Out ?? Path.ChangeExtension(settings.Input, ".hkx");
        CliIO.GuardNotOverwritingInput(settings.Input, outPath);
        HkxFile file = HkxXml.FromXml(CliIO.ReadInputText(settings.Input));
        return HkxCli.Save(file, settings.Original is null ? null : HkxFile.Parse(CliIO.ReadInput(settings.Original)), outPath);
    }
}
