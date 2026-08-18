using System.ComponentModel;
using JackAll.Cli.Infrastructure;
using JackAll.Core.Format.Fcb;
using Spectre.Console.Cli;

namespace JackAll.Cli.Commands.Fcb;

/// <summary>
/// Re-encodes a (possibly hand-edited) XML document back into an .fcb — the reverse of
/// <c>fcb decode</c> and the CLI counterpart of the App's Fcb import. Needs no class config: the XML
/// already carries each value's type and each name/hash. Validates the result by round-tripping it
/// back through <see cref="FcbDocument.Deserialize"/>.
/// </summary>
public sealed class FcbEncodeCommand : CliCommand<FcbEncodeCommand.Settings>
{
    public sealed class Settings : CommandSettings
    {
        [CommandArgument(0, "<file.xml>")]
        [Description("The XML file produced by `fcb decode`.")]
        public string Input { get; init; } = null!;

        [CommandOption("-o|--out <file.fcb>")]
        [Description("Output .fcb path (default: the input path with an .fcb extension).")]
        public string? Out { get; init; }
    }

    protected override int Run(Settings settings, CancellationToken cancellationToken)
    {
        string outPath = settings.Out ?? Path.ChangeExtension(settings.Input, ".fcb");
        CliIO.GuardNotOverwritingInput(settings.Input, outPath);

        FcbObject root = FcbXml.FromXml(CliIO.ReadInputText(settings.Input));
        byte[] fcb = FcbDocument.Serialize(root);

        FcbDocument.Deserialize(fcb); // validity check, same as the App before staging

        CliIO.WriteOutput(outPath, fcb);
        CliIO.ReportWrote(outPath);
        return 0;
    }
}
