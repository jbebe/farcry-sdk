using System.ComponentModel;
using System.Xml.Linq;
using JackAll.Cli.Infrastructure;
using JackAll.Core.Format;
using Spectre.Console.Cli;

namespace JackAll.Cli.Commands.Mft;

/// <summary>Builds a Magma font back from the XML <see cref="MftDecodeCommand"/> writes.</summary>
public sealed class MftEncodeCommand : CliCommand<MftEncodeCommand.Settings>
{
    public sealed class Settings : CommandSettings
    {
        [CommandArgument(0, "<file.xml>")]
        [Description("The decoded font.")]
        public string Input { get; init; } = null!;

        [CommandOption("-o|--out <file.mft>")]
        [Description("Output path (default: the input path with a .mft extension).")]
        public string? Out { get; init; }
    }

    protected override int Run(Settings settings, CancellationToken cancellationToken)
    {
        string outPath = settings.Out ?? Path.ChangeExtension(settings.Input, ".mft");
        MftFont font = MftFont.FromXml(XElement.Parse(CliIO.ReadInputText(settings.Input)));
        CliIO.GuardNotOverwritingInput(settings.Input, outPath);
        CliIO.WriteOutput(outPath, font.Write());
        CliIO.ReportWrote(outPath);
        return 0;
    }
}
