using System.ComponentModel;
using JackAll.Cli.Infrastructure;
using JackAll.Tools.Spk;
using Spectre.Console.Cli;

namespace JackAll.Cli.Commands.Spk;

/// <summary>Writes an .spk bank as an editable XML document, its audio streams beside it.</summary>
public sealed class SpkDecodeCommand : CliCommand<SpkDecodeCommand.Settings>
{
    public sealed class Settings : CommandSettings
    {
        [CommandArgument(0, "<file.spk>")]
        [Description("The .spk sound bank to decode.")]
        public string Input { get; init; } = null!;

        [CommandOption("-o|--out <dir>")]
        [Description("Output folder (default: a folder named after the bank, next to it).")]
        public string? Out { get; init; }
    }

    protected override int Run(Settings settings, CancellationToken cancellationToken)
    {
        SpkBank bank = SpkBank.Parse(CliIO.ReadInput(settings.Input));
        string name = Path.GetFileNameWithoutExtension(settings.Input);
        string dir = CliIO.ResolveOutput(settings.Out, settings.Input, name);
        CliIO.EnsureDirectory(dir);

        string xml = SpkBankXml.ToXml(bank, (file, bytes) => CliIO.WriteOutput(Path.Combine(dir, file), bytes));
        string xmlPath = Path.Combine(dir, name + ".xml");
        CliIO.WriteOutput(xmlPath, xml);
        CliIO.ReportWrote(xmlPath);
        return 0;
    }
}
