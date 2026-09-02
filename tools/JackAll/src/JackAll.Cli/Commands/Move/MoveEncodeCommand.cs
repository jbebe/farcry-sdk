using System.ComponentModel;
using JackAll.Cli.Infrastructure;
using JackAll.Tools.Move;

using Spectre.Console.Cli;

namespace JackAll.Cli.Commands.Move;

/// <summary>Builds an XML document back into a binary MOVE animation graph.</summary>
/// <remarks>Objects are addressed by stream position, so every pointer is renumbered as the graph
/// is written - an XML document that adds or removes objects builds correctly without the author
/// touching an index. See docs/docs/file-formats/move.md.</remarks>
public sealed class MoveEncodeCommand : CliCommand<MoveEncodeCommand.Settings>
{
    public sealed class Settings : CommandSettings
    {
        [CommandArgument(0, "<file.xml>")]
        [Description("The XML document to build.")]
        public string Input { get; init; } = null!;

        [CommandOption("-o|--out <file.bin>")]
        [Description("Output path (default: the input path with a .bin extension).")]
        public string? Out { get; init; }
    }

    protected override int Run(Settings settings, CancellationToken cancellationToken)
    {
        string outPath = settings.Out ?? Path.ChangeExtension(settings.Input, ".bin");

        string xml = CliIO.ReadInputText(settings.Input);
        CliIO.GuardNotOverwritingInput(settings.Input, outPath);

        CliIO.WriteOutput(outPath, MoveXml.Encode(xml));
        CliIO.ReportWrote(outPath);
        return 0;
    }
}
