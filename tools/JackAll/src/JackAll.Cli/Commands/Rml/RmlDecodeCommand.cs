using System.ComponentModel;
using System.Xml.Linq;
using JackAll.Cli.Infrastructure;
using JackAll.Core.Format.Rml;
using Spectre.Console.Cli;

namespace JackAll.Cli.Commands.Rml;

/// <summary>Decodes a binary .rml (Dunia's table-of-contents / resource-manifest XML) to plain,
/// editable XML — the App's Rml export. Unlike .fcb, .rml never splits, so it's one file in, one out.</summary>
public sealed class RmlDecodeCommand : CliCommand<RmlDecodeCommand.Settings>
{
    public sealed class Settings : CommandSettings
    {
        [CommandArgument(0, "<file.rml>")]
        [Description("The binary .rml to decode.")]
        public string Input { get; init; } = null!;

        [CommandOption("-o|--out <file.xml>")]
        [Description("Output .xml path (default: the input path with an .xml extension).")]
        public string? Out { get; init; }
    }

    protected override int Run(Settings settings, CancellationToken cancellationToken)
    {
        string outPath = settings.Out ?? Path.ChangeExtension(settings.Input, ".xml");

        byte[] data = CliIO.ReadInput(settings.Input);
        XElement root = RmlDocument.Deserialize(data);

        CliIO.WriteOutput(outPath, root.ToString());
        CliIO.ReportWrote(outPath);
        return 0;
    }
}
