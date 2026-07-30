using JackAll.Cli.Infrastructure;
using JackAll.Tools.Format;
using Spectre.Console.Cli;
using System.ComponentModel;

namespace JackAll.Cli.Commands.Xbt;

/// <summary>
/// Reassembles an .xbt from a replacement <c>.dds</c> and the <c>.xml</c> header produced by
/// <c>xbt extract</c> — the CLI counterpart of the App's Xbt import. The header carries bytes that
/// can't be synthesized from a DDS alone (see <see cref="XbtTexture"/>), so a real <c>.xml</c> header
/// is required, not optional. Validates the result by round-tripping it back through
/// <see cref="XbtTexture.Split"/>, exactly as the App does before staging.
/// </summary>
public sealed class XbtBuildCommand : CliCommand<XbtBuildCommand.Settings>
{
    public sealed class Settings : CommandSettings
    {
        [CommandArgument(0, "<file.dds>")]
        [Description("The replacement DDS payload.")]
        public string Dds { get; init; } = null!;

        [CommandArgument(1, "[file.xml]")]
        [Description("The header XML from `xbt extract` (default: the .dds path with a .xml extension).")]
        public string? Xml { get; init; }

        [CommandOption("-o|--out <file.xbt>")]
        [Description("Output .xbt path (default: the .dds path with an .xbt extension).")]
        public string? Out { get; init; }
    }

    protected override int Run(Settings settings, CancellationToken cancellationToken)
    {
        string xmlPath = settings.Xml ?? Path.ChangeExtension(settings.Dds, ".xml");
        string outPath = settings.Out ?? Path.ChangeExtension(settings.Dds, ".xbt");

        byte[] dds = CliIO.ReadInput(settings.Dds);
        byte[] header = XbtTexture.HeaderFromXml(CliIO.ReadInputText(xmlPath));
        byte[] combined = XbtTexture.Combine(header, dds);

        // Same validity check the App runs before staging: this throws the way a corrupt .xbt would
        // if HeaderSize doesn't land on a DDS payload.
        XbtTexture.Split(combined);

        CliIO.WriteOutput(outPath, combined);
        CliIO.ReportWrote(outPath);
        return 0;
    }
}
