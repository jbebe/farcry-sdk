using JackAll.Cli.Infrastructure;
using JackAll.Core.Format.Fcb;
using JackAll.Core;
using Spectre.Console.Cli;
using System.ComponentModel;

namespace JackAll.Cli.Commands.Fcb;

/// <summary>
/// Decodes an .fcb to one Gibbed-compatible XML document; <c>fcb encode</c> reads it back.
/// Class/member hashes resolve to readable names via the bundled <c>binary_classes.xml</c> (disable
/// with --no-classes to keep everything hash-only/BinHex).
/// </summary>
public sealed class FcbDecodeCommand : CliCommand<FcbDecodeCommand.Settings>
{
    public sealed class Settings : CommandSettings
    {
        [CommandArgument(0, "<file.fcb>")]
        [Description("The .fcb to decode.")]
        public string Input { get; init; } = null!;

        [CommandOption("-o|--out <file.xml>")]
        [Description("Output XML path (default: the input path with an .xml extension).")]
        public string? Out { get; init; }

        [CommandOption("--no-classes")]
        [Description("Don't load binary_classes.xml — emit hash-only/BinHex for every type and member.")]
        public bool NoClasses { get; init; }
    }

    protected override int Run(Settings settings, CancellationToken cancellationToken)
    {
        byte[] data = CliIO.ReadInput(settings.Input);
        FcbObject root = FcbDocument.Deserialize(data);

        FcbClassDefinitions defs = settings.NoClasses ? FcbClassDefinitions.Empty : BundledAssets.LoadFcbClasses();
        string xml = FcbXml.ToXml(root, defs);

        string outPath = CliIO.ResolveOutput(
            settings.Out, settings.Input, Path.GetFileNameWithoutExtension(settings.Input) + ".xml");
        CliIO.WriteOutput(outPath, xml);
        CliIO.ReportWrote(outPath);
        return 0;
    }
}
