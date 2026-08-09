using System.ComponentModel;
using JackAll.Cli.Infrastructure;
using JackAll.Tools.Mgb;
using Spectre.Console.Cli;

namespace JackAll.Cli.Commands.Mgb;

/// <summary>Decodes a binary .mgb (Magma UI package) to editable XML. Like .rml and unlike .fcb it
/// never splits, so it's one file in, one out.</summary>
/// <remarks>The XML is an interchange format, not something the game loads - edit it and run
/// <c>mgb encode</c> to build a .mgb back. See docs/docs/file-formats/mgb.md.</remarks>
public sealed class MgbDecodeCommand : CliCommand<MgbDecodeCommand.Settings>
{
    public sealed class Settings : CommandSettings
    {
        [CommandArgument(0, "<file.mgb>")]
        [Description("The binary .mgb to decode.")]
        public string Input { get; init; } = null!;

        [CommandOption("-o|--out <file.xml>")]
        [Description("Output .xml path (default: the input path with an .xml extension).")]
        public string? Out { get; init; }
    }

    protected override int Run(Settings settings, CancellationToken cancellationToken)
    {
        string outPath = settings.Out ?? Path.ChangeExtension(settings.Input, ".xml");

        byte[] data = CliIO.ReadInput(settings.Input);
        CliIO.GuardNotOverwritingInput(settings.Input, outPath);

        CliIO.WriteOutput(outPath, MgbXml.Decode(data));
        CliIO.ReportWrote(outPath);
        return 0;
    }
}
