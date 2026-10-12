using System.ComponentModel;
using JackAll.Cli.Infrastructure;
using JackAll.Tools.Hkx;
using Spectre.Console;
using Spectre.Console.Cli;

namespace JackAll.Cli.Commands.Hkx;

/// <summary>Reports what would break each .hkx in game.</summary>
public sealed class HkxCheckCommand : CliCommand<HkxCheckCommand.Settings>
{
    public sealed class Settings : CommandSettings
    {
        [CommandArgument(0, "<file.hkx>")]
        [Description("The Havok files to check.")]
        public string[] Inputs { get; init; } = [];
    }

    protected override int Run(Settings settings, CancellationToken cancellationToken)
    {
        int failed = 0;
        foreach (string input in settings.Inputs)
        {
            IReadOnlyList<HkxProblem> problems = HkxLint.Check(HkxFile.Parse(CliIO.ReadInput(input)));
            if (problems.Count > 0)
            {
                AnsiConsole.MarkupLine($"{input.EscapeMarkup()}:");
                failed += HkxCli.Report(problems) ? 0 : 1;
            }
        }
        Console.WriteLine($"{settings.Inputs.Length - failed} of {settings.Inputs.Length} files have no errors");
        return failed == 0 ? 0 : 1;
    }
}
