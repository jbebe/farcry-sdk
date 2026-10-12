using System.ComponentModel;
using JackAll.Cli.Infrastructure;
using JackAll.Tools.Hkx;
using Spectre.Console.Cli;

namespace JackAll.Cli.Commands.Hkx;

/// <summary>Prints an .hkx's objects and their members by name.</summary>
public sealed class HkxDumpCommand : CliCommand<HkxDumpCommand.Settings>
{
    public sealed class Settings : CommandSettings
    {
        [CommandArgument(0, "<file.hkx>")]
        [Description("The Havok file to print.")]
        public string Input { get; init; } = null!;

        [CommandOption("-o|--out <file>")]
        [Description("Write the text to a file instead of the console.")]
        public string? Out { get; init; }
    }

    protected override int Run(Settings settings, CancellationToken cancellationToken)
    {
        string text = HkxDump.Write(HkxFile.Parse(CliIO.ReadInput(settings.Input)));
        if (settings.Out is null)
        {
            Console.Write(text);
            return 0;
        }
        CliIO.WriteOutput(settings.Out, text);
        CliIO.ReportWrote(settings.Out);
        return 0;
    }
}
