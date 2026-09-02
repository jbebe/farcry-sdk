using System.ComponentModel;
using JackAll.Cli.Infrastructure;
using JackAll.Core.Format;

using Spectre.Console.Cli;

namespace JackAll.Cli.Commands.DepLoad;

/// <summary>Builds an XML document back into a binary `depload.dat`.</summary>
/// <remarks>The parents' sort order, every child slice and the type table are re-derived as the file
/// is written, so an XML document that adds or removes entries builds correctly without the author
/// touching an index. See docs/docs/file-formats/depload.md.</remarks>
public sealed class DepLoadEncodeCommand : CliCommand<DepLoadEncodeCommand.Settings>
{
    public sealed class Settings : CommandSettings
    {
        [CommandArgument(0, "<file.xml>")]
        [Description("The XML document to build.")]
        public string Input { get; init; } = null!;

        [CommandOption("-o|--out <file_depload.dat>")]
        [Description("Output path (default: the input path with a .dat extension).")]
        public string? Out { get; init; }
    }

    protected override int Run(Settings settings, CancellationToken cancellationToken)
    {
        string outPath = settings.Out ?? Path.ChangeExtension(settings.Input, ".dat");

        string xml = CliIO.ReadInputText(settings.Input);
        CliIO.GuardNotOverwritingInput(settings.Input, outPath);

        CliIO.WriteOutput(outPath, DepLoadXml.Encode(xml));
        CliIO.ReportWrote(outPath);
        return 0;
    }
}
