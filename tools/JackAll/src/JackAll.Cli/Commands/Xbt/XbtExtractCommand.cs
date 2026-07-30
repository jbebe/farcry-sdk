using JackAll.Cli.Infrastructure;
using JackAll.Tools.Format;
using Spectre.Console.Cli;
using System.ComponentModel;

namespace JackAll.Cli.Commands.Xbt;

/// <summary>
/// Splits an .xbt into its embedded, fully valid <c>.dds</c> and a companion <c>.xml</c> header (the
/// engine-specific bytes the App can't synthesize from a bare DDS — see <see cref="XbtTexture"/>'s
/// remarks). Same pair the App's Xbt handler exports; <c>xbt build</c> reassembles them.
/// </summary>
public sealed class XbtExtractCommand : CliCommand<XbtExtractCommand.Settings>
{
    public sealed class Settings : CommandSettings
    {
        [CommandArgument(0, "<file.xbt>")]
        [Description("The .xbt texture to split.")]
        public string Input { get; init; } = null!;

        [CommandOption("-o|--out-dir <dir>")]
        [Description("Directory for the .dds/.xml pair (default: next to the input).")]
        public string? OutDir { get; init; }
    }

    protected override int Run(Settings settings, CancellationToken cancellationToken)
    {
        byte[] xbt = CliIO.ReadInput(settings.Input);
        (byte[] header, byte[] dds) = XbtTexture.Split(xbt);

        string baseName = Path.GetFileNameWithoutExtension(settings.Input);
        string ddsPath = CliIO.ResolveOutput(
            settings.OutDir is null ? null : Path.Combine(settings.OutDir, baseName + ".dds"),
            settings.Input, baseName + ".dds");
        string xmlPath = Path.ChangeExtension(ddsPath, ".xml");

        CliIO.WriteOutput(ddsPath, dds);
        CliIO.WriteOutput(xmlPath, XbtTexture.ToXml(header));

        CliIO.ReportWrote(ddsPath);
        CliIO.ReportWrote(xmlPath);
        return 0;
    }
}
