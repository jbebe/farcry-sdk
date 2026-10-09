using JackAll.Cli.Infrastructure;
using JackAll.Tools.Xbt;
using Spectre.Console;
using Spectre.Console.Cli;
using System.ComponentModel;

namespace JackAll.Cli.Commands.Xbt;

/// <summary>
/// Builds an .xbt from a <c>.dds</c>, under the <c>.xml</c> header <c>xbt extract</c> produced or,
/// without one, a fresh header that names no companion - the CLI counterpart of the App's Xbt import.
/// Validates the result by round-tripping it back through <see cref="XbtTexture.Split"/>, exactly as
/// the App does before staging.
/// </summary>
public sealed class XbtBuildCommand : CliCommand<XbtBuildCommand.Settings>
{
    public sealed class Settings : CommandSettings
    {
        [CommandArgument(0, "<file.dds>")]
        [Description("The replacement DDS payload.")]
        public string Dds { get; init; } = null!;

        [CommandArgument(1, "[file.xml]")]
        [Description("The header XML from `xbt extract` (default: the .dds path with a .xml extension; "
            + "when that does not exist either, a new header is written and the .dds must carry the whole mip chain).")]
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
        bool fresh = settings.Xml is null && !File.Exists(xmlPath);
        byte[] header = fresh ? XbtTexture.NewHeader() : XbtTexture.HeaderFromXml(CliIO.ReadInputText(xmlPath));
        byte[] combined = XbtTexture.Combine(header, dds);

        // Same validity check the App runs before staging: this throws the way a corrupt .xbt would
        // if HeaderSize doesn't land on a DDS payload.
        XbtTexture.Split(combined);

        CliIO.WriteOutput(outPath, combined);
        if (fresh)
        {
            AnsiConsole.MarkupLine($"No {Path.GetFileName(xmlPath).EscapeMarkup()} beside the .dds: wrote a new header.");
        }
        CliIO.ReportWrote(outPath);
        return 0;
    }
}
