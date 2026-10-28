using JackAll.Cli.Infrastructure;
using JackAll.Core.Format.Ai;
using JackAll.Tools.Ai;
using Spectre.Console.Cli;
using System.ComponentModel;
using System.Xml.Linq;

namespace JackAll.Cli.Commands.Ai;

/// <summary>
/// Compiles a BlackBox.AI source into a brain workspace - the reverse of <c>ai unpack</c>.
/// </summary>
public sealed class AiPackCommand : CliCommand<AiPackCommand.Settings>
{
    public sealed class Settings : CommandSettings
    {
        [CommandArgument(0, "<file.source.xml>")]
        [Description("The BlackBox.AI source to compile.")]
        public string Input { get; init; } = null!;

        [CommandOption("-o|--out <file.ai.rml>")]
        [Description("Output path (default: the input path with .source.xml replaced by .ai.rml).")]
        public string? Out { get; init; }
    }

    protected override int Run(Settings settings, CancellationToken cancellationToken)
    {
        string stem = Path.GetFileName(settings.Input).Replace(".source.xml", "", StringComparison.OrdinalIgnoreCase);
        string outPath = CliIO.ResolveOutput(settings.Out, settings.Input, stem + ".ai.rml");
        CliIO.GuardNotOverwritingInput(settings.Input, outPath);

        CliIO.WriteOutput(outPath, AiWorkspaceFile.Compile(XElement.Parse(CliIO.ReadInputText(settings.Input))));
        CliIO.ReportWrote(outPath);
        return 0;
    }
}
