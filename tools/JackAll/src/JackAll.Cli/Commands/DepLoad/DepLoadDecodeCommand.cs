using System.ComponentModel;
using JackAll.Cli.Infrastructure;
using JackAll.Core;
using JackAll.Core.Format;
using JackAll.Core.Naming;

using Spectre.Console.Cli;

namespace JackAll.Cli.Commands.DepLoad;

/// <summary>Decodes a `depload.dat` dependency index to editable XML.</summary>
/// <remarks>Shaped after the `_depload.xml` twins the game ships beside its binaries. See
/// docs/docs/file-formats/depload.md.</remarks>
public sealed class DepLoadDecodeCommand : CliCommand<DepLoadDecodeCommand.Settings>
{
    public sealed class Settings : CommandSettings
    {
        [CommandArgument(0, "<file_depload.dat>")]
        [Description("The dependency index to decode.")]
        public string Input { get; init; } = null!;

        [CommandOption("-o|--out <file.xml>")]
        [Description("Output path (default: the input path with a .xml extension).")]
        public string? Out { get; init; }

        [CommandOption("--no-names")]
        [Description("Write bare hashes, skipping the hashlist lookup that labels each entry with its path.")]
        public bool NoNames { get; init; }
    }

    protected override int Run(Settings settings, CancellationToken cancellationToken)
    {
        string outPath = settings.Out ?? Path.ChangeExtension(settings.Input, ".xml");

        byte[] content = CliIO.ReadInput(settings.Input);
        CliIO.GuardNotOverwritingInput(settings.Input, outPath);

        NameDatabase? names = settings.NoNames ? null : BundledAssets.LoadNames();
        CliIO.WriteOutput(outPath, DepLoadXml.Decode(content, names));
        CliIO.ReportWrote(outPath);
        return 0;
    }
}
