using System.ComponentModel;
using JackAll.Cli.Infrastructure;
using JackAll.Tools.Spk;
using Spectre.Console.Cli;

namespace JackAll.Cli.Commands.Spk;

/// <summary>Builds an XML document back into an .spk bank, after checking it for what would break in game.</summary>
public sealed class SpkEncodeCommand : CliCommand<SpkEncodeCommand.Settings>
{
    public sealed class Settings : CommandSettings
    {
        [CommandArgument(0, "<bank.xml>")]
        [Description("The XML document to build; the audio files it names are read from beside it.")]
        public string Input { get; init; } = null!;

        [CommandOption("-o|--out <file.spk>")]
        [Description("Output path (default: the input path with an .spk extension). Name it after the " +
                     "event it plays: the game loads soundbinary\\<id>.spk.")]
        public string? Out { get; init; }
    }

    protected override int Run(Settings settings, CancellationToken cancellationToken)
    {
        string outPath = settings.Out ?? Path.ChangeExtension(settings.Input, ".spk");
        CliIO.GuardNotOverwritingInput(settings.Input, outPath);

        string dir = Path.GetDirectoryName(Path.GetFullPath(settings.Input))!;
        SpkBank bank = SpkBankXml.FromXml(CliIO.ReadInputText(settings.Input), file => CliIO.ReadInput(Path.Combine(dir, file)));
        if (!SpkFormat.Report(SpkBankLint.Check(bank, SpkFormat.BankId(outPath))))
        {
            return 1;
        }

        byte[] bytes = bank.Write();
        SpkBank.Parse(bytes);
        CliIO.WriteOutput(outPath, bytes);
        CliIO.ReportWrote(outPath);
        return 0;
    }
}
