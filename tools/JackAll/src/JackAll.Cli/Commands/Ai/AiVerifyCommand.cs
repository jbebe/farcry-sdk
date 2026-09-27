using JackAll.Cli.Infrastructure;
using JackAll.Tools.Ai;
using Spectre.Console.Cli;
using System.ComponentModel;

namespace JackAll.Cli.Commands.Ai;

/// <summary>
/// Recompiles brain workspaces from their source and reports the first place each would load
/// differently from its shipped compiled half.
/// </summary>
public sealed class AiVerifyCommand : CliCommand<AiVerifyCommand.Settings>
{
    public sealed class Settings : CommandSettings
    {
        [CommandArgument(0, "<file.ai.rml>")]
        [Description("The brain workspaces to check.")]
        public string[] Inputs { get; init; } = [];
    }

    protected override int Run(Settings settings, CancellationToken cancellationToken)
    {
        int failed = 0;
        foreach (string input in settings.Inputs)
        {
            AiWorkspaceFile file = AiWorkspaceFile.Read(CliIO.ReadInput(input));
            string? difference = AiPackedRepository.Read(file.Packed)
                .FirstDifference(AiPackedRepository.Read(AiWorkspacePacker.Pack(file.Source)));
            Console.WriteLine($"{Path.GetFileName(input)}: {difference ?? "identical"}");
            failed += difference is null ? 0 : 1;
        }
        return failed == 0 ? 0 : 1;
    }
}
