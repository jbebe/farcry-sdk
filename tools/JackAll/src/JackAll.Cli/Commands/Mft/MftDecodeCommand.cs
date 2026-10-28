using System.ComponentModel;
using JackAll.Cli.Infrastructure;
using JackAll.Core.Format;
using Spectre.Console.Cli;

namespace JackAll.Cli.Commands.Mft;

/// <summary>Decodes a Magma font to editable XML. See docs/docs/file-formats/mft.md.</summary>
public sealed class MftDecodeCommand : CliCommand<MftDecodeCommand.Settings>
{
    public sealed class Settings : CommandSettings
    {
        [CommandArgument(0, "<file.mft>")]
        [Description("The font to decode.")]
        public string Input { get; init; } = null!;

        [CommandOption("-o|--out <file.xml>")]
        [Description("Output path (default: the input path with a .xml extension).")]
        public string? Out { get; init; }
    }

    protected override int Run(Settings settings, CancellationToken cancellationToken)
    {
        string outPath = settings.Out ?? Path.ChangeExtension(settings.Input, ".xml");
        MftFont font = MftFont.Read(CliIO.ReadInput(settings.Input));
        CliIO.GuardNotOverwritingInput(settings.Input, outPath);
        CliIO.WriteOutput(outPath, font.ToXml().ToString());
        CliIO.ReportWrote(outPath);
        return 0;
    }
}
