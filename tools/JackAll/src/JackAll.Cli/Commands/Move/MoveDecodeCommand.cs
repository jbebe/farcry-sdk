using System.ComponentModel;
using JackAll.Cli.Infrastructure;
using JackAll.Tools.Move;

using Spectre.Console.Cli;

namespace JackAll.Cli.Commands.Move;

/// <summary>Decodes a MOVE animation graph (movemgr.bin, dlc*.bin) to editable XML.</summary>
/// <remarks>The XML is an interchange format, not something the game loads - edit it and run
/// <c>move encode</c> to build a graph back. See docs/docs/file-formats/move.md.</remarks>
public sealed class MoveDecodeCommand : CliCommand<MoveDecodeCommand.Settings>
{
    public sealed class Settings : CommandSettings
    {
        [CommandArgument(0, "<file.bin>")]
        [Description("The binary MOVE graph to decode.")]
        public string Input { get; init; } = null!;

        [CommandOption("-o|--out <file.xml>")]
        [Description("Output .xml path (default: the input path with an .xml extension).")]
        public string? Out { get; init; }

        [CommandOption("-n|--names <movemgrnamed.bin>")]
        [Description("A named twin, to label criteria with the channel and enum value they test. "
                     + "The labels are informational and 'move encode' ignores them.")]
        public string? Names { get; init; }
    }

    protected override int Run(Settings settings, CancellationToken cancellationToken)
    {
        string outPath = settings.Out ?? Path.ChangeExtension(settings.Input, ".xml");

        byte[] data = CliIO.ReadInput(settings.Input);
        CliIO.GuardNotOverwritingInput(settings.Input, outPath);

        IReadOnlyList<MoveChannel>? channels = settings.Names is null
            ? null
            : MoveCodec.ChannelTable(CliIO.ReadInput(settings.Names));

        CliIO.WriteOutput(outPath, MoveXml.Decode(data, channels));
        CliIO.ReportWrote(outPath);
        return 0;
    }
}
